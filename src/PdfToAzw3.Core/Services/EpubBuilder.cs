using System.IO.Compression;
using System.Security;
using System.Text;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Text;

namespace PdfToAzw3.Core.Services;

public sealed class EpubBuilder : IEpubBuilder
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public Task<string> BuildAsync(
        BookDocument book,
        ConversionOptions options,
        string outputPath,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => BuildCore(book, options, outputPath, progress, cancellationToken), cancellationToken);
    }

    private static string BuildCore(
        BookDocument book,
        ConversionOptions options,
        string outputPath,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Không xác định được thư mục tạo EPUB.");
        }

        Directory.CreateDirectory(directory);
        progress?.Report(new ConversionProgress("Building EPUB", 0.82, Detail: "Tạo cấu trúc EPUB"));
        using var archive = ZipFile.Open(fullPath, ZipArchiveMode.Create);
        WriteEntry(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        WriteEntry(archive, "META-INF/container.xml", ContainerXml);
        WriteEntry(archive, "OEBPS/styles/book.css", BuildCss(options));

        var chapters = book.Chapters.Count == 0
            ? [new BookChapter { Title = "Nội dung", AnchorId = "chapter-1", SourcePageNumber = 1 }]
            : book.Chapters;
        var chapterPaths = new List<(BookChapter Chapter, string Path, string Id)>();
        for (var index = 0; index < chapters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = chapters[index];
            var chapterPath = $"OEBPS/text/chapter{index + 1:000}.xhtml";
            var chapterId = $"chapter-{index + 1:000}";
            chapterPaths.Add((chapter, chapterPath, chapterId));
            WriteEntry(archive, chapterPath, BuildChapterXhtml(chapter, book.Metadata, options, index));
            progress?.Report(new ConversionProgress(
                "Building EPUB",
                0.82 + 0.10 * (index + 1) / chapters.Count,
                Detail: $"Đang dựng chapter {index + 1} / {chapters.Count}"));
        }

        WriteEntry(archive, "OEBPS/nav.xhtml", BuildNavigation(book.Metadata, chapterPaths, options));
        WriteEntry(archive, "OEBPS/toc.ncx", BuildNcx(book.Metadata, chapterPaths));
        WriteEntry(archive, "OEBPS/content.opf", BuildContentOpf(book.Metadata, chapterPaths));

        foreach (var resource in book.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(archive, $"OEBPS/images/{resource.FileName}", resource.Content, CompressionLevel.Optimal);
        }

        progress?.Report(new ConversionProgress("EPUB built", 0.93, Detail: fullPath));
        return fullPath;
    }

    private static string BuildChapterXhtml(BookChapter chapter, BookMetadata metadata, ConversionOptions options, int chapterIndex)
    {
        var language = DetectLanguage(metadata);
        var builder = new StringBuilder();
        builder.Append($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        builder.Append($"<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"{language}\" xml:lang=\"{language}\">\n<head><title>{Escape(chapter.Title)}</title><link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/book.css\" /></head><body>\n");
        builder.Append($"<section id=\"{EscapeAttribute(chapter.AnchorId)}\"><h1>{Escape(chapter.Title)}</h1>\n");

        foreach (var block in chapter.Blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var level = Math.Clamp(heading.Level + 1, 2, 4);
                    builder.Append($"<{Tag(level)} id=\"{EscapeAttribute(heading.AnchorId)}\">{Escape(heading.Text)}</{Tag(level)}>\n");
                    break;
                case QuoteBlock quote:
                    builder.Append($"<blockquote><p>{Escape(quote.Text)}</p></blockquote>\n");
                    break;
                case ParagraphBlock paragraph when paragraph.IsCode || paragraph.BlockType == LayoutBlockType.Code:
                    builder.Append($"<pre><code>{Escape(paragraph.Text)}</code></pre>\n");
                    break;
                case ParagraphBlock paragraph:
                    builder.Append($"<p>{Escape(paragraph.Text)}</p>\n");
                    break;
                case ImageBlock image:
                    builder.Append($"<figure><img src=\"../images/{EscapeAttribute(image.ResourceId)}\" alt=\"{EscapeAttribute(image.Caption ?? "Illustration")}\" />");
                    if (!string.IsNullOrWhiteSpace(image.Caption))
                    {
                        builder.Append($"<figcaption>{Escape(image.Caption)}</figcaption>");
                    }

                    builder.Append("</figure>\n");
                    break;
                case ListBlock list:
                    var listTag = list.Ordered ? "ol" : "ul";
                    builder.Append($"<{listTag}>\n");
                    foreach (var item in list.Items)
                    {
                        builder.Append($"<li>{Escape(item)}</li>\n");
                    }

                    builder.Append($"</{listTag}>\n");
                    break;
                case TableBlock table:
                    builder.Append(BuildTable(table));
                    break;
            }
        }

        builder.Append("</section></body></html>");
        return builder.ToString();
    }

    private static string BuildNavigation(BookMetadata metadata, IReadOnlyList<(BookChapter Chapter, string Path, string Id)> chapters, ConversionOptions options)
    {
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"{DetectLanguage(metadata)}\"><head><title>Contents</title></head><body><nav epub:type=\"toc\" id=\"toc\"><h1>Table of Contents</h1><ol>\n");
        foreach (var item in chapters)
        {
            builder.Append($"<li><a href=\"text/{Path.GetFileName(item.Path)}#{EscapeAttribute(item.Chapter.AnchorId)}\">{Escape(item.Chapter.Title)}</a>");
            if (options.GenerateTableOfContents)
            {
                var headings = item.Chapter.Blocks.OfType<HeadingBlock>().ToArray();
                if (headings.Length > 0)
                {
                    builder.Append("<ol>");
                    foreach (var heading in headings)
                    {
                        builder.Append($"<li><a href=\"text/{Path.GetFileName(item.Path)}#{EscapeAttribute(heading.AnchorId)}\">{Escape(heading.Text)}</a></li>");
                    }

                    builder.Append("</ol>");
                }
            }

            builder.Append("</li>\n");
        }

        builder.Append("</ol></nav></body></html>");
        return builder.ToString();
    }

    private static string BuildNcx(BookMetadata metadata, IReadOnlyList<(BookChapter Chapter, string Path, string Id)> chapters)
    {
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\"><head><meta name=\"dtb:uid\" content=\"book-id\" /></head><docTitle><text>{Escape(metadata.Title)}</text></docTitle><navMap>");
        for (var index = 0; index < chapters.Count; index++)
        {
            var item = chapters[index];
            builder.Append($"<navPoint id=\"{item.Id}\" playOrder=\"{index + 1}\"><navLabel><text>{Escape(item.Chapter.Title)}</text></navLabel><content src=\"text/{Path.GetFileName(item.Path)}#{EscapeAttribute(item.Chapter.AnchorId)}\" /></navPoint>");
        }

        builder.Append("</navMap></ncx>");
        return builder.ToString();
    }

    private static string BuildContentOpf(BookMetadata metadata, IReadOnlyList<(BookChapter Chapter, string Path, string Id)> chapters)
    {
        var modified = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" unique-identifier=\"book-id\" version=\"3.0\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"book-id\">urn:uuid:pdf-to-azw3</dc:identifier><dc:title>{Escape(metadata.Title)}</dc:title><dc:creator>{Escape(metadata.Author)}</dc:creator><dc:language>{DetectLanguage(metadata)}</dc:language><dc:publisher>{Escape(metadata.Publisher)}</dc:publisher><dc:description>{Escape(metadata.Description)}</dc:description><meta property=\"dcterms:modified\">{modified}</meta></metadata><manifest><item id=\"nav\" properties=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" /><item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\" /><item id=\"css\" href=\"styles/book.css\" media-type=\"text/css\" />");
        foreach (var item in chapters)
        {
            builder.Append($"<item id=\"{item.Id}\" href=\"text/{Path.GetFileName(item.Path)}\" media-type=\"application/xhtml+xml\" />");
        }

        builder.Append("</manifest><spine toc=\"ncx\">");
        foreach (var item in chapters)
        {
            builder.Append($"<itemref idref=\"{item.Id}\" />");
        }

        builder.Append("</spine></package>");
        return builder.ToString();
    }

    private static string BuildCss(ConversionOptions options)
    {
        var paragraphRules = options.ParagraphStyle switch
        {
            ParagraphStyle.Document => "margin: 0 0 0.8em 0; text-indent: 0;",
            ParagraphStyle.Compact => "margin: 0 0 0.35em 0; text-indent: 0.8em;",
            _ => "margin: 0 0 0.7em 0; text-indent: 1.2em;"
        };

        var fixedLayout = options.Profile == ConversionProfile.FixedLayout;
        return $"body {{ margin: 0; padding: 0 2%; line-height: 1.45; text-align: justify; font-size: 1em; }}\nsection {{ max-width: 42em; margin: 0 auto; }}\np {{ {paragraphRules} }}\nh1 {{ text-align: center; margin: 1.5em 0 1em; page-break-before: {(fixedLayout ? "auto" : "always")}; font-size: 1.7em; }}\nh2 {{ margin: 1.3em 0 0.8em; font-size: 1.35em; }}\nh3, h4 {{ margin: 1em 0 0.6em; font-size: 1.15em; }}\nblockquote {{ margin: 0.9em 1.5em; padding-left: 1em; border-left: 0.2em solid #c7cfdf; }}\npre {{ white-space: pre-wrap; font-family: monospace; margin: 1em 0; padding: 0.8em; background: #f2f4f8; }}\ncode {{ font-family: monospace; }}\nimg {{ display: block; max-width: 100%; height: auto; margin: 1em auto; }}\nfigure {{ margin: 1em 0; text-align: center; }}\nfigcaption {{ margin-top: 0.4em; font-style: italic; text-align: center; }}\ntable {{ border-collapse: collapse; width: 100%; margin: 1em 0; }}\nth, td {{ border: 0.08em solid #aeb8c8; padding: 0.35em; vertical-align: top; }}\n";
    }

    private static string BuildTable(TableBlock table)
    {
        var builder = new StringBuilder("<table>");
        foreach (var row in table.Rows)
        {
            builder.Append("<tr>");
            foreach (var cell in row)
            {
                builder.Append($"<td>{Escape(cell)}</td>");
            }

            builder.Append("</tr>");
        }

        builder.Append("</table>\n");
        return builder.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content, CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        WriteEntry(archive, path, Utf8.GetBytes(content), compressionLevel);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content, CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(path, compressionLevel);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static string Tag(int level) => level switch
    {
        2 => "h2",
        3 => "h3",
        _ => "h4"
    };

    private static string DetectLanguage(BookMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Language) && !metadata.Language.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return metadata.Language.StartsWith("vi", StringComparison.OrdinalIgnoreCase) ? "vi" : "en";
        }

        var text = $"{metadata.Title} {metadata.Description}";
        return text.Any(character => "ăâđêôơưĂÂĐÊÔƠƯ".Contains(character, StringComparison.Ordinal)) ? "vi" : "en";
    }

    private static string Escape(string? value) => SecurityElement.Escape(TextNormalizer.Normalize(value)) ?? string.Empty;

    private static string EscapeAttribute(string? value) => Escape(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string ContainerXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\" /></rootfiles></container>";
}

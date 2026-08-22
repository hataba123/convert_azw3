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
        using var archiveFile = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Create);
        WriteEntry(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        WriteEntry(archive, "META-INF/container.xml", ContainerXml);
        WriteEntry(archive, "OEBPS/styles/book.css", BuildCss(options));
        WriteEntry(archive, "OEBPS/styles/fixed-layout.css", FixedLayoutCss);
        var resources = book.Resources.ToList();
        var cover = CreateCoverResource(book);
        var identifier = $"urn:uuid:{Guid.NewGuid():D}";
        if (cover is not null)
        {
            resources.Insert(0, cover);
            WriteEntry(archive, $"OEBPS/images/{cover.FileName}", cover.Content, CompressionLevel.Optimal);
        }

        if (options.Profile == ConversionProfile.FixedLayout)
        {
            return BuildFixedLayoutEpub(archive, book, book.Metadata, resources, cover, identifier, fullPath, progress, cancellationToken);
        }

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
        if (options.GenerateTableOfContents)
        {
            WriteEntry(archive, "OEBPS/text/toc.xhtml", BuildInlineTableOfContents(book.Metadata, chapterPaths));
        }

        WriteEntry(archive, "OEBPS/toc.ncx", BuildNcx(book.Metadata, chapterPaths, identifier));
        WriteEntry(archive, "OEBPS/content.opf", BuildContentOpf(book.Metadata, chapterPaths, resources, cover, identifier, options.GenerateTableOfContents));

        foreach (var resource in resources.Where(resource => cover is null || !ReferenceEquals(resource, cover)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(archive, $"OEBPS/images/{resource.FileName}", resource.Content, CompressionLevel.Optimal);
        }

        progress?.Report(new ConversionProgress("EPUB built", 0.93, Detail: fullPath));
        return fullPath;
    }

    private static string BuildFixedLayoutEpub(
        ZipArchive archive,
        BookDocument book,
        BookMetadata metadata,
        IReadOnlyList<BookResource> resources,
        BookResource? cover,
        string identifier,
        string fullPath,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (book.FixedLayoutPages.Count == 0)
        {
            throw new InvalidDataException("Fixed Layout chưa có ảnh raster cho từng trang PDF.");
        }

        var pages = book.FixedLayoutPages
            .OrderBy(page => page.PageNumber)
            .ToArray();
        var pagePaths = new List<(FixedLayoutPage Page, string Path, string Id)>();
        for (var index = 0; index < pages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pages[index];
            var pagePath = $"OEBPS/text/page{index + 1:0000}.xhtml";
            var pageId = $"page-{index + 1:0000}";
            pagePaths.Add((page, pagePath, pageId));
            WriteEntry(archive, pagePath, BuildFixedPageXhtml(page, metadata, pageId));
            progress?.Report(new ConversionProgress(
                "Building Fixed Layout EPUB",
                0.82 + 0.08 * (index + 1) / pages.Length,
                page.PageNumber,
                pages.Length,
                $"Đang dựng trang cố định {page.PageNumber:N0} / {pages.Length:N0}"));
        }

        WriteEntry(archive, "OEBPS/nav.xhtml", BuildFixedNavigation(metadata, pagePaths));
        WriteEntry(archive, "OEBPS/toc.ncx", BuildFixedNcx(metadata, pagePaths, identifier));
        WriteEntry(archive, "OEBPS/content.opf", BuildFixedContentOpf(metadata, pagePaths, resources, cover, identifier));

        foreach (var resource in resources.Where(resource => cover is null || !ReferenceEquals(resource, cover)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(archive, $"OEBPS/images/{resource.FileName}", resource.Content, CompressionLevel.Optimal);
        }

        progress?.Report(new ConversionProgress("EPUB built", 0.93, Detail: fullPath));
        return fullPath;
    }

    private static string BuildFixedPageXhtml(FixedLayoutPage page, BookMetadata metadata, string pageId)
    {
        var language = DetectLanguage(metadata);
        return $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"{language}\" xml:lang=\"{language}\"><head><title>{Escape(page.Label)}</title><meta name=\"viewport\" content=\"width={page.PixelWidth}, height={page.PixelHeight}\" /><link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/fixed-layout.css\" /></head><body id=\"{pageId}\"><img src=\"../images/{EscapeAttribute(page.FileName)}\" width=\"{page.PixelWidth}\" height=\"{page.PixelHeight}\" alt=\"Trang {page.PageNumber}: {EscapeAttribute(page.Label)}\" /></body></html>";
    }

    private static string BuildFixedNavigation(
        BookMetadata metadata,
        IReadOnlyList<(FixedLayoutPage Page, string Path, string Id)> pages)
    {
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"{DetectLanguage(metadata)}\"><head><title>Contents</title></head><body><nav epub:type=\"toc\" id=\"toc\"><h1>Pages</h1><ol>\n");
        foreach (var item in pages)
        {
            builder.Append($"<li><a href=\"text/{Path.GetFileName(item.Path)}#{item.Id}\">Trang {item.Page.PageNumber} - {Escape(item.Page.Label)}</a></li>\n");
        }

        builder.Append("</ol></nav></body></html>");
        return builder.ToString();
    }

    private static string BuildFixedNcx(
        BookMetadata metadata,
        IReadOnlyList<(FixedLayoutPage Page, string Path, string Id)> pages,
        string identifier)
    {
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\"><head><meta name=\"dtb:uid\" content=\"{EscapeAttribute(identifier)}\" /></head><docTitle><text>{Escape(metadata.Title)}</text></docTitle><navMap>");
        for (var index = 0; index < pages.Count; index++)
        {
            var item = pages[index];
            builder.Append($"<navPoint id=\"{item.Id}\" playOrder=\"{index + 1}\"><navLabel><text>Trang {item.Page.PageNumber} - {Escape(item.Page.Label)}</text></navLabel><content src=\"text/{Path.GetFileName(item.Path)}#{item.Id}\" /></navPoint>");
        }

        builder.Append("</navMap></ncx>");
        return builder.ToString();
    }

    private static string BuildFixedContentOpf(
        BookMetadata metadata,
        IReadOnlyList<(FixedLayoutPage Page, string Path, string Id)> pages,
        IReadOnlyList<BookResource> resources,
        BookResource? cover,
        string identifier)
    {
        var modified = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" prefix=\"rendition: http://www.idpf.org/vocab/rendition/#\" unique-identifier=\"book-id\" version=\"3.0\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"book-id\">{Escape(identifier)}</dc:identifier><dc:title>{Escape(metadata.Title)}</dc:title><dc:creator>{Escape(metadata.Author)}</dc:creator><dc:language>{DetectLanguage(metadata)}</dc:language><dc:publisher>{Escape(metadata.Publisher)}</dc:publisher><dc:description>{Escape(metadata.Description)}</dc:description><meta property=\"dcterms:modified\">{modified}</meta><meta property=\"rendition:layout\">pre-paginated</meta><meta property=\"rendition:orientation\">auto</meta><meta property=\"rendition:spread\">auto</meta>");
        if (cover is not null)
        {
            builder.Append("<meta name=\"cover\" content=\"cover-image\" />");
        }

        builder.Append("</metadata><manifest><item id=\"nav\" properties=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" /><item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\" /><item id=\"fixed-css\" href=\"styles/fixed-layout.css\" media-type=\"text/css\" />");
        foreach (var item in pages)
        {
            builder.Append($"<item id=\"{item.Id}\" href=\"text/{Path.GetFileName(item.Path)}\" media-type=\"application/xhtml+xml\" />");
        }

        foreach (var resource in resources)
        {
            var properties = cover is not null && ReferenceEquals(resource, cover) ? " properties=\"cover-image\"" : string.Empty;
            builder.Append($"<item id=\"{EscapeAttribute(resource.Id)}\"{properties} href=\"images/{EscapeAttribute(resource.FileName)}\" media-type=\"{EscapeAttribute(resource.MediaType)}\" />");
        }

        builder.Append("</manifest><spine toc=\"ncx\" page-progression-direction=\"ltr\">");
        foreach (var item in pages)
        {
            builder.Append($"<itemref idref=\"{item.Id}\" />");
        }

        builder.Append("</spine></package>");
        return builder.ToString();
    }

    private static string BuildChapterXhtml(BookChapter chapter, BookMetadata metadata, ConversionOptions options, int chapterIndex)
    {
        var language = DetectLanguage(metadata);
        var builder = new StringBuilder();
        var backlinkIds = chapter.Blocks
            .OfType<ParagraphBlock>()
            .SelectMany(paragraph => paragraph.FootnoteReferences)
            .Select(reference => reference.BackLinkId)
            .ToHashSet(StringComparer.Ordinal);
        builder.Append($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        builder.Append($"<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"{language}\" xml:lang=\"{language}\">\n<head><title>{Escape(chapter.Title)}</title><link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/book.css\" /></head><body>\n");
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
                case FootnoteBlock footnote:
                    var backlink = backlinkIds.Contains(footnote.BackLinkId)
                        ? $" <a href=\"#{EscapeAttribute(footnote.BackLinkId)}\" aria-label=\"Quay lại nội dung\">↩</a>"
                        : string.Empty;
                    builder.Append($"<aside epub:type=\"footnote\" id=\"{EscapeAttribute(footnote.AnchorId)}\"><p><sup>{Escape(footnote.Marker)}</sup> {Escape(footnote.Text)}{backlink}</p></aside>\n");
                    break;
                case ParagraphBlock paragraph when paragraph.IsCode || paragraph.BlockType == LayoutBlockType.Code:
                    builder.Append($"<pre><code>{Escape(paragraph.Text)}</code></pre>\n");
                    break;
                case ParagraphBlock paragraph:
                    builder.Append($"<p>{RenderParagraph(paragraph)}</p>\n");
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

        builder.Append("</ol></nav><nav epub:type=\"landmarks\" hidden=\"hidden\"><ol>");
        if (options.GenerateTableOfContents)
        {
            builder.Append("<li><a epub:type=\"toc\" href=\"text/toc.xhtml\">Mục lục</a></li>");
        }

        if (chapters.Count > 0)
        {
            var first = chapters[0];
            builder.Append($"<li><a epub:type=\"bodymatter\" href=\"text/{Path.GetFileName(first.Path)}#{EscapeAttribute(first.Chapter.AnchorId)}\">Nội dung chính</a></li>");
        }

        builder.Append("</ol></nav></body></html>");
        return builder.ToString();
    }

    private static string BuildInlineTableOfContents(
        BookMetadata metadata,
        IReadOnlyList<(BookChapter Chapter, string Path, string Id)> chapters)
    {
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"{DetectLanguage(metadata)}\" xml:lang=\"{DetectLanguage(metadata)}\"><head><title>Mục lục</title><link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/book.css\" /></head><body><section class=\"inline-toc\"><h1>Mục lục</h1><ol>");
        foreach (var item in chapters)
        {
            builder.Append($"<li><a href=\"{Path.GetFileName(item.Path)}#{EscapeAttribute(item.Chapter.AnchorId)}\">{Escape(item.Chapter.Title)}</a></li>");
        }

        builder.Append("</ol></section></body></html>");
        return builder.ToString();
    }

    private static string BuildNcx(BookMetadata metadata, IReadOnlyList<(BookChapter Chapter, string Path, string Id)> chapters, string identifier)
    {
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\"><head><meta name=\"dtb:uid\" content=\"{EscapeAttribute(identifier)}\" /></head><docTitle><text>{Escape(metadata.Title)}</text></docTitle><navMap>");
        for (var index = 0; index < chapters.Count; index++)
        {
            var item = chapters[index];
            builder.Append($"<navPoint id=\"{item.Id}\" playOrder=\"{index + 1}\"><navLabel><text>{Escape(item.Chapter.Title)}</text></navLabel><content src=\"text/{Path.GetFileName(item.Path)}#{EscapeAttribute(item.Chapter.AnchorId)}\" /></navPoint>");
        }

        builder.Append("</navMap></ncx>");
        return builder.ToString();
    }

    private static string BuildContentOpf(
        BookMetadata metadata,
        IReadOnlyList<(BookChapter Chapter, string Path, string Id)> chapters,
        IReadOnlyList<BookResource> resources,
        BookResource? cover,
        string identifier,
        bool includeInlineToc)
    {
        var modified = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var builder = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" unique-identifier=\"book-id\" version=\"3.0\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"book-id\">{Escape(identifier)}</dc:identifier><dc:title>{Escape(metadata.Title)}</dc:title><dc:creator>{Escape(metadata.Author)}</dc:creator><dc:language>{DetectLanguage(metadata)}</dc:language><dc:publisher>{Escape(metadata.Publisher)}</dc:publisher><dc:description>{Escape(metadata.Description)}</dc:description><meta property=\"dcterms:modified\">{modified}</meta>");
        if (cover is not null)
        {
            builder.Append("<meta name=\"cover\" content=\"cover-image\" />");
        }

        builder.Append("</metadata><manifest><item id=\"nav\" properties=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" /><item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\" /><item id=\"css\" href=\"styles/book.css\" media-type=\"text/css\" />");
        if (includeInlineToc)
        {
            builder.Append("<item id=\"inline-toc\" href=\"text/toc.xhtml\" media-type=\"application/xhtml+xml\" />");
        }
        foreach (var item in chapters)
        {
            builder.Append($"<item id=\"{item.Id}\" href=\"text/{Path.GetFileName(item.Path)}\" media-type=\"application/xhtml+xml\" />");
        }

        foreach (var resource in resources)
        {
            var properties = cover is not null && ReferenceEquals(resource, cover) ? " properties=\"cover-image\"" : string.Empty;
            builder.Append($"<item id=\"{EscapeAttribute(resource.Id)}\"{properties} href=\"images/{EscapeAttribute(resource.FileName)}\" media-type=\"{EscapeAttribute(resource.MediaType)}\" />");
        }

        builder.Append("</manifest><spine toc=\"ncx\">");
        if (includeInlineToc)
        {
            builder.Append("<itemref idref=\"inline-toc\" />");
        }
        foreach (var item in chapters)
        {
            builder.Append($"<itemref idref=\"{item.Id}\" />");
        }

        builder.Append("</spine></package>");
        return builder.ToString();
    }

    private static string BuildCss(ConversionOptions options)
    {
        var paragraphRules = options.Profile == ConversionProfile.KindleTechnicalBook
            ? "margin: 0 0 0.8em 0; text-indent: 0;"
            : options.ParagraphStyle switch
        {
            ParagraphStyle.Document => "margin: 0 0 0.8em 0; text-indent: 0;",
            ParagraphStyle.Compact => "margin: 0 0 0.2em 0; text-indent: 0.8em;",
            _ => "margin: 0; text-indent: 1.2em;"
        };

        var fixedLayout = options.Profile == ConversionProfile.FixedLayout;
        return $"body {{ margin: 0; padding: 0; }}\nsection {{ margin: 0; }}\np {{ {paragraphRules} }}\nh1 {{ text-align: center; margin: 1.5em 0 1em; page-break-before: {(fixedLayout ? "auto" : "always")}; break-before: {(fixedLayout ? "auto" : "page")}; }}\nh2, h3, h4 {{ text-align: left; page-break-after: avoid; break-after: avoid; }}\nh2 {{ margin: 1.3em 0 0.8em; }}\nh3, h4 {{ margin: 1em 0 0.6em; }}\nblockquote {{ margin: 0.9em 1.5em; padding-left: 1em; }}\npre {{ white-space: pre-wrap; font-family: monospace; margin: 1em 0; }}\ncode {{ font-family: monospace; }}\nimg {{ display: block; max-width: 100%; height: auto; margin: 1em auto; object-fit: contain; }}\nfigure {{ margin: 1em 0; text-align: center; page-break-inside: avoid; break-inside: avoid; }}\nfigcaption {{ margin-top: 0.4em; font-style: italic; text-align: center; text-indent: 0; }}\ntable {{ border-collapse: collapse; width: 100%; margin: 1em 0; }}\nth, td {{ border: 0.08em solid currentColor; padding: 0.35em; vertical-align: top; }}\n.inline-toc ol {{ list-style: none; padding-left: 0; }}\n.inline-toc li {{ margin: 0.45em 0; }}\n";
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

    private static string RenderParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.InlineRuns.Count == 0)
        {
            return RenderReferences(Escape(paragraph.Text), paragraph.FootnoteReferences, new HashSet<string>(StringComparer.Ordinal), false);
        }

        var renderedReferences = new HashSet<string>(StringComparer.Ordinal);
        var builder = new StringBuilder();
        foreach (var run in paragraph.InlineRuns)
        {
            var content = RenderReferences(Escape(run.Text), paragraph.FootnoteReferences, renderedReferences, run.IsSuperscript);
            if (run.IsBold)
            {
                content = $"<strong>{content}</strong>";
            }

            if (run.IsItalic)
            {
                content = $"<em>{content}</em>";
            }

            if (run.IsSuperscript)
            {
                content = $"<sup>{content}</sup>";
            }

            builder.Append(content);
        }

        return builder.ToString();
    }

    private static string RenderReferences(
        string content,
        IReadOnlyList<FootnoteReference> references,
        ISet<string> renderedReferences,
        bool alreadySuperscript)
    {
        foreach (var reference in references)
        {
            if (!renderedReferences.Add(reference.BackLinkId))
            {
                continue;
            }

            var marker = Escape(reference.Marker);
            var index = content.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                renderedReferences.Remove(reference.BackLinkId);
                continue;
            }

            var anchor = $"<a href=\"#{EscapeAttribute(reference.TargetId)}\" id=\"{EscapeAttribute(reference.BackLinkId)}\">{marker}</a>";
            var link = alreadySuperscript ? anchor : $"<sup>{anchor}</sup>";
            content = content.Remove(index, marker.Length).Insert(index, link);
        }

        return content;
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

    private static BookResource? CreateCoverResource(BookDocument book)
    {
        var metadata = book.Metadata;
        if (string.IsNullOrWhiteSpace(metadata.CoverPath) || !File.Exists(metadata.CoverPath))
        {
            return null;
        }

        var extension = Path.GetExtension(metadata.CoverPath).TrimStart('.').ToLowerInvariant();
        var mediaType = extension switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            _ => string.Empty
        };

        var dimensions = TryReadImageDimensions(metadata.CoverPath, extension);
        if (dimensions is { } size && Math.Max(size.Width, size.Height) < 1200)
        {
            book.Warnings.Add(new AnalysisWarning(
                $"Cover chỉ có {size.Width}×{size.Height} px; nên dùng ảnh có ít nhất một chiều 1.200 px."));
        }
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        return new BookResource
        {
            Id = "cover-image",
            FileName = $"cover.{(extension == "jpeg" ? "jpg" : extension)}",
            MediaType = mediaType,
            Content = File.ReadAllBytes(metadata.CoverPath)
        };
    }

    private static (int Width, int Height)? TryReadImageDimensions(string path, string extension)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (extension == "png" && stream.Length >= 24)
        {
            stream.Position = 16;
            return (ReadBigEndianInt32(reader), ReadBigEndianInt32(reader));
        }

        if (extension is not ("jpg" or "jpeg"))
        {
            return null;
        }

        stream.Position = 2;
        while (stream.Position + 9 < stream.Length)
        {
            if (reader.ReadByte() != 0xFF)
            {
                continue;
            }

            var marker = reader.ReadByte();
            var length = (reader.ReadByte() << 8) | reader.ReadByte();
            if (length < 2 || stream.Position + length - 2 > stream.Length)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xC3)
            {
                reader.ReadByte();
                var height = (reader.ReadByte() << 8) | reader.ReadByte();
                var width = (reader.ReadByte() << 8) | reader.ReadByte();
                return (width, height);
            }

            stream.Position += length - 2;
        }

        return null;
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3] : 0;
    }

    private const string ContainerXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\" /></rootfiles></container>";

    private const string FixedLayoutCss = "html, body { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: #fff; } body { display: flex; align-items: flex-start; justify-content: flex-start; } img { display: block; width: 100%; height: 100%; object-fit: contain; }";
}

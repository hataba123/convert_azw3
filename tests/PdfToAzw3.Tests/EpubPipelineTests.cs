using System.IO.Compression;
using System.Text;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class EpubPipelineTests
{
    [Fact]
    public async Task EpubBuilder_CreatesValidReflowableEpubWithNavigation()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var epubPath = Path.Combine(root, "book.epub");
        var book = new BookDocument
        {
            Metadata = new BookMetadata
            {
                Title = "Sách Tiếng Việt & C#",
                Author = "Nguyễn Văn Lợi",
                Language = "Vietnamese"
            }
        };
        var chapter = new BookChapter { Title = "Chương 1", AnchorId = "chuong-1", SourcePageNumber = 1 };
        chapter.Blocks.Add(new HeadingBlock
        {
            BlockType = LayoutBlockType.Heading,
            Text = "1. Giới thiệu",
            Level = 1,
            AnchorId = "gioi-thieu",
            SourcePageNumber = 1
        });
        chapter.Blocks.Add(new ParagraphBlock
        {
            BlockType = LayoutBlockType.Paragraph,
            Text = "Nội dung <đúng> & không lỗi Unicode.",
            SourcePageNumber = 1
        });
        book.Chapters.Add(chapter);

        await new EpubBuilder().BuildAsync(book, new ConversionOptions(), epubPath);
        var validation = await new EpubValidator().ValidateAsync(epubPath);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        using var archive = ZipFile.OpenRead(epubPath);
        Assert.Equal("application/epub+zip", ReadEntry(archive, "mimetype"));
        Assert.Contains(archive.Entries, entry => entry.FullName == "OEBPS/content.opf");
        Assert.Contains(archive.Entries, entry => entry.FullName == "OEBPS/nav.xhtml");
        var chapterXhtml = ReadEntry(archive, "OEBPS/text/chapter001.xhtml");
        Assert.Contains("Nội dung &lt;đúng&gt; &amp; không lỗi Unicode.", chapterXhtml);
        Assert.Contains("gioi-thieu", ReadEntry(archive, "OEBPS/nav.xhtml"));
    }

    [Fact]
    public async Task EpubBuilder_RendersFootnoteLinksAndCodeBlocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var epubPath = Path.Combine(root, "footnote.epub");
        var book = new BookDocument { Metadata = new BookMetadata { Title = "Footnotes" } };
        var chapter = new BookChapter { Title = "Chapter 1", AnchorId = "chapter-1", SourcePageNumber = 1 };
        var paragraph = new ParagraphBlock { BlockType = LayoutBlockType.Paragraph, Text = "A statement¹", SourcePageNumber = 1 };
        paragraph.FootnoteReferences.Add(new FootnoteReference("¹", "fn-1", "fnref-1"));
        chapter.Blocks.Add(paragraph);
        chapter.Blocks.Add(new FootnoteBlock
        {
            BlockType = LayoutBlockType.Footnote,
            Marker = "1",
            Text = "Additional information.",
            AnchorId = "fn-1",
            BackLinkId = "fnref-1",
            SourcePageNumber = 1
        });
        chapter.Blocks.Add(new ParagraphBlock
        {
            BlockType = LayoutBlockType.Code,
            Text = "var answer = 42;",
            IsCode = true,
            SourcePageNumber = 1
        });
        book.Chapters.Add(chapter);

        await new EpubBuilder().BuildAsync(book, new ConversionOptions(), epubPath);
        var xhtml = ReadEntry(ZipFile.OpenRead(epubPath), "OEBPS/text/chapter001.xhtml");

        Assert.Contains("href=\"#fn-1\"", xhtml);
        Assert.Contains("epub:type=\"footnote\"", xhtml);
        Assert.Contains("<pre><code>var answer = 42;</code></pre>", xhtml);
    }

    [Fact]
    public async Task EpubBuilder_EmbedsImageResourceAndCoverInManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var coverPath = Path.Combine(root, "cover.png");
        var epubPath = Path.Combine(root, "images.epub");
        await File.WriteAllBytesAsync(coverPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var book = new BookDocument { Metadata = new BookMetadata { Title = "Images", CoverPath = coverPath } };
        book.Resources.Add(new BookResource
        {
            Id = "image-001",
            FileName = "image-001.png",
            MediaType = "image/png",
            Content = [0x89, 0x50, 0x4E, 0x47]
        });
        var chapter = new BookChapter { Title = "Chapter", AnchorId = "chapter-1", SourcePageNumber = 1 };
        chapter.Blocks.Add(new ImageBlock { BlockType = LayoutBlockType.Image, ResourceId = "image-001.png", SourcePageNumber = 1 });
        book.Chapters.Add(chapter);

        await new EpubBuilder().BuildAsync(book, new ConversionOptions(), epubPath);

        using var archive = ZipFile.OpenRead(epubPath);
        var opf = ReadEntry(archive, "OEBPS/content.opf");
        Assert.Contains("properties=\"cover-image\"", opf);
        Assert.Contains("images/cover.png", opf);
        Assert.Contains("images/image-001.png", opf);
        Assert.NotNull(archive.GetEntry("OEBPS/images/cover.png"));
        Assert.NotNull(archive.GetEntry("OEBPS/images/image-001.png"));
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

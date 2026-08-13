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

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

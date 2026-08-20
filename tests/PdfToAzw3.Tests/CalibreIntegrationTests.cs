using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class CalibreIntegrationTests
{
    [Fact]
    public void CalibreService_MapsPaperwhiteToPw3Profile()
    {
        Assert.Equal("kindle_pw3", CalibreService.GetOutputProfile(KindleDeviceProfile.Paperwhite));
        Assert.Equal("kindle_scribe", CalibreService.GetOutputProfile(KindleDeviceProfile.Scribe));
    }

    [Fact]
    public async Task EbookConversionService_CreatesAzw3WhenCalibreIsInstalled()
    {
        var calibre = new CalibreService();
        if (calibre.FindExecutable() is null)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var azw3Path = Path.Combine(root, "sample.azw3");
        var book = new BookDocument { Metadata = new BookMetadata { Title = "Calibre integration", Author = "Test" } };
        var chapter = new BookChapter { Title = "Chapter 1", AnchorId = "chapter-1", SourcePageNumber = 1 };
        chapter.Blocks.Add(new ParagraphBlock { BlockType = LayoutBlockType.Paragraph, Text = "Một đoạn văn kiểm tra AZW3.", SourcePageNumber = 1 });
        book.Chapters.Add(chapter);
        var analysis = new PdfAnalysisResult
        {
            File = new PdfFileInfo(Path.Combine(root, "sample.pdf"), "sample.pdf", 1, 1, PdfDocumentKind.Text),
            Summary = new AnalysisSummary { Pages = 1, Chapters = 1, Paragraphs = 1, DocumentKind = PdfDocumentKind.Text },
            Book = book
        };

        var service = new EbookConversionService(new EpubBuilder(), new EpubValidator(), calibre);
        var output = await service.ConvertAsync(analysis, new ConversionOptions(), azw3Path, cancellationToken: new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);

        Assert.True(File.Exists(output.EpubPath));
        Assert.True(File.Exists(output.Azw3Path));
        Assert.True(new FileInfo(output.Azw3Path).Length > 0);
    }
}

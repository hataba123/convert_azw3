using System.IO.Compression;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class ConversionFallbackTests
{
    [Fact]
    public async Task ScannedPdfWithoutReadableText_RasterizesEveryPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var azw3Path = Path.Combine(root, "scan.azw3");
        const int pageCount = 3;
        var book = new BookDocument { Metadata = new BookMetadata { Title = "Scanned book" } };
        var analysis = new PdfAnalysisResult
        {
            File = new PdfFileInfo(Path.Combine(root, "scan.pdf"), "scan.pdf", 1, pageCount, PdfDocumentKind.Scanned),
            Summary = new AnalysisSummary { Pages = pageCount, DocumentKind = PdfDocumentKind.Scanned },
            Book = book
        };
        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            analysis.Pages.Add(new PdfPageAnalysis
            {
                PageNumber = pageNumber,
                Width = 612,
                Height = 792,
                IsLikelyScanned = true
            });
        }

        var calibre = new FakeCalibreService();
        var service = new EbookConversionService(
            new EpubBuilder(),
            new EpubValidator(),
            calibre,
            fixedLayoutPageBuilder: new FixedLayoutPageBuilder(new FakePageRenderer()));

        var output = await service.ConvertAsync(
            analysis,
            new ConversionOptions
            {
                Profile = ConversionProfile.KindleAuto,
                FixedLayoutPresentation = FixedLayoutPresentation.FullPage,
                FixedLayoutDpi = 96
            },
            azw3Path);

        using var archive = ZipFile.OpenRead(output.EpubPath);
        var pageEntries = archive.Entries.Count(entry =>
            entry.FullName.StartsWith("OEBPS/text/page", StringComparison.Ordinal) &&
            entry.FullName.EndsWith(".xhtml", StringComparison.Ordinal));

        Assert.Equal(pageCount, pageEntries);
        Assert.Equal(ConversionProfile.FixedLayout, calibre.LastProfile);
        Assert.Contains(analysis.Book.Warnings, warning => warning.Message.Contains("giữ đủ từng trang", StringComparison.Ordinal));
        Assert.True(File.Exists(output.Azw3Path));
    }

    private sealed class FakePageRenderer : IPdfPageRenderer
    {
        public Task<RenderedPdfPage> RenderAsync(
            string pdfPath,
            int pageNumber,
            double pdfWidth,
            double pdfHeight,
            int dpi,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new RenderedPdfPage(
                pageNumber + 1,
                96,
                128,
                pdfWidth,
                pdfHeight,
                [137, 80, 78, 71, 13, 10, 26, 10],
                dpi));
        }
    }

    private sealed class FakeCalibreService : ICalibreService
    {
        public ConversionProfile LastProfile { get; private set; }

        public string? FindExecutable(string? configuredPath = null) => "fake-calibre";

        public async Task ConvertAsync(
            string epubPath,
            string azw3Path,
            ConversionOptions options,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastProfile = options.Profile;
            await File.WriteAllBytesAsync(azw3Path, [1, 2, 3], cancellationToken);
        }
    }
}

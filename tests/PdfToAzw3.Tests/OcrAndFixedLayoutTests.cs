using System.IO.Compression;
using System.Text;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class OcrAndFixedLayoutTests
{
    [Fact]
    public async Task Reader_AppliesOcrWordsToScannedPageAndKeepsScanClassification()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdfPath = Path.Combine(root, "scan.pdf");
        await File.WriteAllBytesAsync(pdfPath, CreateBlankPdf());

        var reader = PdfPipelineFactory.CreateDefaultReader(new FakeOcrEngine(), new FakePageRenderer());
        var result = await reader.AnalyzeAsync(
            pdfPath,
            new BookMetadata { Title = "Scan" },
            new ConversionOptions
            {
                EnableOcrFallback = true,
                OcrConfidenceThreshold = 0.5
            });

        Assert.Equal(PdfDocumentKind.Scanned, result.Summary.DocumentKind);
        Assert.Equal(1, result.Summary.OcrPages);
        Assert.Equal(100, result.Summary.Quality.OcrPercentage);
        Assert.True(result.Pages[0].HasText);
        Assert.True(result.Pages[0].OcrApplied);
        Assert.Equal(ConversionProfile.FixedLayout, result.Recommendation!.Profile);
        Assert.Contains(
            result.Book.Chapters.SelectMany(chapter => chapter.Blocks).OfType<ParagraphBlock>(),
            paragraph => paragraph.Text.Contains("OCR text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FixedLayoutBuilder_RasterizesEveryPageAndBuildsPrePaginatedEpub()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdfPath = Path.Combine(root, "book.pdf");
        var epubPath = Path.Combine(root, "book.epub");
        await File.WriteAllBytesAsync(pdfPath, CreateSimplePdf());

        var analysis = await PdfPipelineFactory.CreateDefaultReader().AnalyzeAsync(
            pdfPath,
            new BookMetadata { Title = "Fixed" },
            new ConversionOptions());
        await new FixedLayoutPageBuilder(new DocNetPdfPageRenderer()).PrepareAsync(
            pdfPath,
            analysis.Pages,
            analysis.Book,
            96);

        await new EpubBuilder().BuildAsync(
            analysis.Book,
            new ConversionOptions { Profile = ConversionProfile.FixedLayout },
            epubPath);
        var validation = await new EpubValidator().ValidateAsync(epubPath);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Single(analysis.Book.FixedLayoutPages);
        using var archive = ZipFile.OpenRead(epubPath);
        var opf = ReadEntry(archive, "OEBPS/content.opf");
        Assert.Contains("rendition:layout\">pre-paginated", opf);
        Assert.NotNull(archive.GetEntry("OEBPS/images/fixed-page-0001-00.png"));
        var page = ReadEntry(archive, "OEBPS/text/page0001.xhtml");
        Assert.Contains("fixed-page-0001-00.png", page);
        Assert.Contains("viewport", page);
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        public bool IsAvailable => true;

        public string DisplayName => "Fake OCR";

        public Task<OcrPageResult> RecognizeAsync(
            RenderedPdfPage page,
            string language,
            double minimumConfidence,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OcrPageResult>(
                new(
                [
                    new OcrWordResult("OCR", new PdfRect(72, 700, 120, 720), 0.95),
                    new OcrWordResult("text", new PdfRect(128, 700, 170, 720), 0.95)
                ],
                0.95));
        }
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
            return Task.FromResult(new RenderedPdfPage(
                pageNumber + 1,
                100,
                100,
                pdfWidth,
                pdfHeight,
                [137, 80, 78, 71, 13, 10, 26, 10],
                dpi));
        }
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] CreateBlankPdf()
    {
        return CreatePdf(
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n",
            "4 0 obj\n<< /Length 0 >>\nstream\n\nendstream\nendobj\n"
        ]);
    }

    private static byte[] CreateSimplePdf()
    {
        var content = "BT /F1 24 Tf 72 700 Td (Chapter 1) Tj /F1 12 Tf 0 -30 Td (The quick brown fox jumps) Tj ET";
        return CreatePdf(
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream\nendobj\n"
        ]);
    }

    private static byte[] CreatePdf(IReadOnlyList<string> objects)
    {
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var item in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(item);
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
        {
            builder.Append($"{offsets[index]:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}

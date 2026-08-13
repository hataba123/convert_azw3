using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public interface IPdfDocumentReader
{
    Task<PdfAnalysisResult> AnalyzeAsync(
        string path,
        BookMetadata metadata,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IPdfPageAnalyzer
{
    PdfPageAnalysis Analyze(UglyToad.PdfPig.Content.Page page, int pageNumber, CancellationToken cancellationToken = default);
}

public sealed record RenderedPdfPage(
    int PageNumber,
    int PixelWidth,
    int PixelHeight,
    double PdfWidth,
    double PdfHeight,
    byte[] PngContent,
    int Dpi);

public interface IPdfPageRenderer
{
    Task<RenderedPdfPage> RenderAsync(
        string pdfPath,
        int pageNumber,
        double pdfWidth,
        double pdfHeight,
        int dpi,
        CancellationToken cancellationToken = default);
}

public sealed record OcrWordResult(string Text, PdfRect Bounds, double Confidence);

public sealed record OcrPageResult(IReadOnlyList<OcrWordResult> Words, double AverageConfidence);

public interface IOcrEngine
{
    bool IsAvailable { get; }

    string DisplayName { get; }

    Task<OcrPageResult> RecognizeAsync(
        RenderedPdfPage page,
        string language,
        double minimumConfidence,
        CancellationToken cancellationToken = default);
}

public sealed class OcrUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

public interface ILayoutAnalyzer
{
    IReadOnlyList<PdfBlock> Analyze(PdfPageAnalysis page, CancellationToken cancellationToken = default);
}

public interface IReadingOrderDetector
{
    IReadOnlyList<PdfBlock> Order(IReadOnlyList<PdfBlock> blocks, double pageWidth);
}

public interface IParagraphReconstructor
{
    IReadOnlyList<PdfBlock> Reconstruct(IReadOnlyList<PdfBlock> orderedBlocks, bool repairHyphenatedWords);
}

public interface IHeadingDetector
{
    HeadingDetectionResult Detect(PdfBlock block, double medianFontSize);
}

public sealed record HeadingDetectionResult(bool IsHeading, int Level);

public interface IHeaderFooterDetector
{
    HeaderFooterRemovalResult RemoveRepeatedBlocks(
        IReadOnlyList<PdfPageAnalysis> pages,
        ConversionOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record HeaderFooterRemovalResult(
    int HeadersRemoved,
    int FootersRemoved,
    int PageNumbersRemoved,
    IReadOnlyList<AnalysisWarning> Warnings);

public interface IBookDocumentBuilder
{
    BookDocument Build(
        IReadOnlyList<PdfPageAnalysis> pages,
        BookMetadata metadata,
        ConversionOptions options,
        IReadOnlyList<AnalysisWarning> warnings,
        CancellationToken cancellationToken = default);
}

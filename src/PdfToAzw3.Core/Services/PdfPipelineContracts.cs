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

using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public interface IEpubBuilder
{
    Task<string> BuildAsync(
        BookDocument book,
        ConversionOptions options,
        string outputPath,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IEpubValidator
{
    Task<EpubValidationResult> ValidateAsync(string epubPath, CancellationToken cancellationToken = default);
}

public sealed record EpubValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public interface IFixedLayoutPageBuilder
{
    Task PrepareAsync(
        string pdfPath,
        IReadOnlyList<PdfPageAnalysis> pages,
        BookDocument book,
        int dpi,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface ICalibreService
{
    string? FindExecutable(string? configuredPath = null);

    Task ConvertAsync(
        string epubPath,
        string azw3Path,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IEbookConversionService
{
    Task<ConversionOutput> ConvertAsync(
        PdfAnalysisResult analysis,
        ConversionOptions options,
        string azw3Path,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

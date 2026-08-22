using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed class EbookConversionService(
    IEpubBuilder epubBuilder,
    IEpubValidator epubValidator,
    ICalibreService calibreService,
    IAppLogger? logger = null,
    IFixedLayoutPageBuilder? fixedLayoutPageBuilder = null) : IEbookConversionService
{
    public async Task<ConversionOutput> ConvertAsync(
        PdfAnalysisResult analysis,
        ConversionOptions options,
        string azw3Path,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fullAzw3Path = Path.GetFullPath(azw3Path);
        var epubPath = Path.ChangeExtension(fullAzw3Path, ".epub");
        var effectiveOptions = options;
        if (RequiresFixedLayoutFallback(analysis, options))
        {
            effectiveOptions = options.Clone();
            effectiveOptions.Profile = ConversionProfile.FixedLayout;
            const string fallbackMessage = "PDF có trang scan chưa đọc được; đã tự chuyển sang Fixed Layout để giữ đủ từng trang.";
            if (!analysis.Book.Warnings.Any(warning => warning.Message.Equals(fallbackMessage, StringComparison.Ordinal)))
            {
                analysis.Book.Warnings.Add(new AnalysisWarning(fallbackMessage));
            }

            logger?.Warning(fallbackMessage);
            progress?.Report(new ConversionProgress(
                "Preparing Fixed Layout",
                0.80,
                Detail: fallbackMessage));
        }

        logger?.Info($"Conversion started: epub={epubPath}, azw3={fullAzw3Path}");
        try
        {
            if (effectiveOptions.Profile == ConversionProfile.FixedLayout)
            {
                if (fixedLayoutPageBuilder is null)
                {
                    throw new InvalidOperationException("Fixed Layout renderer chưa được cấu hình.");
                }

                await fixedLayoutPageBuilder.PrepareAsync(
                    analysis.File.FullPath,
                    analysis.Pages,
                    analysis.Book,
                    effectiveOptions.FixedLayoutPresentation == FixedLayoutPresentation.OverviewAndRegions
                        ? effectiveOptions.FixedLayoutRegionDpi
                        : effectiveOptions.FixedLayoutDpi,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            await epubBuilder.BuildAsync(analysis.Book, effectiveOptions, epubPath, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ConversionProgress("Validating EPUB", 0.935, Detail: "Kiểm tra EPUB trung gian"));
            var validation = await epubValidator.ValidateAsync(epubPath, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new InvalidDataException($"EPUB không hợp lệ: {string.Join("; ", validation.Errors)}");
            }

            await calibreService.ConvertAsync(epubPath, fullAzw3Path, effectiveOptions, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ConversionProgress("Finalizing", 1, Detail: "Hoàn tất chuyển đổi"));
            logger?.Info($"Conversion completed: epubBytes={new FileInfo(epubPath).Length}, azw3Bytes={new FileInfo(fullAzw3Path).Length}");
            return new ConversionOutput(
                epubPath,
                fullAzw3Path,
                new FileInfo(epubPath).Length,
                new FileInfo(fullAzw3Path).Length,
                analysis.Summary);
        }
        catch (Exception exception)
        {
            logger?.Error("Conversion failed.", exception);
            throw;
        }
    }

    private static bool RequiresFixedLayoutFallback(PdfAnalysisResult analysis, ConversionOptions options) =>
        options.Profile != ConversionProfile.FixedLayout &&
        analysis.Pages.Count > 0 &&
        analysis.Pages.Any(page => page.IsLikelyScanned && !page.HasNativeText && !page.OcrApplied);
}

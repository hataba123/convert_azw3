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
        logger?.Info($"Conversion started: epub={epubPath}, azw3={fullAzw3Path}");
        try
        {
            if (options.Profile == ConversionProfile.FixedLayout)
            {
                if (fixedLayoutPageBuilder is null)
                {
                    throw new InvalidOperationException("Fixed Layout renderer chưa được cấu hình.");
                }

                await fixedLayoutPageBuilder.PrepareAsync(
                    analysis.File.FullPath,
                    analysis.Pages,
                    analysis.Book,
                    options.FixedLayoutDpi,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            await epubBuilder.BuildAsync(analysis.Book, options, epubPath, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ConversionProgress("Validating EPUB", 0.935, Detail: "Kiểm tra EPUB trung gian"));
            var validation = await epubValidator.ValidateAsync(epubPath, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new InvalidDataException($"EPUB không hợp lệ: {string.Join("; ", validation.Errors)}");
            }

            await calibreService.ConvertAsync(epubPath, fullAzw3Path, options, progress, cancellationToken).ConfigureAwait(false);
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
}

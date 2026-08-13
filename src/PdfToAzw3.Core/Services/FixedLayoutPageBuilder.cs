using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed class FixedLayoutPageBuilder(IPdfPageRenderer pageRenderer) : IFixedLayoutPageBuilder
{
    public async Task PrepareAsync(
        string pdfPath,
        IReadOnlyList<PdfPageAnalysis> pages,
        BookDocument book,
        int dpi,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        book.FixedLayoutPages.Clear();
        book.Resources.RemoveAll(resource => resource.Id.StartsWith("fixed-page-", StringComparison.Ordinal));

        var orderedPages = pages.OrderBy(page => page.PageNumber).ToArray();
        if (orderedPages.Length == 0)
        {
            throw new InvalidDataException("Không có trang PDF để tạo Fixed Layout.");
        }

        for (var index = 0; index < orderedPages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = orderedPages[index];
            var rendered = await pageRenderer.RenderAsync(
                pdfPath,
                page.PageNumber - 1,
                page.Width,
                page.Height,
                dpi,
                cancellationToken).ConfigureAwait(false);
            var resourceId = $"fixed-page-{page.PageNumber:0000}";
            var fileName = $"{resourceId}.png";
            book.Resources.Add(new BookResource
            {
                Id = resourceId,
                FileName = fileName,
                MediaType = "image/png",
                Content = rendered.PngContent
            });
            book.FixedLayoutPages.Add(new FixedLayoutPage
            {
                PageNumber = page.PageNumber,
                PixelWidth = rendered.PixelWidth,
                PixelHeight = rendered.PixelHeight,
                ResourceId = resourceId,
                FileName = fileName
            });
            progress?.Report(new ConversionProgress(
                "Rasterizing Fixed Layout",
                0.80 + 0.12 * (index + 1) / orderedPages.Length,
                page.PageNumber,
                orderedPages.Length,
                $"Rasterize trang {page.PageNumber:N0} / {orderedPages.Length:N0}"));
        }
    }
}

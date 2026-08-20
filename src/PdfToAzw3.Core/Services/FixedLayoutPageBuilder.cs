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
            AddPage(book, page.PageNumber, rendered.PixelWidth, rendered.PixelHeight, rendered.PngContent, true, 0, "Trang tổng quan");
            if (dpi >= 250 && rendered.BgraContent is not null)
            {
                var overlap = Math.Clamp(0.05, 0, 0.2);
                var half = rendered.PixelHeight / 2;
                AddPage(book, page.PageNumber, rendered.PixelWidth, half + (int)(rendered.PixelHeight * overlap), Crop(rendered.BgraContent, rendered.PixelWidth, rendered.PixelHeight, 0, half + (int)(rendered.PixelHeight * overlap)), false, 1, "Vùng trên");
                AddPage(book, page.PageNumber, rendered.PixelWidth, rendered.PixelHeight - half + (int)(rendered.PixelHeight * overlap), Crop(rendered.BgraContent, rendered.PixelWidth, rendered.PixelHeight, Math.Max(0, half - (int)(rendered.PixelHeight * overlap)), rendered.PixelHeight - Math.Max(0, half - (int)(rendered.PixelHeight * overlap))), false, 2, "Vùng dưới");
            }
            progress?.Report(new ConversionProgress(
                "Rasterizing Fixed Layout",
                0.80 + 0.12 * (index + 1) / orderedPages.Length,
                page.PageNumber,
                orderedPages.Length,
                $"Rasterize trang {page.PageNumber:N0} / {orderedPages.Length:N0}"));
        }
    }

    private static void AddPage(BookDocument book, int sourcePage, int width, int height, byte[] content, bool overview, int region, string label)
    {
        var resourceId = $"fixed-page-{sourcePage:0000}-{region:00}";
        var fileName = $"{resourceId}.png";
        book.Resources.Add(new BookResource { Id = resourceId, FileName = fileName, MediaType = "image/png", Content = content });
        book.FixedLayoutPages.Add(new FixedLayoutPage { PageNumber = sourcePage, PixelWidth = width, PixelHeight = height, ResourceId = resourceId, FileName = fileName, IsOverview = overview, RegionIndex = region, Label = label });
    }

    private static byte[] Crop(byte[] bgra, int width, int totalHeight, int y, int height)
    {
        var cropped = new byte[width * height * 4];
        Buffer.BlockCopy(bgra, y * width * 4, cropped, 0, cropped.Length);
        return PngEncoder.EncodeBgra(cropped, width, height);
    }
}

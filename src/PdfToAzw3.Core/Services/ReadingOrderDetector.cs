using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed class ReadingOrderDetector : IReadingOrderDetector
{
    public IReadOnlyList<PdfBlock> Order(IReadOnlyList<PdfBlock> blocks, double pageWidth)
    {
        if (blocks.Count <= 1)
        {
            return blocks;
        }

        var orderedByX = blocks.OrderBy(block => block.Bounds.CenterX).ToArray();
        var largestGap = 0.0;
        var splitIndex = -1;
        for (var index = 1; index < orderedByX.Length; index++)
        {
            var gap = orderedByX[index].Bounds.CenterX - orderedByX[index - 1].Bounds.CenterX;
            if (gap > largestGap)
            {
                largestGap = gap;
                splitIndex = index;
            }
        }

        var hasTwoColumns = splitIndex > 0 && splitIndex < orderedByX.Length && largestGap >= pageWidth * 0.16;
        if (!hasTwoColumns)
        {
            return OrderTopToBottom(blocks);
        }

        var fullWidthHeadings = blocks
            .Where(block => IsFullWidthHeading(block, pageWidth))
            .OrderByDescending(block => block.Bounds.Top)
            .ToArray();
        if (fullWidthHeadings.Length == 0)
        {
            return OrderColumns(orderedByX[..splitIndex], orderedByX[splitIndex..]);
        }

        var content = blocks.Except(fullWidthHeadings).ToArray();
        var result = new List<PdfBlock>();
        var firstHeading = fullWidthHeadings[0];
        result.AddRange(OrderColumnBand(content.Where(block => block.Bounds.Top > firstHeading.Bounds.Top), pageWidth));
        for (var index = 0; index < fullWidthHeadings.Length; index++)
        {
            var heading = fullWidthHeadings[index];
            result.Add(heading);
            var nextHeadingTop = index + 1 < fullWidthHeadings.Length
                ? fullWidthHeadings[index + 1].Bounds.Top
                : double.NegativeInfinity;
            result.AddRange(OrderColumnBand(
                content.Where(block => block.Bounds.Top < heading.Bounds.Top && block.Bounds.Top > nextHeadingTop),
                pageWidth));
        }

        return result.Select((block, index) => SetReadingOrder(block, index)).ToArray();
    }

    private static IReadOnlyList<PdfBlock> OrderTopToBottom(IEnumerable<PdfBlock> blocks) => blocks
        .OrderByDescending(block => block.Bounds.Top)
        .ThenBy(block => block.Bounds.Left)
        .ToArray();

    private static IReadOnlyList<PdfBlock> OrderColumnBand(IEnumerable<PdfBlock> blocks, double pageWidth)
    {
        var band = blocks.ToArray();
        if (band.Length <= 1)
        {
            return band;
        }

        var midpoint = pageWidth / 2;
        return band
            .Where(block => block.Bounds.CenterX < midpoint)
            .OrderByDescending(block => block.Bounds.Top)
            .ThenBy(block => block.Bounds.Left)
            .Concat(band
                .Where(block => block.Bounds.CenterX >= midpoint)
                .OrderByDescending(block => block.Bounds.Top)
                .ThenBy(block => block.Bounds.Left))
            .ToArray();
    }

    private static IReadOnlyList<PdfBlock> OrderColumns(IEnumerable<PdfBlock> left, IEnumerable<PdfBlock> right) => left
        .OrderByDescending(block => block.Bounds.Top)
        .ThenBy(block => block.Bounds.Left)
        .Concat(right.OrderByDescending(block => block.Bounds.Top).ThenBy(block => block.Bounds.Left))
        .ToArray();

    private static bool IsFullWidthHeading(PdfBlock block, double pageWidth) =>
        block.Bounds.Width >= pageWidth * 0.62 &&
        (block.Alignment == TextAlignment.Center || block.IsBold || block.FontSize >= 15);

    private static PdfBlock SetReadingOrder(PdfBlock block, int readingOrder)
    {
        block.ReadingOrder = readingOrder;
        return block;
    }
}

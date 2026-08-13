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
            return blocks
                .OrderByDescending(block => block.Bounds.Top)
                .ThenBy(block => block.Bounds.Left)
                .Select((block, index) => SetReadingOrder(block, index))
                .ToArray();
        }

        var leftColumn = orderedByX[..splitIndex]
            .OrderByDescending(block => block.Bounds.Top)
            .ThenBy(block => block.Bounds.Left)
            .ToArray();
        var rightColumn = orderedByX[splitIndex..]
            .OrderByDescending(block => block.Bounds.Top)
            .ThenBy(block => block.Bounds.Left)
            .ToArray();

        return leftColumn
            .Concat(rightColumn)
            .Select((block, index) => SetReadingOrder(block, index))
            .ToArray();
    }

    private static PdfBlock SetReadingOrder(PdfBlock block, int readingOrder)
    {
        block.ReadingOrder = readingOrder;
        return block;
    }
}

using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

internal static class TableDetector
{
    public static bool TryCreate(PdfBlock block, out TableBlock table, bool technicalMode = false)
    {
        table = new TableBlock
        {
            BlockType = LayoutBlockType.Table,
            SourcePageNumber = block.PageNumber,
            UseImageFallback = false
        };

        if (block.Lines.Count < 3 || block.Lines.Any(line => line.Words.Count < 2))
        {
            return false;
        }

        var columnCount = block.Lines.Max(line => line.Words.Count);
        var maximumColumns = technicalMode ? 4 : 8;
        var maximumRows = technicalMode ? 40 : int.MaxValue;
        if (columnCount < 2 || columnCount > maximumColumns || block.Lines.Count > maximumRows || block.Lines.Any(line => line.Words.Count != columnCount))
        {
            return false;
        }

        var orderedRows = block.Lines
            .Select(line => line.Words.OrderBy(word => word.Bounds.Left).ToArray())
            .ToArray();
        var columnPositions = Enumerable.Range(0, columnCount)
            .Select(index => orderedRows.Select(row => row[index].Bounds.Left).Average())
            .ToArray();
        var positionTolerance = Math.Max(3, block.FontSize * 1.8);
        var stableColumns = orderedRows.All(row => row.Select((word, index) => Math.Abs(word.Bounds.Left - columnPositions[index]) <= positionTolerance).All(stable => stable));
        var separatedCells = orderedRows.SelectMany(row => row.Zip(row.Skip(1), (left, right) => right.Bounds.Left - left.Bounds.Right)).All(gap => gap >= block.FontSize * 1.2);
        if (!stableColumns || !separatedCells)
        {
            return false;
        }

        foreach (var row in orderedRows)
        {
            table.Rows.Add(row.Select(word => word.Text).ToList());
        }

        return true;
    }
}

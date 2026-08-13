using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed class HeuristicLayoutAnalyzer : ILayoutAnalyzer
{
    public IReadOnlyList<PdfBlock> Analyze(PdfPageAnalysis page, CancellationToken cancellationToken = default)
    {
        var blocks = new List<PdfBlock>(page.Lines.Count);
        foreach (var line in page.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            blocks.Add(new PdfBlock
            {
                BlockType = LayoutBlockType.Paragraph,
                Bounds = line.Bounds,
                Text = line.Text,
                FontSize = line.FontSize,
                FontName = line.FontName,
                IsBold = line.IsBold,
                IsItalic = line.IsItalic,
                Alignment = DetectAlignment(line, page.Width),
                PageNumber = page.PageNumber,
                Lines = { line }
            });
        }

        return blocks;
    }

    private static TextAlignment DetectAlignment(PdfLine line, double pageWidth)
    {
        if (line.Bounds.Width < pageWidth * 0.45 && Math.Abs(line.Bounds.CenterX - pageWidth / 2) < pageWidth * 0.08)
        {
            return TextAlignment.Center;
        }

        if (line.Bounds.Right > pageWidth * 0.90 && line.Bounds.Left > pageWidth * 0.30)
        {
            return TextAlignment.Right;
        }

        return TextAlignment.Left;
    }
}

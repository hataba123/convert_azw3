using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Text;

namespace PdfToAzw3.Core.Services;

public sealed class ParagraphReconstructor : IParagraphReconstructor
{
    public IReadOnlyList<PdfBlock> Reconstruct(IReadOnlyList<PdfBlock> orderedBlocks, bool repairHyphenatedWords)
    {
        var paragraphs = new List<PdfBlock>();
        PdfBlock? current = null;

        foreach (var line in orderedBlocks)
        {
            if (current is null || !ShouldMerge(current, line))
            {
                if (current is not null)
                {
                    FinalizeBlock(current, paragraphs, repairHyphenatedWords);
                }

                current = CloneBlock(line);
                continue;
            }

            current.Lines.AddRange(line.Lines);
            current.Text = TextNormalizer.JoinLines([current.Text, line.Text], repairHyphenatedWords);
            current.Bounds = new PdfRect(
                Math.Min(current.Bounds.Left, line.Bounds.Left),
                Math.Min(current.Bounds.Bottom, line.Bounds.Bottom),
                Math.Max(current.Bounds.Right, line.Bounds.Right),
                Math.Max(current.Bounds.Top, line.Bounds.Top));
            current.IsBold = current.IsBold && line.IsBold;
            current.IsItalic = current.IsItalic && line.IsItalic;
        }

        if (current is not null)
        {
            FinalizeBlock(current, paragraphs, repairHyphenatedWords);
        }

        return paragraphs;
    }

    private static bool ShouldMerge(PdfBlock previous, PdfBlock next)
    {
        if (previous.PageNumber != next.PageNumber || previous.Alignment == TextAlignment.Center || next.Alignment == TextAlignment.Center)
        {
            return false;
        }

        if (previous.FontSize > 0 && next.FontSize > 0 && Math.Abs(previous.FontSize - next.FontSize) > Math.Max(2, Math.Max(previous.FontSize, next.FontSize) * 0.18))
        {
            return false;
        }

        if (previous.IsBold != next.IsBold || previous.IsItalic != next.IsItalic)
        {
            return false;
        }

        if (previous.ReadingOrder + 1 != next.ReadingOrder)
        {
            return false;
        }

        var verticalGap = previous.Bounds.Bottom - next.Bounds.Top;
        var fontSize = Math.Max(6, Math.Max(previous.FontSize, next.FontSize));
        if (verticalGap > Math.Max(10, fontSize * 1.65))
        {
            return false;
        }

        var horizontalOverlap = Math.Min(previous.Bounds.Right, next.Bounds.Right) - Math.Max(previous.Bounds.Left, next.Bounds.Left);
        var minimumOverlap = Math.Min(previous.Bounds.Width, next.Bounds.Width) * 0.10;
        if (horizontalOverlap < minimumOverlap && Math.Abs(previous.Bounds.Left - next.Bounds.Left) > fontSize * 4)
        {
            return false;
        }

        var nextIsIndented = next.Bounds.Left - previous.Bounds.Left > fontSize * 1.1;
        var previousLooksComplete = previous.Text.EndsWith(".", StringComparison.Ordinal) ||
                                    previous.Text.EndsWith("?", StringComparison.Ordinal) ||
                                    previous.Text.EndsWith("!", StringComparison.Ordinal) ||
                                    previous.Text.EndsWith("\u3002", StringComparison.Ordinal) ||
                                    previous.Text.EndsWith("\uff01", StringComparison.Ordinal) ||
                                    previous.Text.EndsWith("\uff1f", StringComparison.Ordinal);
        return !(nextIsIndented && previousLooksComplete);
    }

    private static PdfBlock CloneBlock(PdfBlock source)
    {
        var clone = new PdfBlock
        {
            BlockType = source.BlockType,
            Bounds = source.Bounds,
            Text = source.Text,
            FontSize = source.FontSize,
            FontName = source.FontName,
            IsBold = source.IsBold,
            IsItalic = source.IsItalic,
            Alignment = source.Alignment,
            ReadingOrder = source.ReadingOrder,
            PageNumber = source.PageNumber
        };
        return clone.WithLines(source.Lines);
    }

    private static void FinalizeBlock(PdfBlock block, ICollection<PdfBlock> target, bool repairHyphenatedWords)
    {
        block.Text = TextNormalizer.Normalize(block.Text);
        if (block.Lines.Count > 1)
        {
            block.Text = TextNormalizer.JoinLines(block.Lines.Select(line => line.Text).ToArray(), repairHyphenatedWords);
        }

        if (block.Text.Length > 0)
        {
            target.Add(block);
        }
    }
}

internal static class PdfBlockExtensions
{
    public static PdfBlock WithLines(this PdfBlock block, IEnumerable<PdfLine> lines)
    {
        block.Lines.AddRange(lines);
        return block;
    }
}

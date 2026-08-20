using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Text;

namespace PdfToAzw3.Core.Services;

internal static class CrossPageParagraphJoiner
{
    private static readonly char[] TerminalPunctuation = ['.', '!', '?', '…', '。', '！', '？', ':'];

    public static (int Joined, int Suspected) Join(
        IReadOnlyList<PdfPageAnalysis> pages,
        ConversionOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.SmartReflow || options.Profile is ConversionProfile.FixedLayout or ConversionProfile.PreserveLayout)
        {
            return (0, 0);
        }

        if (options.Profile is ConversionProfile.KindleAuto or ConversionProfile.KindleTechnicalBook &&
            !IsPredominantlySingleColumn(pages))
        {
            return (0, 0);
        }

        var joined = 0;
        var suspected = 0;
        for (var index = 0; index < pages.Count - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPage = pages[index];
            var nextPage = pages[index + 1];
            var previous = currentPage.Blocks.LastOrDefault();
            var next = nextPage.Blocks.FirstOrDefault();
            if (previous is null || next is null || !LooksLikeContinuation(previous, next))
            {
                if (previous is not null && next is not null && CouldBeSplit(previous, next))
                {
                    suspected++;
                }

                continue;
            }

            previous.Text = TextNormalizer.JoinLines([previous.Text, next.Text], options.RepairHyphenatedWords);
            previous.Lines.AddRange(next.Lines);
            previous.EndPageNumber = next.EndPageNumber == 0 ? next.PageNumber : next.EndPageNumber;
            previous.WasJoinedAcrossPage = true;
            currentPage.Blocks[^1] = previous;
            nextPage.Blocks.RemoveAt(0);
            joined++;
        }

        return (joined, suspected);
    }

    public static bool IsPredominantlySingleColumn(IReadOnlyList<PdfPageAnalysis> pages)
    {
        var pagesWithParallelColumns = pages.Count(page => page.Blocks.Any(left => page.Blocks.Any(right =>
            !ReferenceEquals(left, right) &&
            left.Bounds.Right < right.Bounds.Left - page.Width * 0.04 &&
            Math.Min(left.Bounds.Top, right.Bounds.Top) > Math.Max(left.Bounds.Bottom, right.Bounds.Bottom))));
        return pagesWithParallelColumns < Math.Max(2, (int)Math.Ceiling(pages.Count * 0.2));
    }

    private static bool LooksLikeContinuation(PdfBlock previous, PdfBlock next)
    {
        if (!CouldBeSplit(previous, next) || EndsSentence(previous.Text))
        {
            return false;
        }

        var first = next.Text.TrimStart().FirstOrDefault();
        return char.IsLower(first) || previous.Text.EndsWith("-", StringComparison.Ordinal);
    }

    private static bool CouldBeSplit(PdfBlock previous, PdfBlock next)
    {
        if (previous.BlockType != LayoutBlockType.Paragraph || next.BlockType != LayoutBlockType.Paragraph ||
            previous.Alignment == TextAlignment.Center || next.Alignment == TextAlignment.Center ||
            previous.IsBold != next.IsBold || previous.IsItalic != next.IsItalic ||
            LooksStructural(previous) || LooksStructural(next))
        {
            return false;
        }

        var previousText = previous.Text.Trim();
        var nextText = next.Text.Trim();
        if (previousText.Length == 0 || nextText.Length == 0 || IsSceneBreak(previousText) || IsSceneBreak(nextText) ||
            StartsListOrDialogue(nextText))
        {
            return false;
        }

        return previous.FontSize <= 0 || next.FontSize <= 0 ||
               Math.Abs(previous.FontSize - next.FontSize) <= Math.Max(2, Math.Max(previous.FontSize, next.FontSize) * 0.18);
    }

    private static bool LooksStructural(PdfBlock block)
    {
        var text = block.Text.TrimStart();
        return block.FontName.Contains("mono", StringComparison.OrdinalIgnoreCase) ||
               block.FontName.Contains("courier", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Hình", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Bảng", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Chương", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase) ||
               (block.Lines.Count >= 2 && block.Lines.Count(line => StartsListOrDialogue(line.Text)) >= Math.Ceiling(block.Lines.Count * 0.6)) ||
               TableDetector.TryCreate(block, out _, technicalMode: true);
    }

    private static bool EndsSentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && TerminalPunctuation.Contains(trimmed[^1]);
    }

    private static bool StartsListOrDialogue(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('—') || trimmed.StartsWith('–') || trimmed.StartsWith("- ", StringComparison.Ordinal) ||
               trimmed.StartsWith("•", StringComparison.Ordinal) ||
               (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && (trimmed[1] == '.' || trimmed[1] == ')'));
    }

    private static bool IsSceneBreak(string text)
    {
        var compact = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        return compact is "***" or "---" or "—" or "*";
    }
}

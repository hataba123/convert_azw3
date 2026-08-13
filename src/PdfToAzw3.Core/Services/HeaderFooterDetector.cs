using System.Text.RegularExpressions;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Text;

namespace PdfToAzw3.Core.Services;

public sealed partial class HeaderFooterDetector : IHeaderFooterDetector
{
    public HeaderFooterRemovalResult RemoveRepeatedBlocks(
        IReadOnlyList<PdfPageAnalysis> pages,
        ConversionOptions options,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<AnalysisWarning>();
        var headerCandidates = pages.SelectMany(page => page.Blocks
                .Where(block => block.Bounds.Top >= page.Height * 0.86)
                .Select(block => new Candidate(page, block, false)))
            .ToArray();
        var footerCandidates = pages.SelectMany(page => page.Blocks
                .Where(block => block.Bounds.Bottom <= page.Height * 0.14)
                .Select(block => new Candidate(page, block, true)))
            .ToArray();

        var headersRemoved = options.RemoveRepeatedHeaders ? RemoveRepeated(headerCandidates, pages.Count, LayoutBlockType.Header) : 0;
        var footersRemoved = options.RemoveRepeatedFooters ? RemoveRepeated(footerCandidates, pages.Count, LayoutBlockType.Footer) : 0;
        var pageNumbersRemoved = options.RemovePageNumbers ? RemovePageNumbers(footerCandidates) : 0;

        if (headerCandidates.Length > 0 && headersRemoved == 0 && headerCandidates.Length >= pages.Count / 4)
        {
            warnings.Add(new AnalysisWarning("Có nội dung lặp gần đầu trang nhưng chưa đủ chắc chắn để tự động xóa."));
        }

        return new HeaderFooterRemovalResult(headersRemoved, footersRemoved, pageNumbersRemoved, warnings);
    }

    private static int RemoveRepeated(IReadOnlyList<Candidate> candidates, int pageCount, LayoutBlockType blockType)
    {
        var minimumOccurrences = Math.Max(3, (int)Math.Ceiling(pageCount * 0.15));
        var repeatedKeys = candidates
            .GroupBy(candidate => NormalizeKey(candidate.Block.Text))
            .Where(group => group.Key.Length > 0 && group.Select(item => item.Page.PageNumber).Distinct().Count() >= minimumOccurrences)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var removed = 0;
        foreach (var candidate in candidates)
        {
            if (!repeatedKeys.Contains(NormalizeKey(candidate.Block.Text)))
            {
                continue;
            }

            candidate.Block.BlockType = blockType;
            candidate.Page.Blocks.Remove(candidate.Block);
            removed++;
        }

        return removed;
    }

    private static int RemovePageNumbers(IReadOnlyList<Candidate> candidates)
    {
        var removed = 0;
        foreach (var candidate in candidates)
        {
            if (!PageNumberRegex().IsMatch(candidate.Block.Text.Trim()))
            {
                continue;
            }

            candidate.Block.BlockType = LayoutBlockType.PageNumber;
            candidate.Page.Blocks.Remove(candidate.Block);
            removed++;
        }

        return removed;
    }

    private static string NormalizeKey(string value) =>
        TextNormalizer.Normalize(value).ToLowerInvariant();

    [GeneratedRegex(@"^[\[\(]?\s*\d{1,5}\s*[\]\)]?$", RegexOptions.CultureInvariant)]
    private static partial Regex PageNumberRegex();

    private sealed record Candidate(PdfPageAnalysis Page, PdfBlock Block, bool IsFooter);
}

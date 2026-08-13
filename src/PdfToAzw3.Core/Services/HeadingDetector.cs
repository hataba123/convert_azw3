using System.Text.RegularExpressions;
using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed partial class HeadingDetector : IHeadingDetector
{
    public HeadingDetectionResult Detect(PdfBlock block, double medianFontSize)
    {
        var text = block.Text.Trim();
        if (text.Length == 0 || text.Length > 180 || text.Contains('\n'))
        {
            return new HeadingDetectionResult(false, 0);
        }

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var numbered = NumberedHeadingRegex().IsMatch(text) || ChapterHeadingRegex().IsMatch(text);
        var allCaps = text.Any(char.IsLetter) && text == text.ToUpperInvariant() && wordCount <= 14;
        var visuallyProminent = medianFontSize > 0 && block.FontSize >= medianFontSize * 1.16;
        var styled = block.IsBold && block.FontSize >= medianFontSize * 0.95;
        var likelyHeading = (numbered || allCaps || visuallyProminent || styled) && wordCount <= 36;

        if (!likelyHeading || LooksLikeSentence(text))
        {
            return new HeadingDetectionResult(false, 0);
        }

        var level = GetLevel(text, block.FontSize, medianFontSize);
        return new HeadingDetectionResult(true, level);
    }

    private static int GetLevel(string text, double fontSize, double medianFontSize)
    {
        var match = NumberedHeadingRegex().Match(text);
        if (match.Success)
        {
            var prefix = match.Groups["number"].Value.TrimEnd('.', ')');
            var depth = prefix.Count(character => character == '.') + 1;
            return Math.Clamp(depth, 1, 3);
        }

        if (ChapterHeadingRegex().IsMatch(text) || fontSize >= medianFontSize * 1.35)
        {
            return 1;
        }

        return fontSize >= medianFontSize * 1.18 ? 2 : 3;
    }

    private static bool LooksLikeSentence(string text)
    {
        if (text.EndsWith(".", StringComparison.Ordinal) && !ChapterHeadingRegex().IsMatch(text))
        {
            return true;
        }

        return text.Count(character => character == ',') >= 2;
    }

    [GeneratedRegex(@"^(?<number>(?:\d+(?:\.\d+)*|[IVXLCDM]+|[A-Z])(?:[.)])?)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedHeadingRegex();

    [GeneratedRegex(@"^(?:chapter|chapter\s+\d+|part|section|chương|phần|mục)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterHeadingRegex();
}

using PdfToAzw3.Core.Models;
using UglyToad.PdfPig.Content;

namespace PdfToAzw3.Core.Services;

public sealed class PdfPageAnalyzer : IPdfPageAnalyzer
{
    public PdfPageAnalysis Analyze(Page page, int pageNumber, CancellationToken cancellationToken = default)
    {
        var result = new PdfPageAnalysis
        {
            PageNumber = pageNumber,
            Width = page.Width,
            Height = page.Height
        };

        var wordIndex = 0;
        foreach (var word in page.GetWords())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var letters = word.Letters;
            var firstLetter = letters.FirstOrDefault();
            var fontSize = letters.Count == 0 ? 0 : letters.Average(letter => letter.FontSize);
            var fontName = firstLetter?.FontName ?? string.Empty;
            result.Words.Add(new PdfWord(
                word.Text,
                new PdfRect(word.BoundingBox.Left, word.BoundingBox.Bottom, word.BoundingBox.Right, word.BoundingBox.Top),
                fontSize,
                fontName,
                IsBold(fontName),
                IsItalic(fontName),
                wordIndex++));
        }

        result.HasText = result.Words.Count > 0;
        result.IsLikelyScanned = !result.HasText;
        result.Lines.AddRange(BuildLines(result.Words));
        return result;
    }

    private static IReadOnlyList<PdfLine> BuildLines(IReadOnlyList<PdfWord> words)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var medianFontSize = Median(words.Select(word => word.FontSize).Where(size => size > 0).ToArray());
        var lineTolerance = Math.Max(2.2, medianFontSize * 0.45);
        var lines = new List<PdfLine>();

        foreach (var word in words.OrderByDescending(word => word.Bounds.CenterY).ThenBy(word => word.Bounds.Left))
        {
            var candidate = lines
                .Where(line => Math.Abs(line.Bounds.CenterY - word.Bounds.CenterY) <= lineTolerance)
                .OrderBy(line => Math.Abs(line.Bounds.CenterY - word.Bounds.CenterY))
                .FirstOrDefault();

            if (candidate is null)
            {
                candidate = new PdfLine();
                lines.Add(candidate);
            }

            candidate.Words.Add(word);
        }

        foreach (var line in lines)
        {
            line.Words.Sort((left, right) => left.Bounds.Left.CompareTo(right.Bounds.Left));
        }

        return lines
            .OrderByDescending(line => line.Bounds.CenterY)
            .ThenBy(line => line.Bounds.Left)
            .ToArray();
    }

    private static bool IsBold(string fontName) =>
        fontName.Contains("bold", StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("black", StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("heavy", StringComparison.OrdinalIgnoreCase);

    private static bool IsItalic(string fontName) =>
        fontName.Contains("italic", StringComparison.OrdinalIgnoreCase) ||
        fontName.Contains("oblique", StringComparison.OrdinalIgnoreCase);

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
            : sorted[sorted.Length / 2];
    }
}

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
        ExtractImages(page, pageNumber, result);
        return result;
    }

    private static void ExtractImages(Page page, int pageNumber, PdfPageAnalysis result)
    {
        foreach (var image in page.GetImages())
        {
            byte[] content;
            string mediaType;
            string extension;
            if (image.TryGetPng(out var png))
            {
                content = png;
                mediaType = "image/png";
                extension = "png";
            }
            else
            {
                content = image.RawMemory.ToArray();
                if (content.Length == 0)
                {
                    continue;
                }

                (mediaType, extension) = DetectImageType(content);
                if (string.IsNullOrWhiteSpace(mediaType))
                {
                    continue;
                }
            }

            result.Images.Add(new PdfExtractedImage
            {
                Bounds = new PdfRect(image.Bounds.Left, image.Bounds.Bottom, image.Bounds.Right, image.Bounds.Top),
                Content = content,
                MediaType = mediaType,
                Extension = extension,
                PageNumber = pageNumber
            });
        }
    }

    private static (string MediaType, string Extension) DetectImageType(byte[] content)
    {
        if (content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return ("image/png", "png");
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return ("image/jpeg", "jpg");
        }

        if (content.Length >= 12 && content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46 && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50)
        {
            return ("image/webp", "webp");
        }

        return (string.Empty, string.Empty);
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

using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

internal static partial class ConversionRecommendationAnalyzer
{
    public static ConversionRecommendation Analyze(IReadOnlyList<PdfPageAnalysis> pages, PdfDocumentKind kind)
    {
        var reasons = new List<string>();
        var scanned = pages.Count(page => page.IsLikelyScanned || page.OcrApplied);
        var twoColumns = pages.Count(page => HasParallelColumns(page));
        var complex = pages.Count(IsComplexPage);
        var metrics = MeasureStructure(pages);
        var singleColumn = CrossPageParagraphJoiner.IsPredominantlySingleColumn(pages);
        var usableOcr = HasUsableOcr(pages);

        if (scanned > 0)
        {
            reasons.Add($"{scanned:N0} trang scan/OCR");
        }
        if (twoColumns > 0)
        {
            reasons.Add($"{twoColumns:N0} trang nhiều cột");
        }
        if (metrics.Tables > 0)
        {
            reasons.Add($"có {metrics.Tables:N0} bảng");
        }

        if (kind == PdfDocumentKind.Scanned && !usableOcr)
        {
            reasons.Add("OCR yếu hoặc thiếu text; nên giữ nguyên trang");
            return new ConversionRecommendation(ConversionProfile.FixedLayout, 0.88, reasons);
        }

        if (kind == PdfDocumentKind.Scanned && usableOcr)
        {
            reasons.Add("scan một cột có OCR đủ rõ để tái cấu trúc");
            return new ConversionRecommendation(ConversionProfile.KindleTechnicalBook, 0.75, reasons);
        }

        if (complex >= Math.Max(1, (int)Math.Ceiling(pages.Count * 0.30)))
        {
            reasons.Add("nhiều hình hoặc bố cục hỗn hợp; nên giữ nguyên trang");
            return new ConversionRecommendation(ConversionProfile.FixedLayout, 0.84, reasons);
        }

        if (twoColumns > 0 || !singleColumn || metrics.IsStructuredDocument)
        {
            if (singleColumn && metrics.IsStructuredDocument)
            {
                reasons.Add("văn bản một cột có cấu trúc");
            }
            else
            {
                reasons.Add("nội dung phù hợp tái cấu trúc giáo trình");
            }

            if (metrics.Headings > 0)
            {
                reasons.Add($"{metrics.Headings:N0} heading/mục");
            }

            if (metrics.Lists > 0 || metrics.Captions > 0 || metrics.Footnotes > 0)
            {
                reasons.Add("có danh sách, chú thích hoặc caption");
            }

            return new ConversionRecommendation(ConversionProfile.KindleTechnicalBook, kind == PdfDocumentKind.Scanned ? 0.75 : 0.80, reasons);
        }

        reasons.Add("văn xuôi một cột, ít cấu trúc phụ");
        return new ConversionRecommendation(ConversionProfile.KindleNovel, 0.78, reasons);
    }

    private static bool IsComplexPage(PdfPageAnalysis page) =>
        page.Images.Count >= 2 || (page.Images.Count > 0 && HasParallelColumns(page)) ||
        (!page.HasText && page.Images.Count > 0);

    private static bool HasParallelColumns(PdfPageAnalysis page) => page.Blocks.Any(left => page.Blocks.Any(right =>
        !ReferenceEquals(left, right) && left.Bounds.Right < right.Bounds.Left - page.Width * 0.04 &&
        Math.Min(left.Bounds.Top, right.Bounds.Top) > Math.Max(left.Bounds.Bottom, right.Bounds.Bottom)));

    private static bool HasUsableOcr(IReadOnlyList<PdfPageAnalysis> pages)
    {
        var ocrPages = pages.Where(page => page.OcrApplied).ToArray();
        return ocrPages.Length > 0 &&
               ocrPages.All(page => page.OcrConfidence >= 0.70 && page.Words.Count >= 12) &&
               CrossPageParagraphJoiner.IsPredominantlySingleColumn(pages);
    }

    private static DocumentStructureMetrics MeasureStructure(IReadOnlyList<PdfPageAnalysis> pages)
    {
        var blocks = pages.SelectMany(page => page.Blocks).ToArray();
        var medianFontSize = Median(blocks.Select(block => block.FontSize).Where(size => size > 0).ToArray());
        var headings = blocks.Count(block => IsLikelyHeading(block, medianFontSize));
        var lists = blocks.Count(IsLikelyList);
        var tables = blocks.Count(block => TableDetector.TryCreate(block, out _, technicalMode: true));
        var captions = blocks.Count(IsLikelyCaption);
        var footnotes = blocks.Count(block => FootnotePattern().IsMatch(block.Text));
        var code = blocks.Count(block => block.FontName.Contains("mono", StringComparison.OrdinalIgnoreCase) ||
                                        block.FontName.Contains("courier", StringComparison.OrdinalIgnoreCase));
        var images = pages.Sum(page => page.Images.Count);
        var averageWords = blocks.Length == 0 ? 0 : blocks.Average(block => block.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var structureScore = headings * 1.5 + lists * 1.5 + tables * 2 + captions * 1.5 + footnotes + code * 1.5 + images * 1.5;
        return new DocumentStructureMetrics(
            headings,
            lists,
            tables,
            captions,
            footnotes,
            structureScore >= Math.Max(3, blocks.Length * 0.18) || (headings >= 2 && averageWords < 35));
    }

    private static bool IsLikelyHeading(PdfBlock block, double medianFontSize) =>
        block.Text.Length <= 120 &&
        (block.IsBold || (medianFontSize > 0 && block.FontSize >= medianFontSize * 1.16)) &&
        !IsLikelyList(block) && !FootnotePattern().IsMatch(block.Text);

    private static bool IsLikelyList(PdfBlock block) => block.Lines.Count >= 2 &&
        block.Lines.Count(line => ListMarkerPattern().IsMatch(line.Text)) >= Math.Ceiling(block.Lines.Count * 0.6);

    private static bool IsLikelyCaption(PdfBlock block) => CaptionPattern().IsMatch(block.Text);

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

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?:[-*•▪‣]|\d+[.)])\s+")]
    private static partial System.Text.RegularExpressions.Regex ListMarkerPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?:hình|figure|bảng|table)\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex CaptionPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?:\d{1,2}|[¹²³⁴⁵⁶⁷⁸⁹⁰])\s*[).:]?\s+.+")]
    private static partial System.Text.RegularExpressions.Regex FootnotePattern();

    private sealed record DocumentStructureMetrics(
        int Headings,
        int Lists,
        int Tables,
        int Captions,
        int Footnotes,
        bool IsStructuredDocument);
}

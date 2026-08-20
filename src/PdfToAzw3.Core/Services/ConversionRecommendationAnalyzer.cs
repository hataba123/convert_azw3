using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

internal static class ConversionRecommendationAnalyzer
{
    public static ConversionRecommendation Analyze(IReadOnlyList<PdfPageAnalysis> pages, PdfDocumentKind kind)
    {
        var reasons = new List<string>();
        var scanned = pages.Count(page => page.IsLikelyScanned || page.OcrApplied);
        var complex = pages.Count(IsComplexPage);
        var twoColumns = pages.Count(page => HasParallelColumns(page));
        var tables = pages.Sum(page => page.Blocks.Count(block => block.Lines.Count >= 3));

        if (scanned > 0)
        {
            reasons.Add($"{scanned:N0} trang scan/OCR");
        }
        if (twoColumns > 0)
        {
            reasons.Add($"{twoColumns:N0} trang nhiều cột");
        }
        if (tables > 0)
        {
            reasons.Add($"phát hiện {tables:N0} vùng bảng/biểu đồ");
        }

        if (kind == PdfDocumentKind.Scanned || complex >= Math.Max(1, (int)Math.Ceiling(pages.Count * 0.30)))
        {
            reasons.Add("bố cục cần ưu tiên giữ nguyên trang");
            return new ConversionRecommendation(ConversionProfile.FixedLayout, 0.84, reasons);
        }

        if (twoColumns > 0 || tables > 0)
        {
            reasons.Add("nội dung phù hợp tái cấu trúc giáo trình");
            return new ConversionRecommendation(ConversionProfile.KindleTechnicalBook, 0.78, reasons);
        }

        reasons.Add("nội dung chủ yếu là văn bản một cột");
        return new ConversionRecommendation(ConversionProfile.KindleNovel, 0.72, reasons);
    }

    private static bool IsComplexPage(PdfPageAnalysis page) =>
        page.Images.Count >= 2 || HasParallelColumns(page) || page.Blocks.Count(block => block.Lines.Count >= 3) >= 2;

    private static bool HasParallelColumns(PdfPageAnalysis page) => page.Blocks.Any(left => page.Blocks.Any(right =>
        !ReferenceEquals(left, right) && left.Bounds.Right < right.Bounds.Left - page.Width * 0.04 &&
        Math.Min(left.Bounds.Top, right.Bounds.Top) > Math.Max(left.Bounds.Bottom, right.Bounds.Bottom)));
}

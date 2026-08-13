using System.Globalization;
using PdfToAzw3.Core.Models;
using UglyToad.PdfPig;

namespace PdfToAzw3.Core.Services;

public sealed class PdfPigDocumentReader(
    IPdfPageAnalyzer pageAnalyzer,
    ILayoutAnalyzer layoutAnalyzer,
    IReadingOrderDetector readingOrderDetector,
    IParagraphReconstructor paragraphReconstructor,
    IHeaderFooterDetector headerFooterDetector,
    IBookDocumentBuilder bookDocumentBuilder) : IPdfDocumentReader
{
    public Task<PdfAnalysisResult> AnalyzeAsync(
        string path,
        BookMetadata metadata,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => AnalyzeCore(path, metadata, options, progress, cancellationToken),
            cancellationToken);
    }

    private PdfAnalysisResult AnalyzeCore(
        string path,
        BookMetadata metadata,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Không tìm thấy tệp PDF.", path);
        }

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || fileInfo.Length == 0)
        {
            throw new InvalidDataException("Tệp đầu vào phải là PDF không rỗng.");
        }

        var pages = new List<PdfPageAnalysis>();
        var warnings = new List<AnalysisWarning>();

        progress?.Report(new ConversionProgress("Loading PDF", 0.02, Detail: fileInfo.Name));

        using (var document = PdfDocument.Open(path))
        {
            var totalPages = document.NumberOfPages;
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ConversionProgress(
                    "Analyzing page",
                    0.05 + 0.55 * (pageNumber - 1) / Math.Max(1, totalPages),
                    pageNumber,
                    totalPages,
                    $"Đọc glyph và tọa độ trang {pageNumber.ToString("N0", CultureInfo.CurrentCulture)} / {totalPages.ToString("N0", CultureInfo.CurrentCulture)}"));

                var page = document.GetPage(pageNumber);
                var pageAnalysis = pageAnalyzer.Analyze(page, pageNumber, cancellationToken);
                var lineBlocks = layoutAnalyzer.Analyze(pageAnalysis, cancellationToken);
                var orderedBlocks = readingOrderDetector.Order(lineBlocks, pageAnalysis.Width);
                var paragraphBlocks = paragraphReconstructor.Reconstruct(orderedBlocks, options.RepairHyphenatedWords);
                pageAnalysis.Blocks.AddRange(paragraphBlocks);
                pages.Add(pageAnalysis);
            }
        }

        progress?.Report(new ConversionProgress("Detecting repeated headers and footers", 0.66));
        var removal = headerFooterDetector.RemoveRepeatedBlocks(pages, options, cancellationToken);
        warnings.AddRange(removal.Warnings);

        progress?.Report(new ConversionProgress("Building semantic book model", 0.78));
        var book = bookDocumentBuilder.Build(pages, metadata, options, warnings, cancellationToken);
        var kind = ClassifyDocument(pages);
        var textPages = pages.Count(page => page.HasText);
        var imageCount = pages.Sum(page => page.Blocks.Count(block => block.BlockType == LayoutBlockType.Image));
        var paragraphs = book.Chapters.Sum(chapter => chapter.Blocks.Count(block => block.BlockType == LayoutBlockType.Paragraph));
        var quality = CalculateQuality(pages, book, kind);

        if (kind == PdfDocumentKind.Scanned && !options.EnableOcrFallback)
        {
            warnings.Add(new AnalysisWarning("PDF này có vẻ là tài liệu scan; hãy bật OCR Mode để thử nhận dạng văn bản.", Severity: "Error"));
        }
        else if (kind == PdfDocumentKind.Mixed)
        {
            warnings.Add(new AnalysisWarning("Một số trang có rất ít text layer và có thể cần OCR."));
        }

        var summary = new AnalysisSummary
        {
            Pages = pages.Count,
            Chapters = book.Chapters.Count,
            Images = imageCount,
            HeadersRemoved = removal.HeadersRemoved,
            FootersRemoved = removal.FootersRemoved,
            PageNumbersRemoved = removal.PageNumbersRemoved,
            Paragraphs = paragraphs,
            DocumentKind = kind,
            Quality = quality
        };

        progress?.Report(new ConversionProgress("Analysis complete", 1, Detail: $"{textPages} trang có text layer"));
        var result = new PdfAnalysisResult
        {
            File = new PdfFileInfo(fileInfo.FullName, fileInfo.Name, fileInfo.Length, pages.Count, kind),
            Summary = summary,
            Book = book
        };
        result.Pages.AddRange(pages);
        result.Warnings.AddRange(warnings);
        return result;
    }

    private static PdfDocumentKind ClassifyDocument(IReadOnlyList<PdfPageAnalysis> pages)
    {
        if (pages.Count == 0)
        {
            return PdfDocumentKind.Unknown;
        }

        var pagesWithText = pages.Count(page => page.HasText);
        if (pagesWithText == 0)
        {
            return PdfDocumentKind.Scanned;
        }

        if (pagesWithText == pages.Count)
        {
            return PdfDocumentKind.Text;
        }

        return PdfDocumentKind.Mixed;
    }

    private static ConversionQuality CalculateQuality(
        IReadOnlyList<PdfPageAnalysis> pages,
        BookDocument book,
        PdfDocumentKind kind)
    {
        var textConfidence = kind switch
        {
            PdfDocumentKind.Text => 0.98,
            PdfDocumentKind.Mixed => 0.70,
            PdfDocumentKind.Scanned => 0.15,
            _ => 0.35
        };
        var readingConfidence = pages.Count == 0 ? 0.0 : pages.Average(page => page.Blocks.Count > 0 ? 0.90 : 0.35);
        var paragraphConfidence = book.Chapters.Sum(chapter => chapter.Blocks.Count(block => block is ParagraphBlock)) == 0 ? 0.20 : 0.90;
        var headingConfidence = book.Chapters.Count > 1 ? 0.88 : 0.68;
        var imageConfidence = 0.75;
        var score = (int)Math.Round((textConfidence * 0.35 + readingConfidence * 0.20 + paragraphConfidence * 0.20 + headingConfidence * 0.15 + imageConfidence * 0.10) * 100);
        return new ConversionQuality
        {
            Score = Math.Clamp(score, 0, 100),
            TextExtractionConfidence = textConfidence,
            ReadingOrderConfidence = readingConfidence,
            ParagraphConfidence = paragraphConfidence,
            HeadingConfidence = headingConfidence,
            ImageConfidence = imageConfidence,
            OcrPercentage = kind == PdfDocumentKind.Scanned ? 100 : kind == PdfDocumentKind.Mixed ? 35 : 0
        };
    }
}

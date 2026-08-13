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
    IBookDocumentBuilder bookDocumentBuilder,
    IAppLogger? logger = null,
    IPdfPageRenderer? pageRenderer = null,
    IOcrEngine? ocrEngine = null) : IPdfDocumentReader
{
    public Task<PdfAnalysisResult> AnalyzeAsync(
        string path,
        BookMetadata metadata,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => AnalyzeCoreAsync(path, metadata, options, progress, cancellationToken),
            cancellationToken);
    }

    private async Task<PdfAnalysisResult> AnalyzeCoreAsync(
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
        logger?.Info($"Conversion analysis started: {fileInfo.Name}, size={fileInfo.Length} bytes");

        using (var document = PdfDocument.Open(path))
        {
            ApplyDocumentMetadata(document.Information, metadata, fileInfo);
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
                if (!pageAnalysis.HasNativeText && options.EnableOcrFallback)
                {
                    await TryApplyOcrAsync(
                        path,
                        pageAnalysis,
                        options,
                        warnings,
                        progress,
                        totalPages,
                        cancellationToken).ConfigureAwait(false);
                }

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
        var nativeTextPages = pages.Count(page => page.HasNativeText);
        var ocrPages = pages.Count(page => page.OcrApplied);
        var imageCount = pages.Sum(page => page.Blocks.Count(block => block.BlockType == LayoutBlockType.Image));
        var paragraphs = book.Chapters.Sum(chapter => chapter.Blocks.Count(block => block.BlockType == LayoutBlockType.Paragraph));
        var quality = CalculateQuality(pages, book, kind);

        if (kind == PdfDocumentKind.Scanned && !options.EnableOcrFallback)
        {
            warnings.Add(new AnalysisWarning("PDF này có vẻ là tài liệu scan; hãy bật OCR fallback để nhận dạng text layer.", Severity: "Error"));
        }
        else if (kind == PdfDocumentKind.Mixed && pages.Any(page => !page.HasNativeText && !page.OcrApplied))
        {
            warnings.Add(new AnalysisWarning("Một số trang không có text layer và chưa được OCR; nội dung các trang này có thể thiếu."));
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
            OcrPages = ocrPages,
            DocumentKind = kind,
            Quality = quality
        };

        logger?.Info($"PDF analysis completed: pages={summary.Pages}, chapters={summary.Chapters}, paragraphs={summary.Paragraphs}, images={summary.Images}, ocrPages={summary.OcrPages}, quality={summary.Quality.Score}");
        foreach (var warning in warnings)
        {
            logger?.Warning(warning.Message);
        }

        progress?.Report(new ConversionProgress("Analysis complete", 1, Detail: $"{nativeTextPages} trang native text, {ocrPages} trang OCR"));
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

    private static void ApplyDocumentMetadata(UglyToad.PdfPig.Content.DocumentInformation information, BookMetadata metadata, FileInfo fileInfo)
    {
        var defaultTitle = Path.GetFileNameWithoutExtension(fileInfo.Name);
        if (!string.IsNullOrWhiteSpace(information.Title) &&
            (string.IsNullOrWhiteSpace(metadata.Title) || metadata.Title.Equals(defaultTitle, StringComparison.OrdinalIgnoreCase)))
        {
            metadata.Title = information.Title.Trim();
        }

        if (string.IsNullOrWhiteSpace(metadata.Author) && !string.IsNullOrWhiteSpace(information.Author))
        {
            metadata.Author = information.Author.Trim();
        }

        if (string.IsNullOrWhiteSpace(metadata.Description) && !string.IsNullOrWhiteSpace(information.Subject))
        {
            metadata.Description = information.Subject.Trim();
        }
    }

    private async Task TryApplyOcrAsync(
        string path,
        PdfPageAnalysis page,
        ConversionOptions options,
        List<AnalysisWarning> warnings,
        IProgress<ConversionProgress>? progress,
        int totalPages,
        CancellationToken cancellationToken)
    {
        if (pageRenderer is null || ocrEngine is null || !ocrEngine.IsAvailable)
        {
            var unavailableWarningAdded = warnings.Any(warning => warning.Message.StartsWith("OCR fallback", StringComparison.Ordinal));
            if (!unavailableWarningAdded)
            {
                warnings.Add(new AnalysisWarning(
                    "OCR fallback đã bật nhưng OCR engine hoặc bộ render trang chưa sẵn sàng; các trang scan vẫn chưa có text.",
                    Severity: "Error"));
                unavailableWarningAdded = true;
            }

            return;
        }

        progress?.Report(new ConversionProgress(
            "Running OCR",
            0.05 + 0.55 * (page.PageNumber - 1) / Math.Max(1, totalPages),
            page.PageNumber,
            totalPages,
            $"Nhận dạng OCR trang {page.PageNumber.ToString("N0", CultureInfo.CurrentCulture)} / {totalPages.ToString("N0", CultureInfo.CurrentCulture)}"));

        try
        {
            var renderedPage = await pageRenderer.RenderAsync(
                path,
                page.PageNumber - 1,
                page.Width,
                page.Height,
                options.OcrDpi,
                cancellationToken).ConfigureAwait(false);
            var ocrResult = await ocrEngine.RecognizeAsync(
                renderedPage,
                options.OcrLanguage,
                Math.Clamp(options.OcrConfidenceThreshold, 0, 1),
                cancellationToken).ConfigureAwait(false);
            var acceptedWords = ocrResult.Words
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Where(word => word.Confidence >= Math.Clamp(options.OcrConfidenceThreshold, 0, 1))
                .ToArray();
            if (acceptedWords.Length == 0)
            {
                warnings.Add(new AnalysisWarning("OCR không nhận dạng được text trên trang này.", page.PageNumber));
                return;
            }

            var wordIndex = page.Words.Count;
            page.Words.AddRange(acceptedWords.Select((word, index) => new PdfWord(
                word.Text.Trim(),
                word.Bounds,
                Math.Max(8, word.Bounds.Height * 0.75),
                "OCR",
                false,
                false,
                wordIndex + index)));
            page.HasText = true;
            page.OcrApplied = true;
            page.OcrConfidence = ocrResult.AverageConfidence;
            page.Lines.Clear();
            page.Lines.AddRange(PdfPageAnalyzer.BuildLines(page.Words));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrUnavailableException exception)
        {
            warnings.Add(new AnalysisWarning($"OCR không khả dụng trên trang này: {exception.Message}", page.PageNumber, "Error"));
        }
        catch (Exception exception)
        {
            logger?.Error($"OCR failed for page {page.PageNumber}.", exception);
            warnings.Add(new AnalysisWarning($"OCR thất bại trên trang này: {exception.Message}", page.PageNumber, "Error"));
        }
    }

    private static PdfDocumentKind ClassifyDocument(IReadOnlyList<PdfPageAnalysis> pages)
    {
        if (pages.Count == 0)
        {
            return PdfDocumentKind.Unknown;
        }

        var pagesWithNativeText = pages.Count(page => page.HasNativeText);
        if (pagesWithNativeText == 0)
        {
            return PdfDocumentKind.Scanned;
        }

        if (pagesWithNativeText == pages.Count)
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
        var ocrPercentage = pages.Count == 0 ? 0 : pages.Count(page => page.OcrApplied) * 100d / pages.Count;
        var textConfidence = kind switch
        {
            PdfDocumentKind.Text => 0.98,
            PdfDocumentKind.Mixed when ocrPercentage > 0 => 0.74,
            PdfDocumentKind.Mixed => 0.70,
            PdfDocumentKind.Scanned when ocrPercentage > 0 => 0.58,
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
            OcrPercentage = ocrPercentage
        };
    }
}

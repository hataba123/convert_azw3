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

        var autoDetectedNovelLayout = options.Profile != ConversionProfile.KindleAuto ||
                                      CrossPageParagraphJoiner.IsPredominantlySingleColumn(pages);
        if (!autoDetectedNovelLayout)
        {
            warnings.Add(new AnalysisWarning(
                "Kindle Auto phát hiện bố cục nhiều cột; đã không nối đoạn qua ranh giới trang để tránh đảo nội dung."));
        }

        var crossPageJoin = CrossPageParagraphJoiner.Join(pages, options, cancellationToken);
        if (crossPageJoin.Suspected > 0)
        {
            warnings.Add(new AnalysisWarning(
                $"Có {crossPageJoin.Suspected:N0} ranh giới trang có thể còn chia tách đoạn văn; nên kiểm tra preview."));
        }

        foreach (var page in pages.Where(page => page.Blocks.Count == 0 && page.Images.Count == 0))
        {
            warnings.Add(new AnalysisWarning("Trang không có nội dung có thể đọc sau khi phân tích.", page.PageNumber));
        }

        foreach (var page in pages.Where(page => page.Words.Count is > 0 and < 6 && page.Blocks.Count > 0))
        {
            warnings.Add(new AnalysisWarning("Trang có rất ít chữ; nên kiểm tra xem nội dung có bị thiếu hay không.", page.PageNumber));
        }

        foreach (var page in pages.Where(page => page.Blocks.Any(block => block.Text.Contains('\uFFFD'))))
        {
            warnings.Add(new AnalysisWarning("Trang chứa ký tự thay thế Unicode, có thể do lỗi font hoặc encoding.", page.PageNumber, "Error"));
        }

        progress?.Report(new ConversionProgress("Building semantic book model", 0.78));
        var book = bookDocumentBuilder.Build(pages, metadata, options, warnings, cancellationToken);
        if (pages.Count >= 10 && book.Chapters.Count == 1 && book.Chapters[0].Title == "Nội dung")
        {
            var warning = new AnalysisWarning("Chưa nhận diện chắc chắn được chương; mục lục có thể cần kiểm tra lại.");
            warnings.Add(warning);
            book.Warnings.Add(warning);
        }
        var kind = ClassifyDocument(pages);
        var nativeTextPages = pages.Count(page => page.HasNativeText);
        var ocrPages = pages.Count(page => page.OcrApplied);
        var imageCount = book.Resources.Count(resource => resource.Id.StartsWith("image-", StringComparison.Ordinal));
        var paragraphs = book.Chapters.Sum(chapter => chapter.Blocks.Count(block => block.BlockType == LayoutBlockType.Paragraph));
        var quality = CalculateQuality(pages, book, kind, crossPageJoin.Suspected);

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
            CrossPageParagraphsJoined = crossPageJoin.Joined,
            SuspectedSplitParagraphs = crossPageJoin.Suspected,
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
        PdfDocumentKind kind,
        int suspectedSplitParagraphs)
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
        var emptyPages = pages.Count(page => page.Blocks.Count == 0 && page.Images.Count == 0);
        var readingConfidence = pages.Count == 0 ? 0.0 : Math.Clamp(1 - emptyPages / (double)pages.Count, 0.2, 0.98);
        var paragraphCount = book.Chapters.Sum(chapter => chapter.Blocks.Count(block => block is ParagraphBlock));
        var paragraphConfidence = paragraphCount == 0
            ? 0.20
            : Math.Clamp(1 - suspectedSplitParagraphs / (double)Math.Max(1, paragraphCount), 0.45, 0.97);
        var headingConfidence = book.Chapters.Count > 1 ? 0.90 : Math.Min(0.82, paragraphConfidence);
        var extractedImages = pages.Sum(page => page.Images.Count);
        var preservedImages = book.Resources.Count(resource => resource.Id.StartsWith("image-", StringComparison.Ordinal));
        var imageConfidence = extractedImages == 0 ? 1 : Math.Clamp(preservedImages / (double)extractedImages, 0, 1);
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

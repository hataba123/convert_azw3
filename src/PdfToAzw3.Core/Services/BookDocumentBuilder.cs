using System.Text.RegularExpressions;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Text;

namespace PdfToAzw3.Core.Services;

public sealed partial class BookDocumentBuilder(IHeadingDetector headingDetector) : IBookDocumentBuilder
{
    public BookDocument Build(
        IReadOnlyList<PdfPageAnalysis> pages,
        BookMetadata metadata,
        ConversionOptions options,
        IReadOnlyList<AnalysisWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        var book = new BookDocument { Metadata = metadata };
        book.Warnings.AddRange(warnings);

        var medianFontSize = Median(pages.SelectMany(page => page.Blocks).Select(block => block.FontSize).Where(size => size > 0).ToArray());
        BookChapter? currentChapter = null;
        var chapterNumber = 0;
        foreach (var page in pages.OrderBy(page => page.PageNumber))
        {
            foreach (var block in page.Blocks.OrderBy(block => block.ReadingOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var heading = options.DetectChapters ? headingDetector.Detect(block, medianFontSize) : new HeadingDetectionResult(false, 0);
                if (heading.IsHeading && heading.Level == 1)
                {
                    chapterNumber++;
                    currentChapter = new BookChapter
                    {
                        Title = TextNormalizer.Normalize(block.Text),
                        Level = 1,
                        SourcePageNumber = page.PageNumber,
                        AnchorId = CreateAnchor(block.Text, chapterNumber)
                    };
                    book.Chapters.Add(currentChapter);
                    continue;
                }

                currentChapter ??= CreateDefaultChapter(book, page.PageNumber);
                if (heading.IsHeading)
                {
                    var anchorId = CreateAnchor(block.Text, currentChapter.Blocks.Count + 1);
                    currentChapter.Blocks.Add(new HeadingBlock
                    {
                        BlockType = LayoutBlockType.Heading,
                        SourcePageNumber = page.PageNumber,
                        Text = TextNormalizer.Normalize(block.Text),
                        Level = heading.Level,
                        AnchorId = anchorId
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(block.Text))
                {
                    continue;
                }

                var isQuote = block.Alignment == TextAlignment.Left && block.Bounds.Left > 90 && block.FontSize <= medianFontSize * 0.98;
                var isCode = block.FontName.Contains("mono", StringComparison.OrdinalIgnoreCase) ||
                             block.FontName.Contains("courier", StringComparison.OrdinalIgnoreCase);
                if (isQuote)
                {
                    currentChapter.Blocks.Add(new QuoteBlock
                    {
                        BlockType = LayoutBlockType.Quote,
                        SourcePageNumber = page.PageNumber,
                        Text = TextNormalizer.Normalize(block.Text)
                    });
                }
                else
                {
                    currentChapter.Blocks.Add(new ParagraphBlock
                    {
                        BlockType = isCode ? LayoutBlockType.Code : LayoutBlockType.Paragraph,
                        SourcePageNumber = page.PageNumber,
                        Text = TextNormalizer.Normalize(block.Text),
                        IsCode = isCode
                    });
                }
            }
        }

        if (book.Chapters.Count == 0)
        {
            CreateDefaultChapter(book, 1);
        }

        return book;
    }

    private static BookChapter CreateDefaultChapter(BookDocument book, int pageNumber)
    {
        var chapter = new BookChapter
        {
            Title = "Nội dung",
            Level = 1,
            SourcePageNumber = pageNumber,
            AnchorId = "chapter-1"
        };
        book.Chapters.Add(chapter);
        return chapter;
    }

    private static string CreateAnchor(string text, int suffix)
    {
        var normalized = TextNormalizer.Normalize(text).ToLowerInvariant();
        normalized = NonAlphaNumericRegex().Replace(normalized, "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? $"chapter-{suffix}" : $"{normalized}-{suffix}";
    }

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

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();
}

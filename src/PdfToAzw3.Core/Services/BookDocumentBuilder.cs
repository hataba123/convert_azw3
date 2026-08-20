using System.Text.RegularExpressions;
using System.Security.Cryptography;
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
        var imageResources = new Dictionary<string, BookResource>(StringComparer.Ordinal);
        var imageNumber = 0;
        foreach (var page in pages.OrderBy(page => page.PageNumber))
        {
            var footnoteCandidates = page.Blocks
                .Where(block => block.Bounds.Bottom <= page.Height * 0.28 && TryParseFootnote(block.Text, out _, out _))
                .Select(block => (Block: block, Footnote: ParseFootnote(block.Text)))
                .ToArray();
            var footnoteLinks = footnoteCandidates
                .Select((item, index) => new FootnoteLink(
                    item.Block,
                    item.Footnote.Marker,
                    item.Footnote.Text,
                    $"fn-{page.PageNumber}-{index + 1}",
                    $"fnref-{page.PageNumber}-{index + 1}"))
                .ToArray();
            var linkedFootnotes = new HashSet<string>(StringComparer.Ordinal);
            var pageElements = page.Blocks
                .Select(block => new PageElement(block.Bounds.Top, block, null))
                .Concat(page.Images.Select(image => new PageElement(image.Bounds.Top, null, image)))
                .OrderByDescending(element => element.Top)
                .ThenBy(element => element.Block?.ReadingOrder ?? int.MaxValue)
                .ToArray();
            foreach (var element in pageElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.Image is not null)
                {
                    if (!options.PreserveImages)
                    {
                        continue;
                    }

                    var hash = Convert.ToHexString(SHA256.HashData(element.Image.Content));
                    if (!imageResources.TryGetValue(hash, out var resource))
                    {
                        imageNumber++;
                        var fileName = $"image-{imageNumber:000}.{element.Image.Extension}";
                        resource = new BookResource
                        {
                            Id = $"image-{imageNumber:000}",
                            FileName = fileName,
                            MediaType = element.Image.MediaType,
                            Content = element.Image.Content
                        };
                        imageResources.Add(hash, resource);
                        book.Resources.Add(resource);
                    }

                    currentChapter ??= CreateDefaultChapter(book, page.PageNumber);
                    currentChapter.Blocks.Add(new ImageBlock
                    {
                        BlockType = LayoutBlockType.Image,
                        SourcePageNumber = page.PageNumber,
                        ResourceId = resource.FileName
                    });
                    continue;
                }

                var block = element.Block!;
                var footnote = footnoteLinks.FirstOrDefault(item => ReferenceEquals(item.Block, block));
                if (footnote is not null)
                {
                    currentChapter ??= CreateDefaultChapter(book, page.PageNumber);
                    var marker = footnote.Marker;
                    currentChapter.Blocks.Add(new FootnoteBlock
                    {
                        BlockType = LayoutBlockType.Footnote,
                        SourcePageNumber = page.PageNumber,
                        Marker = marker,
                        Text = footnote.Text,
                        AnchorId = footnote.AnchorId,
                        BackLinkId = footnote.BackLinkId
                    });
                    continue;
                }

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

                if (TableDetector.TryCreate(block, out var table))
                {
                    currentChapter.Blocks.Add(table);
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
                    var paragraph = new ParagraphBlock
                    {
                        BlockType = isCode ? LayoutBlockType.Code : LayoutBlockType.Paragraph,
                        SourcePageNumber = page.PageNumber,
                        Text = TextNormalizer.Normalize(block.Text),
                        IsCode = isCode
                    };
                    paragraph.InlineRuns.AddRange(BuildInlineRuns(block, options.RepairHyphenatedWords));
                    AddFootnoteReferences(paragraph, footnoteLinks, linkedFootnotes);
                    currentChapter.Blocks.Add(paragraph);
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

    private static void AddFootnoteReferences(
        ParagraphBlock paragraph,
        IReadOnlyList<FootnoteLink> candidates,
        ISet<string> linkedFootnotes)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (linkedFootnotes.Contains(candidate.BackLinkId) || !ContainsFootnoteMarker(paragraph.Text, candidate.Marker))
            {
                continue;
            }

            paragraph.FootnoteReferences.Add(new FootnoteReference(
                candidate.Marker,
                candidate.AnchorId,
                candidate.BackLinkId));
            linkedFootnotes.Add(candidate.BackLinkId);
        }
    }

    private static IReadOnlyList<BookTextRun> BuildInlineRuns(PdfBlock block, bool repairHyphenatedWords)
    {
        if (block.Lines.Count == 0)
        {
            return [new BookTextRun(TextNormalizer.Normalize(block.Text), block.IsBold, block.IsItalic)];
        }

        var tokens = new List<BookTextRun>();
        for (var lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines[lineIndex];
            for (var wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
            {
                var word = line.Words[wordIndex];
                var needsSpace = tokens.Count > 0;
                if (needsSpace && lineIndex > 0 && wordIndex == 0 && repairHyphenatedWords &&
                    tokens[^1].Text.EndsWith("-", StringComparison.Ordinal))
                {
                    tokens[^1] = tokens[^1] with { Text = tokens[^1].Text[..^1] };
                    needsSpace = false;
                }

                var text = (needsSpace ? " " : string.Empty) + TextNormalizer.Normalize(word.Text);
                var isSuperscript = word.FontSize > 0 && block.FontSize > 0 && word.FontSize < block.FontSize * 0.82 &&
                                    word.Bounds.Bottom > line.Bounds.Bottom + block.FontSize * 0.12;
                AddOrMergeRun(tokens, new BookTextRun(text, word.IsBold, word.IsItalic, isSuperscript));
            }
        }

        return tokens;
    }

    private static void AddOrMergeRun(List<BookTextRun> runs, BookTextRun run)
    {
        if (runs.Count > 0 && runs[^1].IsBold == run.IsBold && runs[^1].IsItalic == run.IsItalic &&
            runs[^1].IsSuperscript == run.IsSuperscript)
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text + run.Text };
            return;
        }

        runs.Add(run);
    }

    private static bool ContainsFootnoteMarker(string text, string marker)
    {
        if (marker.Any(character => character is '¹' or '²' or '³' or '⁴' or '⁵' or '⁶' or '⁷' or '⁸' or '⁹' or '⁰'))
        {
            return text.Contains(marker, StringComparison.Ordinal);
        }

        return text.TrimEnd().EndsWith(marker, StringComparison.Ordinal) ||
               text.Contains($" {marker}", StringComparison.Ordinal);
    }

    private static bool TryParseFootnote(string text, out string marker, out string body)
    {
        var match = FootnoteRegex().Match(text.Trim());
        marker = match.Success ? match.Groups["marker"].Value : string.Empty;
        body = match.Success ? match.Groups["body"].Value : string.Empty;
        return match.Success && body.Length >= 4;
    }

    private static (string Marker, string Text) ParseFootnote(string text)
    {
        TryParseFootnote(text, out var marker, out var body);
        return (marker, TextNormalizer.Normalize(body));
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

    [GeneratedRegex(@"^\s*(?<marker>\d{1,2}|[¹²³⁴⁵⁶⁷⁸⁹⁰])\s*[\).:]?\s+(?<body>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex FootnoteRegex();

    private sealed record PageElement(double Top, PdfBlock? Block, PdfExtractedImage? Image);

    private sealed record FootnoteLink(PdfBlock Block, string Marker, string Text, string AnchorId, string BackLinkId);
}

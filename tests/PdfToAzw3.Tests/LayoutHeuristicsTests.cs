using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class LayoutHeuristicsTests
{
    [Fact]
    public void ParagraphReconstructor_MergesAdjacentLines()
    {
        var blocks = new[]
        {
            CreateBlock("The quick brown fox jumps", 780, 0),
            CreateBlock("over the lazy dog.", 764, 1)
        };

        var result = new ParagraphReconstructor().Reconstruct(blocks, repairHyphenatedWords: true);

        var paragraph = Assert.Single(result);
        Assert.Equal("The quick brown fox jumps over the lazy dog.", paragraph.Text);
    }

    [Fact]
    public void ParagraphReconstructor_RepairsHyphenatedLineBreak()
    {
        var blocks = new[]
        {
            CreateBlock("inter-", 780, 0),
            CreateBlock("national", 764, 1)
        };

        var result = new ParagraphReconstructor().Reconstruct(blocks, repairHyphenatedWords: true);

        Assert.Equal("international", Assert.Single(result).Text);
    }

    [Fact]
    public void ReadingOrderDetector_ReadsLeftColumnBeforeRightColumn()
    {
        var blocks = new[]
        {
            CreateBlock("A1", 780, 0, 100),
            CreateBlock("B1", 780, 1, 500),
            CreateBlock("A2", 760, 2, 100),
            CreateBlock("B2", 760, 3, 500)
        };

        var result = new ReadingOrderDetector().Order(blocks, 600);

        Assert.Equal(["A1", "A2", "B1", "B2"], result.Select(block => block.Text));
    }

    [Fact]
    public void HeaderFooterDetector_RemovesRepeatedHeadersAndPageNumbers()
    {
        var pages = Enumerable.Range(1, 4).Select(pageNumber =>
        {
            var page = new PdfPageAnalysis { PageNumber = pageNumber, Width = 600, Height = 800 };
            page.Blocks.Add(CreateBlock("Programming C#", 790, 0, 80));
            page.Blocks.Add(CreateBlock(pageNumber.ToString(), 20, 1, 290));
            page.Blocks.Add(CreateBlock($"Body {pageNumber}", 500, 2, 80));
            return page;
        }).ToArray();

        var result = new HeaderFooterDetector().RemoveRepeatedBlocks(pages, new ConversionOptions());

        Assert.Equal(4, result.HeadersRemoved);
        Assert.Equal(4, result.PageNumbersRemoved);
        Assert.All(pages, page => Assert.Single(page.Blocks));
    }

    [Fact]
    public void HeadingDetector_MapsChapterToLevelOne()
    {
        var block = CreateBlock("Chapter 1", 24, 0, 80);
        block.IsBold = true;

        var result = new HeadingDetector().Detect(block, 12);

        Assert.True(result.IsHeading);
        Assert.Equal(1, result.Level);
    }

    [Fact]
    public void BookDocumentBuilder_ConvertsAlignedRowsToTableBlock()
    {
        var page = new PdfPageAnalysis { PageNumber = 1, Width = 600, Height = 800 };
        var block = new PdfBlock
        {
            BlockType = LayoutBlockType.Paragraph,
            Bounds = new PdfRect(80, 500, 520, 560),
            Text = "Name Value Status",
            FontSize = 12,
            FontName = "Arial",
            PageNumber = 1,
            ReadingOrder = 0
        };
        block.Lines.Add(CreateLine([("Name", 80), ("Value", 250), ("Status", 420)], 550));
        block.Lines.Add(CreateLine([("A", 80), ("42", 250), ("Ready", 420)], 530));
        block.Lines.Add(CreateLine([("B", 80), ("17", 250), ("Done", 420)], 510));
        page.Blocks.Add(block);

        var book = new BookDocumentBuilder(new HeadingDetector()).Build(
            [page],
            new BookMetadata { Title = "Table" },
            new ConversionOptions(),
            []);

        Assert.Contains(book.Chapters.SelectMany(chapter => chapter.Blocks), block => block is TableBlock table && table.Rows.Count == 3);
    }

    private static PdfBlock CreateBlock(string text, double top, int readingOrder, double left = 80)
    {
        return new PdfBlock
        {
            BlockType = LayoutBlockType.Paragraph,
            Bounds = new PdfRect(left, top - 12, left + 300, top),
            Text = text,
            FontSize = 12,
            FontName = "Arial",
            ReadingOrder = readingOrder,
            PageNumber = 1,
            Alignment = TextAlignment.Left
        };
    }

    private static PdfLine CreateLine((string Text, double Left)[] words, double top)
    {
        var line = new PdfLine();
        foreach (var word in words)
        {
            line.Words.Add(new PdfWord(word.Text, new PdfRect(word.Left, top - 12, word.Left + 45, top), 12, "Arial", false, false, line.Words.Count));
        }

        return line;
    }
}

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
}

using System.IO.Compression;
using System.Text;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class TechnicalBookSingleColumnTests
{
    [Fact]
    public void RecommendationAnalyzer_SuggestsTechnicalBookForStructuredSingleColumnDocument()
    {
        var page = new PdfPageAnalysis { PageNumber = 1, Width = 600, Height = 800, HasText = true, HasNativeText = true };
        page.Blocks.Add(CreateBlock("1. Giới thiệu", 720, bold: true, fontSize: 18));
        page.Blocks.Add(CreateBlock("Nội dung tài liệu có nhiều đoạn ngắn để giải thích khái niệm.", 670));
        page.Blocks.Add(CreateListBlock(610));
        page.Blocks.Add(CreateBlock("Hình 1. Sơ đồ hệ thống", 540));

        var recommendation = ConversionRecommendationAnalyzer.Analyze([page], PdfDocumentKind.Text);

        Assert.Equal(ConversionProfile.KindleTechnicalBook, recommendation.Profile);
        Assert.Contains("văn bản một cột có cấu trúc", recommendation.Reasons);
    }

    [Fact]
    public void RecommendationAnalyzer_SuggestsNovelForPlainSingleColumnProse()
    {
        var page = new PdfPageAnalysis { PageNumber = 1, Width = 600, Height = 800, HasText = true, HasNativeText = true };
        page.Blocks.Add(CreateBlock("Đây là một đoạn văn xuôi dài, liên tục và không có danh sách, bảng hay heading phụ để trình bày nội dung của một câu chuyện.", 720));
        page.Blocks.Add(CreateBlock("Đoạn văn tiếp theo vẫn duy trì cùng nhịp kể chuyện và không đưa thêm cấu trúc tài liệu nào vào nội dung.", 600));

        var recommendation = ConversionRecommendationAnalyzer.Analyze([page], PdfDocumentKind.Text);

        Assert.Equal(ConversionProfile.KindleNovel, recommendation.Profile);
    }

    [Fact]
    public void RecommendationAnalyzer_SuggestsTechnicalBookForClearSingleColumnScan()
    {
        var page = new PdfPageAnalysis
        {
            PageNumber = 1,
            Width = 600,
            Height = 800,
            HasText = true,
            IsLikelyScanned = true,
            OcrApplied = true,
            OcrConfidence = 0.93
        };
        for (var index = 0; index < 16; index++)
        {
            page.Words.Add(new PdfWord($"word{index}", new PdfRect(72 + index * 4, 700, 90 + index * 4, 712), 12, "OCR", false, false, index));
        }
        page.Blocks.Add(CreateBlock("1. Bài học", 720, bold: true, fontSize: 18));
        page.Blocks.Add(CreateListBlock(650));

        var recommendation = ConversionRecommendationAnalyzer.Analyze([page], PdfDocumentKind.Scanned);

        Assert.Equal(ConversionProfile.KindleTechnicalBook, recommendation.Profile);
    }

    [Fact]
    public void RecommendationAnalyzer_SuggestsFixedLayoutForWeakScan()
    {
        var page = new PdfPageAnalysis
        {
            PageNumber = 1,
            Width = 600,
            Height = 800,
            HasText = true,
            IsLikelyScanned = true,
            OcrApplied = true,
            OcrConfidence = 0.52
        };
        page.Words.Add(new PdfWord("mờ", new PdfRect(72, 700, 90, 712), 12, "OCR", false, false, 0));
        page.Blocks.Add(CreateBlock("Nội dung mờ", 700));

        var recommendation = ConversionRecommendationAnalyzer.Analyze([page], PdfDocumentKind.Scanned);

        Assert.Equal(ConversionProfile.FixedLayout, recommendation.Profile);
    }

    [Fact]
    public void CrossPageJoiner_JoinsTechnicalBookSingleColumnContinuation()
    {
        var first = new PdfPageAnalysis { PageNumber = 1, Width = 600, Height = 800 };
        var second = new PdfPageAnalysis { PageNumber = 2, Width = 600, Height = 800 };
        first.Blocks.Add(CreateBlock("Quy trình được tiếp tục", 40));
        second.Blocks.Add(CreateBlock("trên trang kế tiếp để hoàn thành phần giải thích.", 760));

        var result = CrossPageParagraphJoiner.Join(
            [first, second],
            new ConversionOptions { Profile = ConversionProfile.KindleTechnicalBook },
            CancellationToken.None);

        Assert.Equal(1, result.Joined);
        Assert.Equal("Quy trình được tiếp tục trên trang kế tiếp để hoàn thành phần giải thích.", first.Blocks[0].Text);
    }

    [Fact]
    public void CrossPageJoiner_DoesNotJoinTechnicalCaptionOrList()
    {
        var first = new PdfPageAnalysis { PageNumber = 1, Width = 600, Height = 800 };
        var second = new PdfPageAnalysis { PageNumber = 2, Width = 600, Height = 800 };
        first.Blocks.Add(CreateBlock("Nội dung trước hình", 40));
        second.Blocks.Add(CreateBlock("Hình 2. Kết quả thí nghiệm", 760));

        var result = CrossPageParagraphJoiner.Join(
            [first, second],
            new ConversionOptions { Profile = ConversionProfile.KindleTechnicalBook },
            CancellationToken.None);

        Assert.Equal(0, result.Joined);
    }

    [Fact]
    public void BookDocumentBuilder_PreservesTechnicalListAndImageCaption()
    {
        var page = new PdfPageAnalysis { PageNumber = 1, Width = 600, Height = 800 };
        page.Images.Add(new PdfExtractedImage
        {
            Bounds = new PdfRect(100, 420, 500, 600),
            Content = [1, 2, 3],
            Extension = "png",
            MediaType = "image/png",
            PageNumber = 1
        });
        page.Blocks.Add(CreateListBlock(700));
        page.Blocks.Add(CreateBlock("Hình 1. Minh họa quy trình", 390));

        var book = new BookDocumentBuilder(new HeadingDetector()).Build(
            [page],
            new BookMetadata { Title = "Technical" },
            new ConversionOptions { Profile = ConversionProfile.KindleTechnicalBook },
            []);
        var blocks = book.Chapters.SelectMany(chapter => chapter.Blocks).ToArray();

        Assert.Contains(blocks, block => block is ListBlock list && list.Items.Count == 2);
        Assert.Contains(blocks, block => block is ImageBlock { Caption: "Hình 1. Minh họa quy trình" });
    }

    [Fact]
    public void ReadingOrderDetector_PlacesFullWidthHeadingBeforeBothColumns()
    {
        var blocks = new[]
        {
            CreateBlock("2. Phương pháp", 740, bold: true, fontSize: 18, left: 50, width: 500),
            CreateBlock("Trái", 680, left: 70, width: 180),
            CreateBlock("Phải", 680, left: 350, width: 180)
        };

        var result = new ReadingOrderDetector().Order(blocks, 600);

        Assert.Equal(["2. Phương pháp", "Trái", "Phải"], result.Select(block => block.Text));
    }

    [Fact]
    public async Task EpubBuilder_RendersTechnicalDocumentBlocksWithoutBookIndent()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var epubPath = Path.Combine(root, "technical.epub");
        var book = new BookDocument { Metadata = new BookMetadata { Title = "Technical document" } };
        book.Resources.Add(new BookResource
        {
            Id = "image-001",
            FileName = "image-001.png",
            MediaType = "image/png",
            Content = [0x89, 0x50, 0x4E, 0x47]
        });
        var chapter = new BookChapter { Title = "Nội dung", AnchorId = "chapter-1", SourcePageNumber = 1 };
        var list = new ListBlock { BlockType = LayoutBlockType.List, SourcePageNumber = 1 };
        list.Items.AddRange(["Chuẩn bị", "Kiểm tra"]);
        chapter.Blocks.Add(list);
        chapter.Blocks.Add(new ImageBlock
        {
            BlockType = LayoutBlockType.Image,
            ResourceId = "image-001.png",
            Caption = "Hình 1. Quy trình",
            SourcePageNumber = 1
        });
        book.Chapters.Add(chapter);

        await new EpubBuilder().BuildAsync(
            book,
            new ConversionOptions { Profile = ConversionProfile.KindleTechnicalBook },
            epubPath);

        Assert.True((await new EpubValidator().ValidateAsync(epubPath)).IsValid);
        using var archive = ZipFile.OpenRead(epubPath);
        var chapterXhtml = ReadEntry(archive, "OEBPS/text/chapter001.xhtml");
        var css = ReadEntry(archive, "OEBPS/styles/book.css");
        Assert.Contains("<ul>", chapterXhtml);
        Assert.Contains("<li>Chuẩn bị</li>", chapterXhtml);
        Assert.Contains("<li>Kiểm tra</li>", chapterXhtml);
        Assert.Contains("<figcaption>Hình 1. Quy trình</figcaption>", chapterXhtml);
        Assert.Contains("text-indent: 0", css);
    }

    private static PdfBlock CreateBlock(string text, double top, bool bold = false, double fontSize = 12, double left = 72, double width = 430) => new()
    {
        BlockType = LayoutBlockType.Paragraph,
        Bounds = new PdfRect(left, top - fontSize, left + width, top),
        Text = text,
        FontSize = fontSize,
        FontName = "Arial",
        IsBold = bold,
        PageNumber = 1,
        Alignment = TextAlignment.Left
    };

    private static PdfBlock CreateListBlock(double top)
    {
        var block = CreateBlock("• Chuẩn bị • Thực hiện", top);
        block.Lines.Add(CreateLine("• Chuẩn bị", top));
        block.Lines.Add(CreateLine("• Thực hiện", top - 18));
        return block;
    }

    private static PdfLine CreateLine(string text, double top)
    {
        var line = new PdfLine();
        line.Words.Add(new PdfWord(text, new PdfRect(72, top - 12, 220, top), 12, "Arial", false, false, 0));
        return line;
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

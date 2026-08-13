namespace PdfToAzw3.Core.Models;

public sealed record PdfWord(
    string Text,
    PdfRect Bounds,
    double FontSize,
    string FontName,
    bool IsBold,
    bool IsItalic,
    int Index);

public sealed class PdfLine
{
    public List<PdfWord> Words { get; } = [];

    public PdfRect Bounds => Words.Count == 0
        ? new PdfRect()
        : new PdfRect(
            Words.Min(word => word.Bounds.Left),
            Words.Min(word => word.Bounds.Bottom),
            Words.Max(word => word.Bounds.Right),
            Words.Max(word => word.Bounds.Top));

    public string Text => string.Join(" ", Words.Select(word => word.Text));

    public double FontSize => Words.Count == 0 ? 0 : Words.Average(word => word.FontSize);

    public bool IsBold => Words.Count > 0 && Words.Count(word => word.IsBold) >= Math.Ceiling(Words.Count * 0.5);

    public bool IsItalic => Words.Count > 0 && Words.Count(word => word.IsItalic) >= Math.Ceiling(Words.Count * 0.5);

    public string FontName => Words.FirstOrDefault()?.FontName ?? string.Empty;

    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
}

public sealed class PdfBlock
{
    public LayoutBlockType BlockType { get; set; } = LayoutBlockType.Unknown;

    public PdfRect Bounds { get; set; }

    public string Text { get; set; } = string.Empty;

    public double FontSize { get; set; }

    public string FontName { get; set; } = string.Empty;

    public bool IsBold { get; set; }

    public bool IsItalic { get; set; }

    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public int ReadingOrder { get; set; }

    public int PageNumber { get; set; }

    public List<PdfLine> Lines { get; } = [];
}

public sealed class PdfPageAnalysis
{
    public int PageNumber { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public bool HasText { get; set; }

    public bool HasNativeText { get; set; }

    public bool IsLikelyScanned { get; set; }

    public bool OcrApplied { get; set; }

    public double OcrConfidence { get; set; }

    public List<PdfWord> Words { get; } = [];

    public List<PdfLine> Lines { get; } = [];

    public List<PdfBlock> Blocks { get; } = [];

    public List<PdfExtractedImage> Images { get; } = [];
}

public sealed class PdfExtractedImage
{
    public required PdfRect Bounds { get; init; }

    public required byte[] Content { get; init; }

    public required string MediaType { get; init; }

    public required string Extension { get; init; }

    public int PageNumber { get; init; }
}

public sealed class PdfAnalysisResult
{
    public required PdfFileInfo File { get; init; }

    public List<PdfPageAnalysis> Pages { get; } = [];

    public List<AnalysisWarning> Warnings { get; } = [];

    public required AnalysisSummary Summary { get; init; }

    public required BookDocument Book { get; init; }
}

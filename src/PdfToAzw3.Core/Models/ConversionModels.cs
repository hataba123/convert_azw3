namespace PdfToAzw3.Core.Models;

public enum PdfDocumentKind
{
    Unknown,
    Text,
    Scanned,
    Mixed
}

public enum ConversionProfile
{
    KindleAuto,
    KindleNovel,
    KindleTechnicalBook,
    PreserveLayout,
    FixedLayout
}

public enum ParagraphStyle
{
    Book,
    Document,
    Compact
}

public enum KindleDeviceProfile
{
    Paperwhite,
    Standard,
    Oasis,
    Scribe
}

public enum LayoutBlockType
{
    Unknown,
    Heading,
    Paragraph,
    List,
    Quote,
    Image,
    Table,
    Caption,
    Footnote,
    PageNumber,
    Header,
    Footer,
    Code
}

public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justified
}

public readonly record struct PdfRect(double Left, double Bottom, double Right, double Top)
{
    public double Width => Math.Max(0, Right - Left);

    public double Height => Math.Max(0, Top - Bottom);

    public double CenterX => Left + Width / 2;

    public double CenterY => Bottom + Height / 2;
}

public sealed record PdfFileInfo(
    string FullPath,
    string FileName,
    long SizeBytes,
    int PageCount,
    PdfDocumentKind Kind = PdfDocumentKind.Unknown);

public sealed class BookMetadata
{
    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Language { get; set; } = "Auto Detect";

    public string Publisher { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? CoverPath { get; set; }
}

public sealed class ConversionOptions
{
    public ConversionProfile Profile { get; set; } = ConversionProfile.KindleAuto;

    public ParagraphStyle ParagraphStyle { get; set; } = ParagraphStyle.Book;

    public KindleDeviceProfile TargetDevice { get; set; } = KindleDeviceProfile.Paperwhite;

    public bool SmartReflow { get; set; } = true;

    public bool RemoveRepeatedHeaders { get; set; } = true;

    public bool RemoveRepeatedFooters { get; set; } = true;

    public bool RemovePageNumbers { get; set; } = true;

    public bool RepairHyphenatedWords { get; set; } = true;

    public bool GenerateTableOfContents { get; set; } = true;

    public bool PreserveImages { get; set; } = true;

    public bool DetectChapters { get; set; } = true;

    public bool EnableOcrFallback { get; set; }

    public string OcrLanguage { get; set; } = "Auto";

    public int OcrDpi { get; set; } = 200;

    public double OcrConfidenceThreshold { get; set; } = 0.45;

    public int FixedLayoutDpi { get; set; } = 150;

    public string? CalibreExecutablePath { get; set; }
}

public sealed class AnalysisSummary
{
    public int Pages { get; set; }

    public int Chapters { get; set; }

    public int Images { get; set; }

    public int HeadersRemoved { get; set; }

    public int FootersRemoved { get; set; }

    public int PageNumbersRemoved { get; set; }

    public int Paragraphs { get; set; }

    public int OcrPages { get; set; }

    public int CrossPageParagraphsJoined { get; set; }

    public int SuspectedSplitParagraphs { get; set; }

    public PdfDocumentKind DocumentKind { get; set; }

    public ConversionQuality Quality { get; set; } = new();
}

public sealed class ConversionQuality
{
    public int Score { get; set; }

    public string Label => Score switch
    {
        >= 85 => "Excellent",
        >= 70 => "Good",
        _ => "Review recommended"
    };

    public double TextExtractionConfidence { get; set; }

    public double ReadingOrderConfidence { get; set; }

    public double ParagraphConfidence { get; set; }

    public double HeadingConfidence { get; set; }

    public double ImageConfidence { get; set; }

    public double OcrPercentage { get; set; }
}

public sealed record AnalysisWarning(string Message, int? PageNumber = null, string Severity = "Warning");

public sealed record ConversionProgress(
    string Stage,
    double Fraction,
    int? CurrentPage = null,
    int? TotalPages = null,
    string? Detail = null);

public sealed record ConversionOutput(
    string EpubPath,
    string Azw3Path,
    long EpubSizeBytes,
    long Azw3SizeBytes,
    AnalysisSummary Summary);

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

public enum FixedLayoutPresentation
{
    FullPage,
    OverviewAndRegions
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

    public FixedLayoutPresentation FixedLayoutPresentation { get; set; } = FixedLayoutPresentation.OverviewAndRegions;

    public int FixedLayoutRegionDpi { get; set; } = 300;

    public double FixedLayoutRegionOverlap { get; set; } = 0.05;

    public bool EnhanceScannedPages { get; set; } = true;

    public string? CalibreExecutablePath { get; set; }

    public ConversionOptions Clone() => new()
    {
        Profile = Profile,
        ParagraphStyle = ParagraphStyle,
        TargetDevice = TargetDevice,
        SmartReflow = SmartReflow,
        RemoveRepeatedHeaders = RemoveRepeatedHeaders,
        RemoveRepeatedFooters = RemoveRepeatedFooters,
        RemovePageNumbers = RemovePageNumbers,
        RepairHyphenatedWords = RepairHyphenatedWords,
        GenerateTableOfContents = GenerateTableOfContents,
        PreserveImages = PreserveImages,
        DetectChapters = DetectChapters,
        EnableOcrFallback = EnableOcrFallback,
        OcrLanguage = OcrLanguage,
        OcrDpi = OcrDpi,
        OcrConfidenceThreshold = OcrConfidenceThreshold,
        FixedLayoutDpi = FixedLayoutDpi,
        FixedLayoutPresentation = FixedLayoutPresentation,
        FixedLayoutRegionDpi = FixedLayoutRegionDpi,
        FixedLayoutRegionOverlap = FixedLayoutRegionOverlap,
        EnhanceScannedPages = EnhanceScannedPages,
        CalibreExecutablePath = CalibreExecutablePath
    };
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

public sealed record ConversionRecommendation(
    ConversionProfile Profile,
    double Confidence,
    IReadOnlyList<string> Reasons)
{
    public string Label => Profile switch
    {
        ConversionProfile.FixedLayout => "Fixed Layout",
        ConversionProfile.KindleTechnicalBook => "Kindle Technical Book",
        _ => "Kindle Novel"
    };

    public string Detail => Reasons.Count == 0
        ? $"Độ tin cậy {Confidence:P0}"
        : $"Độ tin cậy {Confidence:P0} · {string.Join(" · ", Reasons.Take(2))}";
}

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

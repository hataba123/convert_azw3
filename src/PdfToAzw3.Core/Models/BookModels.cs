namespace PdfToAzw3.Core.Models;

public sealed class BookDocument
{
    public required BookMetadata Metadata { get; init; }

    public List<BookChapter> Chapters { get; } = [];

    public List<BookResource> Resources { get; } = [];

    public List<FixedLayoutPage> FixedLayoutPages { get; } = [];

    public List<AnalysisWarning> Warnings { get; } = [];
}

public sealed class BookChapter
{
    public required string Title { get; set; }

    public int Level { get; set; } = 1;

    public int SourcePageNumber { get; set; }

    public string AnchorId { get; set; } = string.Empty;

    public List<BookBlock> Blocks { get; } = [];
}

public abstract class BookBlock
{
    public LayoutBlockType BlockType { get; init; }

    public int SourcePageNumber { get; init; }
}

public sealed class ParagraphBlock : BookBlock
{
    public required string Text { get; init; }

    public bool IsCode { get; init; }

    public List<BookTextRun> InlineRuns { get; } = [];

    public List<FootnoteReference> FootnoteReferences { get; } = [];
}

public sealed record BookTextRun(string Text, bool IsBold = false, bool IsItalic = false, bool IsSuperscript = false);

public sealed record FootnoteReference(string Marker, string TargetId, string BackLinkId);

public sealed class FootnoteBlock : BookBlock
{
    public required string Marker { get; init; }

    public required string Text { get; init; }

    public required string AnchorId { get; init; }

    public required string BackLinkId { get; init; }
}

public sealed class HeadingBlock : BookBlock
{
    public required string Text { get; init; }

    public int Level { get; init; } = 1;

    public string AnchorId { get; init; } = string.Empty;
}

public sealed class QuoteBlock : BookBlock
{
    public required string Text { get; init; }
}

public sealed class ImageBlock : BookBlock
{
    public required string ResourceId { get; init; }

    public string? Caption { get; init; }
}

public sealed class ListBlock : BookBlock
{
    public bool Ordered { get; init; }

    public List<string> Items { get; } = [];
}

public sealed class TableBlock : BookBlock
{
    public List<List<string>> Rows { get; } = [];

    public bool UseImageFallback { get; init; }
}

public sealed class BookResource
{
    public required string Id { get; init; }

    public required string FileName { get; init; }

    public required string MediaType { get; init; }

    public required byte[] Content { get; init; }
}

public sealed class FixedLayoutPage
{
    public required int PageNumber { get; init; }

    public required int PixelWidth { get; init; }

    public required int PixelHeight { get; init; }

    public required string ResourceId { get; init; }

    public required string FileName { get; init; }
}

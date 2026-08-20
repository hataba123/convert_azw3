using System.Text;
using PdfToAzw3.Core.Text;

namespace PdfToAzw3.Tests;

public sealed class TextNormalizerTests
{
    [Fact]
    public void JoinLines_MergesAParagraphInsteadOfKeepingPdfLineBreaks()
    {
        var result = TextNormalizer.JoinLines(["The quick brown fox jumps", "over the lazy dog."]);

        Assert.Equal("The quick brown fox jumps over the lazy dog.", result);
    }

    [Fact]
    public void JoinLines_RepairsHyphenatedWordAtLineBreak()
    {
        var result = TextNormalizer.JoinLines(["inter-", "national"]);

        Assert.Equal("international", result);
    }

    [Fact]
    public void JoinLines_PreservesSemanticHyphen()
    {
        var result = TextNormalizer.JoinLines(["state-of-the-art", "design"]);

        Assert.Equal("state-of-the-art design", result);
    }

    [Fact]
    public void Normalize_UsesUnicodeFormCAndKeepsVietnameseText()
    {
        var decomposed = "Tie\u0302\u0301ng Vie\u0302\u0323t: Nguye\u0302\u0303n Va\u0306n Lo\u031B\u0323i";

        var result = TextNormalizer.Normalize(decomposed);

        Assert.Equal("Tiếng Việt: Nguyễn Văn Lợi", result);
        Assert.Equal(result.Normalize(NormalizationForm.FormC), result);
    }

    [Fact]
    public void Normalize_HandlesKindleUnsafeSpacingAndCommonLigatures()
    {
        var result = TextNormalizer.Normalize("A\u00A0ﬁne\u202Fﬃction with non‑breaking hyphen");

        Assert.Equal("A fine ffiction with non-breaking hyphen", result);
    }
}

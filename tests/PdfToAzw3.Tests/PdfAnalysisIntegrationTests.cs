using System.Text;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;

namespace PdfToAzw3.Tests;

public sealed class PdfAnalysisIntegrationTests
{
    [Fact]
    public async Task PdfPigReader_ExtractsTextCoordinatesAndSemanticChapter()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdfPath = Path.Combine(root, "sample.pdf");
        await File.WriteAllBytesAsync(pdfPath, CreateSimplePdf());

        var result = await PdfPipelineFactory.CreateDefaultReader().AnalyzeAsync(
            pdfPath,
            new BookMetadata { Title = "Sample" },
            new ConversionOptions());

        Assert.Equal(PdfDocumentKind.Text, result.Summary.DocumentKind);
        Assert.Equal(1, result.Summary.Pages);
        Assert.NotEmpty(result.Pages[0].Words);
        Assert.Contains(result.Book.Chapters, chapter => chapter.Title.Contains("Chapter 1", StringComparison.Ordinal));
        Assert.True(
            result.Book.Chapters.SelectMany(chapter => chapter.Blocks).OfType<ParagraphBlock>().Any(paragraph => paragraph.Text.Contains("quick brown fox", StringComparison.Ordinal)),
            string.Join(" | ", result.Pages[0].Blocks.Select(block => $"{block.FontSize}:{block.Text}")));
    }

    [Fact]
    public async Task PdfPigReader_RecommendsTechnicalBookForStructuredSingleColumnFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfToAzw3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdfPath = Path.Combine(root, "single-column-technical.pdf");
        await File.WriteAllBytesAsync(pdfPath, CreateStructuredSingleColumnPdf());

        var result = await PdfPipelineFactory.CreateDefaultReader().AnalyzeAsync(
            pdfPath,
            new BookMetadata { Title = "Single column technical" },
            new ConversionOptions { Profile = ConversionProfile.KindleAuto });

        Assert.Equal(ConversionProfile.KindleTechnicalBook, result.Recommendation!.Profile);
        Assert.Contains("văn bản một cột có cấu trúc", result.Recommendation.Reasons);
    }

    private static byte[] CreateSimplePdf()
    {
        var header = "%PDF-1.4\n";
        var content = "BT /F1 24 Tf 72 700 Td (Chapter 1) Tj /F1 12 Tf 0 -30 Td (The quick brown fox jumps) Tj 0 -16 Td (over the lazy dog.) Tj ET";
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream\nendobj\n"
        };

        var builder = new StringBuilder(header);
        var offsets = new List<int> { 0 };
        foreach (var item in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(item);
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
        {
            builder.Append($"{offsets[index]:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateStructuredSingleColumnPdf()
    {
        var header = "%PDF-1.4\n";
        var content = "BT /F1 18 Tf 72 720 Td (1. Introduction) Tj /F1 12 Tf 0 -60 Td (This document explains a structured technical workflow.) Tj 0 -60 Td (- Prepare the source material.) Tj 0 -18 Td (- Validate the converted output.) Tj 0 -60 Td (Figure 1. Workflow overview) Tj ET";
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            $"5 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream\nendobj\n"
        };

        var builder = new StringBuilder(header);
        var offsets = new List<int> { 0 };
        foreach (var item in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(item);
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
        {
            builder.Append($"{offsets[index]:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}

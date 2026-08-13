namespace PdfToAzw3.Core.Services;

public static class PdfPipelineFactory
{
    public static IPdfDocumentReader CreateDefaultReader(
        IOcrEngine? ocrEngine = null,
        IPdfPageRenderer? pageRenderer = null)
    {
        return new PdfPigDocumentReader(
            new PdfPageAnalyzer(),
            new HeuristicLayoutAnalyzer(),
            new ReadingOrderDetector(),
            new ParagraphReconstructor(),
            new HeaderFooterDetector(),
            new BookDocumentBuilder(new HeadingDetector()),
            new FileAppLogger(),
            pageRenderer ?? new DocNetPdfPageRenderer(),
            ocrEngine ?? new DisabledOcrEngine());
    }
}

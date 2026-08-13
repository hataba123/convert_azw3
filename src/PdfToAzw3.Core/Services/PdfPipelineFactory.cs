namespace PdfToAzw3.Core.Services;

public static class PdfPipelineFactory
{
    public static IPdfDocumentReader CreateDefaultReader()
    {
        return new PdfPigDocumentReader(
            new PdfPageAnalyzer(),
            new HeuristicLayoutAnalyzer(),
            new ReadingOrderDetector(),
            new ParagraphReconstructor(),
            new HeaderFooterDetector(),
            new BookDocumentBuilder(new HeadingDetector()),
            new FileAppLogger());
    }
}

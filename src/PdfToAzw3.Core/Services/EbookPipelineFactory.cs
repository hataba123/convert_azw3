namespace PdfToAzw3.Core.Services;

public static class EbookPipelineFactory
{
    public static IEbookConversionService CreateDefaultService()
    {
        var logger = new FileAppLogger();
        return new EbookConversionService(
            new EpubBuilder(),
            new EpubValidator(),
            new CalibreService(logger),
            logger,
            new FixedLayoutPageBuilder(new DocNetPdfPageRenderer()));
    }
}

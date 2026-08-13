namespace PdfToAzw3.Core.Services;

public static class EbookPipelineFactory
{
    public static IEbookConversionService CreateDefaultService()
    {
        return new EbookConversionService(new EpubBuilder(), new EpubValidator(), new CalibreService());
    }
}

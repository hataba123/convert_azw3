using System.Runtime.InteropServices.WindowsRuntime;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PdfToAzw3.Desktop.Services;

public sealed class WindowsOcrEngine : IOcrEngine
{
    public bool IsAvailable
    {
        get
        {
            try
            {
                return OcrEngine.TryCreateFromUserProfileLanguages() is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public string DisplayName => "Windows OCR";

    public async Task<OcrPageResult> RecognizeAsync(
        RenderedPdfPage page,
        string language,
        double minimumConfidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = CreateEngine(language) ?? throw new OcrUnavailableException(
            string.IsNullOrWhiteSpace(language) || language.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? "Windows OCR chưa có ngôn ngữ nhận dạng nào trong hồ sơ người dùng."
                : $"Windows OCR chưa cài ngôn ngữ {language} trên máy này.");

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(page.PngContent);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        var words = new List<OcrWordResult>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(word.Text))
                {
                    continue;
                }

                var bounds = word.BoundingRect;
                var left = bounds.X * page.PdfWidth / page.PixelWidth;
                var right = (bounds.X + bounds.Width) * page.PdfWidth / page.PixelWidth;
                var top = page.PdfHeight - bounds.Y * page.PdfHeight / page.PixelHeight;
                var bottom = page.PdfHeight - (bounds.Y + bounds.Height) * page.PdfHeight / page.PixelHeight;
                words.Add(new OcrWordResult(
                    word.Text,
                    new PdfRect(left, bottom, right, top),
                    0.80));
            }
        }

        _ = minimumConfidence;
        return new OcrPageResult(words, words.Count == 0 ? 0 : 0.80);
    }

    private static OcrEngine? CreateEngine(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }

        var languageTag = language.Trim() switch
        {
            "Vietnamese" => "vi-VN",
            "Tiếng Việt" => "vi-VN",
            "English" => "en-US",
            _ => language.Trim()
        };
        return OcrEngine.TryCreateFromLanguage(new Language(languageTag));
    }
}

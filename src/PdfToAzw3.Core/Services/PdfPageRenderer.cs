using System.Buffers.Binary;
using System.IO.Compression;
using Docnet.Core;
using Docnet.Core.Models;
using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed class DocNetPdfPageRenderer : IPdfPageRenderer
{
    public Task<RenderedPdfPage> RenderAsync(
        string pdfPath,
        int pageNumber,
        double pdfWidth,
        double pdfHeight,
        int dpi,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => RenderCore(pdfPath, pageNumber, pdfWidth, pdfHeight, dpi, cancellationToken),
            cancellationToken);
    }

    private static RenderedPdfPage RenderCore(
        string pdfPath,
        int pageNumber,
        double pdfWidth,
        double pdfHeight,
        int dpi,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp PDF để render.", pdfPath);
        }

        if (pageNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }

        var safeDpi = Math.Clamp(dpi, 72, 600);
        var targetWidth = Math.Max(1, (int)Math.Round(pdfWidth / 72d * safeDpi));
        var targetHeight = Math.Max(1, (int)Math.Round(pdfHeight / 72d * safeDpi));
        cancellationToken.ThrowIfCancellationRequested();

        using var document = DocLib.Instance.GetDocReader(
            pdfPath,
            new PageDimensions(targetWidth, targetHeight));
        using var page = document.GetPageReader(pageNumber);
        cancellationToken.ThrowIfCancellationRequested();
        var pixelWidth = page.GetPageWidth();
        var pixelHeight = page.GetPageHeight();
        var bgra = page.GetImage();
        var png = PngEncoder.EncodeBgra(bgra, pixelWidth, pixelHeight);
        return new RenderedPdfPage(pageNumber + 1, pixelWidth, pixelHeight, pdfWidth, pdfHeight, png, safeDpi);
    }
}

public sealed class DisabledOcrEngine : IOcrEngine
{
    public bool IsAvailable => false;

    public string DisplayName => "OCR chưa được cung cấp";

    public Task<OcrPageResult> RecognizeAsync(
        RenderedPdfPage page,
        string language,
        double minimumConfidence,
        CancellationToken cancellationToken = default)
    {
        throw new OcrUnavailableException("Chưa cấu hình OCR engine cho môi trường hiện tại.");
    }
}

internal static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] EncodeBgra(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Kích thước trang render không hợp lệ.");
        }

        var expectedLength = checked(width * height * 4);
        if (bgra.Length < expectedLength)
        {
            throw new InvalidDataException("Dữ liệu pixel từ PDF renderer không đầy đủ.");
        }

        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, "IHDR"u8.ToArray(), BuildHeader(width, height));

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                var sourceOffset = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var source = sourceOffset + x * 4;
                    var target = x * 4;
                    row[target] = bgra[source + 2];
                    row[target + 1] = bgra[source + 1];
                    row[target + 2] = bgra[source];
                    row[target + 3] = bgra[source + 3];
                }

                zlib.Write(row, 0, row.Length);
            }
        }

        WriteChunk(output, "IDAT"u8.ToArray(), compressed.ToArray());
        WriteChunk(output, "IEND"u8.ToArray(), []);
        return output.ToArray();
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static void WriteChunk(Stream output, byte[] type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(type, data));
        output.Write(crc);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        var result = crc ^ value;
        for (var bit = 0; bit < 8; bit++)
        {
            result = (result & 1) == 1
                ? (result >> 1) ^ 0xEDB88320u
                : result >> 1;
        }

        return result;
    }
}

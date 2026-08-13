using System.IO.Compression;
using System.Xml;

namespace PdfToAzw3.Core.Services;

public sealed class EpubValidator : IEpubValidator
{
    public Task<EpubValidationResult> ValidateAsync(string epubPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ValidateCore(epubPath, cancellationToken), cancellationToken);
    }

    private static EpubValidationResult ValidateCore(string epubPath, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (!File.Exists(epubPath))
        {
            return new EpubValidationResult(false, ["Không tìm thấy tệp EPUB sau khi build."]);
        }

        try
        {
            using var archive = ZipFile.OpenRead(epubPath);
            var mimetype = archive.GetEntry("mimetype");
            if (mimetype is null)
            {
                errors.Add("EPUB thiếu entry mimetype.");
            }
            else
            {
                using var reader = new StreamReader(mimetype.Open());
                if (!string.Equals(reader.ReadToEnd(), "application/epub+zip", StringComparison.Ordinal))
                {
                    errors.Add("Nội dung mimetype không hợp lệ.");
                }
            }

            foreach (var required in new[] { "META-INF/container.xml", "OEBPS/content.opf", "OEBPS/nav.xhtml", "OEBPS/styles/book.css" })
            {
                if (archive.GetEntry(required) is null)
                {
                    errors.Add($"EPUB thiếu entry bắt buộc: {required}.");
                }
            }

            foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = entry.Open();
                using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                while (xmlReader.Read())
                {
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException)
        {
            errors.Add($"EPUB không hợp lệ: {exception.Message}");
        }

        return new EpubValidationResult(errors.Count == 0, errors);
    }
}

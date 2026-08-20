using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

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
            if (archive.Entries.FirstOrDefault()?.FullName != "mimetype")
            {
                errors.Add("Entry mimetype phải đứng đầu EPUB.");
            }

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

            ValidateManifestAndLinks(archive, errors, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException)
        {
            errors.Add($"EPUB không hợp lệ: {exception.Message}");
        }

        return new EpubValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateManifestAndLinks(
        ZipArchive archive,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = entry.Open();
            var document = XDocument.Load(stream, LoadOptions.None);
            documents[entry.FullName] = document;
            var duplicateIds = document.Descendants()
                .Select(element => (string?)element.Attribute("id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .GroupBy(id => id!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);
            foreach (var duplicateId in duplicateIds)
            {
                errors.Add($"XHTML {entry.FullName} có id trùng: {duplicateId}.");
            }
        }

        var opfEntry = archive.GetEntry("OEBPS/content.opf");
        if (opfEntry is not null)
        {
            using var stream = opfEntry.Open();
            var opf = XDocument.Load(stream, LoadOptions.None);
            var manifestItems = opf.Descendants().Where(element => element.Name.LocalName == "item").ToArray();
            var manifestIds = manifestItems
                .Select(item => (string?)item.Attribute("id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            foreach (var duplicateId in manifestIds.GroupBy(id => id!, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            {
                errors.Add($"Manifest có id trùng: {duplicateId}.");
            }

            foreach (var itemRef in opf.Descendants().Where(element => element.Name.LocalName == "itemref"))
            {
                var idRef = (string?)itemRef.Attribute("idref");
                if (!string.IsNullOrWhiteSpace(idRef) && !manifestIds.Contains(idRef, StringComparer.Ordinal))
                {
                    errors.Add($"Spine tham chiếu id không tồn tại trong manifest: {idRef}.");
                }
            }

            foreach (var item in manifestItems)
            {
                var href = (string?)item.Attribute("href");
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                var target = ResolvePath(opfEntry.FullName, href);
                if (!entries.ContainsKey(target))
                {
                    errors.Add($"Manifest tham chiếu tài nguyên không tồn tại: {href}.");
                }
            }
        }

        foreach (var (entryPath, document) in documents)
        {
            foreach (var link in document.Descendants().Where(element => element.Name.LocalName == "a"))
            {
                var href = (string?)link.Attribute("href");
                if (string.IsNullOrWhiteSpace(href) || href.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = href.Split('#', 2);
                var targetPath = parts[0].Length == 0 ? entryPath : ResolvePath(entryPath, parts[0]);
                if (!entries.ContainsKey(targetPath))
                {
                    errors.Add($"Liên kết trong {entryPath} trỏ tới tệp không tồn tại: {href}.");
                    continue;
                }

                if (parts.Length == 2 && parts[1].Length > 0 && documents.TryGetValue(targetPath, out var targetDocument) &&
                    !targetDocument.Descendants().Any(element => string.Equals((string?)element.Attribute("id"), parts[1], StringComparison.Ordinal)))
                {
                    errors.Add($"Liên kết trong {entryPath} trỏ tới anchor không tồn tại: {href}.");
                }
            }
        }
    }

    private static string ResolvePath(string sourcePath, string relativePath)
    {
        var source = new Uri($"https://epub.local/{sourcePath}");
        var resolved = new Uri(source, relativePath);
        return Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
    }
}

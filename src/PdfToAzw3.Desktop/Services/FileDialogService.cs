using Microsoft.Win32;

namespace PdfToAzw3.Desktop.Services;

public interface IFileDialogService
{
    string? SelectPdf();

    string? SelectExecutable();
}

public sealed class FileDialogService : IFileDialogService
{
    public string? SelectPdf()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn tệp PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ebook-convert.exe của Calibre",
            Filter = "Calibre executable (ebook-convert.exe)|ebook-convert.exe|Executable (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

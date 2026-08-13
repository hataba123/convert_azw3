using System.Windows;
using System.IO;
using PdfToAzw3.Desktop.Services;
using PdfToAzw3.Desktop.ViewModels;

namespace PdfToAzw3.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new FileDialogService());
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasPdfFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!HasPdfFile(e) || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        viewModel.TryLoadPdf(files[0]);
        e.Handled = true;
    }

    private static bool HasPdfFile(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        return files.Length > 0 && Path.GetExtension(files[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }
}

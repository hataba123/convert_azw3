using System.Windows;
using System.Windows.Media;
using System.IO;
using PdfToAzw3.Desktop.Services;
using PdfToAzw3.Desktop.ViewModels;

namespace PdfToAzw3.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel(new FileDialogService());
        viewModel.ThemeChanged += ApplyTheme;
        DataContext = viewModel;
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

    private static void ApplyTheme(bool isDark)
    {
        var colors = isDark
            ? new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#111827",
                ["SurfaceBrush"] = "#1B2432",
                ["SurfaceMutedBrush"] = "#202B3C",
                ["BorderBrush"] = "#354156",
                ["TextBrush"] = "#F3F4F6",
                ["MutedTextBrush"] = "#9AA7BB",
                ["AccentBrush"] = "#7692FF",
                ["AccentDarkBrush"] = "#5E79E6",
                ["AccentSoftBrush"] = "#27345E",
                ["SuccessBrush"] = "#51C795",
                ["WarningBrush"] = "#F3B85B"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#F4F6FA",
                ["SurfaceBrush"] = "#FFFFFF",
                ["SurfaceMutedBrush"] = "#F8FAFC",
                ["BorderBrush"] = "#E3E8F0",
                ["TextBrush"] = "#182230",
                ["MutedTextBrush"] = "#6E7A8A",
                ["AccentBrush"] = "#3559E0",
                ["AccentDarkBrush"] = "#2948BF",
                ["AccentSoftBrush"] = "#E8EDFF",
                ["SuccessBrush"] = "#12805C",
                ["WarningBrush"] = "#A56800"
            };

        foreach (var item in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(item.Value)!;
            if (Application.Current.Resources[item.Key] is SolidColorBrush brush)
            {
                try
                {
                    brush.Color = color;
                }
                catch (InvalidOperationException)
                {
                    Application.Current.Resources[item.Key] = new SolidColorBrush(color);
                }
            }
            else
            {
                Application.Current.Resources[item.Key] = new SolidColorBrush(color);
            }
        }
    }
}

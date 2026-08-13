using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Desktop.Services;

namespace PdfToAzw3.Desktop.ViewModels;

public sealed class ChapterListItem(string title, int level, int pageNumber) : ObservableObject
{
    public string Title { get; } = title;

    public int Level { get; } = level;

    public int PageNumber { get; } = pageNumber;

    public string PageLabel => $"Trang {PageNumber}";
}

public sealed class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private PdfFileInfo? _inputFile;
    private string _statusMessage = "Kéo thả một tệp PDF để bắt đầu.";
    private string _progressStage = "Đang chờ tệp PDF";
    private double _progressValue;
    private bool _isBusy;
    private bool _isAnalyzed;
    private string? _errorMessage;

    public MainViewModel(IFileDialogService fileDialogService)
    {
        _fileDialogService = fileDialogService;
        Metadata = new BookMetadata();
        Options = new ConversionOptions();
        Summary = new AnalysisSummary();
        Chapters = [];

        ChoosePdfCommand = new RelayCommand(ChoosePdf);
        ClearPdfCommand = new RelayCommand(ClearPdf, () => HasInputFile);
        AnalyzeCommand = new RelayCommand(() => StatusMessage = "Pipeline phân tích sẽ được kích hoạt ở milestone kế tiếp.", () => HasInputFile && !IsBusy);
        PreviewCommand = new RelayCommand(() => StatusMessage = "Hãy phân tích PDF trước khi xem preview.", () => IsAnalyzed && !IsBusy);
        ConvertCommand = new RelayCommand(() => StatusMessage = "Hãy phân tích PDF trước khi chuyển đổi.", () => IsAnalyzed && !IsBusy);
        CancelCommand = new RelayCommand(() => StatusMessage = "Không có tác vụ đang chạy.", () => IsBusy);
    }

    public BookMetadata Metadata { get; }

    public ConversionOptions Options { get; }

    public AnalysisSummary Summary { get; private set; }

    public ObservableCollection<ChapterListItem> Chapters { get; }

    public ICommand ChoosePdfCommand { get; }

    public ICommand ClearPdfCommand { get; }

    public ICommand AnalyzeCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand ConvertCommand { get; }

    public ICommand CancelCommand { get; }

    public PdfFileInfo? InputFile
    {
        get => _inputFile;
        private set
        {
            if (SetProperty(ref _inputFile, value))
            {
                OnPropertyChanged(nameof(HasInputFile));
                OnPropertyChanged(nameof(InputFileName));
                OnPropertyChanged(nameof(InputFileSize));
                OnPropertyChanged(nameof(InputFilePath));
                OnPropertyChanged(nameof(PageCountText));
                RaiseCommandStates();
            }
        }
    }

    public bool HasInputFile => InputFile is not null;

    public string InputFileName => InputFile?.FileName ?? "Chưa chọn tệp PDF";

    public string InputFilePath => InputFile?.FullPath ?? string.Empty;

    public string InputFileSize => InputFile is null ? "" : FormatFileSize(InputFile.SizeBytes);

    public string PageCountText => InputFile is null || InputFile.PageCount <= 0 ? "Chưa phân tích" : InputFile.PageCount.ToString("N0", CultureInfo.CurrentCulture);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ProgressStage
    {
        get => _progressStage;
        private set => SetProperty(ref _progressStage, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsAnalyzed
    {
        get => _isAnalyzed;
        private set
        {
            if (SetProperty(ref _isAnalyzed, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void ChoosePdf()
    {
        var selectedPath = _fileDialogService.SelectPdf();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            TryLoadPdf(selectedPath);
        }
    }

    public bool TryLoadPdf(string path)
    {
        ErrorMessage = null;
        if (!File.Exists(path))
        {
            ErrorMessage = "Không tìm thấy tệp PDF đã chọn.";
            return false;
        }

        var fileInfo = new FileInfo(path);
        if (!string.Equals(fileInfo.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Tệp được chọn phải có phần mở rộng .pdf.";
            return false;
        }

        if (fileInfo.Length == 0)
        {
            ErrorMessage = "Tệp PDF không được rỗng.";
            return false;
        }

        InputFile = new PdfFileInfo(fileInfo.FullName, fileInfo.Name, fileInfo.Length, 0);
        Metadata.Title = Path.GetFileNameWithoutExtension(fileInfo.Name);
        OnPropertyChanged(nameof(Metadata));
        Summary = new AnalysisSummary();
        OnPropertyChanged(nameof(Summary));
        Chapters.Clear();
        IsAnalyzed = false;
        ProgressValue = 0;
        ProgressStage = "Sẵn sàng phân tích";
        StatusMessage = "Tệp PDF đã sẵn sàng. Hãy chọn Analyze.";
        return true;
    }

    private void ClearPdf()
    {
        InputFile = null;
        Chapters.Clear();
        IsAnalyzed = false;
        ProgressValue = 0;
        ProgressStage = "Đang chờ tệp PDF";
        StatusMessage = "Kéo thả một tệp PDF để bắt đầu.";
        ErrorMessage = null;
    }

    private static string FormatFileSize(long sizeBytes)
    {
        const double kiloByte = 1024;
        const double megaByte = kiloByte * 1024;
        return sizeBytes >= megaByte
            ? $"{sizeBytes / megaByte:0.0} MB"
            : $"{Math.Max(1, sizeBytes / kiloByte):0} KB";
    }

    private void RaiseCommandStates()
    {
        (ClearPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AnalyzeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConvertCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using PdfToAzw3.Core.Models;
using PdfToAzw3.Core.Services;
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
    private readonly IPdfDocumentReader _pdfDocumentReader;
    private PdfFileInfo? _inputFile;
    private PdfAnalysisResult? _analysisResult;
    private CancellationTokenSource? _conversionCancellation;
    private string _statusMessage = "Kéo thả một tệp PDF để bắt đầu.";
    private string _progressStage = "Đang chờ tệp PDF";
    private double _progressValue;
    private bool _isBusy;
    private bool _isAnalyzed;
    private string? _errorMessage;

    public MainViewModel(IFileDialogService fileDialogService, IPdfDocumentReader? pdfDocumentReader = null)
    {
        _fileDialogService = fileDialogService;
        _pdfDocumentReader = pdfDocumentReader ?? PdfPipelineFactory.CreateDefaultReader();
        Metadata = new BookMetadata();
        Options = new ConversionOptions();
        Summary = new AnalysisSummary();
        Chapters = [];

        ChoosePdfCommand = new RelayCommand(ChoosePdf);
        ClearPdfCommand = new RelayCommand(ClearPdf, () => HasInputFile);
        _analyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => HasInputFile && !IsBusy);
        AnalyzeCommand = _analyzeCommand;
        PreviewCommand = new RelayCommand(() => StatusMessage = "Hãy phân tích PDF trước khi xem preview.", () => IsAnalyzed && !IsBusy);
        ConvertCommand = new RelayCommand(() => StatusMessage = "Hãy phân tích PDF trước khi chuyển đổi.", () => IsAnalyzed && !IsBusy);
        CancelCommand = new RelayCommand(CancelAnalysis, () => IsBusy);
    }

    private readonly AsyncRelayCommand _analyzeCommand;

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

    private async Task AnalyzeAsync()
    {
        if (InputFile is null)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        IsAnalyzed = false;
        ProgressValue = 0;
        ProgressStage = "Loading PDF";
        StatusMessage = "Đang mở PDF...";
        _conversionCancellation?.Dispose();
        _conversionCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ConversionProgress>(UpdateProgress);
            var result = await _pdfDocumentReader.AnalyzeAsync(
                InputFile.FullPath,
                Metadata,
                Options,
                progress,
                _conversionCancellation.Token);

            _analysisResult = result;
            InputFile = result.File;
            Summary = result.Summary;
            OnPropertyChanged(nameof(Summary));
            Chapters.Clear();
            foreach (var chapter in result.Book.Chapters)
            {
                Chapters.Add(new ChapterListItem(chapter.Title, chapter.Level, chapter.SourcePageNumber));
            }

            IsAnalyzed = true;
            ProgressValue = 1;
            ProgressStage = "Analysis complete";
            StatusMessage = $"Đã phân tích {result.Summary.Pages:N0} trang với chất lượng {result.Summary.Quality.Score}/100.";
            if (result.Warnings.Count > 0)
            {
                ErrorMessage = string.Join(Environment.NewLine, result.Warnings.Select(warning => $"• {warning.Message}"));
            }
        }
        catch (OperationCanceledException)
        {
            ProgressStage = "Đã hủy";
            StatusMessage = "Phân tích đã được hủy.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Không thể phân tích PDF: {exception.Message}";
            ProgressStage = "Analysis failed";
            StatusMessage = "Đã xảy ra lỗi khi phân tích PDF.";
        }
        finally
        {
            IsBusy = false;
            _conversionCancellation?.Dispose();
            _conversionCancellation = null;
        }
    }

    private void UpdateProgress(ConversionProgress progress)
    {
        ProgressStage = progress.Stage;
        ProgressValue = Math.Clamp(progress.Fraction, 0, 1);
        StatusMessage = progress.Detail ?? progress.Stage;
    }

    private void CancelAnalysis()
    {
        _conversionCancellation?.Cancel();
        StatusMessage = "Đang dừng phân tích...";
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
        _analysisResult = null;
        ProgressValue = 0;
        ProgressStage = "Sẵn sàng phân tích";
        StatusMessage = "Tệp PDF đã sẵn sàng. Hãy chọn Analyze.";
        return true;
    }

    private void ClearPdf()
    {
        _conversionCancellation?.Cancel();
        InputFile = null;
        _analysisResult = null;
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
        _analyzeCommand.RaiseCanExecuteChanged();
        (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConvertCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}

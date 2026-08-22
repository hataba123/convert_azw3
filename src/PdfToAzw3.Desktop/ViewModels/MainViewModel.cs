using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Diagnostics;
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
    private readonly IEbookConversionService _ebookConversionService;
    private readonly IAppLogger _logger;
    private PdfFileInfo? _inputFile;
    private PdfAnalysisResult? _analysisResult;
    private CancellationTokenSource? _conversionCancellation;
    private string _statusMessage = "Kéo thả một tệp PDF để bắt đầu.";
    private string _progressStage = "Đang chờ tệp PDF";
    private double _progressValue;
    private bool _isBusy;
    private bool _isAnalyzed;
    private string? _errorMessage;
    private string _previewContent = string.Empty;
    private string _outputPath = string.Empty;
    private bool _isDarkMode;
    private AnalysisOptionsSnapshot? _analysisOptionsSnapshot;
    private ChapterListItem? _selectedChapter;
    private ConversionRecommendation? _recommendation;

    public MainViewModel(
        IFileDialogService fileDialogService,
        IPdfDocumentReader? pdfDocumentReader = null,
        IEbookConversionService? ebookConversionService = null,
        IAppLogger? logger = null)
    {
        _fileDialogService = fileDialogService;
        _pdfDocumentReader = pdfDocumentReader ?? PdfPipelineFactory.CreateDefaultReader();
        _ebookConversionService = ebookConversionService ?? EbookPipelineFactory.CreateDefaultService();
        _logger = logger ?? new FileAppLogger();
        Metadata = new BookMetadata();
        Options = new ConversionOptions();
        Summary = new AnalysisSummary();
        Chapters = [];
        Warnings = [];

        ChoosePdfCommand = new RelayCommand(ChoosePdf);
        ChooseCoverCommand = new RelayCommand(ChooseCover);
        ChooseCalibreCommand = new RelayCommand(ChooseCalibre);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ClearPdfCommand = new RelayCommand(ClearPdf, () => HasInputFile);
        _analyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => HasInputFile && !IsBusy);
        AnalyzeCommand = _analyzeCommand;
        PreviewCommand = new RelayCommand(ShowPreview, () => IsAnalyzed && !IsBusy);
        _convertCommand = new AsyncRelayCommand(ConvertAsync, () => IsAnalyzed && !IsBusy);
        ConvertCommand = _convertCommand;
        _openOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => File.Exists(OutputPath));
        OpenOutputFolderCommand = _openOutputFolderCommand;
        OpenKindlePreviewerCommand = new RelayCommand(OpenKindlePreviewer, () => File.Exists(EpubOutputPath));
        ApplyRecommendationCommand = new RelayCommand(ApplyRecommendation, () => Recommendation is not null && !IsBusy);
        CancelCommand = new RelayCommand(CancelAnalysis, () => IsBusy);
    }

    private readonly AsyncRelayCommand _analyzeCommand;
    private readonly AsyncRelayCommand _convertCommand;
    private readonly RelayCommand _openOutputFolderCommand;

    public BookMetadata Metadata { get; }

    public ConversionOptions Options { get; }

    public AnalysisSummary Summary { get; private set; }

    public ConversionRecommendation? Recommendation
    {
        get => _recommendation;
        private set
        {
            if (SetProperty(ref _recommendation, value))
            {
                OnPropertyChanged(nameof(HasRecommendation));
                (ApplyRecommendationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasRecommendation => Recommendation is not null;

    public ObservableCollection<ChapterListItem> Chapters { get; }

    public ObservableCollection<AnalysisWarning> Warnings { get; }

    public ChapterListItem? SelectedChapter
    {
        get => _selectedChapter;
        set => SetProperty(ref _selectedChapter, value);
    }

    public ICommand ChoosePdfCommand { get; }

    public ICommand ChooseCoverCommand { get; }

    public ICommand ChooseCalibreCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand ClearPdfCommand { get; }

    public ICommand AnalyzeCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand ConvertCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand OpenOutputFolderCommand { get; }

    public ICommand OpenKindlePreviewerCommand { get; }

    public ICommand ApplyRecommendationCommand { get; }

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

    public string PreviewContent
    {
        get => _previewContent;
        private set => SetProperty(ref _previewContent, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        private set
        {
            if (SetProperty(ref _outputPath, value))
            {
                _openOutputFolderCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(EpubOutputPath));
                (OpenKindlePreviewerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CoverPath => Metadata.CoverPath ?? "Chưa chọn cover";

    public string CalibrePath => Options.CalibreExecutablePath ?? "Tự động tìm ebook-convert.exe";

    public string EpubOutputPath => string.IsNullOrWhiteSpace(OutputPath) ? string.Empty : Path.ChangeExtension(OutputPath, ".epub");

    public bool HasWarnings => Warnings.Count > 0;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                OnPropertyChanged(nameof(ThemeLabel));
            }
        }
    }

    public string ThemeLabel => IsDarkMode ? "Light" : "Dark";

    public event Action<bool>? ThemeChanged;

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
            Recommendation = result.Recommendation;
            var autoSelectedFixedLayout = Options.Profile == ConversionProfile.KindleAuto &&
                                          result.Recommendation?.Profile == ConversionProfile.FixedLayout &&
                                          HasUnreadableScannedPage(result);
            if (autoSelectedFixedLayout)
            {
                Options.Profile = ConversionProfile.FixedLayout;
                OnPropertyChanged(nameof(Options));
            }

            _analysisOptionsSnapshot = AnalysisOptionsSnapshot.Create(Options);
            InputFile = result.File;
            Summary = result.Summary;
            OnPropertyChanged(nameof(Summary));
            Chapters.Clear();
            Warnings.Clear();
            foreach (var warning in result.Warnings)
            {
                Warnings.Add(warning);
            }
            OnPropertyChanged(nameof(HasWarnings));
            foreach (var chapter in result.Book.Chapters)
            {
                Chapters.Add(new ChapterListItem(chapter.Title, chapter.Level, chapter.SourcePageNumber));
            }
            SelectedChapter = Chapters.FirstOrDefault();

            IsAnalyzed = true;
            ProgressValue = 1;
            ProgressStage = "Analysis complete";
            StatusMessage = autoSelectedFixedLayout
                ? $"Đã phân tích {result.Summary.Pages:N0} trang; tự chọn Fixed Layout để giữ đủ trang scan."
                : result.Summary.OcrPages > 0
                    ? $"Đã phân tích {result.Summary.Pages:N0} trang với {result.Summary.OcrPages:N0} trang OCR; chất lượng {result.Summary.Quality.Score}/100."
                    : $"Đã phân tích {result.Summary.Pages:N0} trang với chất lượng {result.Summary.Quality.Score}/100.";
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
            _logger.Error("PDF analysis failed.", exception);
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

    private void ShowPreview()
    {
        if (_analysisResult is null)
        {
            return;
        }

        var preview = new List<string>();
        var selectedBookChapter = SelectedChapter is null
            ? _analysisResult.Book.Chapters.FirstOrDefault()
            : _analysisResult.Book.Chapters.FirstOrDefault(chapter =>
                chapter.SourcePageNumber == SelectedChapter.PageNumber && chapter.Title == SelectedChapter.Title);
        foreach (var chapter in selectedBookChapter is null ? [] : new[] { selectedBookChapter })
        {
            preview.Add(chapter.Title);
            preview.Add(new string('=', Math.Min(60, Math.Max(10, chapter.Title.Length + 4))));
            preview.AddRange(chapter.Blocks.Select(block => block switch
            {
                HeadingBlock heading => $"\n## {heading.Text}",
                QuoteBlock quote => $"“{quote.Text}”",
                ParagraphBlock paragraph => FormatPreviewParagraph(paragraph),
                FootnoteBlock footnote => $"[{footnote.Marker}] {footnote.Text}",
                ListBlock list => string.Join(Environment.NewLine, list.Items.Select((item, index) => list.Ordered ? $"{index + 1}. {item}" : $"• {item}")),
                TableBlock table => string.Join(Environment.NewLine, table.Rows.Select(row => string.Join(" | ", row))),
                ImageBlock image => $"[Hình ảnh: {image.Caption ?? image.ResourceId}]",
                _ => $"[{block.BlockType}]"
            }));
            preview.Add(string.Empty);
        }

        PreviewContent = string.Join(Environment.NewLine, preview);
        StatusMessage = Options.Profile == ConversionProfile.FixedLayout
            ? "Preview hiển thị BookDocument; khi Convert, Fixed Layout sẽ rasterize từng trang PDF."
            : "Preview đã dựng từ BookDocument semantic.";
    }

    private async Task ConvertAsync()
    {
        if (_analysisResult is null || InputFile is null)
        {
            return;
        }

        if (_analysisOptionsSnapshot != AnalysisOptionsSnapshot.Create(Options))
        {
            IsAnalyzed = false;
            ErrorMessage = "Thiết lập phân tích đã thay đổi. Hãy bấm Analyze lại trước khi chuyển đổi.";
            StatusMessage = "Kết quả phân tích cũ không còn phù hợp với thiết lập hiện tại.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        ProgressValue = 0.80;
        ProgressStage = "Preparing conversion";
        StatusMessage = "Đang chuẩn bị EPUB...";
        _conversionCancellation?.Dispose();
        _conversionCancellation = new CancellationTokenSource();
        var outputDirectory = Path.GetDirectoryName(InputFile.FullPath) ?? Environment.CurrentDirectory;
        var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(InputFile.FileName)}.azw3");

        try
        {
            var progress = new Progress<ConversionProgress>(UpdateProgress);
            var output = await _ebookConversionService.ConvertAsync(
                _analysisResult,
                Options,
                outputPath,
                progress,
                _conversionCancellation.Token);
            OutputPath = output.Azw3Path;
            foreach (var warning in _analysisResult.Book.Warnings.Where(warning => !Warnings.Contains(warning)))
            {
                Warnings.Add(warning);
            }
            OnPropertyChanged(nameof(HasWarnings));
            ProgressValue = 1;
            ProgressStage = "Conversion complete";
            StatusMessage = $"Đã tạo AZW3: {Path.GetFileName(output.Azw3Path)} ({FormatFileSize(output.Azw3SizeBytes)}).";
        }
        catch (OperationCanceledException)
        {
            ProgressStage = "Đã hủy";
            StatusMessage = "Chuyển đổi đã được hủy; tiến trình Calibre đã được dừng.";
        }
        catch (CalibreNotFoundException exception)
        {
            ErrorMessage = exception.Message;
            ProgressStage = "Calibre unavailable";
            StatusMessage = "Không thể tạo AZW3 vì chưa tìm thấy Calibre.";
        }
        catch (Exception exception)
        {
            _logger.Error("AZW3 conversion failed.", exception);
            ErrorMessage = $"Không thể tạo AZW3: {exception.Message}";
            ProgressStage = "Conversion failed";
            StatusMessage = "Đã xảy ra lỗi khi chuyển đổi.";
        }
        finally
        {
            IsBusy = false;
            _conversionCancellation?.Dispose();
            _conversionCancellation = null;
        }
    }

    private void CancelAnalysis()
    {
        _conversionCancellation?.Cancel();
        StatusMessage = "Đang dừng tác vụ...";
    }

    private static bool HasUnreadableScannedPage(PdfAnalysisResult result) =>
        result.Pages.Any(page => page.IsLikelyScanned && !page.HasNativeText && !page.OcrApplied);

    private void ChooseCover()
    {
        var selectedPath = _fileDialogService.SelectImage();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        Metadata.CoverPath = selectedPath;
        OnPropertyChanged(nameof(CoverPath));
        StatusMessage = $"Đã chọn cover: {Path.GetFileName(selectedPath)}.";
    }

    private void ChooseCalibre()
    {
        var selectedPath = _fileDialogService.SelectExecutable();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        Options.CalibreExecutablePath = selectedPath;
        OnPropertyChanged(nameof(CalibrePath));
        StatusMessage = $"Đã đặt Calibre: {Path.GetFileName(selectedPath)}.";
    }

    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        ThemeChanged?.Invoke(IsDarkMode);
    }

    private void OpenOutputFolder()
    {
        if (!File.Exists(OutputPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{OutputPath}\"",
            UseShellExecute = true
        });
    }

    private void OpenKindlePreviewer()
    {
        if (!File.Exists(EpubOutputPath))
        {
            return;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(programFiles, "Amazon", "Kindle Previewer 3", "Kindle Previewer 3.exe"),
            Path.Combine(programFilesX86, "Amazon", "Kindle Previewer 3", "Kindle Previewer 3.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Amazon", "Kindle Previewer 3", "Kindle Previewer 3.exe")
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            ErrorMessage = "Không tìm thấy Kindle Previewer 3. EPUB trung gian vẫn có thể mở bằng Calibre Viewer.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"\"{EpubOutputPath}\"",
            UseShellExecute = true
        });
        StatusMessage = "Đã mở EPUB trung gian trong Kindle Previewer.";
    }

    private void ApplyRecommendation()
    {
        if (Recommendation is null)
        {
            return;
        }

        Options.Profile = Recommendation.Profile;
        if (Recommendation.Profile == ConversionProfile.KindleTechnicalBook)
        {
            Options.ParagraphStyle = ParagraphStyle.Document;
        }
        OnPropertyChanged(nameof(Options));
        IsAnalyzed = false;
        StatusMessage = $"Đã áp dụng đề xuất: {Recommendation.Label}. Hãy Analyze lại trước khi Convert.";
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
        Metadata.CoverPath = null;
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(CoverPath));
        Summary = new AnalysisSummary();
        OnPropertyChanged(nameof(Summary));
        Chapters.Clear();
        SelectedChapter = null;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
        IsAnalyzed = false;
        _analysisResult = null;
        Recommendation = null;
        _analysisOptionsSnapshot = null;
        PreviewContent = string.Empty;
        OutputPath = string.Empty;
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
        Recommendation = null;
        _analysisOptionsSnapshot = null;
        PreviewContent = string.Empty;
        OutputPath = string.Empty;
        Chapters.Clear();
        SelectedChapter = null;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
        IsAnalyzed = false;
        ProgressValue = 0;
        ProgressStage = "Đang chờ tệp PDF";
        StatusMessage = "Kéo thả một tệp PDF để bắt đầu.";
        ErrorMessage = null;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
    }

    private static string FormatFileSize(long sizeBytes)
    {
        const double kiloByte = 1024;
        const double megaByte = kiloByte * 1024;
        return sizeBytes >= megaByte
            ? $"{sizeBytes / megaByte:0.0} MB"
            : $"{Math.Max(1, sizeBytes / kiloByte):0} KB";
    }

    private static string FormatPreviewParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.InlineRuns.Count == 0)
        {
            return paragraph.Text;
        }

        return string.Concat(paragraph.InlineRuns.Select(run =>
        {
            var text = run.Text;
            if (run.IsBold)
            {
                text = $"**{text}**";
            }

            if (run.IsItalic)
            {
                text = $"_{text}_";
            }

            return run.IsSuperscript ? $"^({text})" : text;
        }));
    }

    private void RaiseCommandStates()
    {
        (ClearPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _analyzeCommand.RaiseCanExecuteChanged();
        (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _convertCommand.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _openOutputFolderCommand.RaiseCanExecuteChanged();
    }

    private sealed record AnalysisOptionsSnapshot(
        ConversionProfile Profile,
        bool SmartReflow,
        bool RemoveRepeatedHeaders,
        bool RemoveRepeatedFooters,
        bool RemovePageNumbers,
        bool RepairHyphenatedWords,
        bool PreserveImages,
        bool DetectChapters,
        bool EnableOcrFallback,
        string OcrLanguage,
        int OcrDpi,
        double OcrConfidenceThreshold)
    {
        public static AnalysisOptionsSnapshot Create(ConversionOptions options) => new(
            options.Profile,
            options.SmartReflow,
            options.RemoveRepeatedHeaders,
            options.RemoveRepeatedFooters,
            options.RemovePageNumbers,
            options.RepairHyphenatedWords,
            options.PreserveImages,
            options.DetectChapters,
            options.EnableOcrFallback,
            options.OcrLanguage,
            options.OcrDpi,
            options.OcrConfidenceThreshold);
    }
}

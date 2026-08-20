using System.Diagnostics;
using PdfToAzw3.Core.Models;

namespace PdfToAzw3.Core.Services;

public sealed class CalibreService(IAppLogger? logger = null) : ICalibreService
{
    public string? FindExecutable(string? configuredPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(configuredPath);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.AddRange([
            Path.Combine(programFiles, "Calibre2", "ebook-convert.exe"),
            Path.Combine(programFilesX86, "Calibre2", "ebook-convert.exe"),
            Path.Combine(localAppData, "Programs", "Calibre2", "ebook-convert.exe"),
            Path.Combine(localAppData, "Calibre2", "ebook-convert.exe")
        ]);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return FindOnPath();
    }

    public async Task ConvertAsync(
        string epubPath,
        string azw3Path,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable(options.CalibreExecutablePath);
        if (string.IsNullOrWhiteSpace(executable))
        {
            logger?.Warning("Calibre executable was not found.");
            throw new CalibreNotFoundException("Calibre chưa được cài đặt hoặc không tìm thấy ebook-convert.exe.");
        }

        if (!File.Exists(epubPath))
        {
            throw new FileNotFoundException("Không tìm thấy EPUB trung gian.", epubPath);
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(azw3Path));
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Không xác định được thư mục tạo AZW3.");
        }

        Directory.CreateDirectory(outputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = outputDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(epubPath);
        startInfo.ArgumentList.Add(azw3Path);
        startInfo.ArgumentList.Add("--output-profile");
        var outputProfile = GetOutputProfile(options.TargetDevice);
        startInfo.ArgumentList.Add(outputProfile);
        if (options.GenerateTableOfContents)
        {
            startInfo.ArgumentList.Add("--no-inline-toc");
        }

        var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "không xác định";
        logger?.Info($"Calibre version: {version}");
        logger?.Info($"ebook-convert command: {executable} {epubPath} {azw3Path} --output-profile {outputProfile}{(options.GenerateTableOfContents ? " --no-inline-toc" : string.Empty)}");

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        progress?.Report(new ConversionProgress("Generating AZW3", 0.94, Detail: "Đang chạy Calibre ebook-convert"));
        if (!process.Start())
        {
            throw new InvalidOperationException("Không thể khởi động Calibre ebook-convert.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            logger?.Info($"Calibre output: {standardOutput.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            logger?.Warning($"Calibre warning: {standardError.Trim()}");
        }
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new CalibreConversionException($"Calibre không tạo được AZW3 (mã {process.ExitCode}). {detail.Trim()}");
        }

        if (!File.Exists(azw3Path) || new FileInfo(azw3Path).Length == 0)
        {
            throw new CalibreConversionException("Calibre đã kết thúc nhưng không tạo ra tệp AZW3 hợp lệ.");
        }

        progress?.Report(new ConversionProgress("AZW3 generated", 0.99, Detail: azw3Path));
    }

    private string? FindOnPath()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                Arguments = "ebook-convert.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return null;
            }

            var result = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            var path = result.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path.Trim()) ? null : Path.GetFullPath(path.Trim());
        }
        catch (InvalidOperationException exception)
        {
            logger?.Warning($"Không thể kiểm tra PATH để tìm Calibre: {exception.Message}");
            return null;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            logger?.Warning($"Không thể chạy lệnh kiểm tra PATH: {exception.Message}");
            return null;
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException exception)
        {
            logger?.Warning($"Calibre process đã kết thúc trước khi kill: {exception.Message}");
        }
    }

    internal static string GetOutputProfile(KindleDeviceProfile profile) => profile switch
    {
        KindleDeviceProfile.Oasis => "kindle_oasis",
        KindleDeviceProfile.Scribe => "kindle_scribe",
        KindleDeviceProfile.Standard => "kindle",
        _ => "kindle_pw3"
    };
}

public sealed class CalibreNotFoundException(string message) : Exception(message);

public sealed class CalibreConversionException(string message) : Exception(message);

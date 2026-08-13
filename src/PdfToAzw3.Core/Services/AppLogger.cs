using System.Diagnostics;
using System.Text;

namespace PdfToAzw3.Core.Services;

public interface IAppLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}

public sealed class FileAppLogger : IAppLogger
{
    private readonly object _sync = new();
    private readonly string _logDirectory;

    public FileAppLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [").Append(level).Append("] ")
                .AppendLine(message);
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (_sync)
            {
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (IOException logException)
        {
            Debug.WriteLine($"Không thể ghi log: {logException.Message}");
        }
        catch (UnauthorizedAccessException logException)
        {
            Debug.WriteLine($"Không có quyền ghi log: {logException.Message}");
        }
    }
}

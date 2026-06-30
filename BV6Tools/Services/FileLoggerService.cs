using System.IO;

namespace BV6Tools.Services;

public class FileLoggerService : ILoggerService
{
    private readonly string _logFilePath;
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public FileLoggerService()
    {
        var appFolder = AppDomain.CurrentDomain.BaseDirectory;
        _logFilePath = Path.Combine(appFolder, "app_log.txt");

        var fs = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs) { AutoFlush = true };
        WriteLine("INFO", $"--- Application started at {DateTime.Now} ---");
    }

    public void Log(string message)
        => WriteLine("INFO", message);

    public void LogError(string message, Exception? ex = null)
        => WriteLine("ERROR", ex is null ? message : $"{message}\n{ex}");

    public void LogError(Exception ex)
        => WriteLine("ERROR", ex.ToString());

    private void WriteLine(string level, string message)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
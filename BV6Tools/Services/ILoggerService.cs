namespace BV6Tools.Services;

public interface ILoggerService
{
    void Log(string message);
    void LogError(string message, Exception? ex = null);
    void LogError(Exception ex);
}
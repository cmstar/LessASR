using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LocalAsrClient.Core;

namespace LocalAsrClient.App.Infrastructure;

public static class AppExceptionLogger
{
    private static readonly object SyncRoot = new();
    private static string? _logFilePath;

    public static void Initialize()
    {
        ConfigureLogsDirectory(LessAsrPaths.LogsDirectory);
    }

    public static void ConfigureLogsDirectory(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        lock (SyncRoot)
        {
            _logFilePath = Path.Combine(logsDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
        }
    }

    public static void Report(Exception exception, string context, bool showDialog = true, bool isTerminating = false)
    {
        var message = Format(exception, context, isTerminating);
        WriteToDiagnostics(message);
        WriteToLogFile(message);

        if (showDialog)
        {
            ShowErrorDialog(context, exception);
        }
    }

    private static string Format(Exception exception, string context, bool isTerminating)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {context}");
        if (isTerminating)
        {
            builder.AppendLine("进程即将终止。");
        }

        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    private static void WriteToDiagnostics(string message)
    {
        Debug.WriteLine(message);
        Trace.WriteLine(message);

        try
        {
            Console.Error.WriteLine(message);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteToLogFile(string message)
    {
        string? logFilePath;
        lock (SyncRoot)
        {
            logFilePath = _logFilePath;
        }

        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return;
        }

        lock (SyncRoot)
        {
            File.AppendAllText(logFilePath, message + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static void ShowErrorDialog(string context, Exception exception)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            System.Windows.MessageBox.Show(
                BuildDialogMessage(context, exception),
                LessAsrPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            System.Windows.MessageBox.Show(
                BuildDialogMessage(context, exception),
                LessAsrPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }, DispatcherPriority.Normal);
    }

    private static string BuildDialogMessage(string context, Exception exception)
    {
        string? logFilePath;
        lock (SyncRoot)
        {
            logFilePath = _logFilePath;
        }

        var logHint = string.IsNullOrWhiteSpace(logFilePath)
            ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}详细日志：{logFilePath}";

        return $"{context}{Environment.NewLine}{Environment.NewLine}{exception.Message}{logHint}";
    }
}

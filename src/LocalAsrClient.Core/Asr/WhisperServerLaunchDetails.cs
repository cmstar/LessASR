using System.Text;

namespace LocalAsrClient.Core.Asr;

public static class WhisperServerLaunchDetails
{
    private const int MaxOutputInExceptionMessage = 2000;

    public static string FormatLaunch(string arguments)
    {
        return "命令行参数：" + arguments;
    }

    public static string FormatFailure(int? exitCode, string processOutput, bool timedOut = false)
    {
        var builder = new StringBuilder();
        if (timedOut)
        {
            builder.AppendLine("whisper-server 在超时时间内未就绪。");
        }
        else
        {
            builder.Append("whisper-server 已退出");
            if (exitCode is not null)
            {
                builder.Append("（退出码 ").Append(exitCode.Value).Append(')');
            }

            builder.AppendLine("。");
        }

        if (!string.IsNullOrWhiteSpace(processOutput))
        {
            builder.AppendLine("进程输出：");
            builder.Append(processOutput.TrimEnd());
        }
        else
        {
            builder.Append("进程输出：（无）");
        }

        return builder.ToString();
    }

    public static string FormatFailureSummary(int? exitCode, string processOutput, bool timedOut = false)
    {
        if (timedOut)
        {
            return "等待 whisper-server 启动超时（120 秒）。请查看日志中的启动命令与进程输出。";
        }

        var builder = new StringBuilder();
        builder.Append("whisper-server 已退出");
        if (exitCode is not null)
        {
            builder.Append("（退出码 ").Append(exitCode.Value).Append(')');
        }

        builder.Append("。请查看日志中的启动命令与进程输出。");

        var trimmedOutput = processOutput.Trim();
        if (!string.IsNullOrEmpty(trimmedOutput))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("进程输出：");
            builder.Append(Truncate(trimmedOutput, MaxOutputInExceptionMessage));
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "…";
    }
}

namespace LocalAsrClient.Core.Asr;

public static class WhisperServerStatusFormatter
{
    public static string ToDisplayText(WhisperServerStatus status)
    {
        return status switch
        {
            WhisperServerStatus.Stopped => "未启动",
            WhisperServerStatus.Starting => "启动中",
            WhisperServerStatus.Ready => "已就绪",
            WhisperServerStatus.Transcribing => "识别中",
            WhisperServerStatus.Failed => "启动失败",
            _ => status.ToString()
        };
    }
}

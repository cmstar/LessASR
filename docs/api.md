# API

## whisper-server HTTP 接口

客户端通过 `WhisperServerClient` 调用本地托管的 whisper-server。

### POST /inference

whisper.cpp `whisper-server` 默认转写端点（可通过 `--inference-path` 自定义）。

**请求**

- Content-Type: `multipart/form-data`
- 字段：
  - `file`：WAV 音频（`audio/wav`）
  - `response_format`：`json`
  - `language`：可选，推荐 `zh`

**响应**

```json
{
  "text": "识别结果文本"
}
```

**客户端行为**

- 转写前由 `WhisperServerProcessManager` 确保进程已启动并就绪。
- 默认监听 `http://127.0.0.1:8080`。
- 启动参数：`--host 127.0.0.1 --port 8080 -m "<模型路径>"`

### GET /

健康检查用。进程管理器在启动后轮询此端点，直到返回非 5xx 状态或超时（60 秒）。

## 内部抽象接口

Core 层平台抽象见 `src/LocalAsrClient.Core/Abstractions/`：

| 接口 | 职责 |
| --- | --- |
| `IAsrBackend` | ASR 就绪检查与转写 |
| `IAudioRecorder` | 录音开始/停止 |
| `ITextInjector` | 文本注入 |
| `IHotkeyListener` | F10 触发事件 |
| `ISettingsStore` | 应用设置读写 |
| `IStatsRepository` | 每日统计 |
| `ITextHistoryRepository` | 文本历史 |

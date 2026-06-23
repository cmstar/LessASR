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
  - `language`：可选；LessASR 在「首选语言」非自动时发送 Whisper 语言代码（如 `zh`、`en`）
  - LessASR 不向 whisper-server 发送 `prompt`；简体中文 / 繁体中文的简繁转换在客户端通过 OpenCC 后处理完成

**响应**

```json
{
  "text": "识别结果文本"
}
```

**客户端行为**

- 转写前由 `WhisperServerProcessManager` 确保进程已启动并就绪。
- 默认监听 `http://127.0.0.1:<端口>`，端口可在设置页配置（默认 8080）。
- 启动参数：`--host 127.0.0.1 --port <端口> --threads <N> --max-context 0 -m "<模型路径>"`。`--threads` 默认按本机逻辑处理器数量推荐：&lt;8→4、8–11→6、12–15→8、16→10、≥17→12；可在设置页手动指定，重置后恢复为推荐值。`--max-context 0` 禁用跨请求上下文，规避 Windows 上 whisper-server 多次请求后的 handle 泄漏，见 [whisper.cpp#3358](https://github.com/ggml-org/whisper.cpp/issues/3358)

### GET /

健康检查用。进程管理器在启动后轮询此端点，直到返回非 5xx 状态或超时（60 秒）。

## 内部抽象接口

Core 层平台抽象见 `src/LocalAsrClient.Core/Abstractions/`：

| 接口 | 职责 |
| --- | --- |
| `IAsrBackend` | ASR 就绪检查与转写 |
| `IAudioRecorder` | 录音开始/停止 |
| `ITextInjector` | 文本注入 |
| `IHotkeyListener` | 右 Ctrl 触发事件 |
| `ISettingsStore` | 应用设置读写 |
| `IStatsRepository` | 每日统计 |
| `ITextHistoryRepository` | 文本历史 |

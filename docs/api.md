# API

LessASR 支持两种识别后端：由程序托管的本地 whisper.cpp，以及用户配置的 OpenAI 兼容 Audio Transcriptions API。两者通过同一个 `IAsrBackend` 路由供单句和连续听写使用，但请求协议、生命周期与失败恢复相互独立。

## 本地 whisper-server

客户端通过 `WhisperServerClient` 调用由 LessASR 托管的 whisper.cpp `whisper-server`。

### POST /inference

这是 whisper.cpp `whisper-server` 的默认转写端点（可通过 `--inference-path` 自定义）。

请求使用 `multipart/form-data`：

- `file`：WAV 音频，媒体类型 `audio/wav`
- `response_format`：`json`
- `language`：可选；首选语言非自动时发送 Whisper 语言代码，例如 `zh`、`en`
- `prompt`：可选；存在使用中的非空词汇表时发送

响应必须包含字符串类型的 `text`：

```json
{
  "text": "识别结果文本"
}
```

本地客户端行为：

- 转写前由 `WhisperServerProcessManager` 确保进程已启动并就绪。
- 默认监听 `http://127.0.0.1:<端口>`，默认端口为 8080；路径、端口与线程数在“模型”页配置。
- 启动参数为 `--host 127.0.0.1 --port <端口> --threads <N> -m "<模型路径>"`。
- 线程数默认按逻辑处理器数量推荐：&lt;8→4、8–11→6、12–15→8、16→10、≥17→12，也可手动指定。
- 运行中保存新参数时，管理器先保留活动端点并标记需要重启，模型页随后自动停止并重启服务以应用新参数。
- 本地转写保留既有的连接恢复行为；远程 API 不复用这条重试/重启链路。

### GET /

用于健康检查。进程管理器启动后轮询此端点，直到返回非 5xx 状态或 120 秒超时。

## OpenAI 兼容 Audio Transcriptions API

远程配置填写的是完整转写端点，LessASR 不追加路径。例如：

```text
https://api.example.com/v1/audio/transcriptions
http://192.168.1.24:9000/v1/audio/transcriptions
```

“远程”表示 LessASR 不管理该服务的生命周期；目标既可以是云平台，也可以是用户在本机或局域网中启动的兼容服务。

### 请求

每次识别只发送一次 `POST`，使用 `multipart/form-data`：

- `file`：WAV 音频，文件名 `dictation.wav`，媒体类型 `audio/wav`
- `model`：远程配置中的模型标识
- `response_format`：固定为 `json`
- `language`：可选；首选语言非自动时发送
- `prompt`：可选；仅当该远程配置启用词汇表，且当前词汇表非空时发送

API Key 为空时不发送 `Authorization`。非空时发送：

```http
Authorization: Bearer <API Key>
```

LessASR 不发送任何平台专用请求头或扩展字段。

### 响应与失败

成功响应必须是 JSON 对象，且包含字符串类型的 `text`。`text` 可以是空字符串；手动测试配置时，空字符串仍表示端点与协议兼容。

非成功 HTTP 状态会产生包含状态码和截断响应正文的错误；正文中的当前 API Key 会被脱敏。远程请求不自动重试，也不会尝试启动或重启本地 whisper-server。

### 手动测试

每个远程配置有独立测试按钮。测试发送 0.25 秒、16 kHz、单声道 PCM16 的静音 WAV：

- 使用与真实转写相同的完整端点、模型、认证方式和当前首选语言；
- 该配置启用词汇表时发送当前使用中词汇表生成的 `prompt`，关闭时不查询、不发送；
- 不切换当前服务；
- 可能被提供方计费；
- 不触发自动重试。

## 远程地址策略

公网地址和主机名必须使用 HTTPS。HTTP 只允许以下目标：

- `localhost`
- IPv4 回环：`127.0.0.0/8`
- IPv4 私有：`10.0.0.0/8`、`172.16.0.0/12`、`192.168.0.0/16`
- IPv4 链路本地：`169.254.0.0/16`
- IPv6 回环：`::1`
- IPv6 唯一本地：`fc00::/7`
- IPv6 链路本地：`fe80::/10`

HTTP 规则只接受字面 IP 或 `localhost`，不会通过 DNS 解析后放行主机名。所有端点均拒绝：

- `http` / `https` 之外的 scheme
- URL 用户信息或 fragment
- 未指定地址
- 多播地址
- IPv4 广播地址

客户端不提供忽略 TLS 证书错误的开关，也禁止自动跟随 HTTP 重定向。

## API Key 存储

- API Key 可为空，适用于不要求认证的本机或局域网服务。
- 非空 Key 由 `DpapiSecretProtector` 使用 Windows CurrentUser 范围的 DPAPI 加密；操作使用禁止 UI 提示标志，因此正常调用不会要求用户再次输入 Windows 登录密码。
- SQLite 的 `remote_api_profiles.protected_api_key` 只保存 DPAPI 密文。
- UI 不回填已有 Key；协调层只向 ViewModel 返回可用状态，不返回 DPAPI 密文。编辑时留空表示保留；显式清除操作才将密文设为空。
- DPAPI 解密失败时卡片显示“需要重新输入 API Key”，测试、启用和调用均不会降级为无认证请求。

## 内部抽象接口

Core 层平台抽象见 `src/LocalAsrClient.Core/Abstractions/`：

| 接口 | 职责 |
| --- | --- |
| `IAsrBackend` | 当前后端的名称、模型、状态、就绪检查与转写 |
| `IAsrServiceCoordinator` | 远程配置变更、测试与本地/远程选择的业务边界 |
| `IRemoteApiProfileRepository` | 多份远程 API 配置的持久化 |
| `ISecretProtector` | 密钥保护/解密抽象；Windows 实现在 App 层 |
| `IAudioRecorder` | 录音开始/停止 |
| `ITextInjector` | 文本注入 |
| `IHotkeyListener` | 右 Ctrl 触发事件 |
| `ISettingsStore` | 应用设置读写，以及串行、事务化的字段更新 |
| `IVocabularyRepository` | 多词汇表的查询、新建、更新、删除与使用中状态切换 |
| `IStatsRepository` | 每日统计 |
| `ITextHistoryRepository` | 文本历史的新增、查询、单条删除、待清理数量预检与保留期清理 |

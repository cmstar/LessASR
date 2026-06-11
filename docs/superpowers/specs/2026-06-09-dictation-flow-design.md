# 语音输入（听写）流程设计

日期：2026-06-09

## 目标

在 whisper-server 已可启动并通过健康检查的前提下，完成 LessASR 端到端语音输入能力：用户通过右 Ctrl 切换录音，识别后优先注入当前焦点文本框；失败时在浮窗展示结果与错误信息，并允许立即开始下一轮录音。

本规格聚焦**听写交互、状态机、浮窗、录音、转写、注入与错误恢复**，不重复 MVP 中的托盘、设置、历史、统计等通用设计。参见 [Windows 本地语音输入客户端 MVP 设计](./2026-06-07-windows-asr-client-mvp-design.md)。

## 范围

### 包含

- 右 Ctrl 热键与 `DictationOrchestrator` 状态机完整行为
- 浮窗状态、定位、生命周期
- 内存录音 → HTTP 转写 → 文本注入全链路
- 错误展示、日志、状态恢复
- 端到端验收标准

### 不包含

- 取消录音快捷键、长按模式、多热键配置
- LLM 后处理
- 浮窗跟随光标、多显示器智能定位
- 剪贴板粘贴作为第一注入路径

## 热键

- **默认热键**：键盘右侧 Ctrl（`VK_RCONTROL`）。
- **触发方式**：在按键**按下**时触发一次（边缘触发，忽略按住期间的重复事件）。
- **语义**：切换键（toggle），非按住说话（push-to-talk）。

> 注：早期 MVP 草稿使用右 Alt；产品已定稿为右 Ctrl，以 `docs/domain.md` 为准。

## 状态机

### 状态枚举

| 状态 | 含义 |
| --- | --- |
| `Idle` | 空闲，可开始新一轮听写 |
| `EnsuringModelReady` | 正在确保 whisper-server 就绪 |
| `Ready` | 模型已就绪，等待用户再次按键开始录音 |
| `Recording` | 正在录音 |
| `Transcribing` | 录音已停止，正在 ASR 转写 |
| `Injecting` | 转写完成，正在向焦点控件注入文本 |
| `ResultNeedsAction` | 识别成功但无法注入，需用户复制或重试 |
| `Error` | 录音、转写或持久化等环节抛出异常 |

### 状态转移

```text
Idle / Ready
  └─[右 Ctrl]─> EnsuringModelReady（若 ASR 未就绪）
                  └─> Ready（显示「可录音」，不自动录音）
  └─[右 Ctrl]─> Recording（若 ASR 已就绪）

Recording
  └─[右 Ctrl]─> Transcribing ─> Injecting ─> Idle（注入成功）

Injecting
  └─注入失败─> ResultNeedsAction

Transcribing / Injecting / EnsuringModelReady
  └─[右 Ctrl]─> 忽略（不改变状态）

ResultNeedsAction / Error
  └─[右 Ctrl]─> Recording（开始新一轮录音，与 Typeless 一致）

任意转写/注入步骤
  └─未捕获异常─> Error
```

### 右 Ctrl 响应表

| 当前状态 | 按右 Ctrl |
| --- | --- |
| `Idle` | 若 ASR 未就绪 → `EnsuringModelReady`；已就绪 → `Recording` |
| `Ready` | `Recording` |
| `Recording` | 停止录音 → `Transcribing` → … |
| `Transcribing` | 忽略 |
| `Injecting` | 忽略 |
| `EnsuringModelReady` | 忽略 |
| `ResultNeedsAction` | `Recording`（新会话） |
| `Error` | `Recording`（新会话） |

### 模型预热交互

当 ASR 后端状态不是 `Ready` 时，第一次按右 Ctrl：

1. 浮窗显示「模型加载中」。
2. 后台调用 `IAsrBackend.EnsureReadyAsync` 启动或探测 whisper-server。
3. 就绪后进入 `Ready`，浮窗显示「可录音」；**不自动开始录音**。
4. 用户再次按右 Ctrl 才开始 `Recording`。

当 ASR 已就绪时，第一次按右 Ctrl 直接进入 `Recording`，浮窗显示「正在聆听」。

## 浮窗

### 角色

临时状态窗口，非主窗口。展示当前听写阶段、识别结果、错误详情；在需要用户介入时提供复制按钮。

### 定位

- 水平：在主显示器**工作区**（`SystemParameters.WorkArea`）内水平居中。
- 垂直：浮窗底边距工作区底边 **80px**。
- 工作区已排除任务栏占用区域，80px 是相对于任务栏上沿向上偏移，而非屏幕物理底边。
- MVP 仅使用主显示器工作区，不跟随光标或活动窗口所在显示器。

### 窗口属性

- 置顶（`Topmost`）。
- 不抢焦点（`WS_EX_NOACTIVATE`）。
- 不出现在任务栏（`ShowInTaskbar=false`）。
- 无窗口边框，圆角卡片样式。

### 浮窗状态与文案

| OverlayState | 主文案 | 附加内容 |
| --- | --- | --- |
| `LoadingModel` | 模型加载中 | — |
| `Ready` | 可录音 | — |
| `Recording` | 正在聆听 | — |
| `Transcribing` | 识别中 | — |
| `Injecting` | 正在输入 | 可展示识别文本预览 |
| `Injected` | 已输入 | — |
| `ResultNeedsAction` | 未找到可输入位置 | 识别文本 + 复制按钮 |
| `Error` | 输入失败 | 错误详情（`ErrorMessage`）+ 若有识别文本则展示 + 复制按钮 |

### 生命周期

| 结果 | 浮窗行为 |
| --- | --- |
| 注入成功 | 显示「已输入」约 **700ms** 后自动隐藏 |
| `ResultNeedsAction` | 保持可见，直到用户开始新一轮录音或手动隐藏 |
| `Error` | 保持可见，展示错误详情；用户按右 Ctrl 开始新录音时切换到「正在聆听」 |
| `Ready`（模型刚加载完） | 保持可见，等待用户再次按键 |

开始新一轮录音时，浮窗立即切换到 `Recording` /「正在聆听」，覆盖上一轮的错误或结果展示。

### 复制按钮

- 仅在 `ResultNeedsAction` 与 `Error`（且存在可复制的识别文本）时显示。
- 点击后将识别文本写入系统剪贴板；自动注入优先不触碰剪贴板，但兼容回退可临时使用剪贴板并尽量恢复原内容。

## 录音

### 实现

`NAudioMemoryRecorder`（`LocalAsrClient.App`）实现 `IAudioRecorder`。

### 参数

- 采样率：16000 Hz
- 位深：16 bit
- 声道：单声道（mono）
- 容器：内存 WAV（不写磁盘）

### 规则

- `StartAsync` 创建 `WaveInEvent` 并将 PCM 写入 `MemoryStream`。
- `StopAsync` 停止采集，返回 `RecordingResult`（WAV 字节、时长、采样率、声道数）。
- 识别完成（成功或失败）后释放录音缓冲区；不保留音频文件。
- 若在未调用 `StartAsync` 时调用 `StopAsync`，抛出 `InvalidOperationException`，由编排器捕获并进入 `Error`。

### 最短时长（建议）

录音时长 &lt; 0.3 秒时，可提示「录音太短」并回到 `Idle`/`Ready`；实现阶段若成本高可暂缓，但不应因空音频导致未处理异常。

## ASR 转写

### 后端

`ManagedWhisperServerBackend` 确保 `WhisperServerProcessManager` 就绪后，由 `WhisperServerClient` 调用 HTTP API。

### 请求

- 端点：`POST /v1/audio/transcriptions`
- Content-Type：`multipart/form-data`
- 字段：
  - `file`：WAV 音频（`audio/wav`）
  - `response_format`：`json`
  - `language`：`zh`（推荐显式传递，实现时确认 whisper-server 兼容性）

### 响应

解析 JSON 字段 `text` 作为识别结果。HTTP 非 2xx 或 JSON 解析失败视为异常，进入 `Error` 并在浮窗展示具体错误信息。

### HttpClient 与设置同步

当用户修改 whisper-server 地址或端口并保存设置时，`WhisperServerProcessManager` 与 `HttpClient.BaseAddress` 必须同步更新，避免健康检查通过但转写请求发往旧地址。

## 文本注入

### 目标

识别完成后，优先将文本写入按键瞬间捕获的目标控件；经典控件不使用剪贴板，现代应用或未知控件可使用剪贴板粘贴作为兼容回退。

### 实现策略

`SendInputTextInjector` 为 MVP 默认实现，按策略逐级尝试：

1. **目标捕获**：开始录音时记录前台窗口与可识别的输入控件。
2. **直接注入**：`Edit`/RichEdit 通过 `EM_REPLACESEL`，Scintilla 通过 `SCI_REPLACESEL`。
3. **键盘输入回退**：对已识别目标可尝试恢复前台窗口后发送 Unicode `SendInput`。
4. **剪贴板粘贴回退**：无法识别经典输入控件或目标是现代应用时，保存用户剪贴板、写入识别文本、向捕获窗口发送 `Ctrl+V`，随后尽量恢复原剪贴板。

### 注入结果映射

| TextInjectionStatus | 编排器行为 | 浮窗 |
| --- | --- | --- |
| `Success` | → `Idle`，「已输入」 | 短暂显示后隐藏 |
| `NoEditableTarget` | → `ResultNeedsAction`，「未找到可输入位置」 | 保留 + 复制 |
| `PermissionDenied` / `UnsupportedTarget` / `Failed` | → `ResultNeedsAction`，文案使用 `Message` 或合理默认 | 保留 + 复制 |
| 识别文本为空 | → `ResultNeedsAction`，「识别文本为空」 | 保留，可不显示复制 |

> 仅**未捕获异常**（如 ASR HTTP 失败、数据库写入失败）进入 `Error`。「注入失败」与「无焦点」不进入 `Error`。

### 统计与历史

- 注入成功计为成功；`ResultNeedsAction` 计为失败。
- `Error` 是否计入失败统计：计入失败，但不写入文本历史（若无有效识别文本）。
- 文本历史与统计规则与 MVP 一致。

## 错误处理与可观测性

### 浮窗

- `DictationStatus.ErrorMessage` 必须在 `Error` 态展示给用户（主文案仍为「输入失败」）。
- 若异常发生在注入之前且已有识别文本，一并展示在浮窗中。

### 日志

- 热键触发的 `ToggleAsync` 不得在 `catch` 中静默吞掉异常；至少写入应用日志（`AppExceptionLogger` 或等价设施）。
- ASR HTTP 失败应记录状态码与响应摘要（不含音频数据）。

### 状态恢复

- `Error` 与 `ResultNeedsAction` 之后，用户按右 Ctrl **直接开始新一轮录音**，无需先关闭浮窗。
- 实现上禁止出现「Error 后热键无响应」的僵死状态。

### 并发

- `Transcribing` / `Injecting` / `EnsuringModelReady` 期间忽略额外热键触发。
- 编排器内部应防止 `ToggleAsync` 重入（例如用 `SemaphoreSlim` 或等效锁），避免连续按键导致重复 `StopAsync`。

## 模块边界

| 层 | 职责 |
| --- | --- |
| `LocalAsrClient.Core` | `DictationOrchestrator`、状态机、ASR 编排、持久化；定义 `IHotkeyListener`、`IAudioRecorder`、`ITextInjector` 接口 |
| `LocalAsrClient.App` | 热键、录音、注入、浮窗、将 `StatusChanged` 接到 UI |

Core 不得依赖 WPF；浮窗文案由 App 层根据 `DictationStatus` 映射。

## 与现有代码的差距（实现清单摘要）

1. `ToggleAsync` 支持从 `Error` / `ResultNeedsAction` 进入 `Recording`。
2. 浮窗 `Error` 态展示 `ErrorMessage` 与复制按钮。
3. `SendInputTextInjector` 增加焦点检测，返回 `NoEditableTarget`。
4. 热键回调记录异常，不再空 `catch`。
5. 设置变更时同步 `HttpClient.BaseAddress`。
6. 转写请求补充 `language=zh`（按服务端兼容性调整）。
7. 端到端验证：记事本注入、桌面焦点回退、错误后重试。

## 验收标准

### 基本流程（记事本，ASR 已就绪）

1. 焦点在记事本编辑区。
2. 按右 Ctrl → 浮窗「正在聆听」。
3. 说一段中文 → 再按右 Ctrl。
4. 浮窗依次「识别中」→「正在输入」→「已输入」→ 约 700ms 后消失。
5. 文本出现在记事本；经典控件直写路径下剪贴板内容不变。

### 模型未预热

1. 停止 whisper-server。
2. 按右 Ctrl →「模型加载中」→「可录音」。
3. 再按右 Ctrl →「正在聆听」→ 正常完成转写与注入。

### 无注入目标

1. 焦点在桌面或非文本区域。
2. 完成录音与识别。
3. 浮窗保留，显示识别文本与「复制」；文案为「未找到可输入位置」或等价提示。
4. 再按右 Ctrl → 直接进入「正在聆听」。

### 转写失败

1. 模拟 ASR 不可用或返回错误。
2. 浮窗「输入失败」+ 可读错误详情。
3. 再按右 Ctrl → 直接进入「正在聆听」，应用无需重启。

### 浮窗位置

1. 任务栏在屏幕底部时，浮窗完全位于任务栏上方，不被遮挡。
2. 浮窗水平居中于主显示器工作区。

### 持久化

1. 成功识别后，历史与统计按设置更新。
2. 数据目录无音频文件残留。

## 参考

- 架构：`docs/architecture.md`
- 领域：`docs/domain.md`
- whisper-server API：`docs/api.md`
- MVP 总览：`docs/superpowers/specs/2026-06-07-windows-asr-client-mvp-design.md`

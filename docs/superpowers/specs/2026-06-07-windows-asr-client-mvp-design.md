# Windows 本地语音输入客户端 MVP 设计

日期：2026-06-07

## 目标

构建一个 Windows 本地语音输入客户端，体验接近 Typeless：程序常驻托盘，用户通过键盘右侧 Alt 键触发语音输入，识别完成后优先直接写入当前文本框；如果无法写入，则在桌面底部浮窗中展示识别结果并提供复制按钮。

MVP 聚焦基础听写体验，不做 LLM 润色、排版、语义改写或高级模型管理。

## 技术选型

- 客户端：C# / .NET 8 或更新版本
- 桌面 UI：WPF
- 托盘：WPF 托盘封装或 WinForms `NotifyIcon`
- 热键监听：Win32 低级键盘钩子，监听右 Alt
- 音频采集：NAudio 或等价 .NET 音频库
- ASR 后端：客户端托管的 `whisper-server` HTTP 服务
- 本地存储：SQLite

选择 WPF 的原因是它对 Windows 桌面工具、托盘、隐藏窗口、Win32 interop 和快速开发更直接。WinUI 3、Tauri、Electron、UWP 暂不作为 MVP 技术栈。

## 总体架构

系统分为四个主要层级：

```text
Desktop Shell
  主窗口、托盘、浮窗、Debug 面板、中文界面

Dictation Core
  录音状态机、热键流程、ASR 调用编排、文本注入编排、历史和统计写入

ASR Backend
  管理 whisper-server 进程，通过 HTTP 请求完成语音转文字

Persistence
  设置、每日统计、可选文本历史
```

LLM 文本后处理作为后续扩展点保留接口，但 MVP 使用空处理，不改变 ASR 原始结果。

## 客户端生命周期

客户端启动后显示一个小型主窗口，并同时创建系统托盘图标。

窗口行为：

- 最小化窗口时隐藏到托盘。
- 点击关闭按钮时隐藏到托盘，不退出进程。
- 单击托盘图标重新显示主窗口。
- 右键托盘图标弹出菜单，包含“打开窗口”和“退出程序”。
- 只有用户通过托盘菜单选择“退出程序”时，客户端才真正退出。

退出时客户端需要停止托管的 `whisper-server` 进程，并释放键盘钩子、音频设备和数据库连接。

## 主窗口模块

主窗口使用中文界面，MVP 不做多语言。

建议模块：

- 状态
- 历史
- 统计
- 模型
- 设置
- Debug

### 状态

显示当前输入状态、当前模型、ASR 服务状态、当前热键和最近一次识别摘要。

### 历史

展示最近的文本历史记录。每条记录包含：

- 识别文本
- 创建时间
- 字符数
- 词数
- 录音时长
- 识别耗时
- 使用的模型
- 复制按钮

文本历史保留策略可配置：

- 保存 1 天
- 保存 7 天
- 保存 1 个月
- 彻底关闭

当用户缩短保留期限时，客户端应清理超出新期限的文本历史。

### 统计

统计数据不可关闭，最多保留 2 个月。统计不保存识别文本。

每日统计至少包含：

- 日期
- 输入次数
- 成功次数
- 失败次数
- 录音总时长
- 识别处理总耗时
- 输入字符数
- 输入词数

统计面板展示：

- 今天使用次数
- 今天录音总时长
- 今天输入字符数和词数
- 本月使用次数
- 最近 7 天趋势
- 最近 30 天趋势

### 模型

MVP 只支持 Whisper 模型和 `whisper-server` 后端。

模型页显示：

- 当前模型名称
- 模型文件路径
- 服务状态
- 服务地址，例如 `127.0.0.1:端口`
- 最近一次错误

提供操作：

- 启动服务
- 停止服务
- 重启服务
- 健康检查

服务状态：

- 未启动
- 启动中
- 已就绪
- 识别中
- 启动失败
- 已停止

### 设置

MVP 设置项：

- 当前模型路径
- 数据存储路径
- 文本历史保留策略
- 客户端启动时是否自动启动当前模型

默认模型启动策略：

- 默认首次使用时启动模型服务。
- 设置中可开启“客户端启动时自动启动当前模型”。
- MVP 不做空闲超时自动关闭。

### Debug

Debug 是软件内置常驻功能，不需要启动参数或隐藏开关。

Debug 面板用于调试浮窗、热键、状态机、文本注入和 ASR 服务。

浮窗状态模拟按钮：

- 显示：模型加载中
- 显示：可录音
- 显示：正在聆听
- 显示：识别中
- 显示：正在输入
- 显示：已输入
- 显示：识别结果
- 显示：错误状态
- 隐藏浮窗

热键调试信息：

- 当前热键：右 Alt
- 热键监听状态
- 最后一次触发时间
- 当前输入状态

文本注入调试：

- 测试注入到当前窗口
- 测试弹出结果面板
- 复制模拟文本

ASR 服务调试：

- 启动服务
- 停止服务
- 重启服务
- 健康检查
- 显示服务地址、模型路径、最近一次错误

## 输入浮窗

输入浮窗是临时状态窗口，不是主窗口。

行为：

- 按右 Alt 时出现。
- 默认位于桌面底部居中，距离屏幕底部约 80px。
- 置顶显示。
- 不抢焦点，避免破坏当前输入框焦点。
- 成功输入后自动隐藏。
- 无法输入时保留在桌面上，展示识别文本和复制按钮。

MVP 暂不做靠近光标、多显示器跟随或自定义位置。

浮窗状态：

- 模型加载中
- 可录音
- 正在聆听
- 识别中
- 正在输入
- 已输入
- 未找到可输入位置
- 输入失败
- 服务启动失败

## 右 Alt 交互

右 Alt 是默认全局输入键。实现上应监听 `VK_RMENU`，并处理按键重复、按下/抬起和状态机防重入。

状态机：

```text
Idle
  -> EnsureModelReady
  -> Recording
  -> Transcribing
  -> Injecting
  -> Idle

Idle
  -> EnsureModelReady
  -> Error
  -> Idle

Recording
  -> Cancelled/Error
  -> Idle
```

交互规则：

- 模型未启动时按右 Alt：显示浮窗“模型加载中”，后台启动 `whisper-server`。
- 模型启动完成后：浮窗显示“可录音”。
- MVP 中，模型刚加载完成后不自动录音，用户再次按右 Alt 才开始录音。
- 模型已就绪且处于 Idle 时按右 Alt：开始录音，浮窗显示“正在聆听”。
- Recording 状态按右 Alt：停止录音，进入识别。
- Transcribing 或 Injecting 状态按右 Alt：忽略，避免重复触发。
- 识别成功且文本注入成功：浮窗短暂显示“已输入”，然后隐藏。
- 识别成功但文本注入失败：浮窗保留，显示识别文本和复制按钮。

MVP 不设计取消录音快捷键。取消录音、长按模式、多热键配置留待后续版本。

## ASR 服务设计

MVP 使用客户端托管的 HTTP 模式，不使用 CLI 作为主路径。

`whisper-server` 生命周期：

- 客户端负责启动后台进程。
- 后台进程不显示命令行窗口。
- 服务只监听本地地址 `127.0.0.1`。
- 客户端通过健康检查判断服务是否就绪。
- 客户端退出时停止服务进程。
- MVP 不做空闲超时关闭。

默认启动策略：

- 默认首次使用时启动。
- 设置中可选择客户端启动时预热当前模型。

ASR 后端接口应保持可替换，后续可以接 FunASR、Qwen、Memo 或其他 HTTP ASR 服务。

建议接口：

```csharp
public interface IAsrBackend
{
    string Name { get; }
    Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken);
}
```

请求对象不要只绑定文件路径，应支持内存音频：

```csharp
public sealed class AsrRequest
{
    public required AudioInput Audio { get; init; }
    public string? Language { get; init; }
    public string? Prompt { get; init; }
    public IDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
}
```

音频输入类型：

```text
InMemoryAudioInput
  内存中的 wav/pcm 数据、采样率、声道数、格式

FileAudioInput
  文件路径和格式，用于兼容必须读文件的后端
```

## 音频策略

MVP 不保存音频文件。

默认策略：

- 录音数据保存在内存中。
- 识别完成后释放音频内存。
- 失败、取消或程序退出时也不保留音频。

如果某个后端必须使用文件输入，该后端适配器可以创建临时文件，但必须在本次识别结束后立即删除。这个行为不暴露给用户，也不作为历史记录保存。

## 文本注入策略

MVP 目标是不侵占用户剪贴板。

默认策略：

- 识别完成后尝试判断当前焦点是否可输入。
- 如果存在可编辑目标，优先使用非剪贴板方式注入文本。
- 可探索 `SendInput` Unicode 和 UI Automation。
- 如果注入成功，浮窗自动消失。
- 如果注入失败或没有可编辑目标，浮窗保留并显示识别结果和复制按钮。
- 只有用户点击复制按钮时才写入剪贴板。

建议接口：

```csharp
public interface ITextInjector
{
    Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken);
}
```

注入结果类型：

```text
Success
NoEditableTarget
PermissionDenied
UnsupportedTarget
Failed
```

MVP 可保留兼容模式设计空间，但不把剪贴板粘贴作为默认路径。

## 数据存储

使用 SQLite 单文件数据库。

默认位置建议：

```text
%LocalAppData%\LocalAsrClient\data\client.db
```

设置中允许修改数据存储路径。修改路径的迁移策略可在实现计划中细化，MVP 可先要求重启后生效。

建议表：

```text
settings
  key
  value

daily_stats
  date
  input_count
  success_count
  failed_count
  recording_seconds
  processing_seconds
  character_count
  word_count

transcript_history
  id
  created_at
  text
  character_count
  word_count
  recording_seconds
  processing_seconds
  backend_id
  model_id
```

清理策略：

- `daily_stats` 保留最近 2 个月。
- `transcript_history` 按用户设置保留 1 天、7 天、1 个月或不保存。
- 不保存音频。

## 暂缓功能

MVP 不做：

- LLM 润色、排版、语义改写
- 编程术语智能修正
- 命令模式、代码模式、多模式输入
- 模型下载和安装管理
- 多 ASR 后端 UI 管理
- 空闲超时自动关闭模型服务
- 音频保存
- 一键录音功能
- 取消录音快捷键
- 多语言界面
- 浮窗自定义位置
- 多显示器跟随策略

这些功能可以在基础输入体验稳定后逐步扩展。

## 验收标准

MVP 完成后应满足：

- 启动后显示主窗口并创建托盘图标。
- 最小化或关闭主窗口时隐藏到托盘。
- 托盘单击可唤起窗口，右键菜单可打开窗口或退出程序。
- 右 Alt 能全局触发输入状态机。
- 模型未启动时，右 Alt 能触发后台启动 `whisper-server`，浮窗显示加载状态。
- 模型就绪后，右 Alt 能开始录音，再按右 Alt 能停止录音并识别。
- 识别完成后优先尝试非剪贴板注入。
- 注入成功时浮窗自动隐藏。
- 注入失败或无输入目标时，浮窗显示文本和复制按钮。
- 语音音频不保存到历史或长期存储。
- 每日统计不可关闭，并最多保留 2 个月。
- 文本历史按 1 天、7 天、1 个月或关闭策略保存。
- Debug 面板可以模拟浮窗主要状态。

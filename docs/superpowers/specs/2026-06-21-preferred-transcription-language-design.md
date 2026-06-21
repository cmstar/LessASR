# 首选转写语言设置设计

日期：2026-06-21

## 目标

在 LessASR 设置页增加「首选语言」，让用户选择听写时传给 whisper-server 的语言偏好。默认「（自动）」不传语言参数；简中 / 繁中均传 `language=zh`，简繁字形由客户端 OpenCC 后处理完成；**不向 whisper-server 发送 `prompt`**。保存后**下一次听写**即生效，无需重启 whisper-server 或客户端。

## 背景

- 升级前 `DictationOrchestrator` 写死 `Language: "zh"`。
- `WhisperServerClient` 仅按设置条件发送 `language`。
- whisper-server 的 `language` 在每次 `POST /inference` 请求中读取，**不是**进程启动参数。

## 范围

### 包含

- 设置页「首选语言」下拉框（15 项）
- `AppSettings` 持久化与缺省 / 非法值回退
- 听写转写时按设置组装 `AsrRequest` 并发送 HTTP `language`
- OpenCC 简繁后处理（`OpenccNetLib`：`t2s` / `s2t`）
- Core 层单元测试与 `docs/api.md` 更新

### 不包含

- Whisper 完整 99 语言列表或可搜索语言选择器
- whisper-server `prompt` 参数
- 英文专用模型（`.en`）检测或警告
- 设置页以外的语言切换入口（托盘、热键等）

## 业务规则

### 语言选项（共 15 项）

| 排序 | 显示名 | 存储 id | whisper `language` | OpenCC 后处理 |
| --- | --- | --- | --- | --- |
| 1 | （自动） | `auto` | 不传 | 无 |
| 2 | 简体中文 | `zh-Hans` | `zh` | `t2s` |
| 3 | 繁体中文 | `zh-Hant` | `zh` | `s2t` |
| 4 | English | `en` | `en` | 无 |
| 5–15 | 见实现目录 | `ar`…`vi` | 同 id | 无 |

### 排序规则

1. **（自动）** 固定第一项。
2. **简体中文**、**繁体中文** 固定第二、三项。
3. **English** 固定第四项。
4. 其余 11 项按中文显示名拼音升序。

### 默认值与迁移

- 新安装与缺省：`PreferredTranscriptionLanguageId = "auto"`。
- SQLite 无该 key、值为空、或 id 非法：回退 `auto`。
- 升级前固定传 `language=zh`；升级后未保存过的老用户变为「自动」。

### 生效时机

保存设置后，**下一次**听写读取最新配置；无需重启 whisper-server 或 LessASR。

### HTTP 请求规则

- 始终包含：`file`、`response_format=json`。
- `auto`：不添加 `language`。
- 其他非自动项：添加对应 `language`。
- **不发送 `prompt`**。

## 用户体验与界面

- 「设置」Tab，「模型文件路径」之上：标签 **首选语言** + `ComboBox`。
- 灰色说明：**保存后下次听写生效。**
- 修改后须点击 **保存设置** 才持久化。

## 技术方案

| 层 | 职责 |
| --- | --- |
| `TranscriptionLanguageCatalog` | 15 项定义、`ResolveLanguage(id)` |
| `OpenCcScriptConverter` | `zh-Hans`→`t2s`，`zh-Hant`→`s2t` |
| `TranscriptionScriptPostProcessor` | 按设置调用 OpenCC |
| `DictationOrchestrator` | 转写前读 language；转写后走 post-processor |
| `WhisperServerClient` | 仅条件发送 `language` |
| `SettingsViewModel` + `MainWindow.xaml` | UI 绑定 |

## 测试要求

- `TranscriptionLanguageCatalogTests`：`ResolveLanguage` 映射与非法 id 回退
- `OpenCcScriptConverterTests` / `TranscriptionScriptPostProcessorTests`：简繁转换
- `WhisperServerClientTests`：multipart 不含 `prompt`
- `SqliteSettingsStoreTests`：字段 round-trip 与缺省
- `DictationOrchestratorTests`：`zh-Hans` 时 ASR 请求 `Language=zh`

## 验收标准

1. 设置页 15 项语言，默认「（自动）」。
2. 「（自动）」听写：HTTP 不含 `language`。
3. 「简体中文」听写：`language=zh`，输出经 OpenCC 转简体。
4. 「繁体中文」听写：`language=zh`，输出经 OpenCC 转繁体。
5. HTTP 请求均不含 `prompt`。
6. `dotnet test LocalAsrClient.sln` 通过。

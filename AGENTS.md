# Agent Instructions

## Project Overview

Read `README.md` for human-facing project introduction.

本项目是 Windows WPF 本地语音输入客户端 **LessASR**（产品名；代码项目名仍为 `LocalAsrClient`），核心逻辑在 `LocalAsrClient.Core`，WPF 壳在 `LocalAsrClient.App`。

本项目**由 AI Agent 编码实现**。开发者负责提需求、产品设计、确定业务逻辑与边界条件、验收测试，但通常不直接阅读和修改源码。

## Commands

- Restore: `dotnet restore LocalAsrClient.sln`
- Build: `dotnet build LocalAsrClient.sln`
- Test: `dotnet test LocalAsrClient.sln`
- Run: `dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj`
- Publish: `dotnet publish src/LocalAsrClient.App/LocalAsrClient.App.csproj -c Release -r win-x64`

## Project Structure

- `src/LocalAsrClient.Core/`：可测试核心库（状态机、ASR 编排、SQLite 持久化、平台抽象接口）
- `src/LocalAsrClient.App/`：WPF 应用（托盘、浮窗、热键、音频、文本注入）
- `tests/LocalAsrClient.Core.Tests/`：Core 单元测试
- `docs/`：架构、领域、开发约定与规格文档
- `docs/superpowers/plans/`：分步实现计划

## Required References

- Architecture: `docs/architecture.md`
- Domain: `docs/domain.md`
- Development rules: `docs/development.md`
- MVP spec: `docs/superpowers/specs/2026-06-07-windows-asr-client-mvp-design.md`
- Implementation plan: `docs/superpowers/plans/2026-06-07-windows-asr-client-mvp.md`
- whisper-server API: `docs/api.md`

## Working Rules

本节定义 Agent 与人类协作时的行为准则。

**协作**

- 在已明确的产品和业务边界内，Agent 负责完整实现并自主决定文件、模块、命名、内部 API、数据结构、既有库用法、测试等技术细节；禁止询问人类「代码怎么写」，也不得要求人类阅读源码、diff 或日志作技术判断。
- Agent 应先从文档、源码、测试、相关 skill 和工具中查明信息。多个方案实现相同产品行为时，按既有模式、简单、改动小、易验证和可回退的原则自行选择；非阻塞的技术歧义应合理假设并继续推进。
- 仅当项目上下文无法推断，且不同答案会明显改变产品行为、业务规则、交互、优先级或验收标准，或涉及范围扩大、难以回退、数据兼容、安全隐私、持续外部成本等重大影响时，才向人类确认。
- 只有拟实施方案会实际新增或扩大以下影响时，才须在实施前说明并确认：访问既有边界外的资源、影响第三方系统、引入第三方依赖或外部运行时、增加运行时持久化存储、申请系统或硬件权限，或显著增加硬件与网络资源占用。未实际新增或扩大的事项无需说明或确认，也不得为免责而列举；同范围复用和当前需求已明确授权的行为无需重复确认，且不得扩大授权。
- 必须提问时，应使用产品和用户体验语言说明影响与推荐项。除非人类明确只需分析、设计或计划，否则「实现」「修复」「调整」默认包含代码修改和相关检查；汇报侧重用户可见变化、验证方法、重要假设和待决产品问题。

**实现约束（Agent 自行遵守，无需向人类确认）**

- 保持 WPF/Win32 代码在 `LocalAsrClient.App`，Core 不得依赖桌面会话。
- 优先小步、聚焦的改动；复用现有抽象与命名约定。
- 修改架构、领域规则或对外契约时同步更新 `docs/` 相关文档。
- 完成代码修改后运行最相关的 `dotnet test` 或 `dotnet build` 验证。
- 实现计划时按 Task 顺序执行，遵循 TDD 步骤（先写失败测试再实现）。

## Safety

- 不要提交密钥、令牌、密码、Cookie、私钥或生产数据。
- 不要未经明确批准执行破坏性数据迁移或删除操作。
- 不要修改用户本地 `%USERPROFILE%\.lessasr\` 目录中的生产数据库（测试使用内存 SQLite）。

## Do Not Auto-Modify

- `.docs/` 团队级设计指南原文
- 用户机器上的模型文件与 `whisper-server` 二进制

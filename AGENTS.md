# Agent Instructions

## Project Overview

Read `README.md` for human-facing project introduction.

本项目是 Windows WPF 本地语音输入客户端 **LessASR**（产品名；代码项目名仍为 `LocalAsrClient`），核心逻辑在 `LocalAsrClient.Core`，WPF 壳在 `LocalAsrClient.App`。

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

- 人类参与者通常不阅读代码，因此 Agent 应自主完成实现，并在沟通中避免向人类询问编码与实现细节，不向人类询问「代码怎么写、放哪个文件、用什么模式或库、如何命名」等实现细节；应自行阅读 `src/`、`docs/` 与相关 skill，按项目既有模式决策。
- 应向人类确认的是：产品边界、业务规则、交互与体验、优先级、验收结论，以及文档未覆盖且会导致不同产品行为的歧义。
- 汇报时侧重用户可见变化、如何验证、待决的产品问题；避免把实现细节选择题抛给人类。

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

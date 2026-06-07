# LocalAsrClient

Windows 本地语音输入客户端 MVP。程序常驻系统托盘，通过右 Alt 键触发听写，识别结果优先直接写入当前文本框。

## 快速开始

### 前置条件

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 本地 `whisper-server` 可执行文件与 Whisper 模型文件

### 本地开发

```powershell
dotnet restore LocalAsrClient.sln
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

### 运行

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

首次运行需在「设置」页配置模型路径与 `whisper-server` 路径。

## 构建

```powershell
dotnet build LocalAsrClient.sln -c Release
```

## 测试

```powershell
dotnet test LocalAsrClient.sln
```

Core 层测试无需桌面会话即可运行。

## 部署

```powershell
dotnet publish src/LocalAsrClient.App/LocalAsrClient.App.csproj -c Release -r win-x64 --self-contained false
```

发布产物位于 `src/LocalAsrClient.App/bin/Release/net8.0-windows/win-x64/publish/`。

## 文档

- 架构说明：`docs/architecture.md`
- 业务领域说明：`docs/domain.md`
- 开发约定：`docs/development.md`
- MVP 设计规格：`docs/superpowers/specs/2026-06-07-windows-asr-client-mvp-design.md`
- whisper-server 接口：`docs/api.md`
- Agent 工作入口：`AGENTS.md`

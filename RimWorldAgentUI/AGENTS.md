# RimWorldAgentUI WORKING NOTES

## OVERVIEW

`RimWorldAgentUI` 是独立 Web UI Mod：在游戏内提供 HTTP 静态页面服务，并通过 WebSocket 连接 `RimWorldAgent` 的 `UIMessageBus`。

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Mod 入口 | `AgentUIMod.cs` | UI Mod 初始化 |
| 游戏内 UI 状态 | `UI/` | Dialog、MapComponent、BridgeClient |
| Web 服务 | `WebUI/HttpServer.cs` | HTTP `:19997` 静态服务 |
| 静态资源 | `resource/WebUI/` | 构建时复制到 publish |
| Mod 元数据 | `resource/About/About.xml` | RimWorld About 配置 |

## BOUNDARIES

- UI 通过 WebSocket 消费 `UiMessage`，不要解析 Claude SDK 原始消息。
- UI 不直接调用游戏 MCP Tool；业务动作应进入 `RimWorldAgent` / `UIMessageBus` 路径。
- 保持该项目为 `net472` Library，RimWorld 运行时依赖通过 `HintPath` 引入。

## COMMANDS

```bash
dotnet build ../RimWorldAI.sln
dotnet build ../RimWorldAI.sln --configuration Release
```

## DO NOT

- 不要把 Agent Runtime 状态机复制到 UI 项目。
- 不要记录 token、认证 header 或完整敏感会话内容。
- 不要直接依赖 `../RimWorldAgent` 项目类型；保持协议边界。

# RimWorldAgent WORKING NOTES

## OVERVIEW

`RimWorldAgent` 是 Agent Runtime：既可作为独立 EXE 连接游戏 MCP，也可作为 RimWorld Mod 内嵌运行；通过 Node.js `cc-companion` 桥接 Claude Agent SDK。

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Engine 装配 | `Core/AgentEngine.cs` | MCP client/server、loop、runtime 初始化 |
| 主循环 | `Core/AgentRuntime/AgentLoop.cs` | 会话、UI 总线、历史录制、SDK 中继 |
| 调度 | `Core/AgentRuntime/AgentOrchestrator.cs` | Plan/Act、冷启动、中断 |
| Tool 代理 | `Core/AgentRuntime/ToolDispatcher.cs` | Agent MCP 工具调用分发 |
| SDK 消息 | `Core/models/SdkMessage.cs` | typed SDK protocol model |
| SDK→UI | `Core/AgentRuntime/SdkMessageParser.cs` | 转为 `UiMessage` |
| UI WS | `Core/UIMessageBus.cs` | `:19999` 广播和客户端消息 |
| Companion 管理 | `Core/CcbManager/` | 子进程、WS、token 用量 |
| 数据抽象 | `Core/Data/` | `IDbStore` / `IConversationStore` |
| EXE 入口 | `Exe/Program.cs` | standalone runner |
| MOD 入口 | `Mod/GameComponent_RimWorldAgent.cs` | RimWorld 生命周期 |
| SDK 桥 | `cc-companion/` | TypeScript wrapper |

## ARCHITECTURE RULES

- `RimWorldAgent` 不引用 `RimWorldMCP`；所有游戏操作通过 MCP `:9877`。
- EXE/MOD 差异通过 `IGameStateProvider`、`IDbStore`、`IConversationStore` 注入，不写模式分支散落逻辑。
- SDK 消息先解析为 `SdkMessage`，再由 `SdkMessageParser` 转为 `UiMessage`；不要让 UI 层解析 SDK 原始 JSON。
- `UIMessageBus` 只负责 `UiMessage` 广播和客户端消息事件，不承担 SDK 协议职责。
- `cc-companion` 只收 `chat`/`abort`，吐 SDK 消息；不要把 C# 业务逻辑挪进 companion。

## MESSAGE FLOW

```
UI/Web/Dialog → UIMessageBus :19999 → AgentLoop
AgentLoop → CcbWebSocket → cc-companion :19998 → Claude Agent SDK
SDK tool call → Agent MCP :9878 → Proxy → RimWorldMCP :9877
SDK stream/result → SdkMessage → SdkMessageParser → UiMessage → UIMessageBus
```

## DATA / HISTORY

- 会话历史统一走 `IConversationStore`：User、Assistant、System、ToolCall、ToolResult。
- EXE 模式使用 SQLite/JSON 相关实现；MOD 模式注意 RimWorld 环境和原生 SQLite DLL 路径。
- 用户消息在 C# 侧录制与推送；不要依赖 SDK echo 作为唯一用户消息来源。
- tool duration 由 `AgentLoop.OnToolUse` 临时记录，SDK echo `tool_result` 时合并落盘。

## PLAN / ACT RULES

- `enter_plan` 暂停游戏用于分析；`enter_act` 恢复游戏用于执行。
- `GamePaceController` 直接调用 MCP `toggle_pause`，不要维护一套独立暂停缓存。
- Critical 游戏事件会 abort 当前会话并注入通知；Warning/Info/Silent 主要走 suffix 注入。
- `BuildModeSuffixAsync()` 是提醒注入点，改动时同时检查 UI、历史、SDK 消息路径。

## COMPANION / SDK GOTCHAS

- `SdkMessage.FromJson` 对未知字段应 warn 但不拒绝，兼容 SDK 扩展。
- stream text delta 已推 UI，assistant 完整 text block 不要再次推送，避免双渲染。
- 当前部分性能字段已解析但未完全消费：`ttft_ms`、`DurationMs`、`DurationApiMs`、`TotalCostUsd`、`NumTurns`。
- SDK `cwd` 会 sanitize 到 `~/.claude/projects/<sanitized-cwd>/`；不同存档 cwd 形成隔离，不要改 SDK base path。

## COMMANDS

```bash
dotnet build ../RimWorldAI.sln
dotnet run --project ../RimWorldAgent.Tests
cd cc-companion && npm install && npm run build
```

## DO NOT

- 不要直接依赖或引用 `../RimWorldMCP` 项目。
- 不要在 UI 层处理 SDK 原始协议。
- 不要记录 token、API key、认证 header 或敏感 session 信息。
- 不要空 `catch`，除非是 `OperationCanceledException`。

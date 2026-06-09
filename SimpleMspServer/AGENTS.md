# SimpleMspServer WORKING NOTES

## OVERVIEW

`SimpleMspServer` 是共享 MCP/JSON-RPC 协议库，被游戏侧 `RimWorldMCP` 和 Agent 侧 `RimWorldAgent` 共同引用。

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| 协议消息 | `Mcp/McpMessage.cs` | JSON-RPC/MCP message model |
| Tool 提供者接口 | `Mcp/IToolProvider.cs` | Agent/MCP 工具边界 |
| 服务宿主 | `McpServiceHost.cs` | MCP 服务启动/请求处理 |
| 日志抽象 | `IMspLog.cs` | 不绑定具体项目日志实现 |

## BOUNDARIES

- 这里只放协议、transport、host 抽象；不要加入 RimWorld 游戏 API、Agent Runtime、UI 逻辑。
- 公共接口改动会同时影响 `RimWorldMCP` 和 `RimWorldAgent`；先用 CodeGraph 查调用方。
- 该项目是 `net472` Library，依赖保持轻量。

## COMMANDS

```bash
dotnet build ../RimWorldAI.sln
```

## DO NOT

- 不要引用 `Assembly-CSharp`、Unity、Harmony 或 Agent UI 类型。
- 不要在协议库里做业务级重试、预算、事件分级或游戏状态判断。

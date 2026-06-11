# PROJECT KNOWLEDGE BASE

**Generated:** 2026-06-08
**Branch:** rewrite/main

## OVERVIEW

RimWorld AI 是一个 .NET Framework 4.7.2 多项目 RimWorld AI mod 系统：游戏侧 `RimWorldMCP` 暴露 MCP Tools，`RimWorldAgent` 运行 Agent Runtime，`RimWorldAgentUI` 提供 UI，`SimpleMspServer` 是共享 MCP 协议库。

## STRUCTURE

```
RimWorldMod_RimWorldAI/
├── RimWorldAI.sln              # 5 个 net472 项目
├── SimpleMspServer/            # JSON-RPC + MCP/SSE 共享库
├── RimWorldMCP/                # 游戏 Mod DLL，MCP Server :9877，100+ Tool
├── RimWorldAgent/              # Agent Runtime，EXE/MOD 双模式，SDK 桥接
├── RimWorldAgentUI/            # Web UI Mod，HTTP :19997 + WS UI
├── RimWorldAgent.Tests/        # 集成测试 Console
├── scripts/publish.ps1         # Steam Workshop 准备/推送脚本
└── CLAUDE.md                   # 原项目详细架构文档
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| 总览架构 | `CLAUDE.md` | 端口、数据流、模块边界 |
| Solution / build | `RimWorldAI.sln` | 5 个项目，全 `net472` |
| 游戏 MCP 入口 | `RimWorldMCP/RimWorldMCPMod.cs` | Mod loader + 设置 UI |
| 游戏 Tool | `RimWorldMCP/Tools/` | 100+ 操作/查询工具 |
| Agent 初始化 | `RimWorldAgent/Mod/GameComponent_RimWorldAgent.cs` | MOD 生命周期 + Engine 初始化 |
| Agent 主循环 | `RimWorldAgent/Core/AgentRuntime/AgentLoop.cs` | 会话、UI 中继、历史记录 |
| 调度策略 | `RimWorldAgent/Core/AgentRuntime/AgentOrchestrator.cs` | Plan/Act、事件中断 |
| SDK 消息模型 | `RimWorldAgent/Core/models/SdkMessage.cs` | Claude SDK typed messages |
| UI 总线 | `RimWorldAgent/Core/UIMessageBus.cs` | WS :19999 广播与客户端消息 |
| Node SDK 桥 | `RimWorldAgent/cc-companion/` | `@anthropic-ai/claude-agent-sdk` 包装 |
| 发布 | `scripts/publish.ps1` | 原作者 Steam Workshop 发布链路，当前二开默认不要使用 |

## ARCHITECTURE RULES

- `RimWorldMCP` 与 `RimWorldAgent` 保持零编译引用；两边只通过 MCP HTTP/SSE 通信。
- `SimpleMspServer` 是共享协议层；不要把游戏业务逻辑塞进这里。
- Agent 有 EXE 与 MOD 两种启动模式，依赖 `IGameStateProvider` / `IDbStore` / `IConversationStore` 抽象切换实现。
- `cc-companion` 只做 SDK 桥接；C# 侧通过 WS :19998 收发 `chat` / `abort` / SDK 消息。
- UI 只消费 `UiMessage`；不要让 UI 层直接依赖 SDK JSON 或游戏 MCP Tool 结构。
- 当前 Agent 端基于 `@anthropic-ai/claude-agent-sdk`，默认属于 Anthropic/Claude Code 生态；后续二开目标是支持三类 provider：Anthropic、OpenAI 官方 SDK、OpenAI-compatible 网关。实现时应先抽象 provider/adapter，不要把 OpenAI 格式硬塞进现有 Claude SDK 消息解析层。

## COMMANDS

```bash
dotnet build RimWorldAI.sln
dotnet build RimWorldAI.sln --configuration Release
dotnet run --project RimWorldAgent.Tests
```

原作者 Windows Steam Workshop 发布流程（当前二开不要使用，除非用户明确要求）：

```powershell
.\scripts\publish.ps1 -Prepare
.\scripts\publish.ps1 -Push -Agent
.\scripts\publish.ps1 -Push -All
```

CodeGraph 初始化 / 同步：

```bash
/opt/opencode-runtime/bin/codegraph init -i /root/CODE/RimWorld/RimWorldMod_RimWorldAI
/opt/opencode-runtime/bin/codegraph sync /root/CODE/RimWorld/RimWorldMod_RimWorldAI
```

## CONVENTIONS

- C# 使用 4 空格缩进；JSON/XML/Markdown/YAML 使用 2 空格缩进；遵守 `.editorconfig`。
- 禁止空 `catch`，只有 `OperationCanceledException` 允许空处理；其他异常必须记录异常类型和 `ex.Message`。
- 日志不得写入 token、密码、密钥；提交前清理 `[CCGUI_DEBUG]`。
- commit 信息使用简体中文 + Conventional Commits。
- **交付习惯（默认自动，无需逐次确认）**：每完成一处经验证（`tsc --noEmit` / `dotnet build` 0 error）的改动后，默认自动 `commit` + `push` 到 `origin/main`，并重新打包测试 zip 上传公网门户（temp.wuxie233.com，走 public-file-portal）把下载链接给用户实测；逻辑独立的改动拆成多个 commit。仅当用户当轮明确说“先别提交/先别打包”时才暂缓。打包＝`dotnet build Release` →`zip publish/` 三个 mod，**不走** Steam Workshop 推送脚本。
- `publish/`、`bin/`、`obj/`、`.codegraph/` 是生成物，不进入版本库。
- 用户当前是本地二开自用：测试以手动放入 RimWorld 本地 Mods 目录并在游戏内加载为准；不要运行原作者的 Steam Workshop 推送脚本或 `steamcmd +workshop_build_item`，除非用户明确要求发布。

## CRITICAL GOTCHAS

- RimWorld `IntVec3(x, y, z)` 中 `y` 是海拔，用户 `pos_y` 必须映射到 `z`：正确写法是 `new IntVec3(posX, 0, posY)`。
- 新增 Tool 使用 `pos_x/pos_y` 到 `end_x/end_y` 的左下到右上范围；不要设计中心点+半径 API。
- Pawn / Thing 操作使用 `thingIDNumber` 精确定位，不用名称字符串匹配。
- 数据量可能超过 20 条的 list tool 必须分页，默认 `page_size=10`。
- `HttpListener.Start()` 可能因端口/权限失败；成功后再保存 transport 实例，并提供中文诊断。
- `EventLevel` 枚举值在 MCP 与 Agent 两侧必须保持一致。

## NOTES

- `README.md` 明确项目为学习/探索用途，不保证稳定性。
- `todo.md` 记录了 CCB bridge、echo 校验、dialog token 显示、abort suffix 等进行中问题。
- 子项目旧有 `CLAUDE.md` 是详细资料源；本文件是 OpenCode 快速工作入口。

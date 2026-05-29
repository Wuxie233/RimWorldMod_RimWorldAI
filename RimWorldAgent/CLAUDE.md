# RimWorldAgent

AI Colony Runtime — 多 Agent 自主管理殖民地。通过 MCP 协议连接 RimWorldMCP，集成 Claude Agent SDK。

**相关项目**:
- MCP Server: `../RimWorldMCP/`（游戏 Mod DLL，99+ Tool）
- MCP 协议库: `../SimpleMspServer/`（JSON-RPC + SSE Transport）
- CC Companion: `./cc-companion/`（Node.js，CCB 桥接）

## 项目结构

```
RimWorldAgent/
├── CLAUDE.md
├── RimWorldAgent.csproj       ← 单一项目 (net472, OutputType=Exe)
├── README.md
├── resource/                  ← MOD 元数据（构建时复制到根 publish）
│   ├── About/About.xml
│   └── Skills/*.md (13个)
├── Core/                      ← 共享逻辑
│   ├── AgentRuntime/          Scheduler / TaskBoard / Memory / AgentOrchestrator / AgentConfig / ContextBuilder / InternalTools / ToolDispatcher
│   ├── Mcp/                   MCP 客户端 + Agent MCP Server (:9878)
│   └── CcbManager/            CCB 子进程管理 + CcbWebSocket
├── Exe/                       ← EXE Loader
│   └── Program.cs             入口：find CCB → spawn → connect MCP → Agent Main Loop
├── Mod/                       ← MOD Loader (RimWorld 加载)
│   ├── GameComponent_RimworldAgent.cs
│   └── UI/
│       ├── Dialog_AiChat.cs       聊天窗 (Ctrl+Shift+C)
│       ├── MapComponent_McpUI.cs  右下角按钮
│       ├── ChatStateTypes.cs      本地类型
│       └── TodoManager.cs         本地 TODO 管理
├── cc-companion/              ← CCB 桥接 (Node.js, npm start)
└── publish/                   ← 构建输出 (git ignored)
```

## 架构

### 零引用设计

RimWorldAgent 和 RimWorldMCP **互不引用**，仅通过 MCP 协议通信。

```
RimWorldAgent (EXE/MOD)             RimWorldMCP (Mod DLL)
┌──────────────────────┐           ┌──────────────────────┐
│ AgentRuntime         │   HTTP    │ MCP Server :9877     │
│ McpClient ───────────── POST ───→ /mcp (tools/call)    │
│ McpClient ───────────── GET ────→ /sse (事件 SSE 推送)  │
│ AgentMcpServer :9878  ←───────── CCB SDK tools/call     │
│ CcbManager ── spawn ──→ cc-companion (Node.js WS :19999) │
└──────────────────────┘           └──────────────────────┘
```

### 两种启动模式

| 模式 | 进程 | 说明 |
|------|------|------|
| **EXE** | `RimWorldAgent.exe` | 独立进程，开发/测试用 |
| **MOD** | RimWorld 加载 DLL | 游戏内运行, GameComponent 驱动 |

### Agent 架构总览

```
Overseer (策略, 每天/12h) → 全局摘要分析 → 发布 TaskBoard 目标
Economy (生产+军械, 每 2-6h) → 建造/制造/装备分配
Combat (战斗, L3 事件驱动) → 暂停→分析→部署→接敌→收尾→退出
Medic (医疗, 每天+战斗后) → 治疗/手术/仿生体
```

## 开发

### 构建

```bash
cd F:\RiderProjects\RimWorldMCP
dotnet build RimWorldAI.sln           # 全量构建
dotnet build RimWorldAgent/RimWorldAgent.csproj  # 单独 Agent
```

### 关键约束

- 不引用 RimWorldMCP（零编译依赖）
- 引用 SimpleMspServer（MCP 协议共享库）
- 游戏数据通过 MCP HTTP 获取
- 游戏操作通过 MCP Tool 调用
- TaskBoard/Memory 持久化为 JSON

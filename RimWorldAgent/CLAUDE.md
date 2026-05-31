# RimWorldAgent

AI Colony Runtime — AI 自主管理殖民地。通过 MCP 协议连接 RimWorldMCP，集成 Claude Agent SDK。

**相关项目**:
- MCP Server: `../RimWorldMCP/`（游戏 Mod DLL，99+ Tool）
- MCP 协议库: `../SimpleMspServer/`（JSON-RPC + SSE Transport）
- CC Companion: `./cc-companion/`（Node.js，CCB 桥接）

## 项目结构

```
RimWorldAgent/
├── CLAUDE.md
├── RimWorldAgent.csproj       ← 单一项目 (net472, OutputType=Exe)
├── resource/                  ← MOD 元数据（构建时复制到根 publish）
│   ├── About/About.xml
│   └── Skills/*.md (13个)
├── Core/                      ← 共享逻辑
│   ├── AgentRuntime/          Scheduler / AgentOrchestrator / AgentConfig / ContextBuilder / InternalTools / ToolDispatcher
│   │   └── CoreLog.cs         日志抽象出口（委托注入，Core 层统一日志接口）
│   ├── Data/                  ★ 数据抽象层 — IDbStore + JsonDbStore/ScribeDbStore 实现
│   ├── Mcp/                   MCP 客户端 + Agent MCP Server (:9878)
│   └── CcbManager/            CCB 子进程管理 + CcbWebSocket + TokenUsageTracker
├── Exe/                       ← EXE Loader
│   └── Program.cs             入口：find CCB → spawn → connect MCP → Agent Main Loop
├── Mod/                       ← MOD Loader (RimWorld 加载)
│   ├── GameComponent_RimworkAgent.cs   ★ 游戏生命周期 — InitAgentRuntime() 异步启动 + ShutdownEngine() 杀 CCB
│   ├── CompanionInstaller.cs           npm install 管理 + Node.js 查找
│   ├── SafeLog.cs                      线程安全日志队列（后台入队、主线程 Flush）
│   ├── ScribeDbStore.cs                Token 数据 Scribe 持久化（MOD 模式）
│   ├── AgentModSettings.cs             Mod 设置（Token 预算、思考模式等）
│   ├── Hook_Bootstrap.cs               Harmony 补丁引导（[StaticConstructorOnStartup]）
│   ├── Hook_GameDispose.cs             Game.Dispose 后杀 CCB 子进程释放端口
│   └── UI/
│       ├── Dialog_AiChat.cs            聊天窗 (Ctrl+Shift+C) — 三栏：对话(60%)+工具卡片+任务
│       ├── Dialog_AgentLoading.cs      ★ npm 安装/CCB 启动加载提示窗口
│       ├── MapComponent_McpUI.cs       右下角按钮 + 自动打开对话框
│       ├── ChatStateTypes.cs           ★ 聊天状态管理 — 流式 delta 替换、事件队列、CCClient 桥接
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

单 Agent (Commander) 全权负责殖民地所有事务：策略规划、生产建造、战斗指挥、医疗管理。

调度优先级：中断请求 → 每日 PLAN → 定期 ACT 检查 (每 4 游戏小时)

所有通知（MCP 事件、弹框检测）均触发立即中断，通过 `SendAbort()` 打断当前 CCB 会话。每轮工具结果末尾追加当前模式+速度状态。

### Plan/Act 阶段

AI 通过工具显式控制游戏暂停/恢复：
- `enter_plan()` — 暂停游戏，进入规划阶段
- `enter_act()` — 恢复游戏，进入执行阶段
- ACT 模式下 AI 始终在线（3 倍速只是游戏状态，不影响 Agent 可用性）

### Tool Result Suffix 双工通知

Agent 通过 MCP 工具设置一次性 suffix，MCP Server 在下一次工具结果末尾自动追加并清空。AI 在工具调用结果中即时看到通知。

- `set_tool_result_suffix(suffix)` — 设置一次性后缀，追加后自动清空

**NotisAgent 统一入口**：`AgentOrchestrator.NotisAgent(notification)` 封装双路逻辑：
- SessionMcp 可用 → `set_tool_result_suffix`（AI 下次工具结果中看到，优先）
- SessionMcp 不可用或失败 → CcbWs 直接发送到 Companion（降级）
- 不再判断 `IsRunning`——ACT 阶段 AI 始终在线

内部工具通过 `AgentOrchestrator.SessionMcp` 转发到 MCP Server。MCP 侧用 `volatile string ToolResultSuffix` 存储，`ExecuteAsync` 中追加后立即清空。

### Internal Tools（Agent 端，通过 AgentMCP :9878 暴露给 CCB）

这些工具不在 RimWorldMCP 模块中，由 Agent 内部实现：

| Tool | 说明 | 实现 |
|------|------|------|
| `get_skills` | 列出可用领域技能 | `InternalToolRegistry` → `SkillRegistry` |
| `active_skill` | 激活获取 Skill 内容 | `InternalToolRegistry` → `SkillRegistry` |
| `enter_plan` / `enter_act` | Plan/Act 阶段切换 | `InternalToolRegistry` → `GamePaceController` |
| `set_tool_result_suffix` | 设置一次性工具结果后缀 | `InternalToolRegistry` → MCP Server |

### 中断机制

所有 MCP 事件和弹框检测均触发立即中断：
- `AgentOrchestrator.RequestInterrupt(summary)` — 标记中断 + 存储摘要 + **始终** `SendAbort()` 打断 CCB 会话（无会话时 abort 是空操作安全）
- 中断后新会话 prompt 顶部注入通知内容 + "如有必要可以暂停游戏"
- `NotisAgent()` 作为保底双工通知：优先 suffix 注入（SessionMcp 可用时），失败降级直接发送到 Companion
- `AgentOrchestrator.IsRunning` 仅用于 `AgentEngine.TickAsync` 防止重复启动会话，不再作为"休眠/活跃"判断

### CCB 子进程生命周期

**启动**：`StartedNewGame()` / `LoadedGame()` → `InitAgentRuntime()` → `AgentEngine.InitAsync()` → `CcbManager.Start()` → spawn Node.js

**退出清理**（两条路径确保不会残留）：
1. `InitAgentRuntime()` 入口先调 `ShutdownEngine()` → `AgentEngine.Dispose()` → `CcbManager.Dispose()` → `Stop()` → `process.Kill()`
2. Harmony `[Hook_GameDispose]` Postfix 在 `Game.Dispose()` 时直接通过 PID 文件杀进程

**关键**：RimWorld 返回主菜单时 `Game.Dispose()` 会被调用，但 `GameComponent` 不获通知。因此靠下次 `StartedNewGame()` / `LoadedGame()` 入口主动杀残留 + Hook_GameDispose 作保底。

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
- 数据持久化通过 Core.Data/ 抽象层（Token），JsonDbStore (EXE) 和 ScribeDbStore (MOD) 两种实现

### 日志系统

**两层架构**：

```
Core 层           CoreLog (抽象出口) ──委托注入──→ 宿主回调
Mod 层            直接调用 CoreLog     ──注入──→ SafeLog (ConcurrentQueue) ──Flush──→ Verse.Log
```

- **`CoreLog`**（`Core/AgentRuntime/CoreLog.cs`）：Core 层统一日志接口，不引用 Verse。通过 `OnInfo/OnWarn/OnError/OnDebug` 四个委托注入宿主实现。`AgentEngine.InitAsync()` 中初始化所有四个委托
- **`SafeLog`**（`Mod/SafeLog.cs`）：线程安全队列，后台线程（WS ReceiveLoop、CCB stdout）入队，主线程 `GameComponentUpdate` 中 `Flush()` 写入 `Verse.Log`
- **统一规则**：Mod 层全部使用 `CoreLog.Info/Warn/Error`，不直接调 `SafeLog`
- **调试日志**：临时调试用 `[CCGUI_DEBUG]` 前缀，问题解决后 grep 清理

### 异常处理规范

**任何时候捕获异常都不允许忽略（禁止空 catch）**。每个 catch 必须记录日志，包含异常类型和原始错误信息。涉及外部调用（HTTP、WebSocket、文件 I/O、进程管理）时必须展开 `InnerException` 链输出完整堆栈。

**格式**：`$"[模块标识] 操作描述: {ex.GetType().Name}: {ex.Message}"`

**UnwrapException 辅助方法**（Core 层共享）：
```csharp
internal static string UnwrapException(Exception ex)
{
    var sb = new StringBuilder();
    while (ex != null)
    {
        if (sb.Length > 0) sb.Append(" → ");
        sb.Append($"{ex.GetType().Name}: {ex.Message}");
        ex = ex.InnerException;
    }
    return sb.ToString();
}
```

**允许精简**：`OperationCanceledException` 无需日志，保留空 catch

## Claude Code 桥接

游戏事件通过 WebSocket 推送到本地 Node.js 进程（CC Companion），Companion 使用 Claude Agent SDK 与 Claude API 通信。Claude 的响应广播回游戏内聊天窗口。

### 数据流

```
RimWorld (C#)                  CC Companion (Node.js)       Claude API
    │                                │                         │
    │ 游戏事件(WS)                     │  SDK query()            │
    │──────────────────────────────▶ │────────────────────────▶│
    │                                │                         │
    │ 聊天窗 ◀─ WS broadcast ────────│  ◀── assistant/tool ────│
    │                                │                         │
    │  MCP Server :9877 ◀────────────│──── tools/call ─────────│
```

### MessageBus 双总线机制

Companion 通过 `cc-companion/bridge/message-bus.ts` 集中管理所有 WebSocket 广播消息，分为两条独立总线：

```
                    Companion
                    ┌──────────────────────────────────────┐
                    │                                      │
  C# (RimWorld) ──→│  onEvent  ──→ Game Bus ──→ Web 页面   │
                    │    │           colony-stats          │
                    │    │           budget-status         │
                    │    │           user (回显)            │
                    │    │           error                 │
                    │    │                                 │
                    │    └──→ inputStream.enqueue()        │
                    │              │                       │
                    │              ▼                       │
                    │         SDK query()                  │
                    │              │                       │
                    │              ▼                       │
                    │  processResponses ──→ Agent Bus ──→  │
                    │    assistant, stream_event,         │
                    │    result, system/init                │
                    │                                      │
                    └──────────────────────────────────────┘
                              ↓
                     Web 页面 + C# 客户端
```

| Bus | 数据来源 | 消息类型 | 消费者 |
|-----|---------|---------|--------|
| **Game Bus** | C# 游戏事件 → Companion onEvent | `colony-stats`, `budget-status`, `user`(回显), `error`, `model-info` | Web 页面, 游戏内 UI |
| **Agent Bus** | SDK query() → processResponses | `assistant`, `user`, `stream_event`, `result`, `system/init`, `aborted` | Web 页面, C# CCClient |

**关键设计**：
- 两个 Bus 走同一条 WebSocket 连接，通过 `MessageBus` 类型约束保证消息格式一致
- Game Bus 消息不经 SDK（零延迟、不消耗 Token），直接在 Companion 侧广播
- Agent Bus 消息由 `createResponseProcessor` 遍历 SDK AsyncIterator，逐条经 `publishSdkMessage()` 广播
- Companion 是两股流的**多路复用器**——接收端鉴别 `msg.type` 路由到对应处理器

### 连接流程

`CcbManager.SpawnCompanion(sessionId)` 在 Agent 启动时执行：
1. `StopExisting()` — 停止旧进程
2. `KillStaleByPidFile()` — 清理 `.pid` 残留
3. `StartCompanionProcess()` — 创建 `claude-sessions/rimworld-<sessionId>/` 目录，spawn `node --import tsx/esm companion/companion.ts --idle-timeout 30000`；config 通过环境变量传递，SDK 配置由用户 `.claude/settings.json` 提供，Windows 额外通过 Job Object 绑定子进程生命周期
4. `CcbWebSocket.Connect()` — WebSocket 握手（hello/hello-ok）

### 聊天显示状态（ChatDisplayState）

**文件**：`Mod/UI/ChatStateTypes.cs`

流式消息使用 **累积器 + REPLACE** 语义（非 APPEND），每个 `content_block` 独立为一条 `ChatEntry`：

```
stream_event content_block_start{thinking}
  → OnAssistantThinking("")        空串信号 = 结束上条流式 + 新建条目（ThinkingText=""）
stream_event thinking_delta "我在思考..."
  → OnAssistantThinking("我在思考...")  累积 _deltaAccum, 替换 _streamingEntry.ThinkingText
stream_event content_block_start{text}
  → OnAssistantText("")            空串信号 = 结束上条流式 + 新建条目（Text=""）
stream_event text_delta "正文..."
  → OnAssistantText("正文...")      累积 _deltaAccum, 替换 _streamingEntry.Text, 清除 ThinkingText
...
result
  → FinishStreaming()              _streamingEntry → Done
```

**关键设计**：
- `OnUserMessage(text)` — 结束上轮流式 → 新增用户条目（不清理工具卡片）
- `OnSdkMessage` / `OnStreamEvent` 的完整实现在远程 main 的 `Bridge/ChatDisplayState.cs`（远程 main 的 CCClient.ReceiveLoop 直调这些方法）
- 当前 Agent 侧通过 `CcbWebSocket` 事件 → `GameComponent_RimworkAgent.WireChatDisplayUi` → `EnqueueUiEvent` → UI 线程 `DrainEvents` 消费
- SDK echo 的 `user` 消息不解析文本/思考（仅 `CountToolResults`），避免重复显示

### Token 预算与缓存计算

**缓存命中率公式**（与 Web 端一致）：
```
cacheHitRate = TotalCacheReadTokens / (TotalInputTokens + TotalCacheReadTokens + TotalCacheCreateTokens) * 100
```

**Token 提取**：仅从最终 `assistant` 消息的 `usage` 字段提取，跳过 `stream_event` 的 `message_start`（避免 input/cache_read 双重计数）。

**预算更新**：`PushBudgetUpdate` 通过 WS `budget-update` 事件推送 `used/limit/cacheRead/totalInput/cacheCreate` 到 Companion → Web 页面。

### Dialog_AiChat 布局

```
┌─────────────────────────────────────────────────────┐
│  顶栏：殖民地 · 日期                        Token  │
│  [预算横幅：仅 Warning/Critical/Exceeded 时显示]    │
├──────────────────────────┬──────────────────────────┤
│                          │  工具调用 (N)             │
│  对话 (60%)              │  ◎ 工具名称   1.2s       │
│                          │  ✓ 工具名称   0.3s       │
│                          ├──────────────────────────┤
│                          │  任务 (N)                 │
│                          │  ▶ 建造房间               │
│                          │  ○ 种植作物               │
├──────────────────────────┴──────────────────────────┤
│  [输入框                                         发送] │
│  ● 已连接 | PLAN / 暂停 | 透明 - + | 清空 继续 中断 │
└─────────────────────────────────────────────────────┘
```

- 左侧对话流 + 右侧（工具卡片 + 任务面板）均使用手动滚动，无 `BeginScrollView` 闪烁
- 底栏显示 Plan/Act 阶段 + 游戏速度状态（PLAN 琥珀色 / ACT 绿色 / 就绪灰色）

### 加载流程

```
Game Load
  ├── MapComponentOnGUI → !CCClient.IsReady → 弹出 Dialog_AgentLoading
  │     "正在准备 AI 助手..."
  ├── InitAgentRuntime()
  │     ├── 检测 node_modules → "正在安装依赖 (npm install)..."
  │     ├── new AgentEngine, await InitAsync()
  │     │     ├── npm install (如需要)
  │     │     ├── spawn CCB 子进程 (node companion/companion.ts)
  │     │     └── WebSocket hello/hello-ok 握手
  │     ├── CCClient.SetSocket() → IsReady = true
  │     └── ChatDisplayState.Clear() + ToolDispatcher.ResetTaskCount()
  │
  └── MapComponentOnGUI → CCClient.IsReady → 关闭 loading → 打开 Dialog_AiChat
```

### 事件消费（SSE）

Agent 通过 `McpClient.SubscribeEvents(agentId)` 订阅 `GET /sse?agent=overseer`，接收游戏事件推送。事件格式：

```json
{"type":"event","category":"Combat","severity":"Critical","message":"大型突袭来袭"}
```

事件路由规则（已简化）：
- 所有事件均触发中断，不再区分优先级
- 弹框检测由 AgentEngine.TickAsync() 每 2500 tick 扫描

MCP 侧的事件分级表和 Harmony 拦截逻辑详见 `../RimWorldMCP/CLAUDE.md`。

### 参数覆盖顺序

```
用户 .claude/settings.json   ← 低优先级（API Key、Base URL、MCP 等）
        ↓
SDK Options (session.ts)     ← 中优先级（model、settingSources、cwd）
        ↓
环境变量 (ProcessStartInfo)   ← 高优先级（RIMWORLD_PROJECT_PATH、CCB_HOST、CCB_PORT、CCB_AUTH_TOKEN）
```

SDK 从用户本地 `.claude/settings.json` 读取 API Key、Base URL、MCP 服务、权限等配置。C# 不再写入 settings.json，完全沿用用户本地配置。

## 运行教程

### 模式一：EXE 自动模式（开发/测试）

```bash
# 终端 1：启动 companion（由 Agent 自动 spawn，或手动）
cd RimWorldAgent/cc-companion
npm install   # 首次
npm start

# 终端 2：启动 Agent
dotnet run --project RimWorldAgent/RimWorldAgent.csproj
```

### 模式二：MOD 游戏内模式

1. 启动 RimWorld，加载 RimWorldMCP + RimWorldAgent 两个 Mod
2. Agent 自动 spawn companion 进程，连接 MCP Server :9877
3. 打开聊天窗（Ctrl+Shift+C）即可与 AI 交互

### 聊天页面

浏览器打开 `http://127.0.0.1:19999/` 可查看实时对话、SDK 版本、模型、MCP 服务状态。发消息通过 RimWorld 游戏内聊天窗——聊天页面是只读面板。

### 日志查看

| 来源 | 怎么看 |
|------|--------|
| Companion 进程 | Agent `CcbManager` 注册 `OutputDataReceived`/`ErrorDataReceived`，输出到控制台 `[js]` 前缀 |
| SDK 内部 | `session.ts` 中 `stderr: (data) => process.stderr.write(\`[sdk] ${text}\`)` |
| Agent 日志 | `Console.Error` — `[Agent]` 前缀 |

### Token 预算

Token 预算执行在 MCP 侧，详见 `../RimWorldMCP/CLAUDE.md` 的 Token 预算系统章节。

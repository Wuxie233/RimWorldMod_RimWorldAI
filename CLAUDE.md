# RimWorld AI

AI Colony Operating System — 多 Agent 自主管理 RimWorld 殖民地。

## 项目结构

```
RimWorldAI/
├── SimpleMspServer/          ← MCP 协议共享库 (net472)
│   ├── McpMessage.cs         JSON-RPC 2.0 类型
│   ├── ITransport.cs         传输接口
│   ├── SseTransport.cs       SSE + HTTP 传输
│   └── StreamableHttpTransport.cs
│
├── RimWorldMCP/              ← 游戏 Mod (net472)
│   ├── Tools/                100+ 游戏 Tool
│   ├── MCP Server :9877
│   ├── Harmony/              事件拦截
│   └── Transport/            StdioTransport
│
└── RimWorldAgent/             ← Agent Runtime (net472)
    ├── Core/                 共享库
    │   ├── AgentRuntime/     Scheduler + AgentOrchestrator
    │   ├── Mcp/              MCP 客户端 + Agent MCP Server
    │   └── CcbManager/       CCB 子进程 + WebSocket
    ├── Mod/                  MOD 加载模式
    └── cc-companion/        Node.js CCB 桥接

三者互不引用：RimWorldMCP ↔ RimWorldAgent 仅通过 MCP 协议通信。
SimpleMspServer 被两者共同引用。
```

## 构建

```bash
cd F:\RiderProjects\RimWorldMCP
dotnet build RimWorldAI.sln
```

5 个子项目，全部 net472，统一编译。

## 开发规范

以下规则适用于**所有子项目**。

**子项目专属内容见各自 CLAUDE.md**：[RimWorldMCP](RimWorldMCP/CLAUDE.md) | [RimWorldAgent](RimWorldAgent/CLAUDE.md)

### 0. 异常处理必须记录详细信息

**任何时候捕获异常都不允许忽略（空 catch / 空 catch(Exception) / `// ignored` 注释）。** 每个 catch 必须：
- 输出日志，包含异常类型名称、原始报错信息 `ex.Message`
- 使用合适的日志出口：`CoreLog`（Agent 侧）/ `McpLog` 或 `Log.Warning`/`Log.Error`（MCP 游戏侧）/ `_log`（SimpleMspServer 侧）
- 格式：`$"[组件标识] 操作描述失败: {ex.GetType().Name}: {ex.Message}"`
- 涉及外部调用（HTTP、WebSocket、文件 I/O、进程管理）时额外展开 `InnerException` 链

**正确示例：**
```csharp
// 简单操作
catch (Exception ex) { Log.Warning($"[ToolName] 读取数据失败: {ex.Message}"); }

// 外部调用 — 展开完整链
catch (Exception ex) when (!ct.IsCancellationRequested)
{
    var detail = UnwrapException(ex);
    CoreLog.Error($"[McpClient] SSE 断开: {detail}");
}

static string UnwrapException(Exception ex)
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

**错误示例：**
```csharp
try { DoSomething(); } catch { }                 // 禁止
try { DoSomething(); } catch (Exception) { }      // 禁止
catch { /* ignored */ }                            // 禁止
catch (Exception) { /* ignored */ }                // 禁止
```

**允许的精简场景**：`OperationCanceledException` — 异步取消，保留空 catch，日志可选。

### 1. 日志安全

- **不输出敏感信息**：token、密码、密钥、用户隐私数据不写入日志
- **线程安全**：后台线程（HttpListener、WebSocket ReceiveLoop、子进程 stdout）禁止直接调用 `Verse.Log.*`，必须通过 `McpLog`（MCP 侧）或 `SafeLog`（Agent 侧）入队 → 主线程 `GameComponentUpdate` 中 Flush 写入
- **调试日志**：临时调试用 `[CCGUI_DEBUG]` 前缀，问题解决后 grep 一次性清理

### 2. 技术设计文档必须写入 design/ 目录

凡是涉及架构决策、实现模式、多个文件协同配合的技术细节，必须在 `design/` 目录中建立对应的 `.md` 文件。修改了相关代码后，必须同步更新设计文档和 CLAUDE.md。

- 聚焦 **WHY**（设计理由）和 **HOW**（实现模式），不重复代码
- 每个设计文档对应一个子系统
- CLAUDE.md 保留概述和指向设计文档的引用，避免膨胀

**design/ 文档索引**：

| 文档 | 内容 |
|------|------|
| `design/camera-system.md` | 摄像头自动移动：GetTargetRange 接口、缩放规则、7 种实现模式、线程安全、实体查找 |
| `design/bridge-lifecycle.md` | CC Companion 桥接：连接流程、进程清理三层保障、MessageBus 双总线、参数覆盖顺序 |
| `design/tool-system.md` | Tool 系统：ITool 接口、ToolRegistry 反射注册、执行流程、线程模型、McpCommandQueue |
| `design/event-system.md` | 事件系统：Harmony 拦截、四级分级响应、事件驱动暂停、AutoPauseGuard |
| `design/token-budget-system.md` | Token 预算：三档检查、Block/Warn 超限行为、UI 展示、Webhook 通知 |
| `design/mcp-server-integration.md` | MCP Server 集成：SDK 接入方式、per-session 架构、响应清洗、通知通道、net472 适配、CLI 测试 |
| `design/agent-runtime.md` | Agent Runtime：Scheduler、单 Commander 架构、Context Builder、中断机制 |
| `design/tool-result-suffix.md` | Tool Result Suffix：双工通知机制，一次性 suffix 追加到下一次工具结果后自动清空 |

### 3. 提交规范

- git commit 信息使用简体中文，遵循 Conventional Commits 格式（如 `feat(agent): 添加XX功能`）
- 提交前检查 diff 中的敏感信息（token、密码、密钥、内网地址）
- 检查日志输出是否可能泄露敏感数据
- 未经用户明确允许不得提交代码

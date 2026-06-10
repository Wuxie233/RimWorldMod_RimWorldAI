# RimWorldMCP 游戏 AI 助手 Companion

TypeScript 项目，WebSocket 接收游戏事件，通过可切换 AI provider 驱动 AI 操控 RimWorld。`tsx` 运行时，所有依赖自包含。

## 快速开始

### 前置条件

- [Node.js](https://nodejs.org/) >= 18.19.0

### 安装依赖

```bash
npm install
```

依赖包括 Claude Agent SDK、Anthropic SDK、OpenAI SDK、MCP SDK、ws（WebSocket）、tsx（TypeScript 运行时）。

### 启动

**方式一：自动启动（推荐）**

游戏内 → 选项 → Mod 设置 → RimWorld MCP → 开启"自动启动本地 Companion"。加载存档时 C# 侧自动 spawn companion 进程，RimWorld 退出/崩溃时自动清理。

C# spawn 的实际命令等效：

```bash
node --import tsx/esm companion/companion.ts --idle-timeout 30000
```

**方式二：手动启动**

```bash
npm start            # tsx companion/companion.ts
```

终端输出 `[cc-companion] 就绪，等待 RimWorldMCP 连接...` 即启动成功。

**开发模式**（文件改动自动重启）：

```bash
npm run dev          # tsx --watch companion/companion.ts
```

### 启动后

| 服务 | 地址 |
|------|------|
| WebSocket | `ws://127.0.0.1:19999` |
| 聊天页面（只读） | `http://127.0.0.1:19999/` |

## 文件结构

```
cc-companion/
├── agent-runtime/         # provider 无关的会话、MCP 工具循环、legacy 事件映射
├── companion/             # 入口 + 配置 + Claude SDK 加载
│   ├── companion.ts       # 编排入口
│   ├── config.ts          # 配置（CLI + 环境变量）
│   └── sdk-loader.ts      # SDK 加载
├── bridge/                # Claude Agent SDK provider 兼容层
│   └── session.ts         # Claude SDK 会话封装
├── providers/             # claude-sdk / anthropic / openai / openai-compatible
├── rimworld/
│   └── context.ts         # 系统提示词加载（Prompt.md）
├── Prompt.md              # AI 行为提示词
├── package.json
├── tsconfig.json
└── README.md
```

## 数据流

```
RimWorld (C#)                  CC Companion (Node.js)       AI Provider
    │                                │                         │
    │ 游戏事件(WS)                     │  AgentProvider          │
    │──────────────────────────────▶ │────────────────────────▶│
    │                                │                         │
    │ 聊天窗 ◀─ WS broadcast ────────│  ◀── assistant/tool ────│
    │                                │                         │
    │  MCP Server :9877 ◀────────────│──── tools/call ─────────│
```

## AI 网关与参数

Companion 使用项目自有 `AgentProvider` 接口，Claude Agent SDK 只是一个 provider 实现。C# / RimWorld 设置页会把网关类型、模型、API 地址、API Key 通过环境变量传给 Node 子进程；手动启动时也可用 CLI 或环境变量覆盖。

| 环境变量 | CLI 参数 | 默认值 | 说明 |
|----------|----------|--------|------|
| `CCB_HOST` | `--host` | `127.0.0.1` | WebSocket 监听地址 |
| `CCB_PORT` | `--port` | `19999` | WebSocket 监听端口 |
| `CCB_AUTH_TOKEN` | `--token` | 无 | WS 握手认证 |
| `CCB_AI_PROVIDER` | `--provider` | `claude-sdk` | `claude-sdk` / `anthropic` / `openai-compatible` / `openai` |
| `CCB_API_BASE_URL` | `--api-base-url` | 空 | 自定义 API 地址；`openai-compatible` 通常填兼容 `/v1` 地址 |
| `CCB_API_KEY` | `--api-key` | 空 | provider API Key；推荐用 RimWorld 设置或环境变量，不推荐写入命令行历史 |
| `CCB_AGENT_MCP_URL` | `--agent-mcp-url` | `http://localhost:9878/mcp` | OpenAI/Anthropic provider 调用本地 Agent MCP 工具的地址 |
| `CCB_MODEL_NAME` | `--model-name` | 空 | 模型名称 |
| `CCB_REQUEST_TIMEOUT_MS` | `--request-timeout-ms` | `120000` | 单次 provider 请求超时 |
| `CCB_MAX_RETRIES` | `--max-retries` | `1` | provider API 错误后的重试次数 |
| `CCB_MAX_TOOL_TURNS` | `--max-tool-turns` | `20` | 单回合最大 tool loop 次数，耗尽会返回 `result:error` |
| `CCB_HISTORY_MAX_MESSAGES` | `--history-max-messages` | `40` | 非 Claude provider 进程内历史消息上限 |
| `CCB_HISTORY_MAX_CHARS` | `--history-max-chars` | `120000` | 非 Claude provider 进程内历史字符上限 |
| `CCB_MCP_TOOL_CACHE_TTL_MS` | `--mcp-tool-cache-ttl-ms` | `60000` | Agent MCP 工具列表缓存 TTL，失败后会清 client/cache 并在下次恢复 |
| `CCB_IDLE_TIMEOUT` | `--idle-timeout` | `0`（永不退出），C# spawn 时传 `30000` | 无 client 连接/断开后超时自动退出（ms） |
| `CCB_SETTING_SOURCES` | `--setting-sources` | `user,project,local` | settings 加载源 |
| `CCB_DEBUG` | `--debug` | `false` | 输出 companion 调试日志，默认关闭以避免噪音和敏感信息泄露 |
| `RIMWORLD_PROJECT_PATH` | `--project-path` | `process.cwd()` | SDK 项目目录 |

## 支持的 Provider

- `claude-sdk`：保留原 Claude Agent SDK 行为，兼容现有会话和 Claude Code 设置来源。
- `anthropic`：经 Vercel AI SDK（`@ai-sdk/anthropic`）调 Anthropic，支持 `CCB_API_BASE_URL` 覆盖地址；思考开启时启用 extended thinking（需 claude 3.7+ 模型）。
- `openai`：经 Vercel AI SDK（`@ai-sdk/openai`）调 OpenAI，可用 `CCB_API_BASE_URL` 覆盖；思考开启时启用 reasoning summary（需 gpt-5 / o 系列等 reasoning 模型）。
- `openai-compatible`：经 `@ai-sdk/openai-compatible` 的 `baseURL` 支持本地网关、代理网关或第三方兼容 `/v1` 服务（reasoning 视网关支持情况，默认不启用）。

非 Claude provider 共用 `agent-runtime/ai-sdk-turn.ts`，基于 Vercel AI SDK `streamText` + `fullStream` 产出真流式 `text-delta` / `reasoning-delta`；工具调用由 companion 的 provider runtime 统一编排：模型发起 tool call，companion 调 Agent MCP `:9878`，再把工具结果回填模型。C# 侧仍接收兼容当前 `SdkMessage` 的 SDK-like JSON，因此 UI、历史和 token 链路不需要直接解析各 provider 原始格式。

运行时会为每次 session rebuild 生成新的 generation guard，旧 iterator 的输出不会再广播到 C#。所有回合都必须以 `result` 终态结束；abort、provider API error、tool loop exhaustion 都会映射为 `result:error`，避免失败在 UI 上看起来像成功。

OpenAI / Anthropic provider 会在 companion 进程内维护跨轮 history，并按消息数和字符数裁剪，避免每条 chat 从零开始或无限膨胀。`claude-sdk` 仍走 Claude Agent SDK 原有会话路径。

RimWorld 设置页会保存 API Key 到本地 Mod 设置；不要开启 `sdk-log.txt` 后粘贴或分享日志，日志不会主动输出 API Key，但请求/响应内容可能包含用户输入。

## 示例

OpenAI-compatible 网关：

```bash
CCB_AI_PROVIDER=openai-compatible \
CCB_API_BASE_URL=http://127.0.0.1:11434/v1 \
CCB_API_KEY=local-key \
CCB_MODEL_NAME=qwen2.5-coder \
npm start
```

Anthropic 原生格式：

```bash
CCB_AI_PROVIDER=anthropic \
CCB_API_KEY=sk-ant-... \
CCB_MODEL_NAME=claude-sonnet-4-6 \
npm start
```

## 进程生命周期

C# 侧自动 spawn 时传入 `--idle-timeout 30000`，companion 在 WS 断开 30s 无重连后自动退出。同时 C# 侧在返回主菜单、退出游戏、读档/新档时主动杀旧进程。Windows 额外通过 Job Object 绑定，RimWorld 强杀时 OS 自动清理 companion。

## 会话存储

`claude-sdk` 会话仍由 Claude Agent SDK 持久化到 `~/.claude/projects/<sanitizedCwd>/<sessionId>.jsonl`。OpenAI / Anthropic provider 当前由 companion 进程内维护对话上下文，RimWorld 侧的对话历史仍由 C# 运行时保存。

# RimWorldMCP WORKING NOTES

## OVERVIEW

`RimWorldMCP` 是游戏内 Mod DLL：启动 MCP Server `:9877`，把 RimWorld 状态与操作暴露为 LLM 可调用 Tool。

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Mod 入口/设置 | `RimWorldMCPMod.cs` | 继承 `Verse.Mod`，配置 UI |
| MCP 生命周期 | `McpServiceManager.cs` | 服务启动、端口、transport 管理 |
| 存档组件 | `GameComponent_McpServer.cs` | sessionId 持久化、存档生命周期 |
| Tool 注册 | `Tools/ToolRegistry.cs` | Tool 集合入口 |
| 新增游戏 Tool | `Tools/Tool_*.cs` | 每个 Tool 独立类 |
| 写操作调度 | `McpCommandQueue.cs` | 主线程执行写操作 |
| 事件拦截 | `Harmony/` | Letter/Message/Alert 等事件 |
| 地图网格 | `MapRendering/` | chunk、字符图、符号字典 |
| 符号生成 | `scripts/generate_symbols.py` | 从游戏 XML 生成 `Symbols.json` |
| 符号校验 | `scripts/check_symbols.py` | 一对一映射和字符合法性 |

## TOOL DEVELOPMENT RULES

- 新增 Tool 前先追游戏原版链路：UI 点击 → Designator/Command → JobGiver/JobDriver → 执行。
- 优先复用原版 `Designator`、`Job`、`Bill`、`DefDatabase<>`，不要重造游戏规则。
- 写操作必须通过 `McpCommandQueue` 调度到主线程；只读查询可在 HTTP 线程执行。
- 所有带坐标的 Tool 都要实现 `GetTargetRange(JsonElement? args)`，用于自动移动视角。
- 参数统一 `pos_x/pos_y/end_x/end_y`；`pos_y` 映射到 `IntVec3.z`，海拔 `IntVec3.y` 固定为 `0`。
- 涉及 Pawn / Thing 用 `thingIDNumber`，不要用名称匹配。
- List 型 Tool 必须分页，输出保持表格化和决策必要信息。

## SYMBOL SYSTEM

- `resource/Symbols.json` 是源文件，构建时复制到 `publish/RimWorldMCP/1.6/Assemblies/`。
- `SymbolDictionary.Initialize()` 每次启动读词表重建；词表缺失或损坏应直接失败。
- 固定网格字符可以硬编码；Def 映射字符必须来自 `Symbols.json`。
- 修改符号后运行 `scripts/check_symbols.py` 校验重复、私有区和 fallback pool。

## EVENT / LIFECYCLE GOTCHAS

- `McpServiceManager.Start()` 应保持幂等；端口变更通常需要重启 RimWorld 生效。
- `Game.Dispose()` 不一定能覆盖所有返回主菜单路径，跨存档静态状态要谨慎清理。
- L3 Critical 事件会中断 Agent；L2/L1/L0 主要走 suffix 注入和通知。
- `EventLevel` 数值必须和 `RimWorldAgent` 侧保持一致。

## COMMANDS

```bash
dotnet build ../RimWorldAI.sln
dotnet build ../RimWorldAI.sln --configuration Release
python3 scripts/check_symbols.py
```

## DO NOT

- 不要写 `new IntVec3(posX, posY, 0)`。
- 不要为新 Tool 设计中心点+半径坐标接口。
- 不要吞异常；非 `OperationCanceledException` 必须日志化。
- 不要把 token、OSS key、session secret 写进日志。

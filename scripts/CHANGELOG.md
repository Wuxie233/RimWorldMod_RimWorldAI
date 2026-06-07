# RimWorldAI 更新日志

<!-- 每次推送前在此追加本次更新内容，publish.ps1 自动读取最新一条 -->
<!-- 格式: ## [YYYY-MM-DD] - 标题 (多个 mod 用,分隔) -->
<!--        - 条目 -->

## [2026-06-07] - 对话持久化 + 跨平台 SQLite + Steam Workshop 推送

- MOD 模式对话 SQLite 持久化，按 MCP sessionId 隔离存档
- System.Data.SQLite.Core → Microsoft.Data.Sqlite + SQLitePCLRaw（跨平台零原生依赖）
- 原生 DLL 隔离到 Native\，避免 RimWorld ModAssemblyHandler 误扫描
- 移除 MCP 侧双重后缀注入（suffix + 游戏速度），统一到 Agent 侧
- 修复 companion.ts 双 session 导致消息/Token 重复
- Token 耗时记录走 AddDuration 隔离
- CcbWS 重连死循环修复 + 日志降级 Info
- 弹框扫描路径 1 移除（路径 2 windowOpenRemind 覆盖）
- TrappedColonistTracker 精简为 PathBlocked 检测，"被困" 提升到 Critical
- Steam Workshop 一键推送脚本 + changelog 机制

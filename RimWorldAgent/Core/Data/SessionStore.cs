using System.IO;

namespace RimWorldAgent.Core.Data
{
    /// <summary>CCB 工作目录存储（project-path）+ 当前存档身份（save-id）。</summary>
    public static class SessionStore
    {
        /// <summary>companion 工作目录 / 全局项目根（跨存档共用）。</summary>
        public static string ProjectPath { get; set; } = "";

        /// <summary>当前存档 ID（来自 MCP get_session_id），由 GameComponent 在拿到后设置。</summary>
        public static string SaveId { get; set; } = "";

        /// <summary>
        /// 当前存档的隔离目录：{ProjectPath}/saves/{SaveId}。
        /// SaveId 为空时回退到 ProjectPath，避免冷启动早期空引用。
        /// </summary>
        public static string SaveDir =>
            string.IsNullOrEmpty(SaveId)
                ? ProjectPath
                : Path.Combine(ProjectPath, "saves", SaveId);
    }
}

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace RimWorldMCP.Tools
{
    public class Tool_SetResearchProject : ITool
    {
        public string Name => "set_research_project";
        public string Description => "设置当前研究项目。项目 defName 需先用 list_research_projects 查询获取。";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { project_defName = new { type = "string", description = "研究项目 defName" } },
            required = new[] { "project_defName" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("project_defName", out var defNameProp))
                return ToolResult.Error("缺少 project_defName");

            var projectDefName = defNameProp.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(projectDefName))
                return ToolResult.Error("project_defName 不能为空。");

            try
            {
                // 查找研究项目
                var project = ResearchProjectDef.Named(projectDefName);
                if (project == null)
                    return ToolResult.Error($"未知研究项目: {projectDefName}。请先用 list_research_projects 查询可用项目。");

                var researchManager = Find.ResearchManager;
                if (researchManager == null)
                    return ToolResult.Error("ResearchManager 不可用。");

                // 检查前置条件
                if (!project.PrerequisitesCompleted)
                {
                    var unmet = project.prerequisites?.Where(p => !p.IsFinished)
                        .Select(p => p.label ?? p.defName).ToList();
                    if (unmet != null && unmet.Count > 0)
                        return ToolResult.Error(
                            $"无法研究 {project.label} ({projectDefName})。未满足前置条件: {string.Join(", ", unmet)}");
                    return ToolResult.Error($"无法研究 {project.label} ({projectDefName})。前置条件未满足。");
                }

                // 检查是否已完成
                if (project.IsFinished)
                    return ToolResult.Error($"研究项目 {project.label} ({projectDefName}) 已经完成，无需再次研究。");

                var projLabel = project.label ?? projectDefName;

                // 通过 McpCommandQueue 在主线程上执行操作
                var result = await Task.Run(() =>
                {
                    var tcs = new TaskCompletionSource<object?>();
                    McpCommandQueue.Enqueue(new McpCommand
                    {
                        Action = () =>
                        {
                            researchManager.SetCurrentProject(project);
                            return projectDefName;
                        },
                        Completion = tcs
                    });
                    return tcs.Task;
                });

                var sb = new StringBuilder();
                sb.AppendLine($"已将研究项目设为: {projLabel} ({projectDefName})");

                // 显示附加信息
                if (project.baseCost > 0)
                    sb.AppendLine($"- 研究工作量: {project.baseCost:N0}");

                if (project.requiredResearchBuilding != null)
                    sb.AppendLine($"- 需要研究设施: {project.requiredResearchBuilding.label}");

                if (project.techLevel > 0)
                    sb.AppendLine($"- 科技等级: {project.techLevel}");

                return ToolResult.Success(sb.ToString());
            }
            catch (Exception ex)
            {
                return ToolResult.Error($"设置研究项目失败: {ex.Message}");
            }
        }
    }
}

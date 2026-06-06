using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldMCP.MapRendering
{
    /// <summary>
    /// AI 观察覆盖层 — 在地图上短暂显示半透明彩色标记，告知玩家 AI 正在关注这些区域。
    /// 使用 GenDraw.DrawFieldEdges 每帧绘制，零持久状态，天然自过期。
    /// </summary>
    public static class AiObservationOverlay
    {
        private const float DurationSec = 0.8f;

        private class Observation
        {
            public List<IntVec3> Cells = null!;
            public Color Color;
            public float ExpireRealTime;
        }

        private static readonly Dictionary<Map, List<Observation>> _active = new Dictionary<Map, List<Observation>>();

        /// <summary>
        /// 在地图上显示 AI 观察标记，~0.8 秒后自动消失（实时钟，不受暂停影响）。
        /// 颜色默认 Cyan（青色），与用户常用的白/红/蓝区分。
        /// </summary>
        public static void Show(Map map, CellRect rect, string label, string? colorName = null)
        {
            if (map == null || rect.IsEmpty) return;

            var color = ParseColor(colorName);

            var cells = new List<IntVec3>(rect.Area);
            foreach (var cell in rect)
                cells.Add(cell);

            if (!_active.TryGetValue(map, out var list))
            {
                list = new List<Observation>();
                _active[map] = list;
            }
            list.Add(new Observation
            {
                Cells = cells,
                Color = color,
                ExpireRealTime = Time.realtimeSinceStartup + DurationSec
            });
        }

        /// <summary>每帧调用——绘制活跃观察 + 清理过期项（纯实时钟，不受暂停影响）</summary>
        public static void Tick(Map? map)
        {
            if (map == null) return;
            if (!_active.TryGetValue(map, out var list)) return;

            var nowReal = Time.realtimeSinceStartup;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (nowReal >= list[i].ExpireRealTime)
                {
                    list.RemoveAt(i);
                    continue;
                }

                try
                {
                    GenDraw.DrawFieldEdges(list[i].Cells, list[i].Color);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[AiObservationOverlay] 绘制标记失败: {ex.Message}");
                }
            }

            if (list.Count == 0)
                _active.Remove(map);
        }

        private static Color ParseColor(string? colorName)
        {
            if (string.IsNullOrEmpty(colorName))
                return Color.cyan;

            switch (colorName!.ToLowerInvariant())
            {
                case "red":    return Color.red;
                case "green":  return Color.green;
                case "blue":   return Color.blue;
                case "yellow": return Color.yellow;
                case "magenta": return Color.magenta;
                case "white":  return Color.white;
                case "cyan":   return Color.cyan;
                case "orange": return new Color(1f, 0.6f, 0f);
                default:       return Color.cyan;
            }
        }
    }
}

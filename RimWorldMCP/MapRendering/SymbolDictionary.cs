using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RimWorld;
using Verse;

namespace RimWorldMCP.MapRendering
{
    public static class SymbolDictionary
    {
        private static Dictionary<string, char> _forward = new();
        private static Dictionary<char, (string DefName, string Label, string Category)> _reverse = new();
        private static List<char> _fallbackPool = new();
        private static string _dictHash = "";

        // ---- 公共属性 ----

        public static string DictHash => _dictHash;
        public static int EntryCount => _forward.Count;

        // ---- 初始化 ----

        public static void Initialize()
        {
            Rebuild();
        }

        /// <summary>
        /// 从 mod 目录加载词表文件和兜底池。
        /// 返回 (curatedMap, fallbackPool)。词表缺失或损坏直接抛异常。
        /// </summary>
        private static (Dictionary<string,char> symbols, List<char> pool)? LoadCuratedTable()
        {
            // 词表与 DLL 同目录: Mod/1.6/Assemblies/Symbols.json
            var asmDir = Path.GetDirectoryName(typeof(SymbolDictionary).Assembly.Location);
            var path = Path.Combine(asmDir ?? ".", "Symbols.json");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"[SymbolDictionary] 未找到词表文件: {path}\n" +
                    $"词表应与 DLL 同目录 (Mod/1.6/Assemblies/Symbols.json)。\n" +
                    $"请运行 scripts/generate_symbols.py 生成。");
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("symbols", out var syms))
            {
                throw new InvalidDataException(
                    $"[SymbolDictionary] 词表文件缺少 symbols 字段: {path}");
            }

            var curated = new Dictionary<string, char>();
            var charToDef = new Dictionary<char, string>();

            foreach (var prop in syms.EnumerateObject())
            {
                var defName = prop.Name;
                string? charStr = null;
                if (prop.Value.ValueKind == JsonValueKind.Object
                    && prop.Value.TryGetProperty("char", out var jCh))
                    charStr = jCh.GetString();

                if (charStr == null || charStr.Length != 1)
                {
                    McpLog.Warn($"[SymbolDictionary] 词表跳过无效条目: {defName}");
                    continue;
                }
                char ch = charStr[0];

                if (charToDef.TryGetValue(ch, out var existing))
                {
                    McpLog.Error($"[SymbolDictionary] 词表冲突: '{ch}' 同时分配给 {existing} 和 {defName}，保留后者");
                }
                charToDef[ch] = defName;
                curated[defName] = ch;
            }

            McpLog.Info($"[SymbolDictionary] 词表加载: {curated.Count} 条");

            // 解析 fallback_pool
            var pool = new List<char>();
            if (root.TryGetProperty("fallback_pool", out var poolArray))
            {
                foreach (var item in poolArray.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s) && s.Length == 1)
                        pool.Add(s[0]);
                }
            }
            McpLog.Info($"[SymbolDictionary] 兜底池加载: {pool.Count} 个字符");

            return (curated, pool);
        }

        private static string ResolveCategory(Def def)
        {
            return def switch
            {
                TerrainDef => "地形",
                ThingDef td when td.category == ThingCategory.Building => "建筑",
                ThingDef td when td.category == ThingCategory.Item => "物品",
                ThingDef td when td.category == ThingCategory.Plant => "植物",
                ThingDef td when td.category == ThingCategory.Pawn => "生物",
                _ => "其他"
            };
        }

        private static void Rebuild()
        {
            _forward = new();
            _reverse = new();
            _fallbackPool = new();

            var loaded = LoadCuratedTable();
            if (loaded == null) return;

            var curated = loaded.Value.symbols;
            _fallbackPool = loaded.Value.pool;

            var usedChars = new HashSet<char>();

            var allDefs = new List<Def>();
            allDefs.AddRange(DefDatabase<ThingDef>.AllDefs);
            allDefs.AddRange(DefDatabase<TerrainDef>.AllDefs);
            var sorted = allDefs.OrderBy(d => d.defName).ToList();

            int fromTable = 0, fromFallback = 0;

            // 第一轮：注册词表中已有的 def
            foreach (var def in sorted)
            {
                if (!curated.TryGetValue(def.defName, out var ch))
                    continue;
                if (usedChars.Contains(ch))
                    continue;  // 词表已验证一对一

                _forward[def.defName] = ch;
                usedChars.Add(ch);
                _reverse[ch] = (def.defName, def.label ?? def.defName, ResolveCategory(def));
                fromTable++;
            }

            // 第二轮：兜底 — 词表中不存在的 def 从 fallback_pool 分配
            foreach (var def in sorted)
            {
                if (_forward.ContainsKey(def.defName))
                    continue;

                if (_fallbackPool.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"[SymbolDictionary] 兜底池耗尽！defName={def.defName} 未在词表中且兜底字符用完。" +
                        $"请重新运行 generate_symbols.py 生成更大的兜底池。");
                }

                char sym = _fallbackPool[0];
                _fallbackPool.RemoveAt(0);

                _forward[def.defName] = sym;
                _reverse[sym] = (def.defName, def.label ?? def.defName, ResolveCategory(def));
                fromFallback++;
            }

            // 计算 Hash
            var sortedNames = allDefs.Select(d => d.defName).OrderBy(n => n).ToList();
            _dictHash = string.Join(",", sortedNames).Length.ToString("x8");
            McpLog.Info($"[SymbolDictionary] 重建完成 — 词表: {fromTable}, 兜底: {fromFallback}, 总计: {_forward.Count}, 兜底池剩余: {_fallbackPool.Count}, Hash: {_dictHash}");
        }

        // ---- 查询方法 ----

        public static char GetChar(string defName)
        {
            if (_forward.TryGetValue(defName, out var c))
                return c;
            return '?';
        }

        public static char GetChar(ThingDef def) => GetChar(def.defName);

        public static char GetChar(TerrainDef def) => GetChar(def.defName);

        public static string Lookup(char c)
        {
            if (_reverse.TryGetValue(c, out var info))
                return $"{info.DefName} ({info.Label}, {info.Category})";
            return $"未知符号: {c}";
        }

        public static Dictionary<char, (string DefName, string Label, string Category)> GetAll() => new(_reverse);

        public static Dictionary<char, (string DefName, string Label, string Category)> GetByChars(IEnumerable<char> chars)
        {
            var result = new Dictionary<char, (string, string, string)>();
            foreach (var c in chars)
            {
                if (_reverse.TryGetValue(c, out var info))
                    result[c] = info;
            }
            return result;
        }

        public static string GetLegendString(HashSet<char> usedSymbols)
        {
            var sb = new StringBuilder();
            foreach (var c in usedSymbols)
            {
                if (_reverse.TryGetValue(c, out var info))
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(c);
                    sb.Append('=');
                    sb.Append(info.Label);
                }
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;
using Lab;

namespace Tests.Lab
{
    /// <summary>
    /// 手感场景用例的"规则侧"读数：从数据文件（预设、技能时间线、手感覆盖行、创建物模板）按规则算期望值，
    /// 用例里不出现裸数——期望来自预设字段 × 毫秒换算（<see cref="FeelCalibration.MillisecondsToTicks"/>）× 受击裁决规则。
    /// </summary>
    internal sealed class FeelRules
    {
        public const double StepSeconds = 1.0 / 60.0;

        private readonly JsonObject _values;

        private FeelRules(JsonObject values)
        {
            _values = values;
        }

        /// <summary>预设（<c>feel.preset.*</c>）的字段表。</summary>
        public static FeelRules Preset(string presetId)
        {
            // 框架手感根 data/_feel 里没有时再找默认手感模板根 data/_feel_templates（feel.preset.tpl_*）。
            var inFramework = Path.Combine("data", "_feel", "feel", "feel.preset.json");
            var path = presetId.StartsWith("feel.preset.tpl_", StringComparison.Ordinal)
                ? Path.Combine("data", "_feel_templates", "feel", "feel.preset.json")
                : inFramework;
            var row = Row(path, presetId);
            return new FeelRules((JsonObject)row["values"]);
        }

        /// <summary>该格子的缺省预设（来自格子目录）。</summary>
        public static FeelRules ForCell(string cell) =>
            Preset(LabTestSupport.Runner.Dataset.Catalog.GetScenario(cell).DefaultPreset);

        public double N(string field) => ((JsonNumber)_values[field]).Value;

        public string S(string field) => ((JsonString)_values[field]).Value;

        public bool B(string field) => ((JsonBool)_values[field]).Value;

        /// <summary>毫秒 → tick（生产换算：四舍五入到整数 tick，下限 1）。</summary>
        public static int T(double milliseconds) => FeelCalibration.MillisecondsToTicks(milliseconds, StepSeconds);

        public int Ticks(string presetField) => T(N(presetField));

        // ---------- 技能时间线 ----------

        public static JsonObject Skill(string id) => Row(Path.Combine("data", "_lab_action", "skill", "skill.def.json"), id);

        public static JsonObject Timeline(string skillId) => (JsonObject)Skill(skillId)["timeline"];

        public static double TimelineMs(string skillId, string field) => ((JsonNumber)Timeline(skillId)[field]).Value;

        /// <summary>某个标记的毫秒位置；没有该标记时抛出。</summary>
        public static double MarkerMs(string skillId, string marker)
        {
            foreach (var m in (JsonArray)Timeline(skillId)["markers"])
            {
                var o = (JsonObject)m;
                if (((JsonString)o["name"]).Value == marker)
                {
                    return ((JsonNumber)o["at_ms"]).Value;
                }
            }

            throw new InvalidOperationException($"技能 {skillId} 没有标记 {marker}");
        }

        public static (double Open, double Close) CancelWindowMs(string skillId, string cls)
        {
            foreach (var w in (JsonArray)Timeline(skillId)["cancel_windows"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["class"]).Value == cls)
                {
                    return (((JsonNumber)o["open_ms"]).Value, ((JsonNumber)o["close_ms"]).Value);
                }
            }

            throw new InvalidOperationException($"技能 {skillId} 没有取消窗口 {cls}");
        }

        // ---------- 手感覆盖行（feel.action / feel.character） ----------

        /// <summary>覆盖行里对某字段的 set 写入；没有写这个字段时返回 null。</summary>
        public static double? OverrideNumber(string table, string id, string field)
        {
            var row = Row(Path.Combine("data", "_lab_action", "feel", table + ".json"), id);
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return ((JsonNumber)o["value"]).Value;
                }
            }

            return null;
        }

        public static string? OverrideText(string table, string id, string field)
        {
            var row = Row(Path.Combine("data", "_lab_action", "feel", table + ".json"), id);
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return ((JsonString)o["value"]).Value;
                }
            }

            return null;
        }

        // ---------- 通用 ----------

        private static readonly Dictionary<string, JsonObject> Cache = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        public static JsonObject Row(string relativePath, string id)
        {
            var path = Path.Combine(LabTestSupport.RepoRoot(), relativePath);
            if (!Cache.TryGetValue(path, out var root))
            {
                root = LabJson.ParseObject(File.ReadAllText(path, Encoding.UTF8), path);
                Cache[path] = root;
            }

            foreach (var row in (JsonArray)root["rows"])
            {
                var o = (JsonObject)row;
                var key = o.ContainsKey("id") ? "id" : "key";
                if (((JsonString)o[key]).Value == id)
                {
                    return o;
                }
            }

            throw new InvalidOperationException($"{relativePath} 里没有行 {id}");
        }
    }

    /// <summary>手感场景指纹读数辅助：按 <c>组.度量</c> 取值，解析实验室的分号/冒号文本约定。</summary>
    internal sealed class FeelFp
    {
        private readonly Fingerprint _fp;

        public FeelFp(Fingerprint fp)
        {
            _fp = fp;
        }

        public static FeelFp Of(string script, string cell, LabRunVariant? variant = null) =>
            new FeelFp(LabTestSupport.Runner.Run(LabTestSupport.Script(script), cell, variant));

        public JsonValue Raw(string path)
        {
            var dot = path.IndexOf('.');
            var group = (JsonObject)_fp.Groups[path.Substring(0, dot)];
            return group[path.Substring(dot + 1)];
        }

        public bool Has(string group) => _fp.Groups.ContainsKey(group);

        public double Num(string path) => ((JsonNumber)Raw(path)).Value;

        public string Text(string path) => ((JsonString)Raw(path)).Value;

        public List<double> Nums(string path)
        {
            var list = new List<double>();
            foreach (var v in (JsonArray)Raw(path))
            {
                list.Add(((JsonNumber)v).Value);
            }

            return list;
        }

        /// <summary>分号分隔的条目；空文本给空列表。</summary>
        public List<string> Items(string path)
        {
            var text = Text(path);
            return text.Length == 0 ? new List<string>() : new List<string>(text.Split(';'));
        }

        /// <summary>条目形如 <c>tick:...</c> 或 <c>actor@tick:...</c>：取其 tick。</summary>
        public static int TickOf(string item)
        {
            var at = item.IndexOf('@');
            var rest = at >= 0 ? item.Substring(at + 1) : item;
            var end = rest.IndexOfAny(new[] { ':', '=', ',' });
            return int.Parse(end < 0 ? rest : rest.Substring(0, end), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>在 <c>markers</c>/<c>starts</c> 等条目里找 <c>actor@tick:name</c> 形式的第 <paramref name="nth"/> 个（0 起）匹配的 tick。</summary>
        public static int FirstTickWhere(IEnumerable<string> items, Func<string, bool> match)
        {
            foreach (var item in items)
            {
                if (match(item))
                {
                    return TickOf(item);
                }
            }

            throw new InvalidOperationException("没有匹配的条目");
        }
    }
}

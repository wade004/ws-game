using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <see cref="EffectRef.Params"/>/<see cref="AuraEffectEntry.Params"/> 等效果参数
    /// <see cref="JsonObject"/> 的读取帮助方法：各效果原语的参数形状由 06 第 3.2/3.3 节"参数要点"
    /// 列描述但未强类型化（<c>EffectRef.Params</c> 的判断记录"保持数据表原始 JSON 结构不做强类型
    /// 展开"），本类把"按字段名读取、缺失给默认值"这一重复模式收拢成静态方法，供
    /// <c>EffectDispatcher</c>/<c>AuraHost</c>/<c>ProcHost</c>/<c>SpellModResolver</c> 复用。
    /// </summary>
    internal static class ParamsX
    {
        public static Id GetId(JsonObject o, string key, Id fallback)
        {
            if (o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            return fallback;
        }

        public static Id? GetIdOpt(JsonObject o, string key)
        {
            if (o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            return null;
        }

        public static double GetNumber(JsonObject o, string key, double fallback = 0)
        {
            return o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : fallback;
        }

        public static string GetString(JsonObject o, string key, string fallback = "")
        {
            return o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : fallback;
        }

        public static Vec2 GetVec2(JsonObject o, string key, Vec2 fallback)
        {
            if (o.TryGetValue(key, out var v) && v is JsonObject obj
                && obj.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && obj.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }

            return fallback;
        }

        public static IReadOnlyList<Id> GetIdArray(JsonObject o, string key)
        {
            var list = new List<Id>();
            if (o.TryGetValue(key, out var v) && v is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonString s && Id.TryParse(s.Value, out var id))
                    {
                        list.Add(id);
                    }
                }
            }

            return list;
        }

        public static IReadOnlyList<string> GetStringArray(JsonObject o, string key)
        {
            var list = new List<string>();
            if (o.TryGetValue(key, out var v) && v is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonString s)
                    {
                        list.Add(s.Value);
                    }
                }
            }

            return list;
        }

        public static JsonObject MergeNumber(JsonObject original, string key, double value)
        {
            var builder = new JsonObjectBuilder();
            foreach (var entry in original)
            {
                if (entry.Key == key) continue;
                builder.Add(entry.Key, entry.Value);
            }

            builder.Add(key, new JsonNumber(value));
            return builder.Build();
        }
    }
}

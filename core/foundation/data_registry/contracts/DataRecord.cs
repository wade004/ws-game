using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 一条已加载数据记录的只读视图：包裹底层 <see cref="JsonObject"/>，提供类型化访问器
    /// （见 04 第 4 节 DataRegistry 接口"只读访问"）。<c>GetXxx</c> 系列在字段缺失或类型不符时
    /// 抛 <see cref="DataFieldException"/>（呼应 11 第 4 节"调用方必须处理该分支"——这里的分支
    /// 是"用 <c>TryGetXxx</c> 还是确信字段一定存在用 <c>GetXxx</c>"）；<c>TryGetXxx</c> 系列
    /// 缺失或类型不符时返回 false，不抛异常。字段值为 JSON <c>null</c> 视为"未提供"（<see cref="Has"/>
    /// 返回 false），呼应 <c>l10n.locale.fallback</c> 一类 <c>Optional&lt;T&gt;</c> 字段的
    /// null 表示方式。
    /// <para>
    /// 类型别名判断记录：本类需要一个名为 <see cref="Id"/> 的属性（04 第 4 节 DataRegistry 契约
    /// 用语、任务书字段列表原文如此），与 <c>Core.Foundation.Common.Id</c> 类型同名；类内其余
    /// 位置一律用 <c>using CommonId = Core.Foundation.Common.Id;</c> 别名引用该类型，避免"属性名
    /// 与类型名相同"在类型位置产生的歧义（成员声明可用同名类型别名书写，其行为等价，编译器不
    /// 会把类型别名解析成成员）。
    /// </para>
    /// </summary>
    public sealed class DataRecord
    {
        public TableSchema Table { get; }

        /// <summary>主键字符串：内容表等于 <c>id</c> 字段原文；登记表等于 <c>key</c> 字段原文，
        /// 复合主键登记表（<c>l10n.text</c>）等于 <c>"{key}@{locale}"</c>（见
        /// <see cref="TableSchema.HasLocaleCompositeKey"/>）。</summary>
        public string Key { get; }

        /// <summary>内容表（主键为 <c>id</c>）的解析后 Id；登记表为 null。</summary>
        public CommonId? Id { get; }

        public JsonObject Raw { get; }

        public DataRecord(TableSchema table, string key, CommonId? id, JsonObject raw)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Id = id;
            Raw = raw ?? throw new ArgumentNullException(nameof(raw));
        }

        /// <summary>字段是否存在且不是 JSON <c>null</c>。</summary>
        public bool Has(string field) => Raw.TryGetValue(field, out var v) && v.Kind != JsonKind.Null;

        private JsonValue RequireField(string field)
        {
            if (!Has(field))
            {
                throw new DataFieldException(Table.Name, Key, field, "字段缺失或为 null");
            }
            return Raw[field];
        }

        public string GetString(string field)
        {
            var v = RequireField(field);
            if (v is JsonString s) return s.Value;
            throw new DataFieldException(Table.Name, Key, field, $"期望 String，实际 {v.Kind}");
        }

        public bool TryGetString(string field, out string value)
        {
            if (Has(field) && Raw[field] is JsonString s) { value = s.Value; return true; }
            value = string.Empty;
            return false;
        }

        public long GetInt(string field)
        {
            var v = RequireField(field);
            if (v is JsonNumber n && n.TryGetInt64(out var i)) return i;
            throw new DataFieldException(Table.Name, Key, field, $"期望 Int，实际 {v.Kind}");
        }

        public bool TryGetInt(string field, out long value)
        {
            if (Has(field) && Raw[field] is JsonNumber n && n.TryGetInt64(out value)) return true;
            value = 0;
            return false;
        }

        public double GetNumber(string field)
        {
            var v = RequireField(field);
            if (v is JsonNumber n) return n.Value;
            throw new DataFieldException(Table.Name, Key, field, $"期望 Number，实际 {v.Kind}");
        }

        public bool TryGetNumber(string field, out double value)
        {
            if (Has(field) && Raw[field] is JsonNumber n) { value = n.Value; return true; }
            value = 0;
            return false;
        }

        public bool GetBool(string field)
        {
            var v = RequireField(field);
            if (v is JsonBool b) return b.Value;
            throw new DataFieldException(Table.Name, Key, field, $"期望 Bool，实际 {v.Kind}");
        }

        public bool TryGetBool(string field, out bool value)
        {
            if (Has(field) && Raw[field] is JsonBool b) { value = b.Value; return true; }
            value = false;
            return false;
        }

        public CommonId GetId(string field)
        {
            var v = RequireField(field);
            if (v is JsonString s && CommonId.TryParse(s.Value, out var id)) return id;
            throw new DataFieldException(Table.Name, Key, field, $"期望合法 Id 字符串，实际 {v.Kind}");
        }

        public bool TryGetId(string field, out CommonId value)
        {
            if (Has(field) && Raw[field] is JsonString s && CommonId.TryParse(s.Value, out value)) return true;
            value = default;
            return false;
        }

        public IReadOnlyList<CommonId> GetIdList(string field)
        {
            var v = RequireField(field);
            if (!(v is JsonArray arr))
            {
                throw new DataFieldException(Table.Name, Key, field, $"期望 Id 数组，实际 {v.Kind}");
            }

            var result = new List<CommonId>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonString s) || !CommonId.TryParse(s.Value, out var id))
                {
                    throw new DataFieldException(Table.Name, Key, field, $"数组第 {i} 个元素不是合法 Id 字符串");
                }
                result.Add(id);
            }
            return result;
        }

        public bool TryGetIdList(string field, out IReadOnlyList<CommonId> value)
        {
            if (Has(field) && Raw[field] is JsonArray arr)
            {
                var result = new List<CommonId>(arr.Count);
                for (int i = 0; i < arr.Count; i++)
                {
                    if (!(arr[i] is JsonString s) || !CommonId.TryParse(s.Value, out var id))
                    {
                        value = Array.Empty<CommonId>();
                        return false;
                    }
                    result.Add(id);
                }
                value = result;
                return true;
            }
            value = Array.Empty<CommonId>();
            return false;
        }

        /// <summary>Vec2 的 JSON 表示为 <c>{"x": Number, "y": Number}</c>（04 未规定具体形状，
        /// 判断记录见 <c>data_registry/schema/README.md</c>）。</summary>
        public Vec2 GetVec2(string field)
        {
            var v = RequireField(field);
            if (v is JsonObject o && o.TryGetValue("x", out var xv) && xv is JsonNumber xn
                                    && o.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }
            throw new DataFieldException(Table.Name, Key, field, "期望 Vec2（{\"x\": Number, \"y\": Number}）");
        }

        public bool TryGetVec2(string field, out Vec2 value)
        {
            if (Has(field) && Raw[field] is JsonObject o
                            && o.TryGetValue("x", out var xv) && xv is JsonNumber xn
                            && o.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                value = new Vec2(xn.Value, yn.Value);
                return true;
            }
            value = Vec2.Zero;
            return false;
        }

        public JsonObject GetObject(string field)
        {
            var v = RequireField(field);
            if (v is JsonObject o) return o;
            throw new DataFieldException(Table.Name, Key, field, $"期望 Object，实际 {v.Kind}");
        }

        public bool TryGetObject(string field, out JsonObject value)
        {
            if (Has(field) && Raw[field] is JsonObject o) { value = o; return true; }
            value = null!;
            return false;
        }

        public JsonArray GetArray(string field)
        {
            var v = RequireField(field);
            if (v is JsonArray a) return a;
            throw new DataFieldException(Table.Name, Key, field, $"期望 Array，实际 {v.Kind}");
        }

        public bool TryGetArray(string field, out JsonArray value)
        {
            if (Has(field) && Raw[field] is JsonArray a) { value = a; return true; }
            value = null!;
            return false;
        }
    }
}

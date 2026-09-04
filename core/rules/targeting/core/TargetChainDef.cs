using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
// 类型别名判断记录（同 DataRecord.cs "类型别名判断记录"惯例）：本类需要一个名为 Shape 的属性
// （见 06 第 5 节 TargetChainDef.shape 字段），与 Core.Foundation.EngineAdapter.Shape 类型同名；
// 类内其余位置一律用别名 EngineShape 引用该类型，避免"属性名与类型名相同"在类型位置产生歧义。
using EngineShape = Core.Foundation.EngineAdapter.Shape;

namespace Core.Rules.Targeting
{
    /// <summary>排序键（见 06 第 5 节 <c>TargetChainDef.sortBy.key</c>）。</summary>
    public enum TargetSortKey
    {
        Distance,
        HpPct,
        Threat,
        Level,
    }

    /// <summary>排序方向。</summary>
    public enum SortDirection
    {
        Asc,
        Desc,
    }

    /// <summary>一条 <c>sort_by</c> 声明：排序键 + 方向。</summary>
    public readonly struct TargetSortSpec
    {
        public TargetSortKey Key { get; }

        public SortDirection Direction { get; }

        public TargetSortSpec(TargetSortKey key, SortDirection direction)
        {
            Key = key;
            Direction = direction;
        }
    }

    /// <summary>
    /// 一条 <c>target.chain_def</c> 记录的强类型视图（见 06 第 5 节 <c>TargetChainDef</c> 结构），
    /// 从 <see cref="DataRecord"/> 构造，构造期完成全部字段解析与合法性检查（与
    /// <c>Core.Numbers.PowerSet.PowerTypeDefinition</c> 同一惯例：非法数据在构造期即抛
    /// <see cref="DataFieldException"/>）。
    /// </summary>
    public sealed class TargetChainDef
    {
        public Id Id { get; }

        /// <summary>目标来源策略名（见 <see cref="ITargetSourceStrategy.Name"/>）。</summary>
        public string Source { get; }

        /// <summary>
        /// 形状"模板"：<see cref="EngineShape.Origin"/> 恒为 <see cref="Vec2.Zero"/>、
        /// <see cref="EngineShape.Direction"/>/<see cref="EngineShape.Rotation"/> 恒为 0——这两组
        /// 字段数据里不声明，由 <c>TargetHost</c> 在每次 <c>Resolve</c> 时用施法者当前坐标/朝向重新
        /// 构造一个锚定后的 <see cref="EngineShape"/>（判断记录见 targeting/README.md，与
        /// common/README.md 判断记录 2 "<c>ISkillHost.FindUnits</c> 的 origin 参数用于锚定可复用
        /// 形状模板"同一模式）。链未声明 <c>shape</c> 字段时为 null。
        /// </summary>
        public EngineShape? Shape { get; }

        /// <summary>Expr 文本或内置过滤简写组成的列表，按声明顺序全部通过（AND）才保留候选
        /// （判断记录见 targeting/README.md：与 <c>SkillFilter.Matches</c> 三维度取 OR 不同——
        /// 这里的每一项都是调用方显式列出的独立条件，语义上是"同时满足"而不是"任一即可"）。</summary>
        public IReadOnlyList<string> Filters { get; }

        public TargetSortSpec? SortBy { get; }

        /// <summary>0 表示不限；默认（数据未提供该字段时）为 1。</summary>
        public int MaxTargets { get; }

        public Id? Fallback { get; }

        public TargetChainDef(DataRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            Id = record.GetId("id");
            Source = record.GetString("source");
            Shape = record.TryGetObject("shape", out var shapeObj) ? (EngineShape?)ParseShape(record, shapeObj) : null;
            Filters = record.TryGetArray("filters", out var filtersArr) ? ParseFilters(record, filtersArr) : Array.Empty<string>();
            SortBy = record.TryGetObject("sort_by", out var sortObj) ? (TargetSortSpec?)ParseSortBy(record, sortObj) : null;

            if (record.TryGetInt("max_targets", out var maxTargets))
            {
                if (maxTargets < 0)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "max_targets", "不能为负数");
                }

                MaxTargets = checked((int)maxTargets);
            }
            else
            {
                MaxTargets = 1;
            }

            Fallback = record.TryGetId("fallback", out var fallbackId) ? (Id?)fallbackId : null;
        }

        private static EngineShape ParseShape(DataRecord record, JsonObject obj)
        {
            if (!obj.TryGetValue("kind", out var kindValue) || !(kindValue is JsonString kindStr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "shape.kind", "缺少字符串 kind 字段");
            }

            double GetNumber(string field)
            {
                return obj.TryGetValue(field, out var v) && v is JsonNumber n ? n.Value : 0.0;
            }

            switch (kindStr.Value)
            {
                case "circle":
                    return EngineShape.Circle(Vec2.Zero, GetNumber("radius"));

                case "cone":
                    return EngineShape.Cone(Vec2.Zero, 0, GetNumber("angle"), GetNumber("radius"));

                case "line":
                    return EngineShape.Line(Vec2.Zero, 0, GetNumber("length"), GetNumber("width"));

                case "rect":
                    // 判断记录：05 第 3.5 节 rect 的参数是 origin/halfExtents/rotation，但任务书给出的
                    // shape 数据字段清单只有 kind/radius/angle/length/width（与 line 共用 length/width
                    // 命名），本类型把 length/width 当作 rect 的"全宽/全高"换算成 halfExtents（各自除
                    // 2），rotation 与 origin 同 cone/line 一样由 TargetHost 用施法者朝向/坐标重新锚定。
                    return EngineShape.Rect(Vec2.Zero, new Vec2(GetNumber("length") / 2.0, GetNumber("width") / 2.0), 0);

                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "shape.kind",
                        $"非法取值 \"{kindStr.Value}\"，只允许 circle|cone|line|rect");
            }
        }

        private static IReadOnlyList<string> ParseFilters(DataRecord record, JsonArray arr)
        {
            var list = new List<string>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonString s))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "filters", $"第 {i} 个元素不是字符串");
                }

                list.Add(s.Value);
            }

            return list;
        }

        private static TargetSortSpec ParseSortBy(DataRecord record, JsonObject obj)
        {
            if (!obj.TryGetValue("key", out var keyValue) || !(keyValue is JsonString keyStr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "sort_by.key", "缺少字符串 key 字段");
            }

            TargetSortKey key;
            switch (keyStr.Value)
            {
                case "distance": key = TargetSortKey.Distance; break;
                case "hp_pct": key = TargetSortKey.HpPct; break;
                case "threat": key = TargetSortKey.Threat; break;
                case "level": key = TargetSortKey.Level; break;
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "sort_by.key",
                        $"非法取值 \"{keyStr.Value}\"，只允许 distance|hp_pct|threat|level");
            }

            var direction = SortDirection.Asc;
            if (obj.TryGetValue("direction", out var dirValue))
            {
                if (!(dirValue is JsonString dirStr))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "sort_by.direction", "期望字符串");
                }

                switch (dirStr.Value)
                {
                    case "asc": direction = SortDirection.Asc; break;
                    case "desc": direction = SortDirection.Desc; break;
                    default:
                        throw new DataFieldException(record.Table.Name, record.Key, "sort_by.direction",
                            $"非法取值 \"{dirStr.Value}\"，只允许 asc|desc");
                }
            }

            return new TargetSortSpec(key, direction);
        }
    }
}

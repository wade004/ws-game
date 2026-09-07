using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 一条 <c>display.anim_set.clips</c> 剪辑内单个关键帧事件（见 04_数据与内容管线.md 第 7.1.1 节
    /// <c>events: [{name, time_pct}]</c>）的不可变运行期视图（ADR-0017 决策 c 新增：统一解析结果
    /// 类型，供表现层/引擎适配层把 model 型剪辑的关键帧事件——命中帧等——注册进各自的关键帧驱动
    /// 机制，不必各自重复解析同一段 JSON 结构）。
    /// </summary>
    public readonly struct AnimClipEventSpec : IEquatable<AnimClipEventSpec>
    {
        /// <summary>事件标记名（如 <c>"hit_frame"</c>，与 sprite 型 <c>Presentation.Render.
        /// FrameAnimClip.HitFrameMarker</c> 同一命名惯例，见判断记录"sprite/model 命中帧标记名统一为
        /// hit_frame"；本模块——core/foundation/display_info——不引用 presentation 程序集，这里只以
        /// 纯文本 <c>&lt;c&gt;</c> 标注对照，不用 <c>cref</c>）。</summary>
        public string Name { get; }

        /// <summary>事件在剪辑时间轴上的相对位置，取值 <c>[0, 1]</c>（0 = 剪辑开始，1 = 剪辑结束）。</summary>
        public double TimePct { get; }

        public AnimClipEventSpec(string name, double timePct)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            TimePct = timePct;
        }

        public bool Equals(AnimClipEventSpec other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) && TimePct.Equals(other.TimePct);

        public override bool Equals(object? obj) => obj is AnimClipEventSpec other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name?.GetHashCode() ?? 0) * 397) ^ TimePct.GetHashCode();
            }
        }

        public override string ToString() => $"{Name}@{TimePct:0.###}";
    }

    /// <summary>一条 <c>display.anim_set.clips</c> 映射的单个剪辑条目（<c>{resource_ref, events}</c>）。</summary>
    public sealed class AnimClipDef
    {
        /// <summary>指向具体动画剪辑资产的资源引用，由引擎适配层解析。</summary>
        public Id ResourceRef { get; }

        /// <summary>该剪辑登记的全部关键帧事件，按 <see cref="AnimClipEventSpec.TimePct"/> 升序排列；
        /// 未声明 <c>events</c> 字段时为空列表。</summary>
        public IReadOnlyList<AnimClipEventSpec> Events { get; }

        public AnimClipDef(Id resourceRef, IReadOnlyList<AnimClipEventSpec>? events = null)
        {
            ResourceRef = resourceRef;
            Events = events ?? Array.Empty<AnimClipEventSpec>();
        }
    }

    /// <summary>
    /// 一条 <c>display.anim_set</c> 记录的不可变强类型视图（04 第 7.1.1 节）：<c>clips</c> 字段
    /// （剪辑名 → <see cref="AnimClipDef"/>）的统一解析结果（ADR-0017 决策 c）。
    /// <para>
    /// 判断记录（不复用 <c>Adapter.Unity.Presentation.AnimSetRecordParser</c> 的既有解析逻辑）：
    /// 该解析器位于 <c>adapters/unity</c>（W6-B 范围，本任务不得改动），且只解析 <c>resource_ref</c>、
    /// 显式跳过 <c>events</c>（见该类型顶部判断记录"不解析 events：sprite 型命中帧改走
    /// FrameAnimClip.Keyframes"）——这一取舍是 model 型命中帧同步尚未接线之前的临时简化，本类型
    /// 补齐 <c>events</c> 的完整解析，供 W6-B 收口 <c>AnimClipResolver</c> 时改为消费本类型，不必
    /// 再自行解析一遍同一段 JSON。
    /// </para>
    /// </summary>
    public sealed class AnimSetDef
    {
        public Id Id { get; }

        /// <summary>剪辑名（如 <c>"idle"</c>/<c>"attack"</c>）到 <see cref="AnimClipDef"/> 的映射，
        /// 键与 04 第 7.1.1 节剪辑名惯例一致，不要求点分 Id 格式。</summary>
        public IReadOnlyDictionary<string, AnimClipDef> Clips { get; }

        public AnimSetDef(Id id, IReadOnlyDictionary<string, AnimClipDef> clips)
        {
            Id = id;
            Clips = clips ?? throw new ArgumentNullException(nameof(clips));
        }

        /// <summary>从一条已加载的 <c>display.anim_set</c> <see cref="DataRecord"/> 构造（假设记录已
        /// 通过 <see cref="AnimSetEventsShapeRule"/> 校验；本方法自身也做最小防御——单条剪辑形状非法
        /// 时抛 <see cref="DataFieldException"/>，与本模块其余 <c>FromRecord</c> 一贯的"假设已校验、
        /// 缺失必填字段仍抛出"惯例一致）。</summary>
        public static AnimSetDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var clips = new Dictionary<string, AnimClipDef>(StringComparer.Ordinal);

            if (record.TryGetObject("clips", out var clipsObj))
            {
                foreach (var kv in clipsObj)
                {
                    clips[kv.Key] = ParseClip(record, kv.Key, kv.Value);
                }
            }

            return new AnimSetDef(id, clips);
        }

        private static AnimClipDef ParseClip(DataRecord record, string clipName, JsonValue value)
        {
            if (!(value is JsonObject clipObj))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "clips", $"剪辑 \"{clipName}\" 的值必须是对象");
            }

            if (!clipObj.TryGetValue("resource_ref", out var refVal) || !(refVal is JsonString refStr) || !Id.TryParse(refStr.Value, out var resourceRef))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "clips", $"剪辑 \"{clipName}\" 缺少合法的 resource_ref");
            }

            var events = new List<AnimClipEventSpec>();
            if (clipObj.TryGetValue("events", out var eventsVal) && !(eventsVal is JsonNull))
            {
                if (!(eventsVal is JsonArray eventsArr))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "clips", $"剪辑 \"{clipName}\" 的 events 必须是数组");
                }

                for (var i = 0; i < eventsArr.Count; i++)
                {
                    if (!(eventsArr[i] is JsonObject eventObj)
                        || !eventObj.TryGetValue("name", out var nameVal) || !(nameVal is JsonString nameStr) || string.IsNullOrEmpty(nameStr.Value)
                        || !eventObj.TryGetValue("time_pct", out var pctVal) || !(pctVal is JsonNumber pctNum))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "clips",
                            $"剪辑 \"{clipName}\" 的 events 第 {i} 项必须是 {{name: 非空 String, time_pct: Number}}");
                    }

                    events.Add(new AnimClipEventSpec(nameStr.Value, pctNum.Value));
                }
            }

            return new AnimClipDef(resourceRef, events);
        }
    }
}

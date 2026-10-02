using System;
using System.Collections.Generic;
using System.Globalization;
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

        public override string ToString() => Name + "@" + TimePct.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>一条 <c>display.anim_set.clips</c> 映射的单个剪辑条目（<c>{resource_ref, events}</c>）。</summary>
    public sealed class AnimClipDef
    {
        /// <summary>指向具体动画剪辑资产的资源引用，由引擎适配层解析。</summary>
        public Id ResourceRef { get; }

        /// <summary>该剪辑登记的全部关键帧事件，按 <see cref="AnimClipEventSpec.TimePct"/> 升序排列；
        /// 未声明 <c>events</c> 字段时为空列表。</summary>
        public IReadOnlyList<AnimClipEventSpec> Events { get; }

        /// <summary>手感落地 M4-D（04 第 10 节）：切入本剪辑时的交叉淡入时长（毫秒，数据行 <c>clips[*].blend_ms</c>）；
        /// null = 数据没有声明（调用方用自己的默认，<c>ModelCharacterRig</c> 取 <c>DefaultBlendSeconds</c>）；0 = 硬切。
        /// 只对 model 型的骨骼剪辑有意义（sprite 型帧序列播放器不做交叉淡入，忽略它）。</summary>
        public double? BlendMs { get; }

        public AnimClipDef(Id resourceRef, IReadOnlyList<AnimClipEventSpec>? events = null)
            : this(resourceRef, events, null)
        {
        }

        /// <summary>带混合时长的构造重载（旧构造保持原签名并转调本重载，<paramref name="blendMs"/> 为 null）。</summary>
        public AnimClipDef(Id resourceRef, IReadOnlyList<AnimClipEventSpec>? events, double? blendMs)
        {
            ResourceRef = resourceRef;
            Events = events ?? Array.Empty<AnimClipEventSpec>();
            BlendMs = blendMs;
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
    public sealed class AnimSetDef : IAnimBlendSource
    {
        /// <summary>
        /// ADR-0111：战斗姿态变体剪辑键的前缀。处于战斗姿态时，动画状态 <c>&lt;key&gt;</c>（
        /// <c>idle/move/attack/cast/hit/death/jump</c>）的默认剪辑先查 <see cref="Clips"/> 里的
        /// <c>combat_&lt;key&gt;</c>（如 <c>combat_idle</c>），<b>没有该键就回落到 <c>&lt;key&gt;</c></b>；
        /// 优先级：技能覆盖/武器风格覆盖剪辑 &gt; 战斗姿态变体键 &gt; 普通键。前缀字符串全仓库只在这里定义
        /// 一处，需要它的地方一律经本常量或 <see cref="CombatClipKey"/> 取用。
        /// </summary>
        public const string CombatClipKeyPrefix = "combat_";

        /// <summary>ADR-0111：<paramref name="baseClipKey"/>（如 <c>"idle"</c>）对应的战斗姿态变体剪辑键
        /// （<c>"combat_idle"</c>），见 <see cref="CombatClipKeyPrefix"/>。</summary>
        public static string CombatClipKey(string baseClipKey) => CombatClipKeyPrefix + baseClipKey;

        public Id Id { get; }

        /// <summary>剪辑名（如 <c>"idle"</c>/<c>"attack"</c>）到 <see cref="AnimClipDef"/> 的映射，
        /// 键与 04 第 7.1.1 节剪辑名惯例一致，不要求点分 Id 格式；除七个基础状态键外，还可声明战斗姿态
        /// 变体键 <c>combat_&lt;基础键&gt;</c>（ADR-0111，见 <see cref="CombatClipKeyPrefix"/>）。</summary>
        public IReadOnlyDictionary<string, AnimClipDef> Clips { get; }

        /// <summary>
        /// 手感设计/04 第 7 节：继承的姿势集（<c>extends</c>）；null 表示不继承。<see cref="Clips"/> 在经
        /// <see cref="FromRecord(DataRecord, IDataRegistryView)"/> 构造时已把继承链合并进来（子集声明的键覆盖父集同名键，
        /// "同名"按规范键判定，见 <see cref="PoseKeys.Canonicalize"/>）；本属性只保留"声明了继承谁"这件事。
        /// </summary>
        public Id? Extends { get; }

        /// <summary>手感落地 M4-D（04 第 10 节）：每对键的切入混合时长声明（数据行 <c>blends: [{from, to, blend_ms}]</c>），
        /// 经 <see cref="FromRecord(DataRecord, IDataRegistryView)"/> 构造时已合并继承链（子集声明的同一对覆盖父集；
        /// 键按规范键判同对）；空列表 = 没有声明。键是 <see cref="Clips"/> 里的剪辑键，换算成资源引用见
        /// <see cref="TryGetBlendSeconds"/>。</summary>
        public IReadOnlyList<AnimBlendPair> Blends { get; }

        private Dictionary<(Id, Id), double>? _pairMs;
        private Dictionary<Id, double>? _clipMs;

        public AnimSetDef(Id id, IReadOnlyDictionary<string, AnimClipDef> clips)
            : this(id, clips, null)
        {
        }

        /// <summary>带继承声明的构造重载（旧构造保持原签名并转调本重载，<paramref name="extends"/> 为 null）。</summary>
        public AnimSetDef(Id id, IReadOnlyDictionary<string, AnimClipDef> clips, Id? extends)
            : this(id, clips, extends, null)
        {
        }

        /// <summary>带每对键混合时长声明的构造重载（M4-D；旧构造转调本重载，<paramref name="blends"/> 为 null = 无声明）。</summary>
        public AnimSetDef(Id id, IReadOnlyDictionary<string, AnimClipDef> clips, Id? extends, IReadOnlyList<AnimBlendPair>? blends)
        {
            Id = id;
            Clips = clips ?? throw new ArgumentNullException(nameof(clips));
            Extends = extends;
            Blends = blends ?? Array.Empty<AnimBlendPair>();
        }

        /// <summary>
        /// <see cref="IAnimBlendSource"/>：从剪辑 <paramref name="fromClip"/>（null = 此前没有播放过剪辑）切到
        /// <paramref name="toClip"/> 的交叉淡入时长（秒）。优先级：每对键（<see cref="Blends"/>）&gt; 目标剪辑的逐键
        /// <see cref="AnimClipDef.BlendMs"/> &gt; 没有声明（返回 false，调用方用默认）。键在 <see cref="Clips"/> 里找不到的
        /// 悬空声明被忽略（由校验规则报警告）；同一资源被多个键引用（别名）时共用同一个值，同资源多值取键名序最前者。
        /// </summary>
        public bool TryGetBlendSeconds(Id? fromClip, Id toClip, out double seconds)
        {
            EnsureBlendTables();
            if (fromClip.HasValue && _pairMs!.TryGetValue((fromClip.Value, toClip), out var pairMs))
            {
                seconds = pairMs / 1000.0;
                return true;
            }
            if (_clipMs!.TryGetValue(toClip, out var clipMs))
            {
                seconds = clipMs / 1000.0;
                return true;
            }
            seconds = 0.0;
            return false;
        }

        private void EnsureBlendTables()
        {
            if (_pairMs != null && _clipMs != null)
            {
                return;
            }

            var keys = new List<string>(Clips.Keys);
            keys.Sort(StringComparer.Ordinal);
            var clipMs = new Dictionary<Id, double>();
            var byCanonical = new Dictionary<string, Id>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var def = Clips[key];
                byCanonical[PoseKeys.Canonicalize(key)] = def.ResourceRef;
                if (def.BlendMs.HasValue && !clipMs.ContainsKey(def.ResourceRef))
                {
                    clipMs[def.ResourceRef] = def.BlendMs.Value;
                }
            }

            var pairMs = new Dictionary<(Id, Id), double>();
            foreach (var pair in Blends)
            {
                if (byCanonical.TryGetValue(PoseKeys.Canonicalize(pair.FromKey), out var fromRef)
                    && byCanonical.TryGetValue(PoseKeys.Canonicalize(pair.ToKey), out var toRef))
                {
                    pairMs[(fromRef, toRef)] = pair.BlendMs;
                }
            }

            _clipMs = clipMs;
            _pairMs = pairMs;
        }

        /// <summary>从一条已加载的 <c>display.anim_set</c> <see cref="DataRecord"/> 构造（假设记录已
        /// 通过 <see cref="AnimSetEventsShapeRule"/> 校验；本方法自身也做最小防御——单条剪辑形状非法
        /// 时抛 <see cref="DataFieldException"/>，与本模块其余 <c>FromRecord</c> 一贯的"假设已校验、
        /// 缺失必填字段仍抛出"惯例一致）。</summary>
        public static AnimSetDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var clips = ParseOwnClips(record);
            Id? extends = record.TryGetId("extends", out var parentId) ? parentId : (Id?)null;
            return new AnimSetDef(id, clips, extends, ParseOwnBlends(record));
        }

        /// <summary>
        /// 手感设计/04 第 7 节：解析并<b>合并继承链</b>——<c>extends</c> 指向的姿势集（递归，深度不限）先进入、
        /// 本记录声明的键逐键覆盖同名键（规范键相同即同名：子集写 <c>idle.combat</c> 覆盖父集的旧键 <c>combat_idle</c>）。
        /// 合并后的 <see cref="Clips"/> 就是"手写等价集"，之后才走回落链。没有 <c>extends</c> 的记录结果与
        /// <see cref="FromRecord(DataRecord)"/> 逐项一致。继承成环、指向不存在的记录抛 <see cref="DataFieldException"/>
        /// （数据校验本应在加载期拦下，见 <see cref="AnimSetPoseRule"/>；这里不静默降级）。
        /// </summary>
        public static AnimSetDef FromRecord(DataRecord record, IDataRegistryView registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var self = FromRecord(record);
            if (self.Extends == null)
            {
                return self;
            }

            // 自下而上收集继承链（本记录在前），再自上而下合并。
            var chain = new List<DataRecord> { record };
            var visited = new HashSet<string>(StringComparer.Ordinal) { record.Key };
            var cursor = record;
            while (cursor.TryGetId("extends", out var parentRef))
            {
                var parent = registry.Get(record.Table.Name, parentRef);
                if (parent == null)
                {
                    throw new DataFieldException(record.Table.Name, cursor.Key, "extends", $"继承的姿势集 \"{parentRef.Value}\" 不存在");
                }
                if (!visited.Add(parent.Key))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "extends", $"姿势集继承成环：{string.Join(" -> ", Keys(chain))} -> {parent.Key}");
                }
                chain.Add(parent);
                cursor = parent;
            }

            var merged = new List<KeyValuePair<string, AnimClipDef>>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var own = ParseOwnClips(chain[i]);
                if (own.Count == 0)
                {
                    continue;
                }

                var overridden = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in own)
                {
                    overridden.Add(PoseKeys.Canonicalize(kv.Key));
                }
                // M4-D：子集覆盖同名键时，子集没有声明 blend_ms 就沿用被覆盖键的 blend_ms（体量组换了资源引用，切入混合时长不必重复声明）。
                var inheritedBlend = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var e in merged)
                {
                    var canonical = PoseKeys.Canonicalize(e.Key);
                    if (e.Value.BlendMs.HasValue && overridden.Contains(canonical))
                    {
                        inheritedBlend[canonical] = e.Value.BlendMs.Value;
                    }
                }
                merged.RemoveAll(e => overridden.Contains(PoseKeys.Canonicalize(e.Key)));
                foreach (var kv in own)
                {
                    var def = kv.Value;
                    if (!def.BlendMs.HasValue && inheritedBlend.TryGetValue(PoseKeys.Canonicalize(kv.Key), out var inherited))
                    {
                        def = new AnimClipDef(def.ResourceRef, def.Events, inherited);
                    }
                    merged.Add(new KeyValuePair<string, AnimClipDef>(kv.Key, def));
                }
            }

            var clips = new Dictionary<string, AnimClipDef>(merged.Count, StringComparer.Ordinal);
            foreach (var kv in merged)
            {
                clips[kv.Key] = kv.Value;
            }

            // 每对键混合时长：自上而下合并，子集声明的同一对（规范键）覆盖父集。
            var blends = new List<AnimBlendPair>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var pair in ParseOwnBlends(chain[i]))
                {
                    var from = PoseKeys.Canonicalize(pair.FromKey);
                    var to = PoseKeys.Canonicalize(pair.ToKey);
                    blends.RemoveAll(b => PoseKeys.Canonicalize(b.FromKey) == from && PoseKeys.Canonicalize(b.ToKey) == to);
                    blends.Add(pair);
                }
            }
            return new AnimSetDef(self.Id, clips, self.Extends, blends);
        }

        private static List<AnimBlendPair> ParseOwnBlends(DataRecord record)
        {
            var result = new List<AnimBlendPair>();
            if (!record.TryGetArray("blends", out var arr))
            {
                return result;
            }

            for (var i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonObject o)
                    || !o.TryGetValue("from", out var fromVal) || !(fromVal is JsonString fromStr) || string.IsNullOrEmpty(fromStr.Value)
                    || !o.TryGetValue("to", out var toVal) || !(toVal is JsonString toStr) || string.IsNullOrEmpty(toStr.Value)
                    || !o.TryGetValue("blend_ms", out var msVal) || !(msVal is JsonNumber msNum) || msNum.Value < 0.0)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "blends",
                        $"blends 第 {i} 项必须是 {{from: 非空 String, to: 非空 String, blend_ms: 非负 Number}}");
                }
                result.Add(new AnimBlendPair(fromStr.Value, toStr.Value, msNum.Value));
            }
            return result;
        }

        private static IEnumerable<string> Keys(List<DataRecord> records)
        {
            foreach (var r in records) yield return r.Key;
        }

        private static Dictionary<string, AnimClipDef> ParseOwnClips(DataRecord record)
        {
            var clips = new Dictionary<string, AnimClipDef>(StringComparer.Ordinal);
            if (record.TryGetObject("clips", out var clipsObj))
            {
                foreach (var kv in clipsObj)
                {
                    clips[kv.Key] = ParseClip(record, kv.Key, kv.Value);
                }
            }
            return clips;
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

            double? blendMs = null;
            if (clipObj.TryGetValue("blend_ms", out var blendVal) && !(blendVal is JsonNull))
            {
                if (!(blendVal is JsonNumber blendNum) || blendNum.Value < 0.0)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "clips", $"剪辑 \"{clipName}\" 的 blend_ms 必须是非负数字（毫秒）");
                }
                blendMs = blendNum.Value;
            }

            return new AnimClipDef(resourceRef, events, blendMs);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// 度量所属的比较类别（06 第 3.1 节"逻辑组逐字节比较，表现组允差比较"）：
    /// <see cref="Logic"/> 只依赖模拟 tick，逐字节一致；<see cref="Presentation"/> 依赖帧时间线与假适配器 View，
    /// 在声明允差内比较；<see cref="RealTime"/> 依赖真实时钟与分配计数，只做"不超过基线的若干倍"的上限检查。
    /// </summary>
    public enum MetricClass
    {
        Logic,
        Presentation,
        RealTime,
    }

    /// <summary>允差形态：精确相等、绝对允差、倍率上限（带绝对下限，防止基线接近 0 时误报）。</summary>
    public enum ToleranceKind
    {
        Exact,
        Absolute,
        RatioCeiling,
    }

    /// <summary>
    /// 一个度量的声明：名字、类别、允差。允差写在代码里随版本走（基线文件只存值）；
    /// 允差只允许收紧、不允许放宽（06 第 3.3 节），放宽等于改基线语义，须走评审。
    /// </summary>
    public sealed class MetricSpec
    {
        public string Name { get; }

        public MetricClass Class { get; }

        public ToleranceKind Tolerance { get; }

        /// <summary><see cref="ToleranceKind.Absolute"/> 的绝对允差，或 <see cref="ToleranceKind.RatioCeiling"/> 的倍率。</summary>
        public double Amount { get; }

        /// <summary><see cref="ToleranceKind.RatioCeiling"/> 的绝对下限：实际值不超过 <c>max(基线 × 倍率, 下限)</c> 即通过。</summary>
        public double Floor { get; }

        public string Description { get; }

        private MetricSpec(string name, MetricClass cls, ToleranceKind tolerance, double amount, double floor, string description)
        {
            Name = name;
            Class = cls;
            Tolerance = tolerance;
            Amount = amount;
            Floor = floor;
            Description = description;
        }

        public static MetricSpec Exact(string name, MetricClass cls, string description) =>
            new MetricSpec(name, cls, ToleranceKind.Exact, 0, 0, description);

        public static MetricSpec Absolute(string name, MetricClass cls, double tolerance, string description) =>
            new MetricSpec(name, cls, ToleranceKind.Absolute, tolerance, 0, description);

        public static MetricSpec RatioCeiling(string name, MetricClass cls, double ratio, double floor, string description) =>
            new MetricSpec(name, cls, ToleranceKind.RatioCeiling, ratio, floor, description);

        public string DescribeTolerance()
        {
            switch (Tolerance)
            {
                case ToleranceKind.Exact:
                    return "exact";
                case ToleranceKind.Absolute:
                    return "abs<=" + Amount.ToString("R", CultureInfo.InvariantCulture);
                default:
                    return "ceil<=max(x" + Amount.ToString("R", CultureInfo.InvariantCulture) + ","
                        + Floor.ToString("R", CultureInfo.InvariantCulture) + ")";
            }
        }
    }

    /// <summary>
    /// 度量组接口：以后新增的组（动作阶段、输入缓冲、顿帧、受击反应、群体命中、装备解析……）只需实现本接口并
    /// 注册进 <see cref="MetricRegistry"/>，不改运行器、指纹序列化与比较器。
    /// <para>实现约定：<see cref="Specs"/> 声明本组全部度量；<see cref="Compute"/> 只产出声明过的度量，
    /// 数值先经 <see cref="MetricSink.Round"/> 规整（9 位小数，去掉浮点噪声）；逻辑类度量只许读逻辑时间线，
    /// 不得读 <see cref="LabRecording.Frames"/> 与 <see cref="LabRecording.Real"/>。</para>
    /// </summary>
    public interface IMetricGroup
    {
        string Name { get; }

        IReadOnlyList<MetricSpec> Specs { get; }

        void Compute(LabRecording recording, MetricSink sink);
    }

    /// <summary>
    /// 条件度量组：只对部分运行适用（例如只有换装场景的记录才有换装数据）。不适用的运行里该组整体不出现在指纹中；
    /// 比较时，基线与实际都没有该组即视为双方都不适用，不报"新增组"，因此给不适用的既有脚本新增条件组不改它们的基线。
    /// </summary>
    public interface IConditionalMetricGroup : IMetricGroup
    {
        bool AppliesTo(LabRecording recording);
    }

    /// <summary>度量产出接收器：校验名字属于声明并保持声明顺序（指纹键序固定）。</summary>
    public sealed class MetricSink
    {
        private readonly Dictionary<string, JsonValue> _values = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
        private readonly IReadOnlyList<MetricSpec> _specs;

        internal MetricSink(IReadOnlyList<MetricSpec> specs)
        {
            _specs = specs;
        }

        /// <summary>数值规整：9 位小数四舍五入，并把 -0 归一为 0。</summary>
        public static double Round(double value)
        {
            var r = Math.Round(value, 9, MidpointRounding.AwayFromZero);
            return r == 0 ? 0.0 : r;
        }

        public void Add(string name, double value) => Put(name, LabJson.Num(Round(value)));

        public void Add(string name, int value) => Put(name, LabJson.Num(value));

        public void Add(string name, string value) => Put(name, LabJson.Str(value));

        public void Add(string name, IEnumerable<double> values)
        {
            var items = new List<JsonValue>();
            foreach (var v in values)
            {
                items.Add(LabJson.Num(Round(v)));
            }

            Put(name, new JsonArray(items));
        }

        private void Put(string name, JsonValue value)
        {
            var declared = false;
            foreach (var spec in _specs)
            {
                if (string.Equals(spec.Name, name, StringComparison.Ordinal))
                {
                    declared = true;
                    break;
                }
            }

            if (!declared)
            {
                throw new InvalidOperationException($"度量 {name} 没有在 Specs 里声明");
            }

            _values[name] = value;
        }

        internal JsonObject Build(string groupName)
        {
            var builder = new JsonObjectBuilder();
            foreach (var spec in _specs)
            {
                if (!_values.TryGetValue(spec.Name, out var value))
                {
                    throw new InvalidOperationException($"度量组 {groupName} 声明了 {spec.Name} 但 Compute 没有产出");
                }

                builder.Add(spec.Name, value);
            }

            return builder.Build();
        }
    }

    /// <summary>度量组注册表：保持注册顺序（指纹里组的先后顺序固定）。</summary>
    public sealed class MetricRegistry
    {
        private readonly List<IMetricGroup> _groups = new List<IMetricGroup>();

        public IReadOnlyList<IMetricGroup> Groups => _groups;

        public MetricRegistry Register(IMetricGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            foreach (var g in _groups)
            {
                if (string.Equals(g.Name, group.Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"度量组 {group.Name} 重复注册", nameof(group));
                }
            }

            _groups.Add(group);
            return this;
        }

        public IMetricGroup? Find(string name)
        {
            foreach (var g in _groups)
            {
                if (string.Equals(g.Name, name, StringComparison.Ordinal))
                {
                    return g;
                }
            }

            return null;
        }

        /// <summary>计算全部组的度量，返回 <c>{组名: {度量名: 值}}</c> 对象。</summary>
        public JsonObject Compute(LabRecording recording)
        {
            var builder = new JsonObjectBuilder();
            foreach (var group in _groups)
            {
                if (group is IConditionalMetricGroup conditional && !conditional.AppliesTo(recording))
                {
                    continue;
                }

                var sink = new MetricSink(group.Specs);
                group.Compute(recording, sink);
                builder.Add(group.Name, sink.Build(group.Name));
            }

            return builder.Build();
        }

        /// <summary>内置四组：响应、移动、攻击、性能（06 第 3.1 节本期落地的四组），加条件组：换装解析（仅换装场景）、手感七组、空间语义（仅带竖直轴的格子/跳跃脚本/靶子声明了高度的运行）。</summary>
        public static MetricRegistry CreateDefault() =>
            new MetricRegistry()
                .Register(new ResponseMetricGroup())
                .Register(new MovementMetricGroup())
                .Register(new AttackMetricGroup())
                .Register(new PerformanceMetricGroup())
                .Register(new EquipMetricGroup())
                .Register(new InputBufferMetricGroup())
                .Register(new ActionTimelineMetricGroup())
                .Register(new HitstopMetricGroup())
                .Register(new HitReactionMetricGroup())
                .Register(new MotionMetricGroup())
                .Register(new SpatialHitMetricGroup())
                .Register(new ProjectileMetricGroup())
                .Register(new PoiseMetricGroup())
                .Register(new PresentationTimelineMetricGroup())
                .Register(new SpaceMetricGroup())
                .Register(new SpaceExtMetricGroup())
                .Register(new SpaceNavMetricGroup());
    }
}

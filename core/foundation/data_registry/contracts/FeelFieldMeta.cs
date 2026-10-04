using System;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 手感字段的半属（手感设计/00 第 2 节、05 第 4 节）：判定型字段改变模拟结果，只放逻辑层可读的表；
    /// 呈现型字段只改变玩家看到/听到的内容，只放表现层表。一个字段只属于一边。
    /// </summary>
    public enum FeelHalf
    {
        /// <summary>判定型（改变"动作何时发生、位置怎么变、谁被打到"）。</summary>
        Judging,

        /// <summary>呈现型（只改变"玩家看到听到什么"）。</summary>
        Presenting,
    }

    /// <summary>手感字段所属的七个分组（手感设计/05 第 3.1 节）。</summary>
    public enum FeelGroup
    {
        /// <summary>输入：缓冲、宽限、轴处理。</summary>
        Input,

        /// <summary>移动：加减速、转向、步态、步幅。</summary>
        Movement,

        /// <summary>动作：分相倍率、取消/连招窗口、转向辅助。</summary>
        Action,

        /// <summary>受击：冲击等级、顿帧、硬直、击退、倒地。</summary>
        Reaction,

        /// <summary>镜头：跟随、前瞻、死区、阻尼、战斗缩放、冲击增益与限频。</summary>
        Camera,

        /// <summary>特效：反馈包引用、拖尾/残影。</summary>
        Effects,

        /// <summary>音频：挥空/命中/增味/脚步各层强度档、材质、同时发声上限。</summary>
        Audio,
    }

    /// <summary>
    /// 允许的覆盖操作集合（手感设计/05 第 3.3 节）。<c>set</c>/<c>multiply</c>/<c>add</c> 三种基本操作；
    /// <c>remove</c> 只用于列表字段（显式删除元素，不用空值暗示删除）。布尔与引用类字段只允许 <c>Set</c>。
    /// </summary>
    [Flags]
    public enum FeelOpSet
    {
        None = 0,
        Set = 1,
        Multiply = 2,
        Add = 4,
        Remove = 8,
    }

    /// <summary>
    /// 手感字段的合成来源（手感设计/05 第 3.4 节）：决定体型侧（第 2/3 层）与武器层（第 4 层）谁是主。
    /// </summary>
    public enum FeelComposition
    {
        /// <summary>角色为主：武器层不写该字段（写了为错误）。</summary>
        CharacterPrimary,

        /// <summary>武器为主：体型侧只允许 <c>multiply</c>。</summary>
        WeaponPrimary,

        /// <summary>攻击期间武器临时覆盖：武器层的值只在动作进行中生效，动作结束自动撤回。</summary>
        AttackOverride,
    }

    /// <summary>
    /// 手感字段的单位（手感设计/00 第 6 节单位枚举）。相对量由 <c>feel.calibration</c> 标定换算为绝对量；
    /// 与 <see cref="FieldUnit"/>（时间模型单位，04 第 3.4 节）互不相干。
    /// </summary>
    public enum FeelUnit
    {
        /// <summary>无量纲且不换算（布尔、枚举、引用、计数等）。</summary>
        None,

        /// <summary>毫秒；判定型时间字段解析时按固定步长换算为 tick（四舍五入，非零至少 1 tick）。</summary>
        Milliseconds,

        /// <summary>位移：身高倍数，标定后乘参考身高得世界单位（也用于世界距离型镜头参数）。</summary>
        BodyHeights,

        /// <summary>位移：基础移速下的秒数，标定后乘参考基础移速得世界单位。不推荐：没有任何字段使用该单位（登记表单位枚举只加不改，故保留）。</summary>
        BaseSpeedSeconds,

        /// <summary>速度：基础移速的倍数。运动层的目标速度 = 倍数 × 单位的移动速度属性（属性已承载基础移速）；解析结果的绝对值视图 = 倍数 × 标定的参考基础移速。</summary>
        BaseSpeedRatio,

        /// <summary>角速度：度/秒，无需换算。</summary>
        DegreesPerSecond,

        /// <summary>角度：度，无需换算。</summary>
        Degrees,

        /// <summary>无量纲倍率/比例（相对量，不随标定换算）。</summary>
        Ratio,

        /// <summary>动作分相：剪辑长度的比例（0～1），由剪辑元数据换算，解析器不换算。</summary>
        ClipRatio,

        /// <summary>镜头幅度：画面高度的比例，标定后乘参考镜头高度得世界单位。</summary>
        ScreenHeightRatio,

        /// <summary>音频强度档（离散整数档位）。</summary>
        IntensityTier,

        /// <summary>计数（槽位数、并发数等整数）。</summary>
        Count,
    }

    /// <summary>
    /// 手感字段的登记元数据（手感设计/00 第 4 节、05 第 4 节；ADR-0118 决策 6）：在既有字段登记机制
    /// （04 第 3.2～3.6 节）上新增的半属/分组/允许操作/合成来源/单位/副手可叠加六项，由
    /// <see cref="FieldSchema.WithFeel"/> 挂到具体字段上。一份登记同时驱动数据校验、解析器合成、调参面板
    /// 自动生成与测试自动枚举。
    /// <para>
    /// 判断记录（元数据类型放在 data_registry 而不是 feel 模块）：<see cref="FieldSchema"/> 属于 data_registry，
    /// 它要持有这份元数据；feel 模块依赖 data_registry（校验规则、表登记），反过来依赖会成环。因此纯元数据类型
    /// （本类与上面的枚举）落在 data_registry 的 contracts，解析器与校验规则落在 feel 模块。
    /// </para>
    /// </summary>
    public sealed class FeelFieldMeta
    {
        public FeelHalf Half { get; }

        public FeelGroup Group { get; }

        public FeelOpSet Ops { get; }

        public FeelComposition Composition { get; }

        public FeelUnit Unit { get; }

        /// <summary>副手武器可叠加：副手武器的 <c>feel.weapon</c> 行只允许对这类字段以 add/multiply 叠加
        /// （手感设计/05 第 3.4 节）。仅"武器为主"字段可设。</summary>
        public bool OffhandStackable { get; }

        public FeelFieldMeta(
            FeelHalf half,
            FeelGroup group,
            FeelOpSet ops,
            FeelComposition composition,
            FeelUnit unit = FeelUnit.None,
            bool offhandStackable = false)
        {
            if (ops == FeelOpSet.None)
            {
                throw new ArgumentException("手感字段至少允许一种覆盖操作", nameof(ops));
            }
            if (offhandStackable && composition != FeelComposition.WeaponPrimary)
            {
                throw new ArgumentException("副手可叠加只能用于\"武器为主\"字段", nameof(offhandStackable));
            }
            if (offhandStackable && (ops & (FeelOpSet.Add | FeelOpSet.Multiply)) == FeelOpSet.None)
            {
                throw new ArgumentException("副手可叠加字段必须允许 add 或 multiply", nameof(offhandStackable));
            }

            Half = half;
            Group = group;
            Ops = ops;
            Composition = composition;
            Unit = unit;
            OffhandStackable = offhandStackable;
        }
    }
}

using Core.Foundation.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 同 <c>stack_category</c> 光环叠加数超过 <c>max_stacks</c> 时的处理策略（见 06 第 3.8 节
    /// "忽略新实例的叠加请求，或按策略替换为最新参数（策略配置项：ignore|refresh_only|replace）"）。
    /// </summary>
    public enum StackOverflowPolicy
    {
        /// <summary>忽略本次叠加请求：不刷新持续时间、不增加层数、不替换参数。</summary>
        Ignore,

        /// <summary>只刷新持续时间，不增加层数（层数保持在 <c>max_stacks</c>）。</summary>
        RefreshOnly,

        /// <summary>撤销旧实例的全部效果，用本次请求的参数重新创建一个新实例（层数重置为 1）。</summary>
        Replace,
    }

    /// <summary>
    /// <c>core/rules/skill</c> 模块的构造期口味配置（见落地方案 T2-4/T2-5/T2-6 行、06 第 3.6/3.8
    /// 节"策略配置项"）。全部字段均有默认值，但默认取值本身不代表任何具体游戏的口味决策——
    /// 每款游戏应在自己的口味配置清单中显式声明这些字段的取值（呼应 T2-4 禁止事项"禁止把公共
    /// 冷却写死为启用或关闭"）。
    /// </summary>
    public sealed class SkillOptions
    {
        /// <summary>
        /// 公共冷却（GCD）是否启用（见 06 第 3.6 节"公共冷却开关：是否存在跨技能的统一冷却……
        /// 均为策略配置项；关闭时步骤 4 恒通过"）。默认 <c>false</c>——不预设任何游戏一定需要
        /// 公共冷却，禁止把该配置项的默认取值当作产品决策（T2-4 禁止事项）。
        /// </summary>
        public bool GcdEnabled { get; set; } = false;

        /// <summary>
        /// 公共冷却时长（以数据集声明的时间单位计，见 04 第 3.1 节）。仅 <see cref="GcdEnabled"/>
        /// 为 true 且技能 <c>respects_gcd</c> 为 true 时生效。本字段的具体数值同样由游戏口味配置
        /// 清单给出，此处默认值只是一个占位式合理起点，不代表任何产品决策。
        /// </summary>
        public double GcdDuration { get; set; } = 1.5;

        /// <summary>
        /// 法术队列窗口长度（见 06 第 3.6 节"法术队列……队列窗口长度是策略配置项"）：读条剩余
        /// 时间小于等于本值时，再次 <c>CastSkill</c> 会进入队列而不是立即失败。默认 0.3。
        /// </summary>
        public double QueueWindow { get; set; } = 0.3;

        /// <summary>叠加超限策略（见 <see cref="StackOverflowPolicy"/>）。默认
        /// <see cref="StackOverflowPolicy.RefreshOnly"/>（06 第 3.8 节"默认刷新持续时间"语义
        /// 的延伸——超限时至少保证持续时间被刷新）。</summary>
        public StackOverflowPolicy StackOverflowPolicy { get; set; } = StackOverflowPolicy.RefreshOnly;

        /// <summary>
        /// 是否允许同一光环定义的多个来源分别计时（见 06 第 3.8 节"是否允许多来源分别计时是
        /// （建议）扩展位，默认关闭以简化状态管理"）。关闭时，同一 <c>target</c> 身上同一
        /// <c>aura_def</c> 无论来源是谁都视为同一个实例槽位；开启时按 <c>(defId, sourceId)</c>
        /// 分别维护独立实例。默认 false。
        /// </summary>
        public bool AllowMultiSourceTiming { get; set; } = false;

        /// <summary>单条 <c>skill.def</c>/<c>skill.aura_def</c> 效果列表长度上限（见 04 第 5 节
        /// "效果数上限……策略配置项，默认建议值见 06"）。默认 8。</summary>
        public int MaxEffectsPerSkill { get; set; } = 8;

        /// <summary>
        /// Proc 触发链递归深度上限（见落地方案 T2-6 禁止事项"禁止触发链无限递归（需有防护测试
        /// 用例）"）：A 触发 B、B 触发 A 一类循环链，深度超过本值时拒绝并记诊断。默认 8。
        /// </summary>
        public int MaxTriggerDepth { get; set; } = 8;

        /// <summary>Proc 命中判定使用的 <see cref="Core.Foundation.Rng.IRngHost"/> 分流 id
        /// （见 03 第 9 节 <c>RngHost</c>"按 stream 分流生成随机数以保证可复现"）。默认
        /// <c>skill.proc</c>。</summary>
        public Id RngStream { get; set; } = new Id("skill.proc");
    }
}

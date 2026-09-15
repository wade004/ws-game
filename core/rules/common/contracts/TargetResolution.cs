using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// <c>target.chain_def.overflow_policy</c> 三态（T-N3-8；ADR-0031 决策 6、拍板 7；06 第 3.7 节
    /// 2026-09-14 修订段）：候选目标数超过 <c>max_targets</c> 时的处理策略。
    /// <para>
    /// 判断记录（系数定义为临时判断，待设计层确认）：06 第 3.7 节修订段原文只给出三个策略名字
    /// （"截断、平摊、总量封顶"）与默认值（截断），未展开到"每个目标分配系数"这一精确公式层面；
    /// ADR-0031 决策 6 同样只提到"目标形状新增 max_targets 与超出策略（截断、平摊、总量封顶），
    /// 默认截断"。本枚举各成员的系数定义是 T-N3-8 按字面含义给出的临时判断（已在任务汇报里标注
    /// "待设计层确认"）：<see cref="Truncate"/> 与改动前 <c>TargetHost.ApplyMaxTargets</c> 的既有
    /// 截断行为逐一对应（旧 <c>ITargetHost.Resolve</c> 签名的既有行为，保证向后兼容）；
    /// <see cref="Split"/>/<see cref="Cap"/> 是本任务新引入的两种"全部命中、按比例稀释效果值"的
    /// 变体，二者的差异（除以命中数 n 时分子取 <c>max_targets</c> 还是取 1）在"平摊"（在
    /// max_targets 份额内均分，总量随 max_targets 变化）与"总量封顶"（硬封顶为单个目标的满额值，
    /// 总量不随 max_targets 变化，只由 max_targets 决定从多少目标数开始触发稀释）两个字面含义之间
    /// 择一，见各成员注释。
    /// </para>
    /// </summary>
    public enum TargetOverflowPolicy
    {
        /// <summary>截断（默认）：候选按链既有的过滤/排序结果截断，只保留前 <c>max_targets</c> 个，
        /// 多出的候选完全不命中（不出现在 <see cref="TargetResolution.Targets"/> 里）；命中的每个
        /// 目标分配系数恒为 1。与改动前 <c>TargetHost</c> 的既有截断行为逐一对应。</summary>
        Truncate,

        /// <summary>平摊：候选全部命中（不按数量截断），效果值总量守恒为"<c>max_targets</c> 个目标
        /// 的满额值"，按实际命中数量 n 均分——每个目标分配系数 = <c>max_targets</c> / n（n &gt;
        /// <c>max_targets</c> 时才 &lt; 1，见 <see cref="TargetResolution"/> 判断记录"未超限时系数
        /// 恒为 1"）。</summary>
        Split,

        /// <summary>总量封顶：候选全部命中（不按数量截断），效果值总量硬封顶为"单个目标的满额值"
        /// （不随命中数量增长，也不随 <c>max_targets</c> 的具体取值增长——<c>max_targets</c> 只决定
        /// 从多少目标数开始触发这条稀释），按实际命中数量 n 均分——每个目标分配系数 = 1 / n。</summary>
        Cap,
    }

    /// <summary>
    /// <see cref="ITargetHost.ResolveWithCoefficients"/> 的返回值（T-N3-8；ADR-0031 拍板 7；06 第 3.7
    /// 节修订段）：候选目标列表 + 每个目标的分配系数（<see cref="TargetOverflowPolicy"/> 三态各自
    /// 定义，见该枚举成员注释）。未超过 <see cref="Cap"/>（或 <see cref="Cap"/> 为 0，即不限）时，
    /// 策略不参与——<see cref="Targets"/> 与旧 <see cref="ITargetHost.Resolve(Id, Id)"/> 返回的候选
    /// 集合逐一对应、系数恒为 1（见 <c>Core.Rules.Targeting.TargetHost</c>"旧签名投影关系"判断
    /// 记录，targeting/README.md 同条目）。
    /// <para>
    /// 不可变：构造期对入参序列做防御性拷贝（同 <see cref="ResolveResult"/> 判断记录），构造之后
    /// 传入的原始集合被外部修改不会影响本实例。
    /// </para>
    /// </summary>
    public sealed class TargetResolution
    {
        /// <summary>候选目标与其分配系数，顺序与旧 <see cref="ITargetHost.Resolve(Id, Id)"/> 排序
        /// 结果一致（<see cref="TargetOverflowPolicy.Truncate"/> 策略下按该顺序截断）。</summary>
        public IReadOnlyList<(Id Target, double Coefficient)> Targets { get; }

        /// <summary>本次解析实际生效的超出策略（链未声明 <c>overflow_policy</c> 字段时为
        /// <see cref="TargetOverflowPolicy.Truncate"/>，见 schema 缺省值）。</summary>
        public TargetOverflowPolicy Policy { get; }

        /// <summary>目标数上限（<c>target.chain_def.max_targets</c>）；0 表示不限——不限时
        /// <see cref="Policy"/> 不参与，全部候选命中、系数恒为 1。</summary>
        public int Cap { get; }

        public TargetResolution(IEnumerable<(Id Target, double Coefficient)> targets, TargetOverflowPolicy policy, int cap)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            Targets = targets.ToArray();
            Policy = policy;
            Cap = cap;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Core.Rules.Combat
{
    /// <summary>
    /// ADR-0059（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）：<see cref="AutoAttackHost"/>
    /// 对外暴露的普通攻击可观测状态——"开启/关闭"与"当前目标"两个状态（任务书拍板"状态迁移要可
    /// 观测"）合并成一个三值快照，供消费方（表现层/AI/测试）一次查询即可判断当前该不该播放挥击
    /// 表现，不需要分别查两个布尔/可空字段再自己组合语义。
    /// </summary>
    public enum AutoAttackState
    {
        /// <summary>未开启（<see cref="AutoAttackHost.SetEnabled"/> 传 <c>false</c>，或该单位从未
        /// 开启过）。</summary>
        Off,

        /// <summary>已开启，但当前没有有效目标——"未攻击"态（任务书原句）：从未
        /// <see cref="AutoAttackHost.SetTarget"/> 过、或目标消失/死亡后被清空，见
        /// <see cref="AutoAttackHost"/> 判断记录"目标消失/死亡的行为"。</summary>
        NoTarget,

        /// <summary>已开启且当前有目标——挥击计时器正在为该目标推进（是否命中受
        /// <c>ControlFlags.NoAttack</c>/射程等瞬时门控影响，那些门控不改变本状态本身，只影响
        /// 某一次挥击是否真正结算，见 <see cref="AutoAttackHost"/> 判断记录）。</summary>
        Attacking,
    }

    /// <summary>
    /// ADR-0061（消费方反馈第四批第 1 条）：<see cref="AutoAttackState"/> 与 <c>presentation/ui</c>
    /// 路径小语法使用的 snake_case 文本互转——惯例同 <c>Core.Rules.Common.AuraPolarityNames</c>：
    /// <c>Core.Foundation.Expr.ExprValue</c> 判别联合没有"枚举"这一等级的值类型（见
    /// <c>UnitSubQueries.Auras</c> 判断记录"polarity 这一层仍输出字符串"），<c>player.auto_attack.state</c>/
    /// <c>target.auto_attack.state</c> 路径查询因此在跨越这条边界时把强类型枚举降级为它在本类型
    /// 登记的 snake_case 文本，<c>HudViewModel</c> 读到该文本后再转换回枚举本身对外暴露——全部三个
    /// 取值均参与互转（与 <see cref="Core.Rules.Common.AuraPolarity.Undeclared"/> 不同，
    /// <see cref="AutoAttackState"/> 没有"未声明"这一额外语义，<see cref="AutoAttackState.Off"/> 本身
    /// 就是"没有在打"的合法可观测状态，不是数据缺失的哨兵值）。
    /// </summary>
    public static class AutoAttackStateNames
    {
        private static readonly IReadOnlyDictionary<AutoAttackState, string> ToName = new Dictionary<AutoAttackState, string>
        {
            [AutoAttackState.Off] = "off",
            [AutoAttackState.NoTarget] = "no_target",
            [AutoAttackState.Attacking] = "attacking",
        };

        private static readonly IReadOnlyDictionary<string, AutoAttackState> FromName = BuildReverse(ToName);

        private static Dictionary<string, AutoAttackState> BuildReverse(IReadOnlyDictionary<AutoAttackState, string> map)
        {
            var reverse = new Dictionary<string, AutoAttackState>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        /// <summary>枚举值 → snake_case 文本。全部 <see cref="AutoAttackState"/> 取值都在映射表中，
        /// 不会失败。</summary>
        public static string ToText(AutoAttackState state) => ToName[state];

        /// <summary>snake_case 文本 → 枚举值；未登记的文本抛 <see cref="ArgumentException"/>（同
        /// <c>Core.Rules.Common.AuraPolarityNames.Parse</c> 判断记录"运行时路径不静默降级"：
        /// <c>UnitSubQueries.AutoAttack</c> 只会写出 <see cref="ToName"/> 里登记的三个取值之一，一旦
        /// 读到无法识别的文本，说明转发链路本身已经损坏，不应该悄悄折叠成某个看似正常的默认状态。</summary>
        public static AutoAttackState Parse(string text)
        {
            if (TryParse(text, out var state))
            {
                return state;
            }

            throw new ArgumentException($"未知的 AutoAttackState 文本：\"{text ?? "<null>"}\"", nameof(text));
        }

        public static bool TryParse(string? text, out AutoAttackState state)
        {
            if (text != null && FromName.TryGetValue(text, out state))
            {
                return true;
            }

            state = default;
            return false;
        }
    }
}

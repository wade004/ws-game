using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Core.Rules.Common
{
    /// <summary>
    /// "结算类原语集合"（T-N3-1，[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md)
    /// 决策 2/12；06 第 3.2 节 2026-09-14 修订段"效果值契约与结算类原语集合"）：供 3.10 节技能预算
    /// 校验（<c>SkillBudgetAnalyzer</c>，T-N3-9）的适用范围判定与一键智能释放候选集
    /// （<c>AiHost</c>/独立求值组件，T-N3-10）共用同一判定——一个技能的 <c>effects[]</c> 只要含本
    /// 集合内任一 <see cref="EffectKind"/>，就参与预算校验、进入智能释放候选集；效果列表不含以上
    /// 任一项的技能（开锁、传送、造物、学习、写世界标志等）两者都不参与。
    /// <para>
    /// 06 原文集合：<c>school_damage</c>、<c>weapon_damage_pct</c>、<c>heal</c>、<c>projectile</c>，
    /// 以及 <c>apply_aura</c> 所引光环含 <c>periodic_damage</c>/<c>periodic_heal</c>/<c>mod_stat</c>/
    /// <c>control</c>/<c>absorb</c> 任一效果者。
    /// </para>
    /// <para>
    /// <b>契约疑点（T-N3-1 上报，待设计层确认）：</b>06 原文的"结算类原语集合"不是一份能直接按
    /// <c>effects[].kind</c> 这一层扁平判定的清单——前四项（<c>school_damage</c>/
    /// <c>weapon_damage_pct</c>/<c>heal</c>/<c>projectile</c>）是无条件的效果原语类型，但
    /// <c>apply_aura</c> 一项是有条件的："这条 <c>apply_aura</c> 效果算不算结算类"取决于它引用的
    /// <c>skill.aura_def</c> 自身的 <c>effects[].kind</c> 是否含五种光环效果之一，不是
    /// <c>apply_aura</c> 这个 <see cref="EffectKind"/> 本身的固有属性。本类型按任务书"取值以 06
    /// 修订段列出的集合为准"，把 <c>apply_aura</c> 无条件计入 <see cref="All"/>（即"可能是结算
    /// 类"，按效果原语类型这一层最贴近原文的判定），<see cref="IsSettlement(string)"/> 只做
    /// <see cref="All"/> 的成员判定，<b>不下钻解析 <c>apply_aura</c> 具体引用的光环定义</b>——这一层
    /// 更细的判定（"这条 <c>apply_aura</c> 引用的光环是否真的含五种效果之一"）需要调用方另行解析
    /// <c>aura_def</c> 引用后逐条核对光环自身的 <c>effects[].kind</c>，不是本类型职责范围，留给
    /// T-N3-9（<c>SkillBudgetAnalyzer</c>）与 T-N3-10（独立求值组件）在真正消费这份集合时决定
    /// 具体收窄方式。
    /// </para>
    /// </summary>
    public static class SettlementEffectKinds
    {
        private static readonly EffectKind[] AllKinds =
        {
            EffectKind.SchoolDamage,
            EffectKind.WeaponDamagePct,
            EffectKind.Heal,
            EffectKind.Projectile,
            EffectKind.ApplyAura,
        };

        /// <summary>
        /// 结算类原语的 snake_case 文本集合（与 <see cref="EffectKindNames"/> 同一命名惯例），固定
        /// 顺序（同 06 表格行序：school_damage、weapon_damage_pct、heal、projectile、apply_aura）、
        /// 只读不可变——包装为 <see cref="ReadOnlyCollection{T}"/>，转型为可写集合接口后调用变更
        /// 方法会抛 <see cref="NotSupportedException"/>，不提供任何能让调用方修改本集合内容的途径。
        /// </summary>
        public static readonly IReadOnlyList<string> All = BuildAll();

        private static IReadOnlyList<string> BuildAll()
        {
            var names = new string[AllKinds.Length];
            for (var i = 0; i < AllKinds.Length; i++)
            {
                names[i] = EffectKindNames.ToText(AllKinds[i]);
            }

            return new ReadOnlyCollection<string>(names);
        }

        /// <summary>
        /// 某个效果原语 <c>kind</c> 文本是否属于结算类集合（<see cref="All"/> 的成员判定，按原文
        /// 字符串比较，不做大小写归一化——数据表 <c>kind</c> 本就一律 snake_case）。未登记/未知的
        /// <c>kind</c> 文本（含 <c>null</c>）返回 <c>false</c>，不抛异常，同
        /// <see cref="EffectKindNames.TryParse"/> 惯例。<c>apply_aura</c> 返回 <c>true</c> 只代表
        /// "按效果原语类型这一层可能是结算类"，不代表已经核实过具体引用的光环内容，见类型顶部
        /// 判断记录。
        /// </summary>
        public static bool IsSettlement(string? kind)
        {
            if (kind == null)
            {
                return false;
            }

            for (var i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i], kind, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

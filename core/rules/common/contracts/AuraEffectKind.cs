using System;
using System.Collections.Generic;

namespace Core.Rules.Common
{
    /// <summary>
    /// AuraEffect 类型的固定枚举（见 06 第 3.3 节表格，前 10 项取值与顺序照抄该表）。同属拍板决策 10
    /// 的受控开口，新增走 ADR。<see cref="ModPowerRegen"/>/<see cref="PeriodicEnergize"/>（ADR-0108
    /// "静息回复"）追加在末尾——枚举新增值只追加、不插入既有项之间（11 第 3 节 ABI 只新增规则，避免
    /// 已编译的消费方按数值序号读到本枚举时错位）。
    /// </summary>
    public enum AuraEffectKind
    {
        ModStat,
        PeriodicDamage,
        PeriodicHeal,
        Absorb,
        Immunity,
        ProcTrigger,
        SpellMod,
        OverrideSkill,
        Control,
        Flag,

        /// <summary>ADR-0108 新增：持续修饰目标某个资源类型的运行期回复速率（<see
        /// cref="Core.Numbers.PowerSet.IPowerHost.AddRegenModifier"/>），见
        /// <c>AuraHost.ReapplyPowerRegenMods</c> 判断记录。</summary>
        ModPowerRegen,

        /// <summary>ADR-0108 新增：周期形态的资源恢复（<see cref="AuraEffectKind.PeriodicDamage"/>/
        /// <see cref="PeriodicHeal"/> 的资源恢复版本），每跳经 <c>EffectKind.Energize</c> 落地，见
        /// <c>AuraHost.FirePeriodic</c> 判断记录。</summary>
        PeriodicEnergize,
    }

    /// <summary><see cref="AuraEffectKind"/> 与数据表使用的 snake_case 文本（06 第 3.3 节表格第一列）互转，
    /// 惯例同 <see cref="EffectKindNames"/>。</summary>
    public static class AuraEffectKindNames
    {
        private static readonly IReadOnlyDictionary<AuraEffectKind, string> ToName = new Dictionary<AuraEffectKind, string>
        {
            [AuraEffectKind.ModStat] = "mod_stat",
            [AuraEffectKind.PeriodicDamage] = "periodic_damage",
            [AuraEffectKind.PeriodicHeal] = "periodic_heal",
            [AuraEffectKind.Absorb] = "absorb",
            [AuraEffectKind.Immunity] = "immunity",
            [AuraEffectKind.ProcTrigger] = "proc_trigger",
            [AuraEffectKind.SpellMod] = "spell_mod",
            [AuraEffectKind.OverrideSkill] = "override_skill",
            [AuraEffectKind.Control] = "control",
            [AuraEffectKind.Flag] = "flag",
            [AuraEffectKind.ModPowerRegen] = "mod_power_regen",
            [AuraEffectKind.PeriodicEnergize] = "periodic_energize",
        };

        private static readonly IReadOnlyDictionary<string, AuraEffectKind> FromName = BuildReverse(ToName);

        private static Dictionary<string, AuraEffectKind> BuildReverse(IReadOnlyDictionary<AuraEffectKind, string> map)
        {
            var reverse = new Dictionary<string, AuraEffectKind>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        public static string ToText(AuraEffectKind kind) => ToName[kind];

        public static AuraEffectKind Parse(string text)
        {
            if (TryParse(text, out var kind))
            {
                return kind;
            }

            throw new ArgumentException($"未知的 AuraEffectKind 文本：\"{text ?? "<null>"}\"", nameof(text));
        }

        public static bool TryParse(string? text, out AuraEffectKind kind)
        {
            if (text != null && FromName.TryGetValue(text, out kind))
            {
                return true;
            }

            kind = default;
            return false;
        }
    }
}

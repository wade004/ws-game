using System;
using System.Collections.Generic;

namespace Core.Rules.Common
{
    /// <summary>
    /// AuraEffect 类型的固定枚举（见 06 第 3.3 节表格，共 10 项，取值与顺序照抄该表）。同属拍板决策 10
    /// 的受控开口，新增走 ADR。
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

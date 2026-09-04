using System;
using System.Collections.Generic;

namespace Core.Rules.Common
{
    /// <summary>
    /// Effect 原语的固定枚举（见 06 第 3.2 节"Effect 原语注册表"，拍板决策 10"新增原语受控开口"：
    /// 新增一种效果类型需过 ADR，见 architecture/adr/0010-新增原语走审批.md）。取值与顺序照抄 06
    /// 第 3.2 节表格行序，共 19 项。
    /// </summary>
    public enum EffectKind
    {
        SchoolDamage,
        WeaponDamagePct,
        Heal,
        ApplyAura,
        Dispel,
        Energize,
        TriggerSpell,
        ModifyCooldown,
        AddCharge,
        Projectile,
        Move,
        Summon,
        Interrupt,
        Teleport,
        OpenLock,
        CreateItem,
        LearnSkill,
        SetWorldFlag,
        Script,
    }

    /// <summary>
    /// <see cref="EffectKind"/> 与数据表使用的 snake_case 文本（06 第 3.2 节表格第一列）互转。
    /// 数据表（<c>skill.def.effects[].kind</c> 一类字段）一律用 snake_case 文本，运行期代码一律用
    /// 强类型枚举，本类是两者之间唯一的转换点。
    /// </summary>
    public static class EffectKindNames
    {
        private static readonly IReadOnlyDictionary<EffectKind, string> ToName = new Dictionary<EffectKind, string>
        {
            [EffectKind.SchoolDamage] = "school_damage",
            [EffectKind.WeaponDamagePct] = "weapon_damage_pct",
            [EffectKind.Heal] = "heal",
            [EffectKind.ApplyAura] = "apply_aura",
            [EffectKind.Dispel] = "dispel",
            [EffectKind.Energize] = "energize",
            [EffectKind.TriggerSpell] = "trigger_spell",
            [EffectKind.ModifyCooldown] = "modify_cooldown",
            [EffectKind.AddCharge] = "add_charge",
            [EffectKind.Projectile] = "projectile",
            [EffectKind.Move] = "move",
            [EffectKind.Summon] = "summon",
            [EffectKind.Interrupt] = "interrupt",
            [EffectKind.Teleport] = "teleport",
            [EffectKind.OpenLock] = "open_lock",
            [EffectKind.CreateItem] = "create_item",
            [EffectKind.LearnSkill] = "learn_skill",
            [EffectKind.SetWorldFlag] = "set_world_flag",
            [EffectKind.Script] = "script",
        };

        private static readonly IReadOnlyDictionary<string, EffectKind> FromName = BuildReverse(ToName);

        private static Dictionary<string, EffectKind> BuildReverse(IReadOnlyDictionary<EffectKind, string> map)
        {
            var reverse = new Dictionary<string, EffectKind>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        /// <summary>枚举值 → snake_case 文本。全部 <see cref="EffectKind"/> 取值都在映射表中，不会失败。</summary>
        public static string ToText(EffectKind kind) => ToName[kind];

        /// <summary>snake_case 文本 → 枚举值；未登记的文本抛 <see cref="ArgumentException"/>。</summary>
        public static EffectKind Parse(string text)
        {
            if (TryParse(text, out var kind))
            {
                return kind;
            }

            throw new ArgumentException($"未知的 EffectKind 文本：\"{text ?? "<null>"}\"", nameof(text));
        }

        /// <summary>snake_case 文本 → 枚举值；未登记的文本返回 false，不抛异常。</summary>
        public static bool TryParse(string? text, out EffectKind kind)
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

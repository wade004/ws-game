using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Presentation.Ui
{
    /// <summary>技能提示框里的一行（标签 + 值）。</summary>
    public readonly struct SkillTooltipRow
    {
        public string Label { get; }

        public string Value { get; }

        public SkillTooltipRow(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>技能提示框里一项资源消耗（数值 + 资源显示名）。</summary>
    public readonly struct SkillTooltipCost
    {
        public Id PowerType { get; }

        public string PowerName { get; }

        public double Amount { get; }

        public SkillTooltipCost(Id powerType, string powerName, double amount)
        {
            PowerType = powerType;
            PowerName = powerName;
            Amount = amount;
        }
    }

    /// <summary>
    /// 技能提示框内容（ADR-0175）：名称、类型、读条/冷却/消耗/射程、效果数值、描述，全部由 <c>skill.def</c> 数据（与可选的当前属性）算出，
    /// 不含任何引擎类型；引擎侧的提示框控件（技能栏悬停详情）只负责把它画出来。
    /// <para>
    /// 判断记录：①数值字段（<see cref="CastTime"/>/<see cref="CooldownDuration"/>/<see cref="Costs"/>/<see cref="Range"/>/<see cref="EffectValues"/>）与文案行
    /// （<see cref="Rows"/>）两份并存——消费方既能整块画 <see cref="Rows"/>，也能按数值自己排版或做断言；②只列数据里真的有的项
    /// （冷却为 0、瞬发的读条行、没有消耗不补占位行；射程 0 表示作用于自身，显示"自身"）；
    /// ③效果行只覆盖带数值的伤害/治疗类原语（<c>school_damage</c>/<c>heal</c>），值 = 基础值 + Σ(系数 × 施法者当前属性)——属性读取由调用方通过
    /// <see cref="SkillTooltipContext.Stat"/> 提供，不提供则只算基础值（<c>base_curve_ref</c> 取代 <c>base_value</c> 的曲线型数值不在提示框里展开，只显示可算出的部分）；
    /// ④文案全部走本地化键，键缺失/取不到文案时退回固定中文词与格式（与物品提示框同一口径）；游戏提供了本表键（框架起步包有一份中英文行），英文界面就不会混出中文。
    /// </para>
    /// </summary>
    public sealed class SkillTooltipContent
    {
        public Id SkillId { get; }

        /// <summary><c>skill.def.name_key</c> 本地化；没有键或取不到文案退回技能 id 的短名。</summary>
        public string Name { get; }

        /// <summary>类型显示名：读条 / 瞬发 / 引导。</summary>
        public string TypeText { get; }

        public bool IsInstant { get; }

        /// <summary>读条时长（秒，已含调用方给的当前有效值）；瞬发为 0。</summary>
        public double CastTime { get; }

        /// <summary>冷却时长（秒，已含调用方给的当前有效值）；没有冷却为 0。</summary>
        public double CooldownDuration { get; }

        /// <summary>射程（<c>skill.def.range</c>）；0 = 作用于自身/不限。</summary>
        public double Range { get; }

        public IReadOnlyList<SkillTooltipCost> Costs { get; }

        /// <summary>带数值的效果算出的值：<c>(kind, 值)</c>，按 <c>effects</c> 顺序，kind 为 <c>school_damage</c>/<c>heal</c>。</summary>
        public IReadOnlyList<KeyValuePair<string, double>> EffectValues { get; }

        /// <summary><c>skill.def.desc_key</c> 本地化；没有键或取不到文案为空串。</summary>
        public string Description { get; }

        public IReadOnlyList<SkillTooltipRow> Rows { get; }

        public SkillTooltipContent(
            Id skillId, string name, string typeText, bool isInstant, double castTime, double cooldownDuration, double range,
            IReadOnlyList<SkillTooltipCost> costs, IReadOnlyList<KeyValuePair<string, double>> effectValues, string description,
            IReadOnlyList<SkillTooltipRow> rows)
        {
            SkillId = skillId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            TypeText = typeText ?? throw new ArgumentNullException(nameof(typeText));
            IsInstant = isInstant;
            CastTime = castTime;
            CooldownDuration = cooldownDuration;
            Range = range;
            Costs = costs ?? throw new ArgumentNullException(nameof(costs));
            EffectValues = effectValues ?? throw new ArgumentNullException(nameof(effectValues));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }
    }

    /// <summary>调用方给的"当前状态"：让提示框数字反映当前属性与实际有效时长，而不是只看静态数据。全部可选。</summary>
    public sealed class SkillTooltipContext
    {
        /// <summary>施法者某属性的当前最终值（效果缩放用）；缺省 = 不做属性缩放，只算基础值。</summary>
        public Func<Id, double>? Stat { get; set; }

        /// <summary>当前有效冷却时长（受冷却缩减等影响）；缺省取 <c>skill.def.cooldown_duration</c>。</summary>
        public double? CooldownDuration { get; set; }

        /// <summary>当前有效读条时长（受急速等影响）；缺省取 <c>skill.def.cast_time</c>。</summary>
        public double? CastTime { get; set; }
    }

    /// <summary>技能提示框内容的数据规则（纯数据，不依赖引擎；ADR-0175）。</summary>
    public static class SkillTooltipBuilder
    {
        // 行标签 / 类型 / 格式的本地化键。取得到文案就用，取不到退回后面的固定中文（不带本地化表的调用方也能出完整内容）。
        public static readonly Id KeyType = new Id("l10n.ui.skill_tooltip.type");
        public static readonly Id KeyTypeCast = new Id("l10n.ui.skill_tooltip.type_cast");
        public static readonly Id KeyTypeInstant = new Id("l10n.ui.skill_tooltip.type_instant");
        public static readonly Id KeyTypeChannel = new Id("l10n.ui.skill_tooltip.type_channel");
        public static readonly Id KeyCastTime = new Id("l10n.ui.skill_tooltip.cast_time");
        public static readonly Id KeyCooldown = new Id("l10n.ui.skill_tooltip.cooldown");
        public static readonly Id KeyCost = new Id("l10n.ui.skill_tooltip.cost");
        public static readonly Id KeyRange = new Id("l10n.ui.skill_tooltip.range");
        public static readonly Id KeyRangeSelf = new Id("l10n.ui.skill_tooltip.range_self");
        public static readonly Id KeyDamage = new Id("l10n.ui.skill_tooltip.damage");
        public static readonly Id KeyHeal = new Id("l10n.ui.skill_tooltip.heal");
        public static readonly Id KeyFmtSeconds = new Id("l10n.ui.skill_tooltip.fmt_seconds");
        public static readonly Id KeyFmtRange = new Id("l10n.ui.skill_tooltip.fmt_range");

        public const string LabelType = "类型";
        public const string TextTypeCast = "读条";
        public const string TextTypeInstant = "瞬发";
        public const string TextTypeChannel = "引导";
        public const string LabelCastTime = "施法时间";
        public const string LabelCooldown = "冷却";
        public const string LabelCost = "消耗";
        public const string LabelRange = "射程";
        public const string TextRangeSelf = "自身";
        public const string LabelDamage = "伤害";
        public const string LabelHeal = "治疗";
        public const string FmtSeconds = "{0} 秒";
        public const string FmtRange = "{0} 米";

        public const string EffectSchoolDamage = "school_damage";
        public const string EffectHeal = "heal";

        /// <summary>技能 <paramref name="skill"/> 的提示框内容；<c>skill.def</c> 里没有这个技能返回 null。</summary>
        public static SkillTooltipContent? Build(IDataRegistryView registry, Id skill, Func<Id, string>? text = null, SkillTooltipContext? context = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var row = registry.Get("skill.def", skill);
            if (row == null)
            {
                return null;
            }

            string Localize(Id key, string fallback)
            {
                if (text == null)
                {
                    return fallback;
                }

                var value = text(key);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }

            string Fmt(Id key, string fallbackFormat, double value) =>
                string.Format(CultureInfo.InvariantCulture, Localize(key, fallbackFormat), Format(value));

            var name = row.TryGetId("name_key", out var nameKey) ? Localize(nameKey, ShortName(skill)) : ShortName(skill);
            var description = row.TryGetId("desc_key", out var descKey) ? Localize(descKey, string.Empty) : string.Empty;

            var castTimeData = Number(row, "cast_time");
            var channelTime = Number(row, "channel_time");
            var isChannel = channelTime > 0d;
            var castTime = context?.CastTime ?? (isChannel ? channelTime : castTimeData);
            var isInstant = !isChannel && castTimeData <= 0d;
            var cooldown = context?.CooldownDuration ?? Number(row, "cooldown_duration");
            var range = Number(row, "range");

            var typeText = isChannel
                ? Localize(KeyTypeChannel, TextTypeChannel)
                : isInstant ? Localize(KeyTypeInstant, TextTypeInstant) : Localize(KeyTypeCast, TextTypeCast);

            var costs = new List<SkillTooltipCost>();
            if (row.TryGetArray("cost", out var costArray))
            {
                foreach (var entry in costArray)
                {
                    if (entry is JsonObject cost && cost.TryGetValue("power_type", out var powerValue) && powerValue is JsonString powerText
                        && Id.TryParse(powerText.Value, out var power) && cost.TryGetValue("amount", out var amountValue) && amountValue is JsonNumber amount)
                    {
                        var powerName = ShortName(power);
                        var definition = registry.Get("arch.power_type", power);
                        if (definition != null && definition.TryGetId("name_key", out var powerKey))
                        {
                            powerName = Localize(powerKey, powerName);
                        }

                        costs.Add(new SkillTooltipCost(power, powerName, amount.Value));
                    }
                }
            }

            var effectValues = new List<KeyValuePair<string, double>>();
            if (row.TryGetArray("effects", out var effects))
            {
                foreach (var effect in effects)
                {
                    if (effect is JsonObject e && e.TryGetValue("kind", out var kindValue) && kindValue is JsonString kind
                        && (kind.Value == EffectSchoolDamage || kind.Value == EffectHeal)
                        && e.TryGetValue("params", out var paramsValue) && paramsValue is JsonObject p)
                    {
                        effectValues.Add(new KeyValuePair<string, double>(kind.Value, EffectValue(p, context?.Stat)));
                    }
                }
            }

            var rows = new List<SkillTooltipRow>
            {
                new SkillTooltipRow(Localize(KeyType, LabelType), typeText),
            };

            if (!isInstant && castTime > 0d)
            {
                rows.Add(new SkillTooltipRow(Localize(KeyCastTime, LabelCastTime), Fmt(KeyFmtSeconds, FmtSeconds, castTime)));
            }

            if (cooldown > 0d)
            {
                rows.Add(new SkillTooltipRow(Localize(KeyCooldown, LabelCooldown), Fmt(KeyFmtSeconds, FmtSeconds, cooldown)));
            }

            if (costs.Count > 0)
            {
                var parts = new List<string>(costs.Count);
                foreach (var cost in costs)
                {
                    parts.Add(Format(cost.Amount) + " " + cost.PowerName);
                }

                rows.Add(new SkillTooltipRow(Localize(KeyCost, LabelCost), string.Join(", ", parts)));
            }

            rows.Add(new SkillTooltipRow(
                Localize(KeyRange, LabelRange),
                range > 0d ? Fmt(KeyFmtRange, FmtRange, range) : Localize(KeyRangeSelf, TextRangeSelf)));

            foreach (var pair in effectValues)
            {
                rows.Add(new SkillTooltipRow(
                    pair.Key == EffectSchoolDamage ? Localize(KeyDamage, LabelDamage) : Localize(KeyHeal, LabelHeal),
                    Format(pair.Value)));
            }

            return new SkillTooltipContent(skill, name, typeText, isInstant, castTime, cooldown, range, costs, effectValues, description, rows);
        }

        /// <summary>效果值 = 基础值 + Σ(系数 × 施法者当前属性)；没有属性读取函数只算基础值。旧写法（<c>scaling_stat</c> + <c>coefficient</c>）仅在没有 <c>scaling</c> 列表时读取。</summary>
        public static double EffectValue(JsonObject effectParams, Func<Id, double>? stat)
        {
            var value = Number(effectParams, "base_value");
            if (stat == null)
            {
                return value;
            }

            if (effectParams.TryGetValue("scaling", out var scalingValue) && scalingValue is JsonArray scaling && scaling.Count > 0)
            {
                foreach (var entry in scaling)
                {
                    if (entry is JsonObject s && s.TryGetValue("stat", out var statValue) && statValue is JsonString statText
                        && Id.TryParse(statText.Value, out var statId))
                    {
                        value += Number(s, "coefficient") * stat(statId);
                    }
                }

                return value;
            }

            if (effectParams.TryGetValue("scaling_stat", out var legacy) && legacy is JsonString legacyText && Id.TryParse(legacyText.Value, out var legacyId))
            {
                value += Number(effectParams, "coefficient") * stat(legacyId);
            }

            return value;
        }

        private static double Number(DataRecord row, string key) =>
            row.TryGetNumber(key, out var v) ? v : 0d;

        private static double Number(JsonObject obj, string key) =>
            obj.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : 0d;

        private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string ShortName(Id id)
        {
            var value = id.Value;
            var dot = value.LastIndexOf('.');
            return dot >= 0 ? value.Substring(dot + 1) : value;
        }
    }
}

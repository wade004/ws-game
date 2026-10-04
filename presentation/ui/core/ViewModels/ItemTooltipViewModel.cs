using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Presentation.Ui
{
    /// <summary>物品提示框里的一行属性（标签 + 值）。</summary>
    public readonly struct ItemTooltipRow
    {
        public string Label { get; }

        public string Value { get; }

        public ItemTooltipRow(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// 物品提示框内容（手感设计/08 第 3 节运行期、ADR-0152）：名称、品质、属性行，全部由 <c>item.template</c> 数据算出，
    /// 不含任何引擎类型；引擎侧的提示框控件（皮肤包 <c>tooltip/*</c>）只负责把它画出来。
    /// <para>
    /// 判断记录（属性行的取舍）：只列数据里真的有的项——物品等级、武器伤害/速度、固定属性（<c>stats</c>）、等级需求；没有的项不出现，
    /// 不补"0"占位行。文案：名称/品质/槽位/属性名走本地化键（<paramref name="text"/> 为空或键缺失时退回短名），行标签是固定的中文词。
    /// </para>
    /// </summary>
    public sealed class ItemTooltipContent
    {
        public Id TemplateId { get; }

        /// <summary>物品名（<c>item.template.name_key</c> 本地化；没有键或取不到文案退回模板 id 的短名）。</summary>
        public string Name { get; }

        /// <summary>品质短名（<c>item.template.quality</c> 去 <c>item.quality.</c> 前缀；皮肤包 <c>quality_frame</c> 按它命名）。</summary>
        public string QualityShortName { get; }

        /// <summary>品质显示名（品质定义 <c>name_key</c> 本地化；取不到退回短名）。</summary>
        public string QualityText { get; }

        /// <summary>槽位显示名（槽位定义 <c>name_key</c> 本地化；取不到退回槽位短名）。</summary>
        public string SlotText { get; }

        public IReadOnlyList<ItemTooltipRow> Rows { get; }

        public ItemTooltipContent(Id templateId, string name, string qualityShortName, string qualityText, string slotText, IReadOnlyList<ItemTooltipRow> rows)
        {
            TemplateId = templateId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            QualityShortName = qualityShortName ?? throw new ArgumentNullException(nameof(qualityShortName));
            QualityText = qualityText ?? throw new ArgumentNullException(nameof(qualityText));
            SlotText = slotText ?? throw new ArgumentNullException(nameof(slotText));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }
    }

    /// <summary>物品提示框内容与"拖拽到槽位是否合法"的数据规则（纯数据，不依赖引擎）。</summary>
    public static class ItemTooltipBuilder
    {
        public const string LabelItemLevel = "物品等级";
        public const string LabelSlot = "槽位";
        public const string LabelDamage = "伤害";
        public const string LabelSpeed = "攻速";
        public const string LabelRequiredLevel = "需要等级";

        /// <summary>物品模板 <paramref name="template"/> 的提示框内容；模板在数据里不存在返回 null。</summary>
        public static ItemTooltipContent? Build(IDataRegistryView registry, Id template, Func<Id, string>? text = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var row = registry.Get("item.template", template);
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

            var name = ItemName(registry, template, text);

            var qualityShort = string.Empty;
            var qualityText = string.Empty;
            if (row.TryGetId("quality", out var quality))
            {
                qualityShort = EquipmentViewModel.QualityShortName(quality);
                qualityText = qualityShort;
                var qualityRow = registry.Get("item.quality_definition", quality);
                if (qualityRow != null && qualityRow.TryGetId("name_key", out var qualityKey))
                {
                    qualityText = Localize(qualityKey, qualityShort);
                }
            }

            var slotText = row.TryGetId("slot", out var slot) ? SlotText(registry, slot, text) : string.Empty;

            var rows = new List<ItemTooltipRow>();
            if (slotText.Length > 0)
            {
                rows.Add(new ItemTooltipRow(LabelSlot, slotText));
            }

            if (row.TryGetInt("item_level", out var itemLevel))
            {
                rows.Add(new ItemTooltipRow(LabelItemLevel, itemLevel.ToString(CultureInfo.InvariantCulture)));
            }

            if (row.TryGetObject("weapon_profile", out var profile))
            {
                var min = Number(profile, "damage_min");
                var max = Number(profile, "damage_max");
                var speed = Number(profile, "speed");
                rows.Add(new ItemTooltipRow(LabelDamage, Format(min) + " - " + Format(max)));
                rows.Add(new ItemTooltipRow(LabelSpeed, Format(speed)));
            }

            if (row.TryGetArray("stats", out var stats))
            {
                foreach (var entry in stats)
                {
                    if (entry is JsonObject stat && stat.TryGetValue("stat", out var statId) && statId is JsonString statText && Id.TryParse(statText.Value, out var id))
                    {
                        var statName = ShortName(id);
                        var definition = registry.Get("stat.definition", id);
                        if (definition != null && definition.TryGetId("name_key", out var statKey))
                        {
                            statName = Localize(statKey, statName);
                        }

                        var op = stat.TryGetValue("op", out var opValue) && opValue is JsonString opText ? opText.Value : "flat";
                        var value = Number(stat, "value");
                        var shown = op == "pct" ? "+" + Format(value) + "%" : op == "mult" ? "x" + Format(value) : (value >= 0 ? "+" : string.Empty) + Format(value);
                        rows.Add(new ItemTooltipRow(statName, shown));
                    }
                }
            }

            if (row.TryGetObject("requirements", out var requirements) && requirements.TryGetValue("level", out var levelValue) && levelValue is JsonNumber level)
            {
                rows.Add(new ItemTooltipRow(LabelRequiredLevel, Format(level.Value)));
            }

            return new ItemTooltipContent(template, name, qualityShort, qualityText, slotText, rows);
        }

        /// <summary>物品模板声明的槽位是否就是 <paramref name="slotId"/>（拖拽到装备槽位前的合法性判断；穿戴最终结果以装备载体返回的 <c>EquipResult</c> 为准）。</summary>
        public static bool FitsSlot(IDataRegistryView registry, Id template, Id slotId)
        {
            var row = registry.Get("item.template", template);
            return row != null && row.TryGetId("slot", out var slot) && slot.Equals(slotId);
        }

        /// <summary>
        /// 物品显示名：<c>item.template.name_key</c> 经 <paramref name="text"/> 本地化；没有模板行、没有键、没有文本函数或取不到文案时退回模板 id 的短名（如 <c>std_bow</c>）。
        /// 背包行标签、提示框标题与拖拽相关的取名共用这一处，各处物品名因此一致。
        /// </summary>
        public static string ItemName(IDataRegistryView? registry, Id template, Func<Id, string>? text = null)
        {
            var row = registry?.Get("item.template", template);
            if (text != null && row != null && row.TryGetId("name_key", out var key))
            {
                var value = text(key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return ShortName(template);
        }

        /// <summary>
        /// 槽位显示名：槽位定义（<c>item.slot_definition</c>）的 <c>name_key</c> 经 <paramref name="text"/> 本地化；没有定义行、没有键、没有文本函数或取不到文案时退回槽位短名（如 <c>std_main_hand</c>）。
        /// 提示框的槽位行与装备面板的槽位标签共用这一处，两处的槽位名因此一致。
        /// </summary>
        public static string SlotText(IDataRegistryView registry, Id slot, Func<Id, string>? text = null)
        {
            var fallback = EquipmentViewModel.SlotShortName(slot);
            var slotRow = registry.Get("item.slot_definition", slot);
            if (text == null || slotRow == null || !slotRow.TryGetId("name_key", out var slotKey))
            {
                return fallback;
            }

            var value = text(slotKey);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

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

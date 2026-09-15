using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="ItemInstance"/> 与 JSON 的互转（见 10 第 2.2 节"ItemInstance 是一条独立结构（实例
    /// id、模板 id、堆叠数、品质、词缀引用等）"）。<see cref="InventoryPersistable"/>/
    /// <see cref="EquipmentPersistable"/> 共用同一份转换逻辑，避免两处各写一遍容易漂移的字段名。
    /// <para>
    /// T-N2-7（ADR-0032 决策 8；10 第 2.5 节修订段）新增 <c>quality</c>/<c>affixes</c> 两个 key——
    /// **判断记录（兼容方式：不升 <c>save_version</c>、Load 内向后兼容读取，不登记迁移函数）**：
    /// 本仓库对"既有存档段的条目形状新增可选字段"的既定先例是 1.7.0 的两条（见
    /// <c>CHANGELOG.md</c> [1.7.0] 迁移说明）——① AUD-03 <c>world.vendor_stock</c> 段 <c>timer</c>
    /// 物品条目新增可选字段，"<c>Load</c> 完全向后兼容纯数字旧格式，无需游戏侧改动"；②
    /// <c>player.achievement_state</c> 每条记录新增可选字段 <c>pending_reward</c>，"向后兼容，旧
    /// 存档缺省该字段按 <c>false</c> 处理"——两条都不升 <c>save_version</c>、不登记
    /// <c>ISaveMigration</c> 迁移函数，直接在字段读取处做缺省处理；<c>player.inventory</c>/
    /// <c>player.equipment</c> 段本身也没有独立的段级版本号（10 第 5 节"迁移链读取/改写的是存档
    /// 文档信封层（顶层）的 <c>save_version</c> 字段"，物品实例只是这两段内部数组/映射的条目，不是
    /// 单独注册的 <c>IPersistable</c> 段），与"整段缺失"（10 第 2 节"缺失段语义"，走
    /// <c>load(null)</c> 清空）是两回事——本次改动是"段仍在，条目形状新增两个可选 key"，比
    /// AUD-03/achievement_state 更贴近；因此比照同一先例：<see cref="FromJson"/> 直接在解析处对
    /// 缺失的 <c>quality</c>/<c>affixes</c> key 做缺省，不新增 <c>ISaveMigration</c>、不递增顶层
    /// <c>save_version</c>。
    /// </para>
    /// <para>
    /// **判断记录（<c>quality</c> 缺省值——上报待设计层确认，本任务采用的临时判断）**：ADR-0032
    /// 决策 8"物品实例存……品质"未进一步说明旧存档（无 <c>quality</c> key）读档时该品质取什么值；
    /// 07 第 1.6 节修订段与任务书原文给出的方向是"缺省取模板自身品质"（等价于"这件旧物品从掉落那一
    /// 刻起就是模板默认品质"这一最保守假设，不会凭空让旧物品变得比掉落时更强/更弱）。<see
    /// cref="FromJson"/> 因此接受一个 <paramref name="resolveTemplateQuality"/> 回调，由调用方
    /// （<c>InventoryPersistable</c>/<c>EquipmentPersistable</c>，经 <see
    /// cref="Core.Carriers.Item.InventoryHost.ResolveTemplateQuality"/>）按 <c>template_id</c> 查
    /// <c>item.template.quality</c> 字段解析——本方法自身是纯 JSON→结构体转换，不持有
    /// <c>IDataRegistryView</c>，无法自行查表。<c>affixes</c> 缺省为空列表，语义明确（旧物品没有
    /// 词缀身份数据可恢复，只能视为无词缀），不存在同等的待确认问题。
    /// </para>
    /// </summary>
    internal static class ItemInstanceJson
    {
        public static JsonValue ToJson(ItemInstance instance)
        {
            var affixesJson = new List<JsonValue>(instance.Affixes.Count);
            foreach (var affixId in instance.Affixes)
            {
                affixesJson.Add(new JsonString(affixId.Value));
            }

            return new JsonObjectBuilder()
                .Add("instance_id", new JsonString(instance.InstanceId.Value))
                .Add("template_id", new JsonString(instance.TemplateId.Value))
                .Add("count", new JsonNumber(instance.Count))
                .Add("quality", new JsonString(instance.Quality.Value))
                .Add("affixes", new JsonArray(affixesJson))
                .Add("extra", instance.Extra)
                .Build();
        }

        /// <summary>见类型顶部判断记录。<paramref name="resolveTemplateQuality"/> 在存档数据缺失
        /// <c>quality</c> key 时被调用一次，传入已解析出的 <c>template_id</c>，用于解析出兼容读取
        /// 的缺省品质（按模板自身 <c>quality</c> 字段）。</summary>
        public static ItemInstance FromJson(JsonValue raw, Func<Id, Id> resolveTemplateQuality)
        {
            if (!(raw is JsonObject obj))
            {
                throw new FormatException($"ItemInstance 的存档数据不是 JSON 对象（实际种类：{raw.Kind}）");
            }

            if (!obj.TryGetValue("instance_id", out var idRaw) || !(idRaw is JsonString idStr) ||
                !Id.TryParse(idStr.Value, out var instanceId))
            {
                throw new FormatException("ItemInstance 的存档数据缺少合法 instance_id");
            }

            if (!obj.TryGetValue("template_id", out var tplRaw) || !(tplRaw is JsonString tplStr) ||
                !Id.TryParse(tplStr.Value, out var templateId))
            {
                throw new FormatException("ItemInstance 的存档数据缺少合法 template_id");
            }

            if (!obj.TryGetValue("count", out var countRaw) || !(countRaw is JsonNumber countNum))
            {
                throw new FormatException("ItemInstance 的存档数据缺少合法 count");
            }

            var extra = obj.TryGetValue("extra", out var extraRaw) && extraRaw is JsonObject extraObj
                ? extraObj
                : null;

            // T-N2-7：quality key 缺省时按模板自身 quality 字段兼容读取（见类型顶部判断记录）；
            // key 存在但值非法（非字符串/非法 Id）与 instance_id/template_id 同一口径，抛
            // FormatException——不静默吞掉坏数据。
            Id quality;
            if (obj.TryGetValue("quality", out var qualityRaw))
            {
                if (!(qualityRaw is JsonString qualityStr) || !Id.TryParse(qualityStr.Value, out quality))
                {
                    throw new FormatException("ItemInstance 的存档数据 quality 字段不是合法 Id");
                }
            }
            else
            {
                quality = resolveTemplateQuality(templateId);
            }

            // affixes key 缺省时按空列表处理（旧物品没有词缀身份数据可恢复）；key 存在但不是字符串
            // 数组，或数组元素不是合法 Id，同上一致抛 FormatException。
            IReadOnlyList<Id> affixes = Array.Empty<Id>();
            if (obj.TryGetValue("affixes", out var affixesRaw))
            {
                if (!(affixesRaw is JsonArray affixesArr))
                {
                    throw new FormatException("ItemInstance 的存档数据 affixes 字段不是 JSON 数组");
                }

                var list = new List<Id>(affixesArr.Count);
                foreach (var item in affixesArr)
                {
                    if (!(item is JsonString affixStr) || !Id.TryParse(affixStr.Value, out var affixId))
                    {
                        throw new FormatException("ItemInstance 的存档数据 affixes 数组元素不是合法 Id");
                    }

                    list.Add(affixId);
                }

                affixes = list;
            }

            return new ItemInstance(instanceId, templateId, (int)countNum.Value, quality, affixes, extra);
        }
    }
}

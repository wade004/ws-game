using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="ItemInstance"/> 与 JSON 的互转（见 10 第 2.2 节"ItemInstance 是一条独立结构（实例
    /// id、模板 id、堆叠数、实例级词缀/耐久等可选字段）"）。<see cref="InventoryPersistable"/>/
    /// <see cref="EquipmentPersistable"/> 共用同一份转换逻辑，避免两处各写一遍容易漂移的字段名。
    /// </summary>
    internal static class ItemInstanceJson
    {
        public static JsonValue ToJson(ItemInstance instance) =>
            new JsonObjectBuilder()
                .Add("instance_id", new JsonString(instance.InstanceId.Value))
                .Add("template_id", new JsonString(instance.TemplateId.Value))
                .Add("count", new JsonNumber(instance.Count))
                .Add("extra", instance.Extra)
                .Build();

        public static ItemInstance FromJson(JsonValue raw)
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

            return new ItemInstance(instanceId, templateId, (int)countNum.Value, extra);
        }
    }
}

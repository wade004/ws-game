using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// 第七方审核 CR140-01 收口补齐：<c>world.gobj_pending_loot</c> 段，持久化 <see
    /// cref="GameObjectHost"/> 在 <see cref="GobjLootDeliveryPolicy.Partial"/> 策略下记账的
    /// 未交付宝箱/采集物余量（CR150-04 根治后 <c>gather_node</c> 复用同一份台账；见 <see
    /// cref="GameObjectHost.PendingLootSnapshot"/> 判断记录）。
    /// <para>
    /// 判断记录——段 key 与是否注册：本模块（L3）不依赖 <c>core/gameplay/loot</c>（L4）的存档
    /// 概念，仿照该模块 <c>DroppedLootPersistable</c> 的既有惯例，直接以字面量
    /// <c>"world.gobj_pending_loot"</c> 作为段 key，不写入 <c>core/foundation/save_system.
    /// SaveSections.KnownOrder</c>（那张固定全序表对应 10_存档与持久化.md 已拍板的分组顺序，本段
    /// 是本轮新引入、文档尚未收录的"世界附属段"，改表需要先出 ADR/改文档；未登记的自定义段按
    /// key 的 ordinal 顺序排在已知段之后，不影响正确性，只是相对顺序不保证与其它未登记段一致）。
    /// 是否把本类注册进 <c>ISaveSystem</c> 由装配层决定（同 <c>DroppedLootPersistable</c> 判断
    /// 记录），本类自身不假设一定被注册。
    /// </para>
    /// <para>
    /// 判断记录——字段名与旧存档兼容：<see cref="Save"/> 输出 <c>{ "pending_loot": [...] }</c>
    /// （而不是把数组直接当段体），预留同一段未来追加其它字段的空间；<see cref="Load"/> 对
    /// <c>data is JsonNull</c>（这段在存档里整体不存在，即 CHANGELOG 里"旧存档无该字段视为空"）
    /// 与"存在但 <c>pending_loot</c> 键缺失/类型不对"两种情况都当作空表处理，不抛异常——旧版本
    /// 存档（本字段引入之前产生的）读档时不会因为缺这一段而失败。
    /// </para>
    /// <para>
    /// CR150-02 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：条目键从
    /// <c>"gobjInstanceId"</c> 改名为 <c>"originKey"</c>——存的值不再是瞬态运行期实体 id（那正是
    /// CR150-02 复现的缺陷根源：实体重建后旧 id 永久失联），而是 <see
    /// cref="GameObjectEntity.OriginKey"/> 稳定身份，字段名跟着改名以准确反映这一变化，避免"字段名
    /// 仍叫 gobjInstanceId 但存的其实是别的东西"这类误导。<see cref="Load"/> 只在存档里整段/这一
    /// 具体字段缺失时退化为空表（不抛异常），不做旧字段名兼容读取——本段本就未收录进 10 号文档的
    /// 固定分组顺序、只在本轮（1.5.0 之后）新引入，未随任何已发布版本对外承诺过字段级兼容。
    /// </para>
    /// </summary>
    public sealed class GobjPendingLootPersistable : IPersistable
    {
        private readonly GameObjectHost _host;

        public GobjPendingLootPersistable(GameObjectHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public string SectionKey => "world.gobj_pending_loot";

        public JsonValue Save()
        {
            var array = new List<JsonValue>();
            foreach (var kv in _host.PendingLootSnapshot())
            {
                if (kv.Value == null || kv.Value.Count == 0)
                {
                    continue;
                }

                var items = new List<JsonValue>(kv.Value.Count);
                foreach (var stack in kv.Value)
                {
                    items.Add(new JsonObjectBuilder()
                        .Add("templateId", new JsonString(stack.TemplateId.Value))
                        .Add("count", new JsonNumber(stack.Count))
                        .Build());
                }

                array.Add(new JsonObjectBuilder()
                    .Add("originKey", new JsonString(kv.Key.Value))
                    .Add("items", new JsonArray(items))
                    .Build());
            }

            return new JsonObjectBuilder().Add("pending_loot", new JsonArray(array)).Build();
        }

        public void Load(JsonValue data)
        {
            var result = new Dictionary<Id, IReadOnlyList<ItemStack>>();

            if (data is JsonObject obj
                && obj.TryGetValue("pending_loot", out var raw)
                && raw is JsonArray array)
            {
                foreach (var entryRaw in array)
                {
                    if (!(entryRaw is JsonObject entryObj))
                    {
                        throw new FormatException($"{SectionKey} 段的元素不是 JSON 对象");
                    }

                    var originKey = new Id(((JsonString)entryObj["originKey"]).Value);
                    var items = new List<ItemStack>();
                    foreach (var itemRaw in (JsonArray)entryObj["items"])
                    {
                        var itemObj = (JsonObject)itemRaw;
                        var templateId = new Id(((JsonString)itemObj["templateId"]).Value);
                        var count = (int)((JsonNumber)itemObj["count"]).Value;
                        items.Add(new ItemStack(templateId, count));
                    }

                    if (items.Count > 0)
                    {
                        result[originKey] = items;
                    }
                }
            }

            // data is JsonNull（旧存档没有这段）、或段存在但字段缺失/类型不对：result 保持空表，
            // 视为"无待补发余量"，不抛异常（判断记录见类型顶部）。
            _host.RestorePendingLoot(result);
        }
    }
}

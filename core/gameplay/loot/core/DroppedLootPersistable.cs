using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <c>world.dropped_loot</c> 段（05 第 1.6 节"（建议）随当前地图状态一并存档"，见 <see
    /// cref="LootOptions.PersistDropped"/>）。任务书标注"补录"——该段 key 未出现在
    /// <c>core/foundation/save_system.SaveSections</c> 的已知段清单里（那是其它任务的目录，本任务
    /// 不允许改动，见 README"契约缺口"），但 <c>IPersistable.SectionKey</c> 本就"可以是任意非空字符串"
    /// （见该接口注释），本类直接以字面量 <c>"world.dropped_loot"</c> 作为段 key 即满足"补录"意图，
    /// 不需要触碰 <c>SaveSections</c>。
    /// <para>
    /// 判断记录：本类不由 <see cref="LootHost"/> 自动向任何 <c>ISaveSystem</c> 注册——是否持久化地面
    /// 掉落物取决于 <see cref="LootOptions.PersistDropped"/>（策略配置项），真正调用
    /// <c>ISaveSystem.RegisterPersistable</c> 是组装层的事（惯例同
    /// <c>core/gameplay/world_state</c> README"不负责什么"一节）。
    /// </para>
    /// </summary>
    public sealed class DroppedLootPersistable : IPersistable
    {
        private readonly LootHost _lootHost;

        public DroppedLootPersistable(LootHost lootHost)
        {
            _lootHost = lootHost ?? throw new ArgumentNullException(nameof(lootHost));
        }

        public string SectionKey => "world.dropped_loot";

        public JsonValue Save()
        {
            var array = new List<JsonValue>();
            foreach (var id in _lootHost.ActiveLootIds)
            {
                // ActiveLootIds 只反映本模块自己的跟踪表；实体本体经 IWorldSim 持有，这里用
                // LootHost 暴露的 TryGetDropped 取回强类型引用，避免重复维护第二份状态。
                if (_lootHost.TryGetDropped(id, out var entity))
                {
                    array.Add(ToJson(entity));
                }
            }

            return new JsonArray(array);
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonArray array))
            {
                throw new FormatException($"{SectionKey} 段的数据不是 JSON 数组（实际种类：{data.Kind}）");
            }

            // 外部审核阻塞项 1 收口（见 LootHost.ClearDroppedExcept 判断记录）：先把整份存档快照
            // 反序列化出来、收集本次要恢复的全部 entityId，再一次性清掉"当前跟踪、但不在这份快照
            // 里"的陈旧记录——顺序很关键：必须先知道"这次要保留哪些 id"才能决定"清理哪些 id"，不能
            // 边解析边清理（否则先解析出的实体尚未来得及登记进 keepIds，就可能被后解析的另一条记录
            // 的清理逻辑误删——虽然本模块内不会发生这种情况，但两阶段分离本身更不容易出错，也更
            // 贴近"读档 = 归零重建"的语义：先确定目标状态全貌，再一次性收敛到它）。
            var entities = new List<DroppedLootEntity>(array.Count);
            var restoredIds = new HashSet<Id>();
            foreach (var raw in array)
            {
                var entity = FromJson(raw);
                entities.Add(entity);
                restoredIds.Add(entity.EntityId);
            }

            _lootHost.ClearDroppedExcept(restoredIds);

            var maxSequence = 0;
            foreach (var entity in entities)
            {
                _lootHost.RestoreDropped(entity);
                var seq = LootHost.ExtractSequence(entity.EntityId);
                if (seq > maxSequence)
                {
                    maxSequence = seq;
                }
            }

            _lootHost.ReserveLootIdSequenceAtLeast(maxSequence);
        }

        private static JsonValue ToJson(DroppedLootEntity entity)
        {
            var items = new List<JsonValue>(entity.Items.Count);
            foreach (var stack in entity.Items)
            {
                items.Add(new JsonObjectBuilder()
                    .Add("templateId", new JsonString(stack.TemplateId.Value))
                    .Add("count", new JsonNumber(stack.Count))
                    .Build());
            }

            var builder = new JsonObjectBuilder()
                .Add("entityId", new JsonString(entity.EntityId.Value))
                .Add("mapId", new JsonString(entity.MapId.Value))
                .Add("position", new JsonObjectBuilder()
                    .Add("x", new JsonNumber(entity.Position.X))
                    .Add("y", new JsonNumber(entity.Position.Y))
                    .Build())
                .Add("items", new JsonArray(items));

            if (entity.OwnerHint.HasValue)
            {
                builder.Add("ownerHint", new JsonString(entity.OwnerHint.Value.Value));
            }

            if (entity.ExpireAt.HasValue)
            {
                builder.Add("expireAt", new JsonNumber(entity.ExpireAt.Value));
            }

            return builder.Build();
        }

        private static DroppedLootEntity FromJson(JsonValue raw)
        {
            if (!(raw is JsonObject obj))
            {
                throw new FormatException("world.dropped_loot 段的元素不是 JSON 对象");
            }

            var entityId = new Id(((JsonString)obj["entityId"]).Value);
            var mapId = new Id(((JsonString)obj["mapId"]).Value);

            var posObj = (JsonObject)obj["position"];
            var position = new Vec2(((JsonNumber)posObj["x"]).Value, ((JsonNumber)posObj["y"]).Value);

            var items = new List<ItemStack>();
            foreach (var itemRaw in (JsonArray)obj["items"])
            {
                var itemObj = (JsonObject)itemRaw;
                var templateId = new Id(((JsonString)itemObj["templateId"]).Value);
                var count = (int)((JsonNumber)itemObj["count"]).Value;
                items.Add(new ItemStack(templateId, count));
            }

            Id? ownerHint = obj.TryGetValue("ownerHint", out var ownerRaw) && ownerRaw is JsonString ownerStr
                ? new Id(ownerStr.Value)
                : (Id?)null;

            double? expireAt = obj.TryGetValue("expireAt", out var expireRaw) && expireRaw is JsonNumber expireNum
                ? expireNum.Value
                : (double?)null;

            return new DroppedLootEntity(entityId, mapId, items, ownerHint, expireAt)
            {
                Position = position,
                // H4 补齐（见 DroppedLootEntity.GenericDisplayTemplateId 判断记录）：读档还原的掉落物
                // 同样要经这条路径重新落地 TemplateId——LootHost.Drop 只在"首次掉落"那一刻设置一次，
                // 存档只序列化 items/ownerHint/expireAt/position（见本类型 Save 方法），不含
                // TemplateId 本身，读档反序列化时若不重新赋值，读档产生的掉落物 View 又会退化回
                // "没有匹配 DisplayInfo"的空视图（本缺口正是 VerticalSliceTests.
                // FullVerticalSlice_..._Save_Load_... 用例"存档 -> 读档"环节实测复现的来源）。
                TemplateId = DroppedLootEntity.GenericDisplayTemplateId,
            };
        }
    }
}

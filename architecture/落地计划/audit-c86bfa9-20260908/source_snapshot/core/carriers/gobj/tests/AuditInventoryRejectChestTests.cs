using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>隔离审计探针：真实 InventoryHost 的 Reject 满包策略接入真实
    /// GameObjectHost 的 chest 路径，记录“先标记已开，再尝试入包”的可观察结果。</summary>
    public sealed class AuditInventoryRejectChestTests
    {
        private static JsonObject Template(string id, string kind, JsonObject typeData) =>
            J.O(
                ("id", J.S(id)),
                ("name_key", J.S("l10n." + id.Replace('.', '_') + ".name")),
                ("kind", J.S(kind)),
                ("type_data", typeData),
                ("display_ref", J.S("display.audit_gobj")));

        private static JsonObject Object(params (string Key, string Value)[] fields)
        {
            var pairs = new List<(string Key, JsonValue)>();
            foreach (var field in fields)
            {
                pairs.Add((field.Key, J.S(field.Value)));
            }

            return J.O(pairs.ToArray());
        }

        [Fact]
        public void Audit_ChestRejectsLoot_AfterMarkingOpen_AndDoesNotRetry()
        {
            var lootTableRef = new Id("loot.audit_full_chest");
            var fillerId = new Id("item.sample_filler");
            var rewardId = new Id("item.sample_gold");
            var world = new GobjWorldBuilder
            {
                RealInventoryOptions = new InventoryOptions
                {
                    MaxSlots = 1,
                    FullPolicy = InventoryFullPolicy.Reject,
                },
            }
            .Template(Template(
                "gobj.audit_full_chest", "chest",
                Object(("loot_table_ref", lootTableRef.Value))))
            .Build();

            world.Loot.Table(lootTableRef, new ItemStack(rewardId, 1));
            world.AddUnit(new Id("unit.sample_1"), new Vec2(0, 0));
            Assert.True(world.InventoryApi.AddItem(new Id("unit.sample_1"), fillerId, 1));
            var fillerInstanceId = world.InventoryApi.ListItems(new Id("unit.sample_1"))[0].InstanceId;

            var gobjId = world.SpawnFromTemplate(
                new Id("gobj.audit_full_chest"), new Id("map.sample"), new Vec2(0, 0));

            var result = world.Host.Interact(new Id("unit.sample_1"), gobjId);

            Assert.True(result.Success);
            Assert.Equal(0, world.InventoryApi.ListItems(new Id("unit.sample_1"))
                .Count(item => item.TemplateId.Equals(rewardId)));
            Assert.True(world.Host.GetState(gobjId, "open_state")!.Value.AsBool);
            Assert.Single(world.Loot.Calls);

            Assert.True(world.InventoryApi.RemoveItem(new Id("unit.sample_1"), fillerInstanceId, 1));
            world.Host.Interact(new Id("unit.sample_1"), gobjId);
            Assert.Equal(0, world.InventoryApi.ListItems(new Id("unit.sample_1"))
                .Count(item => item.TemplateId.Equals(rewardId)));
            Assert.Single(world.Loot.Calls);
        }
    }
}

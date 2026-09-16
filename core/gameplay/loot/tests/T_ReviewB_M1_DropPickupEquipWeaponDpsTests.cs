using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Gameplay.Loot;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// 2026-09-16 深度复审 B-M1 端到端回归：<see cref="EquipmentHost.GetWeaponDps"/> 此前忽略已装备
    /// 实例真实品质、只认模板自身登记的默认品质（见 <c>core/carriers/item/core/EquipmentHost.cs</c>
    /// 判断记录）。本文件覆盖完整的"掉落三次掷骰 → 拾取（带品质身份入包）→ 装备 → 查询武器秒伤"链路
    /// ——不是单元测试直接调用 <see cref="InventoryHost.AddItem(Id, Id, int, Id?,
    /// System.Collections.Generic.IReadOnlyList{Id})"/>，而是走真实 <see cref="LootHost.RollDetailed"/>
    /// 品质骰（<c>quality_weights</c> 把模板默认品质权重设为 0，强制骰出更高品质，同 <see
    /// cref="Core.Carriers.Item.EquipmentHostTests"/> 之外的独立最小夹具惯例），证明这条链路的每一步
    /// （掉落识别品质 → 落地/拾取保真 → 背包实例携带 → 装备转发 → GetWeaponDps 读取）整体贯通，不只是
    /// <see cref="Core.Carriers.Item.EquipmentHost"/> 内部单测覆盖的那一小段。
    /// </summary>
    public sealed class T_ReviewB_M1_DropPickupEquipWeaponDpsTests
    {
        private const string SlotRows =
            "[{\"id\": \"item.slot.rb1_weapon\", \"name_key\": \"l10n.slot.rb1_weapon\"," +
            " \"is_weapon\": true, \"budget_coefficient\": 2.0}]";

        // 模板默认品质 rb1_common（budget_multiplier=1.0）；掉落骰会强制命中 rb1_epic（3.0）。
        private const string QualityRows =
            "[{\"id\": \"item.quality.rb1_common\", \"name_key\": \"l10n.quality.rb1_common\"," +
            " \"budget_multiplier\": 1.0}," +
            "{\"id\": \"item.quality.rb1_epic\", \"name_key\": \"l10n.quality.rb1_epic\"," +
            " \"budget_multiplier\": 3.0}]";

        private const string StatRows =
            "[{\"id\": \"stat.rb1_str\", \"name_key\": \"l10n.stat.rb1_str\", \"category\": \"primary\"}]";

        // item_level=1 → 10（单点曲线）。
        private const string WeaponDpsCurveRows =
            "[{\"id\": \"item.weapon_dps.rb1_default\", \"entries\": [{\"x\": 1, \"y\": 10}]}]";

        private const string TemplateRows =
            "[{\"id\": \"item.rb1_sword\", \"slot\": \"item.slot.rb1_weapon\"," +
            " \"quality\": \"item.quality.rb1_common\", \"item_level\": 1," +
            " \"display_ref\": \"display.item.rb1_sword\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.rb1_sword\"," +
            " \"weapon_profile\": {\"damage_min\": 5, \"damage_max\": 25, \"speed\": 2.0," +
            " \"weapon_school\": \"skill.school.physical\"}}]";

        // quality_weights：模板默认品质权重 0（不可能骰中），rb1_epic 权重 1（必中）——不依赖 RNG
        // 种子即可确定性地复现"掉落品质骰命中了与模板默认不同的品质"这一常规场景。
        private const string LootTableRows =
            "[{\"id\": \"loot.rb1_sword_drop\", \"groups\": [" +
            "{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"item.rb1_sword\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}," +
            " \"quality_weights\": {\"item.quality.rb1_common\": 0, \"item.quality.rb1_epic\": 1}}" +
            "]}]}]";

        /// <summary>无需真正结算效果/光环的最小 <see cref="IEffectSink"/> 占位——本用例的武器模板不带
        /// 任何装备联动特效授予，构造 <see cref="EquipmentHost"/> 仅因签名要求而传入，从不会被调用。</summary>
        private sealed class NoopEffectSink : IEffectSink
        {
            public ResolveResult ApplyEffect(EffectContext context) =>
                new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);

            public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
                new AuraInstanceRef(new Id("aura.rb1_noop"));

            public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef)
            {
            }
        }

        [Fact]
        public void DropPickupEquip_QualityRollDeviatesFromTemplateDefault_GetWeaponDpsUsesRolledQuality()
        {
            var bus = LootTestSupport.NewEventBus();

            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, LootTestSupport.Envelope(LootSchemas.Table.Name, LootTableRows))
                .Add("item.template", LootTestSupport.Envelope("item.template", TemplateRows))
                .Add("item.slot_definition", LootTestSupport.Envelope("item.slot_definition", SlotRows))
                .Add("item.quality_definition", LootTestSupport.Envelope("item.quality_definition", QualityRows))
                .Add("item.affix", LootTestSupport.Envelope("item.affix", "[]"))
                .Add("stat.definition", LootTestSupport.Envelope("stat.definition", StatRows))
                .Add(ItemSchemas.WeaponDpsCurve.Name, LootTestSupport.Envelope(ItemSchemas.WeaponDpsCurve.Name, WeaponDpsCurveRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.Affix);
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(ItemSchemas.WeaponDpsCurve);
            registry.RegisterValidationRule(new LootContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = LootTestSupport.NewWorld(bus);
            var units = new WorldUnitAccess(world);
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);

            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(), () => 0.0);

            var equipmentOptions = new ItemOptions { WeaponDpsCurveId = new Id("item.weapon_dps.rb1_default") };
            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, new NoopEffectSink(),
                (unitId, skillId, sourceId, learn) => { }, units, equipmentOptions);

            var playerId = new Id("player.rb1_hero");
            LootTestSupport.AddPlayer(world, playerId, new Id("map.rb1"), new Vec2(0, 0));
            statHost.RegisterUnit(playerId);

            // 掉落：品质骰确定性命中 rb1_epic（非模板默认 rb1_common）。
            var rolled = loot.RollDetailed(new Id("loot.rb1_sword_drop"), new RollContext(playerId));
            var outcome = Assert.Single(rolled);
            Assert.Equal(new Id("item.quality.rb1_epic"), outcome.QualityId);

            // 拾取：品质身份保真进背包实例。
            var lootId = loot.Drop(new Id("map.rb1"), new Vec2(0, 0), rolled);
            var pickupResult = loot.PickUp(playerId, lootId);
            Assert.True(pickupResult.Success);

            var items = inventory.ListItems(playerId);
            var instance = Assert.Single(items);
            Assert.Equal(new Id("item.quality.rb1_epic"), instance.Quality);

            // 装备：三参 Equip 转发实例真实品质。
            var equipResult = equipment.Equip(playerId, instance.InstanceId, new Id("item.slot.rb1_weapon"));
            Assert.True(equipResult.Success);

            var dps = equipment.GetWeaponDps(playerId);

            // 手算：曲线(1)=10 × 品质预算倍率 3.0（rb1_epic，掉落骰命中，非模板默认 rb1_common 的
            // 1.0）× 武器槽位系数 2.0 = 60。B-M1 修复前会错误地按模板默认品质算出 20。
            Assert.Equal(60.0, dps, 9);
        }
    }
}

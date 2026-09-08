using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// CR140-02（外部审计 audit-c86bfa9-20260908，P2）复现与根治：跨图 <see cref="IWorldSim.ClearAll"/>
    /// 触发 <c>entity.destroyed</c>，<c>AuraHost</c> 响应该事件移除玩家名下全部运行期 Aura（含装备
    /// <c>grants.auras</c>/套装门槛加成），但 <c>Core.Carriers.Item.EquipmentHost._equipped</c> 台账
    /// 按 <c>unitId</c> 记账、不受 <c>ClearAll</c> 影响——常驻壳把玩家实体重新登记回 <see
    /// cref="IWorldSim"/> 后，<c>GameplayAssembly.EnterMap</c>（post-load 统一钩子）修复前只做掉落/
    /// 区域触发/刷新/经济补货，没有重放装备/套装授予的 Aura 这一步：装备本身"还穿着"，光环却已经
    /// 悄悄消失（外部审计探针 <c>equipment_aura_mapclear.log</c> 复现：<c>afterAura=False</c>，
    /// <c>afterEquipped=True</c>）。
    /// <para>
    /// 本文件用真实 <c>GameplayAssembly.Carriers</c>（真实 <c>CreatureFactory</c>/<c>AuraHost</c>/
    /// <c>EquipmentHost</c>/<c>InventoryHost</c> 全链路组合，同 <c>EquipmentSetBonusSharedAuraTests</c>
    /// 数据组装写法）+ 真实 <see cref="WorldSim.ClearAll"/> + 手工重放"常驻壳把玩家实体加回"这一步
    /// + <see cref="GameplayAssembly.EnterMap"/> 验证：跨图后光环/其派生属性正确恢复，且
    /// <see cref="GameplayAssembly.EnterMap"/> 被重复调用（未经历新一轮 <c>ClearAll</c>）不会让光环
    /// 重复叠加。
    /// </para>
    /// </summary>
    public sealed class CR140_02_EquipmentAuraMapClearTests
    {
        private static readonly Id MapId = new Id("world.cr140_02_map");
        private static readonly Id PlayerId = new Id("unit.cr140_02_player");
        private static readonly Id PlayerFactionId = new Id("fac.cr140_02_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.cr140_02_sample");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id AuraDefId = new Id("skill.aura_def.cr140_02_shared");

        private static readonly Id SlotOrdinary = new Id("item.slot.cr140_02_ordinary");
        private static readonly Id SlotOrdinaryTwo = new Id("item.slot.cr140_02_ordinary_two");
        private static readonly Id SlotSetOne = new Id("item.slot.cr140_02_set_one");
        private static readonly Id SlotSetTwo = new Id("item.slot.cr140_02_set_two");

        private static readonly Id TemplateOrdinary = new Id("item.sample_cr140_02_ordinary");
        private static readonly Id TemplateOrdinaryTwo = new Id("item.sample_cr140_02_ordinary_two");
        private static readonly Id TemplateSetOne = new Id("item.sample_cr140_02_set_one");
        private static readonly Id TemplateSetTwo = new Id("item.sample_cr140_02_set_two");
        private static readonly Id SetId = new Id("item.set.cr140_02_sample");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.cr140_02_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.cr140_02_sample" + "\", \"name_key\": \"l10n.arch.class.cr140_02_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.cr140_02_sample\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var auraDefRows = "[{\"id\": \"" + AuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 100}}]}]";

            var itemSlotDefinitionRows = "[" +
                "{\"id\": \"" + SlotOrdinary.Value + "\", \"name_key\": \"l10n.item.slot.cr140_02_ordinary\"}," +
                "{\"id\": \"" + SlotOrdinaryTwo.Value + "\", \"name_key\": \"l10n.item.slot.cr140_02_ordinary_two\"}," +
                "{\"id\": \"" + SlotSetOne.Value + "\", \"name_key\": \"l10n.item.slot.cr140_02_set_one\"}," +
                "{\"id\": \"" + SlotSetTwo.Value + "\", \"name_key\": \"l10n.item.slot.cr140_02_set_two\"}" +
                "]";

            var itemQualityDefinitionRows =
                "[{\"id\": \"item.quality.cr140_02_common\", \"name_key\": \"l10n.item.quality.cr140_02_common\"}]";

            var itemSetRows = "[{\"id\": \"" + SetId.Value + "\", \"name_key\": \"l10n.item.set.cr140_02_sample\", " +
                "\"pieces\": [\"" + TemplateSetOne.Value + "\", \"" + TemplateSetTwo.Value + "\"], " +
                "\"bonuses\": [{\"count\": 2, \"aura_ref\": \"" + AuraDefId.Value + "\"}]}]";

            var itemTemplateRows = "[" +
                "{\"id\": \"" + TemplateOrdinary.Value + "\", \"slot\": \"" + SlotOrdinary.Value + "\", " +
                "\"quality\": \"item.quality.cr140_02_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr140_02_ordinary\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr140_02_ordinary\", " +
                "\"grants\": {\"auras\": [\"" + AuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateOrdinaryTwo.Value + "\", \"slot\": \"" + SlotOrdinaryTwo.Value + "\", " +
                "\"quality\": \"item.quality.cr140_02_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr140_02_ordinary_two\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr140_02_ordinary_two\", " +
                "\"grants\": {\"auras\": [\"" + AuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateSetOne.Value + "\", \"slot\": \"" + SlotSetOne.Value + "\", " +
                "\"quality\": \"item.quality.cr140_02_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr140_02_set_one\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr140_02_set_one\", " +
                "\"set_id\": \"" + SetId.Value + "\"}," +
                "{\"id\": \"" + TemplateSetTwo.Value + "\", \"slot\": \"" + SlotSetTwo.Value + "\", " +
                "\"quality\": \"item.quality.cr140_02_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr140_02_set_two\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr140_02_set_two\", " +
                "\"set_id\": \"" + SetId.Value + "\"}" +
                "]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", auraDefRows))
                .Add("item.slot_definition", Envelope("item.slot_definition", itemSlotDefinitionRows))
                .Add("item.quality_definition", Envelope("item.quality_definition", itemQualityDefinitionRows))
                .Add("item.set", Envelope("item.set", itemSetRows))
                .Add("item.template", Envelope("item.template", itemTemplateRows))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));
        }

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.cr140_02_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player };
        }

        private static Id EquipFreshInstance(Fixture fx, Id templateId, Id slot)
        {
            fx.Gameplay.Carriers.Inventory.AddItem(PlayerId, templateId, 1);
            var items = fx.Gameplay.Carriers.Inventory.ListItems(PlayerId);
            var instanceId = items[items.Count - 1].InstanceId;
            var result = fx.Gameplay.Carriers.Equipment.Equip(PlayerId, instanceId, slot);
            Assert.True(result.Success, $"装备 \"{templateId}\" 到槽位 \"{slot}\" 应当成功：{result.Reason}");
            return instanceId;
        }

        /// <summary>核心复现与根治：装备一件带 <c>grants.auras</c> 的物品，跨图（<see
        /// cref="IWorldSim.ClearAll"/> + 玩家实体重新登记 + <see cref="GameplayAssembly.EnterMap"/>）
        /// 后光环与其派生的属性加成应当恢复，装备联动本身（<c>_equipped</c>）修复前后都不受影响
        /// （只是修复前光环没有跟着重建）。</summary>
        [Fact]
        public void EquipOrdinaryAura_CrossMapClear_ReenterMap_ReappliesAura_AndDerivedStat()
        {
            var fx = Build();
            EquipFreshInstance(fx, TemplateOrdinary, SlotOrdinary);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
            Assert.NotNull(fx.Gameplay.Carriers.Equipment.GetEquipped(PlayerId, SlotOrdinary));

            // 跨图：ClearAll 销毁全部实体（含玩家），触发 entity.destroyed，AuraHost 清空该玩家名下
            // 全部 Aura；随后模拟常驻壳"把玩家实体加回"这一步，再调用 EnterMap（post-load 统一钩子）。
            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "ClearAll 应当真的清空了光环（复现前提，不是本方法自己制造的假象）。");

            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();

            // 修复前：EnterMap 只做掉落/区域触发/刷新/经济补货，光环不会跟着重建，装备本身仍然
            // "穿着"（_equipped 从未受 ClearAll 影响）。
            Assert.NotNull(fx.Gameplay.Carriers.Equipment.GetEquipped(PlayerId, SlotOrdinary));

            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "CR140-02 核心断言：跨图后光环应当被 EnterMap 重新施加。");
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            // 幂等：EnterMap 被再次调用（未经历新一轮 ClearAll）不应让光环叠加。
            fx.Gameplay.EnterMap(MapId, PlayerId);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }

        /// <summary>审计 probe：两件装备共享同一 aura，默认 AllowMultiSourceTiming=false；跨图重放后
        /// 逐件卸下时，第一件不应移除仍被第二件持有的共享 aura，只有最后一件卸下才应移除。</summary>
        [Fact]
        public void TwoEquipmentSharedAura_CrossMapReapply_FirstUnequipKeepsAura_LastUnequipRemovesIt()
        {
            var fx = Build();
            EquipFreshInstance(fx, TemplateOrdinary, SlotOrdinary);
            EquipFreshInstance(fx, TemplateOrdinaryTwo, SlotOrdinaryTwo);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));

            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();
            fx.Gameplay.EnterMap(MapId, PlayerId);

            var afterReapply = fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId);
            Assert.True(afterReapply, $"expected aura after ReapplyGrants=true, actual={afterReapply}");

            var firstUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotOrdinary);
            Assert.NotNull(firstUnequipped);
            var afterFirstUnequip = fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId);
            Assert.True(afterFirstUnequip,
                $"expected aura after first unequip=true because second equipment still holds it, actual={afterFirstUnequip}");

            var lastUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotOrdinaryTwo);
            Assert.NotNull(lastUnequipped);
            var afterLastUnequip = fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId);
            Assert.False(afterLastUnequip,
                $"expected aura after last unequip=false, actual={afterLastUnequip}");
        }

        /// <summary>套装门槛加成同样应当在跨图后恢复：凑齐 2 件套装件触发的共享光环，ClearAll 后
        /// 同样应当被 EnterMap 重新施加，不需要玩家重新装/卸一次套装件。</summary>
        [Fact]
        public void SetBonus_CrossMapClear_ReenterMap_ReappliesSharedAura()
        {
            var fx = Build();
            EquipFreshInstance(fx, TemplateSetOne, SlotSetOne);
            EquipFreshInstance(fx, TemplateSetTwo, SlotSetTwo);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));

            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();
            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "套装门槛加成也应当在跨图后被 EnterMap 重新施加。");
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            // 门槛真正被打破（卸下一件）光环应当正常消失，证明 EnterMap 重建的记账没有把套装状态
            // 锁死成"永远已应用"。
            var unequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotSetOne);
            Assert.NotNull(unequipped);
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }
    }
}

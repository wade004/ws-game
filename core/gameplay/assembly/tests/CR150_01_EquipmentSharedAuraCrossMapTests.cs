using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// CR150-01（外部审计 audit-3224ca1-20260908，P2）复现与根治：<see
    /// cref="Core.Carriers.Item.Core.EquipmentHost.ReapplyGrants"/> 重放两件共享同一
    /// <c>aura_def</c> 的装备（或"装备 + 套装门槛加成"共享同一个 <c>aura_def</c>）时，此前用
    /// "循环期间实时查询 <see cref="IAuraQuery.HasAura"/>" 判断"是否需要重新 ApplyAura"——第一件
    /// 重放会把该 <c>aura_def</c> 的 <c>HasAura</c> 从 false 变为 true，第二件因此误判"从来没有
    /// 失效过"，转而复用自己名下那份早已随 <c>World.ClearAll</c> 失效的旧句柄：这份旧句柄既没有
    /// 被重新计数，也不是 <c>AuraHost</c> 真正认得的活句柄。结果是共享光环的引用计数只算上了第一件，
    /// 卸下第一件装备时第二件明明仍然装备着，光环却被提前移除（外部审计探针
    /// <c>probe_a_shared_aura.log</c> 复现：<c>afterFirstUnequip</c> 实际 False，预期仍应为 True）。
    /// <para>
    /// 根治后改用"本次 <see cref="Core.Carriers.Item.Core.EquipmentHost.ReapplyGrants"/> 调用开始前
    /// 按 <c>aura_def</c> 惰性缓存的快照"驱动判断，不再随循环内的重放结果实时变化（见该方法判断
    /// 记录）。本文件覆盖两件普通装备共享、装备与套装门槛加成共享、
    /// <c>SkillOptions.AllowMultiSourceTiming=true</c> 模式下两件装备各自独立句柄、以及重复调用
    /// <c>EnterMap</c>（未经历新一轮 <c>ClearAll</c>）的幂等性。
    /// </para>
    /// </summary>
    public sealed class CR150_01_EquipmentSharedAuraCrossMapTests
    {
        private static readonly Id MapId = new Id("world.cr150_01_map");
        private static readonly Id PlayerId = new Id("unit.cr150_01_player");
        private static readonly Id PlayerFactionId = new Id("fac.cr150_01_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.cr150_01_sample");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id AuraDefId = new Id("skill.aura_def.cr150_01_shared");

        private static readonly Id SlotOne = new Id("item.slot.cr150_01_one");
        private static readonly Id SlotTwo = new Id("item.slot.cr150_01_two");
        private static readonly Id SlotSetOne = new Id("item.slot.cr150_01_set_one");
        private static readonly Id SlotSetTwo = new Id("item.slot.cr150_01_set_two");

        private static readonly Id TemplateOne = new Id("item.sample_cr150_01_one");
        private static readonly Id TemplateTwo = new Id("item.sample_cr150_01_two");
        private static readonly Id TemplateSetOne = new Id("item.sample_cr150_01_set_one");
        private static readonly Id TemplateSetTwo = new Id("item.sample_cr150_01_set_two");
        private static readonly Id SetId = new Id("item.set.cr150_01_sample");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.cr150_01_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.cr150_01_sample" + "\", \"name_key\": \"l10n.arch.class.cr150_01_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.cr150_01_sample\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var auraDefRows = "[{\"id\": \"" + AuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 100}}]}]";

            var itemSlotDefinitionRows = "[" +
                "{\"id\": \"" + SlotOne.Value + "\", \"name_key\": \"l10n.item.slot.cr150_01_one\"}," +
                "{\"id\": \"" + SlotTwo.Value + "\", \"name_key\": \"l10n.item.slot.cr150_01_two\"}," +
                "{\"id\": \"" + SlotSetOne.Value + "\", \"name_key\": \"l10n.item.slot.cr150_01_set_one\"}," +
                "{\"id\": \"" + SlotSetTwo.Value + "\", \"name_key\": \"l10n.item.slot.cr150_01_set_two\"}" +
                "]";

            var itemQualityDefinitionRows =
                "[{\"id\": \"item.quality.cr150_01_common\", \"name_key\": \"l10n.item.quality.cr150_01_common\"}]";

            var itemSetRows = "[{\"id\": \"" + SetId.Value + "\", \"name_key\": \"l10n.item.set.cr150_01_sample\", " +
                "\"pieces\": [\"" + TemplateSetOne.Value + "\", \"" + TemplateSetTwo.Value + "\"], " +
                "\"bonuses\": [{\"count\": 2, \"aura_ref\": \"" + AuraDefId.Value + "\"}]}]";

            var itemTemplateRows = "[" +
                "{\"id\": \"" + TemplateOne.Value + "\", \"slot\": \"" + SlotOne.Value + "\", " +
                "\"quality\": \"item.quality.cr150_01_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr150_01_one\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr150_01_one\", " +
                "\"grants\": {\"auras\": [\"" + AuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateTwo.Value + "\", \"slot\": \"" + SlotTwo.Value + "\", " +
                "\"quality\": \"item.quality.cr150_01_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr150_01_two\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr150_01_two\", " +
                "\"grants\": {\"auras\": [\"" + AuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateSetOne.Value + "\", \"slot\": \"" + SlotSetOne.Value + "\", " +
                "\"quality\": \"item.quality.cr150_01_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr150_01_set_one\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr150_01_set_one\", " +
                "\"set_id\": \"" + SetId.Value + "\"}," +
                "{\"id\": \"" + TemplateSetTwo.Value + "\", \"slot\": \"" + SlotSetTwo.Value + "\", " +
                "\"quality\": \"item.quality.cr150_01_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.cr150_01_set_two\", \"stack_size\": 1, \"name_key\": \"l10n.item.cr150_01_set_two\", " +
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

        private static Fixture Build(bool allowMultiSourceTiming = false)
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
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.cr150_01_test")), bus);
            var skillOptions = new SkillOptions { AllowMultiSourceTiming = allowMultiSourceTiming };

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                skillOptions: skillOptions);

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

        private static void CrossMap(Fixture fx)
        {
            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "ClearAll 应当真的清空了光环（复现前提，不是本方法自己制造的假象）。");

            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();
            fx.Gameplay.EnterMap(MapId, PlayerId);
        }

        /// <summary>核心复现与根治：两件普通装备共享同一 aura_def（默认
        /// AllowMultiSourceTiming=false），跨图重放后逐件卸下——第一件不应移除仍被第二件持有的
        /// 共享光环，只有最后一件卸下才应移除。</summary>
        [Fact]
        public void TwoOrdinaryEquipments_SharedAura_CrossMapReapply_FirstUnequipKeepsAura_LastUnequipRemovesIt()
        {
            var fx = Build();
            EquipFreshInstance(fx, TemplateOne, SlotOne);
            EquipFreshInstance(fx, TemplateTwo, SlotTwo);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            CrossMap(fx);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "CR150-01 核心断言：跨图重放后共享光环应当恢复。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            var firstUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotOne);
            Assert.NotNull(firstUnequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "CR150-01 核心断言：卸下第一件后，第二件仍装备着，共享光环不应被误删。");
            Assert.Equal(1 + 100, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            var lastUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotTwo);
            Assert.NotNull(lastUnequipped);
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "最后一件卸下后共享光环才应真正消失。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }

        /// <summary>装备 + 套装门槛加成共享同一 aura_def：跨图重放后，先卸下普通装备不应影响仍然
        /// 凑齐门槛的套装光环；卸到门槛被打破后套装光环才应消失，且套装光环本身也不应被普通装备的
        /// 卸装误删。</summary>
        [Fact]
        public void OrdinaryEquipmentAndSetBonus_SharedAura_CrossMapReapply_IndependentUnequipDoesNotDoubleRemove()
        {
            var fx = Build();
            EquipFreshInstance(fx, TemplateOne, SlotOne);
            EquipFreshInstance(fx, TemplateSetOne, SlotSetOne);
            EquipFreshInstance(fx, TemplateSetTwo, SlotSetTwo);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            CrossMap(fx);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "CR150-01：装备 grants.auras 与套装门槛加成共享同一 aura_def 时，跨图重放后也应恢复。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            // 卸下普通装备：套装两件仍在，门槛未破，光环不应消失（普通装备的引用计数应正确递减到 0
            // 但套装那一侧仍持有共享句柄）。
            var ordinaryUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotOne);
            Assert.NotNull(ordinaryUnequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "普通装备卸下后，套装门槛仍满足，共享光环不应被误删。");

            // 打破套装门槛：光环才应真正消失。
            var setUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotSetOne);
            Assert.NotNull(setUnequipped);
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "套装门槛被打破后共享光环才应消失。");
        }

        /// <summary><c>SkillOptions.AllowMultiSourceTiming=true</c> 模式下，两件装备共享同一
        /// aura_def 会各自开出独立实例——跨图重放后应各自独立恢复，卸下任意一件都只影响自己那份，
        /// 不影响另一件持有的独立实例。</summary>
        [Fact]
        public void TwoOrdinaryEquipments_SharedAura_AllowMultiSourceTiming_CrossMapReapply_IndependentInstances()
        {
            var fx = Build(allowMultiSourceTiming: true);
            EquipFreshInstance(fx, TemplateOne, SlotOne);
            EquipFreshInstance(fx, TemplateTwo, SlotTwo);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));

            CrossMap(fx);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "CR150-01：AllowMultiSourceTiming=true 模式下跨图重放也应恢复各自独立的光环实例。");

            var firstUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotOne);
            Assert.NotNull(firstUnequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "AllowMultiSourceTiming=true 下卸下第一件不应影响第二件的独立实例。");

            var lastUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotTwo);
            Assert.NotNull(lastUnequipped);
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "两件都卸下后光环才应完全消失。");
        }

        /// <summary>幂等：跨图重放一次之后，<see cref="GameplayAssembly.EnterMap"/> 被再次调用（未
        /// 经历新一轮 ClearAll）不应让共享光环的引用计数重复累加——之后逐件卸下的行为应与只重放一次
        /// 完全一致。</summary>
        [Fact]
        public void TwoOrdinaryEquipments_SharedAura_RepeatedEnterMapWithoutClear_DoesNotDoubleCount()
        {
            var fx = Build();
            EquipFreshInstance(fx, TemplateOne, SlotOne);
            EquipFreshInstance(fx, TemplateTwo, SlotTwo);

            CrossMap(fx);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            // 未经历新一轮 ClearAll，直接再次调用 EnterMap（幂等场景）。
            fx.Gameplay.EnterMap(MapId, PlayerId);
            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            var firstUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotOne);
            Assert.NotNull(firstUnequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "重复调用 EnterMap 不应让引用计数虚高，卸下第一件后第二件仍应保住共享光环。");

            var lastUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, SlotTwo);
            Assert.NotNull(lastUnequipped);
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
        }
    }
}

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
    /// CORE-170-01 根治（architecture/落地计划/audit-8160178-20260908，P2，第十轮审计已复现）：
    /// <c>GameplayAssembly.EnterMap</c> 先重放装备 grants（<c>EquipmentHost.ReapplyGrants</c>），再
    /// 重放种族被动（<c>RulesAssembly.ReapplyRacePassiveAuras</c>）；后者修复前只按
    /// <c>IAuraQuery.HasAura</c> 判断是否已生效，生效就跳过——装备与种族被动共享同一 <c>aura_def</c>
    /// 时，装备先重放创建了共享光环实例并在跨来源引用计数账本（<see
    /// cref="Core.Rules.Common.AuraHandleLedger"/>）上登记了一份引用，种族看到 <c>HasAura=true</c>
    /// 直接跳过、从未为自己登记引用；随后卸下装备释放这唯一一份引用、计数归零，把种族仍然依赖的
    /// 共享光环实例整个删除（真实探针 <c>core-persistence-probe.raw.log</c> RACE-EQUIPMENT-SHARED-AURA
    /// 段复现：<c>after_unequip</c> 实际 <c>hasAura=false,stacks=0,power=11</c>，预期应为
    /// <c>hasAura=true,stacks=1,power=61</c>）。种族属性修正（<c>race.stat_mods</c>，经
    /// <c>StatModifierWriter</c> 登记表）不受影响，只有光环本身（buff 状态/触发效果）随装备一起消失。
    /// <para>
    /// 根治后 <c>RulesAssembly</c> 持有的 <see cref="Core.Rules.Common.AuraHandleLedger"/> 单一实例
    /// 同时供 <c>EquipmentHost</c>（装备/套装门槛加成）与 <c>RulesAssembly</c> 自己（种族被动，经
    /// <c>RulesAssembly._raceAuraHandles</c> 私有簿记）登记引用；任一来源单独失效只释放自己那一份，
    /// 只有全部来源都释放完毕才真正移除共享实例。本文件覆盖审计验收清单：首次注册（<c>RegisterUnit</c>
    /// 期间种族先创建共享实例）、真实装备（装备后再叠加一份引用）、<c>World.ClearAll</c> + 排空事件、
    /// 重新 <c>AddEntity</c> + <c>EnterMap</c>、真实卸装、再排空事件后种族 aura 与属性修正保留、装备
    /// 独占修正消失；以及未经历新一轮 <c>ClearAll</c> 时重复调用 <c>EnterMap</c> 的幂等性。种族独占
    /// （无装备共享同一 <c>aura_def</c>）与装备独占（无种族）两类既有场景分别由
    /// <see cref="RacePassiveAuraCrossMapTests"/> 与 <see cref="CR150_01_EquipmentSharedAuraCrossMapTests"/>
    /// 覆盖，本文件不重复，只新增"种族 + 装备共享同一 aura_def"这一此前完全没有测试覆盖的组合来源
    /// 场景。
    /// </para>
    /// </summary>
    public sealed class CORE_170_01_RaceEquipmentSharedAuraTests
    {
        private static readonly Id MapId = new Id("world.core_170_01_map");
        private static readonly Id PlayerId = new Id("unit.core_170_01_player");
        private static readonly Id PlayerFactionId = new Id("fac.core_170_01_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.core_170_01_sample");
        private static readonly Id RaceId = new Id("arch.race.core_170_01_sample");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id AuraDefId = new Id("skill.aura_def.core_170_01_shared");

        private static readonly Id AuraSlot = new Id("item.slot.core_170_01_aura");
        private static readonly Id StatOnlySlot = new Id("item.slot.core_170_01_stat_only");
        private static readonly Id AuraItemTemplate = new Id("item.sample_core_170_01_aura");
        private static readonly Id StatOnlyItemTemplate = new Id("item.sample_core_170_01_stat_only");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.core_170_01_sample" + "\", \"name_key\": \"l10n.arch.class.core_170_01_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var auraDefRows = "[{\"id\": \"" + AuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 50}}]}]";

            var archRaceRows = "[{\"id\": \"" + RaceId.Value + "\", \"name_key\": \"l10n.arch.race.core_170_01_sample.name\", " +
                "\"stat_mods\": {\"" + StatPower.Value + "\": 10}, " +
                "\"passive_auras\": [\"" + AuraDefId.Value + "\"]}]";

            var itemSlotDefinitionRows = "[" +
                "{\"id\": \"" + AuraSlot.Value + "\", \"name_key\": \"l10n.item.slot.core_170_01_aura\"}," +
                "{\"id\": \"" + StatOnlySlot.Value + "\", \"name_key\": \"l10n.item.slot.core_170_01_stat_only\"}" +
                "]";

            var itemQualityDefinitionRows =
                "[{\"id\": \"item.quality.core_170_01_common\", \"name_key\": \"l10n.item.quality.core_170_01_common\"}]";

            // AuraItemTemplate：只 grants.auras 共享种族同一个 aura_def，不带独立 stats——它本身对
            // power 的贡献完全来自共享光环的 mod_stat 效果，不是装备自己的属性修正，这样卸下它之后
            // "光环是否还在"才是唯一变量，不会被装备自己的属性修正干扰读数。
            // StatOnlyItemTemplate：只带独立 stats（不 grants 任何 aura）——用来验证"装备独占的修正"
            // 在卸装后确实消失，与"种族仍然依赖的共享光环不应被误删"形成对照。
            var itemTemplateRows = "[" +
                "{\"id\": \"" + AuraItemTemplate.Value + "\", \"slot\": \"" + AuraSlot.Value + "\", " +
                "\"quality\": \"item.quality.core_170_01_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.core_170_01_aura\", \"stack_size\": 1, \"name_key\": \"l10n.item.core_170_01_aura\", " +
                "\"grants\": {\"auras\": [\"" + AuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + StatOnlyItemTemplate.Value + "\", \"slot\": \"" + StatOnlySlot.Value + "\", " +
                "\"quality\": \"item.quality.core_170_01_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.core_170_01_stat_only\", \"stack_size\": 1, \"name_key\": \"l10n.item.core_170_01_stat_only\", " +
                "\"stats\": [{\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 5}]}" +
                "]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("arch.race", Envelope("arch.race", archRaceRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", auraDefRows))
                .Add("item.slot_definition", Envelope("item.slot_definition", itemSlotDefinitionRows))
                .Add("item.quality_definition", Envelope("item.quality_definition", itemQualityDefinitionRows))
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
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_170_01_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0), RaceId = RaceId };
            world.AddEntity(player);
            // 惯例同 RacePassiveAuraCrossMapTests：RegisterUnit 传入 raceId 的同时同步写入
            // PlayerUnit.RaceId（框架不会自动同步，见该字段判断记录）。RegisterUnit 本身会经
            // ArchetypeRegistry.ApplyTo 立即施加种族被动光环（初始注册，见 archAuraApplier 判断
            // 记录），此时装备还没发生，种族是这个共享 aura_def 的第一个、也是当前唯一的来源。
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, RaceId, level: 1);

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

        /// <summary>
        /// 核心复现与根治，覆盖审计验收清单全部步骤：首次注册（种族先创建共享实例）→ 真实装备
        /// （装备再叠加一份引用，且额外装备一件带独立 stats 的物品用于对照"装备独占修正"）→
        /// <c>World.ClearAll</c> + 排空事件 → 重新 <c>AddEntity</c> + <c>EnterMap</c> → 真实卸装
        /// （先卸带共享光环的那件）→ 排空事件：断言种族 aura 与其属性修正（+10 stat_mods、+50 aura
        /// mod_stat）保留；再卸下带独立 stats 的那件：断言装备独占修正（+5）消失，种族部分不受影响。
        /// </summary>
        [Fact]
        public void RaceAndEquipment_SharedAuraDef_FullLifecycle_UnequipAfterCrossMapKeepsRaceAura()
        {
            var fx = Build();

            // 首次注册：种族被动光环已经生效（唯一来源），种族属性修正也已生效。
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // 真实装备：AuraItemTemplate 与种族共享同一 aura_def（叠加一份引用，MaxStacks=1 下层数
            // 不应增加），StatOnlyItemTemplate 带独立 +5 修正（装备独占，与光环无关）。
            EquipFreshInstance(fx, AuraItemTemplate, AuraSlot);
            EquipFreshInstance(fx, StatOnlyItemTemplate, StatOnlySlot);
            fx.Bus.DispatchPending();

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50 + 5, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // 跨图：ClearAll 销毁全部实体，触发 entity.destroyed，AuraHost 清空该玩家名下全部光环
            // （含种族被动、装备 grants.auras）；属性修正（种族 stat_mods、装备独占 stats）走独立的
            // StatModifierWriter/IStatHost 登记表，不受影响。
            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "ClearAll 应当真的清空了共享光环（复现前提，不是本方法自己制造的假象）。");
            Assert.Equal(1 + 10 + 5, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // 重新进图：EnterMap 依次重放装备 grants、种族被动——生产真实顺序，正是 CORE-170-01 的
            // 触发顺序（装备先重放创建共享实例，种族后重放）。
            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();
            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "跨图重放后共享光环应当恢复（装备与种族两个来源都应该拿到有效引用）。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50 + 5, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // CORE-170-01 核心断言：卸下共享光环那件装备后，种族仍然持有一份独立引用，共享光环与其
            // 属性修正都不应消失——修复前这里会变成 hasAura=false（真实探针复现值）。
            var auraItemUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, AuraSlot);
            fx.Bus.DispatchPending();
            Assert.NotNull(auraItemUnequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "CORE-170-01 核心断言：卸下共享光环装备后，种族被动光环不应被误删。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            // 种族的属性修正（+10）与共享光环的 mod_stat（+50）都应保留；装备独占的 +5 此时仍装备着，也应保留。
            Assert.Equal(1 + 10 + 50 + 5, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));

            // 再卸下装备独占修正那件：只有这份 +5 应当消失，种族 aura 与其修正不受影响。
            var statOnlyUnequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, StatOnlySlot);
            fx.Bus.DispatchPending();
            Assert.NotNull(statOnlyUnequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "装备独占修正的卸装不应影响种族仍然依赖的共享光环。");
            // 装备独占修正（+5）应当消失，种族 stat_mods（+10）与共享光环 mod_stat（+50）应当保留。
            Assert.Equal(1 + 10 + 50, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }

        /// <summary>幂等：跨图重放一次之后，<see cref="GameplayAssembly.EnterMap"/> 被再次调用（未
        /// 经历新一轮 <c>ClearAll</c>）不应让种族在 <see cref="Core.Rules.Common.AuraHandleLedger"/>
        /// 上的引用重复累加——之后卸下共享光环装备的行为应与只重放一次完全一致。</summary>
        [Fact]
        public void RaceAndEquipment_SharedAuraDef_RepeatedEnterMapWithoutClear_DoesNotDoubleCountRaceHandle()
        {
            var fx = Build();
            EquipFreshInstance(fx, AuraItemTemplate, AuraSlot);
            fx.Bus.DispatchPending();

            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();
            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            // 未经历新一轮 ClearAll，直接再次调用 EnterMap 两次（幂等场景）——种族这次应该在
            // _raceAuraHandles 里发现"记录仍指向当前活实例"，直接跳过，不重复调用 AuraHandles.Register。
            fx.Gameplay.EnterMap(MapId, PlayerId);
            fx.Gameplay.EnterMap(MapId, PlayerId);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId));
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));

            // 卸下唯一一件共享光环的装备：如果种族的引用在重复 EnterMap 期间被错误地重复登记（计数
            // 虚高），这里也会碰巧通过（计数还没归零）；但下面第二次断言（再卸一次视角）用真正的
            // "种族独占"验证——装备卸下后共享光环应仍然存在，本身就是 CORE-170-01 的核心断言，
            // 重复调用 EnterMap 不应改变这个结论。
            var unequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, AuraSlot);
            fx.Bus.DispatchPending();
            Assert.NotNull(unequipped);
            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "重复调用 EnterMap 不应让装备一侧的引用计数虚高——即便计数虚高侥幸也不会失败本断言，" +
                "真正验证计数没有虚高的是：卸装后种族仍单独持有恰好一份引用，光环应当持续存在，不会在" +
                "任何后续操作下意外消失。");
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(PlayerId, AuraDefId));
            Assert.Equal(1 + 10 + 50, fx.Gameplay.Carriers.Rules.Stats.GetStat(PlayerId, StatPower));
        }

        /// <summary>未设置种族（<see cref="PlayerUnit.RaceId"/> 为 <c>null</c>）时，即便装备
        /// grants.auras 配置了与某个种族共享的同一 <c>aura_def</c>，<see
        /// cref="GameplayAssembly.EnterMap"/> 也应静默跳过种族重放，不抛异常，装备自身的引用计数
        /// 行为退化为 CR150-01 覆盖的"纯装备来源"场景——不因为多了一个从未被本单位实际使用的种族
        /// 分支而受影响。</summary>
        [Fact]
        public void EnterMap_PlayerWithoutRaceId_EquipmentSharedAuraDef_DoesNotThrow_AndBehavesAsEquipmentOnly()
        {
            var fx = Build();
            fx.Player.RaceId = null;

            // 注意：Build() 里 RegisterUnit 已经用 raceId=RaceId 注册过（种族被动已经生效）——这里
            // 只清掉 PlayerUnit.RaceId 字段本身，模拟"实体字段与 Rules 内部注册状态不同步"的边界
            // （同 RacePassiveAuraCrossMapTests.EnterMap_PlayerWithoutRaceId_DoesNotThrow 判断记录：
            // EnterMap 只看 PlayerUnit.RaceId 决定要不要调用 ReapplyRacePassiveAuras，不重新查询
            // Rules 内部状态）。ClearAll 之后种族被动光环与装备光环一起被清空，重进图时 EnterMap 因为
            // RaceId=null 跳过种族重放，只剩装备一侧重新施加——验证不会因为"曾经有过种族"这一事实
            // 意外抛异常或产生错误的计数状态。
            EquipFreshInstance(fx, AuraItemTemplate, AuraSlot);
            fx.Bus.DispatchPending();

            fx.World.ClearAll();
            fx.Bus.DispatchPending();
            fx.World.AddEntity(fx.Player);
            fx.Bus.DispatchPending();

            var exception = Record.Exception(() => fx.Gameplay.EnterMap(MapId, PlayerId));
            Assert.Null(exception);

            Assert.True(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "装备一侧的 grants.auras 仍应正常重放，不受 RaceId=null 影响。");

            var unequipped = fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, AuraSlot);
            fx.Bus.DispatchPending();
            Assert.NotNull(unequipped);
            Assert.False(fx.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(PlayerId, AuraDefId),
                "RaceId=null 时种族从未参与这份引用计数，卸下唯一的装备来源后光环应当正常消失" +
                "（退化为纯装备来源场景，同 CR150-01 单件装备卸装即移除的行为）。");
        }
    }
}

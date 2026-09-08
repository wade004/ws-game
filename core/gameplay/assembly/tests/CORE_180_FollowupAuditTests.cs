using System;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 第十一轮外部审核复核（architecture/落地计划/audit-e070e3f-20260908，core-findings.md）三条
    /// P1/P2 缺陷的复现与根治验收——全部用真实 <see cref="GameplayAssembly"/>/<see
    /// cref="Core.Rules.Assembly.RulesAssembly"/>/<see cref="Core.Foundation.SaveSystem.SaveSystem"/>/
    /// <see cref="IEventBus"/> 组合（不用假实现替换任何一层），惯例同 <c>CORE_170_03_
    /// SaveRollbackEventSuppressionTests</c>。数据/断言取值沿用报告归档的独立探针
    /// （<c>architecture/落地计划/audit-e070e3f-20260908/core/repro/FollowupCoreProbe.cs</c>）
    /// 已经验证过的 fixture 与 EXPECTED 结论。
    /// <list type="bullet">
    /// <item>CORE-180-01（P1）：成功 <c>SaveSystem.Load</c>（不经过 <see
    /// cref="GameplayAssembly.RestoreFromSlot"/>，直接调用 <c>ISaveSystem.Load</c> 本身）后，评级
    /// 换算属性缓存与资源池上限/当前值必须与快照一致。</item>
    /// <item>CORE-180-02（P2）：后段异常触发的回滚，装备段必须在等级已经回滚到位之后才重新装备，
    /// 不能因为回滚顺序颠倒而把原本满足需求的高等级装备漏装在背包里。</item>
    /// <item>CORE-180-03（P2，含候选 CAND-01）：同图 <see cref="GameplayAssembly.RestoreFromSlot"/>
    /// 在两个独立构造的有效存档之间切换种族/职业时，必须清理旧来源的属性修正/光环并施加新来源的，
    /// 不能只改字段。</item>
    /// </list>
    /// </summary>
    public sealed class CORE_180_FollowupAuditTests
    {
        private static readonly Id Unit = new Id("unit.core_180_followup");
        private static readonly Id Map = new Id("world.core_180_followup");
        private static readonly Id Faction = new Id("fac.core_180_followup");
        private static readonly Id Class = new Id("arch.class.core_180_followup");
        private static readonly Id Curve = new Id("prog.curve.core_180_followup");
        private static readonly Id Rating = new Id("stat.core_180_followup_rating");
        private static readonly Id MaxHealth = new Id("stat.max_health");
        private static readonly Id Health = WellKnownPowers.Health;
        private static readonly Id Slot = new Id("item.slot.core_180_followup");
        private static readonly Id Item = new Id("item.core_180_followup_level2");
        private static readonly Id Quality = new Id("item.quality.core_180_followup");
        private static readonly Id RaceA = new Id("arch.race.core_180_followup_a");
        private static readonly Id RaceB = new Id("arch.race.core_180_followup_b");
        private static readonly Id StatRacePower = new Id("stat.core_180_followup_race_power");
        private static readonly Id AuraA = new Id("skill.aura_def.core_180_followup_a");
        private static readonly Id AuraB = new Id("skill.aura_def.core_180_followup_b");
        private static readonly Id ClassA = new Id("arch.class.core_180_followup_arch_a");
        private static readonly Id ClassB = new Id("arch.class.core_180_followup_arch_b");
        private static readonly Id StatClassPower = new Id("stat.core_180_followup_class_power");

        private sealed class Fixture
        {
            public EventBus Bus = null!;
            public StubFileSystem Fs = null!;
            public SaveSystem Save = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(int level = 1, Id? raceId = null, Id? classId = null)
        {
            var resolvedClassId = classId ?? Class;
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(Data(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues));

            var world = new WorldSim(bus);
            var fs = new StubFileSystem();
            var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_180_followup")), bus);
            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
                playerUnitProvider: () => Unit, playerFactionId: Faction,
                statOptions: new StatHostOptions { EnableRatingConversion = true });

            var player = new PlayerUnit(Unit, Map, Faction, resolvedClassId) { Position = Vec2.Zero, RaceId = raceId };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(Unit, resolvedClassId, raceId: raceId, level: level);
            gameplay.RegisterPersistables(save, player);

            return new Fixture { Bus = bus, Fs = fs, Save = save, Gameplay = gameplay, Player = player };
        }

        // -------------------------------------------------------------
        // CORE-180-01：成功 Load 后内部派生缓存必须与快照一致。
        // -------------------------------------------------------------

        [Fact]
        public void CORE_180_01_SuccessfulLoad_RestoresRatingConvertedStat_DirectSaveSystemLoad()
        {
            var fx = Build(level: 1);
            fx.Gameplay.Carriers.Rules.Stats.SetBase(Unit, Rating, 50);
            fx.Gameplay.Carriers.Rules.Progression.RestoreState(Unit, Curve, 10, 0);
            fx.Bus.DispatchPending();
            var ratingAt10 = fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating);
            Assert.Equal(10, ratingAt10);

            var saved = fx.Save.Save(new SaveRequest(new Id("slot.core_180_01_rating"), "test"));
            Assert.True(saved.Success);

            // 破坏当前状态：把等级打回 1，评级换算属性随之跌到 5——模拟"读档前运行期状态与要
            // 恢复的快照不同"。
            fx.Gameplay.Carriers.Rules.Progression.RestoreState(Unit, Curve, 1, 0);
            fx.Bus.DispatchPending();
            Assert.Equal(5, fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating));

            // 直接调用 SaveSystem.Load 本身（不经过 GameplayAssembly.RestoreFromSlot）——CORE-180-01
            // 的根治必须覆盖这条路径，不能只在 RestoreFromSlot 里打补丁。
            var loaded = fx.Save.Load(new Id("slot.core_180_01_rating"));
            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Equal(10, fx.Gameplay.Carriers.Rules.Progression.GetLevel(Unit));
            Assert.Equal(10, fx.Player.Level);

            // drain 不应改变结果（内部重建走的是直接方法调用，不经事件队列）。
            fx.Bus.DispatchPending();
            Assert.Equal(10, fx.Gameplay.Carriers.Rules.Stats.GetStat(Unit, Rating));
        }

        [Fact]
        public void CORE_180_01_SuccessfulLoad_RestoresPowerMaxAndHealth_DirectSaveSystemLoad()
        {
            var fx = Build(level: 2);
            fx.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
            var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
            var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, Slot);
            Assert.True(equip.Success);
            fx.Bus.DispatchPending();

            var maxWithEquip = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
            Assert.Equal(200, maxWithEquip);
            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(
                Unit, Health, maxWithEquip - fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health), new Id("test.fill_health"));
            fx.Bus.DispatchPending();
            var healthWithEquip = fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health);
            Assert.Equal(200, healthWithEquip);

            var saved = fx.Save.Save(new SaveRequest(new Id("slot.core_180_01_vitals"), "test"));
            Assert.True(saved.Success);

            // 破坏当前状态：卸下装备，上限跌回 100。
            fx.Gameplay.Carriers.Equipment.Unequip(Unit, Slot);
            fx.Bus.DispatchPending();
            Assert.Equal(100, fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health));

            var loaded = fx.Save.Load(new Id("slot.core_180_01_vitals"));
            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.True(fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, Slot).HasValue);

            fx.Bus.DispatchPending();
            Assert.Equal(200, fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health));
            Assert.Equal(200, fx.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health));
        }

        // -------------------------------------------------------------
        // CORE-180-02：失败回滚必须先恢复等级、再恢复装备。
        // -------------------------------------------------------------

        private sealed class ThrowingPersistable : IPersistable
        {
            public string SectionKey => "zz.core_180_02_probe_failure";
            public JsonValue Save() => JsonNull.Instance;
            public void Load(JsonValue data) => throw new InvalidOperationException("core-180-02 probe failure");
        }

        [Fact]
        public void CORE_180_02_Rollback_RestoresEquipment_AfterLevelRolledBack()
        {
            var fx = Build(level: 2);
            fx.Save.RegisterPersistable(new ThrowingPersistable());
            fx.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
            var instance = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
            var equip = fx.Gameplay.Carriers.Equipment.Equip(Unit, instance, Slot);
            Assert.True(equip.Success);
            fx.Bus.DispatchPending();
            var baselineInventoryCount = fx.Gameplay.Carriers.Inventory.ListItems(Unit).Count;

            var saved = fx.Save.Save(new SaveRequest(new Id("slot.core_180_02_rollback"), "test"));
            Assert.True(saved.Success);

            // 手工构造一份"后段会失败"的存档文档：progression 段降到等级 1（低于装备的等级 2 需求），
            // equipment 段清空（模拟"这份低等级快照压根没有装备"），其余段沿用真实 Save() 的结果，
            // 末尾附加一个恒定抛异常的自定义段触发本次读档失败——与真实探针
            // ROLLBACK-EQUIPMENT-BEFORE-PROGRESSION 完全同款构造手法。
            var savedText = fx.Fs.ReadText("user://saves/slot.core_180_02_rollback.json")!;
            var savedDoc = (JsonObject)JsonReader.Parse(savedText);
            var sections = (JsonObject)savedDoc["sections"];
            var lowProgression = new JsonObjectBuilder()
                .Add("curve_id", new JsonString(Curve.Value)).Add("level", new JsonNumber(1)).Add("xp", new JsonNumber(0)).Build();
            var emptyEquipment = new JsonObjectBuilder().Build();
            var patchedSections = new JsonObjectBuilder();
            foreach (var entry in sections)
            {
                patchedSections.Add(
                    entry.Key,
                    entry.Key == SaveSections.PlayerProgression ? lowProgression :
                    entry.Key == SaveSections.PlayerEquipment ? emptyEquipment : entry.Value);
            }

            var patched = new JsonObjectBuilder();
            foreach (var entry in savedDoc)
            {
                patched.Add(entry.Key, entry.Key == "sections" ? patchedSections.Build() : entry.Value);
            }

            fx.Fs.WriteTextAtomic("user://saves/slot.core_180_02_rollback.json", JsonWriter.Write(patched.Build()));

            var loaded = fx.Save.Load(new Id("slot.core_180_02_rollback"));

            Assert.Equal(LoadStatus.PersistableThrew, loaded.Status);
            Assert.Equal(2, fx.Gameplay.Carriers.Rules.Progression.GetLevel(Unit));
            Assert.Equal(2, fx.Player.Level);
            Assert.True(
                fx.Gameplay.Carriers.Equipment.GetEquipped(Unit, Slot).HasValue,
                "CORE-180-02 核心断言：回滚必须先恢复等级、再恢复装备，装备不应该因为回滚顺序颠倒而漏装。");
            Assert.Equal(baselineInventoryCount, fx.Gameplay.Carriers.Inventory.ListItems(Unit).Count);
        }

        // -------------------------------------------------------------
        // CORE-180-03（含候选 CAND-01）：同图 RestoreFromSlot 必须重放种族/职业的属性修正与光环，
        // 不能只改字段。
        // -------------------------------------------------------------

        [Fact]
        public void CORE_180_03_SameMapRestoreFromSlot_ReappliesRaceStatModsAndAuras()
        {
            var fxA = Build(level: 1, raceId: RaceA);
            fxA.Bus.DispatchPending();
            Assert.Equal(61, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower));
            Assert.True(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA));
            Assert.False(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB));

            var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.core_180_03_a"), "test"));
            Assert.True(savedA.Success);

            var fxB = Build(level: 1, raceId: RaceB);
            fxB.Bus.DispatchPending();
            var raceBOracleStat = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
            Assert.Equal(91, raceBOracleStat);
            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_180_03_b"), "test"));
            Assert.True(savedB.Success);

            // 把 B 档复制成 A 档同一个槽位的内容，在 A 的宿主上做"同图"读档（目标地图与当前地图相同，
            // 不会触发 EnterMap）——真实探针 SAME-MAP-RACE-SAVE-LOAD 同款构造。
            var savedBJson = fxB.Fs.ReadText("user://saves/slot.core_180_03_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_180_03_b.json", savedBJson);

            var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.core_180_03_b"));
            fxA.Bus.DispatchPending();

            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Equal(Map, fxA.Player.MapId); // 同图：地图字段应保持不变。
            Assert.Equal(RaceB, fxA.Player.RaceId);
            Assert.Equal(raceBOracleStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower));
            Assert.False(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA));
            Assert.True(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB));

            // 重复加载同一份 B 档不应叠加（幂等）。
            fxA.Gameplay.RestoreFromSlot(new Id("slot.core_180_03_b"));
            fxA.Bus.DispatchPending();
            Assert.Equal(raceBOracleStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower));
            Assert.Equal(1, fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.GetStacks(Unit, AuraB));
        }

        [Fact]
        public void CORE_180_CAND_01_SameMapRestoreFromSlot_ReappliesArchetypeBaseStats()
        {
            var fxA = Build(level: 1, classId: ClassA);
            fxA.Bus.DispatchPending();
            Assert.Equal(10, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower));

            var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.core_180_cand01_a"), "test"));
            Assert.True(savedA.Success);

            var fxB = Build(level: 1, classId: ClassB);
            fxB.Bus.DispatchPending();
            var classBOracleStat = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower);
            Assert.Equal(20, classBOracleStat);
            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_180_cand01_b"), "test"));
            Assert.True(savedB.Success);

            var savedBJson = fxB.Fs.ReadText("user://saves/slot.core_180_cand01_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_180_cand01_b.json", savedBJson);

            var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.core_180_cand01_b"));
            fxA.Bus.DispatchPending();

            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Equal(ClassB, fxA.Player.ArchetypeId);
            Assert.Equal(classBOracleStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower));

            // 重复加载不叠加。
            fxA.Gameplay.RestoreFromSlot(new Id("slot.core_180_cand01_b"));
            fxA.Bus.DispatchPending();
            Assert.Equal(classBOracleStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower));
        }

        private static InMemoryDataSource Data()
        {
            string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var levels = string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"level\":" + i + ",\"xp_to_next\":100,\"growth\":{}}"));
            return new InMemoryDataSource()
                .Add("stat.definition", E("stat.definition",
                    "[{\"id\":\"" + Rating.Value + "\",\"name_key\":\"l10n.rating\",\"group\":\"primary\",\"default_base\":0,\"is_rating\":true,\"rating_conversion_ref\":\"stat.rating.core_180_followup\"}," +
                    "{\"id\":\"" + MaxHealth.Value + "\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100}," +
                    "{\"id\":\"" + StatRacePower.Value + "\",\"name_key\":\"l10n.race.power\",\"group\":\"primary\",\"default_base\":1}," +
                    "{\"id\":\"" + StatClassPower.Value + "\",\"name_key\":\"l10n.class.power\",\"group\":\"primary\",\"default_base\":1}]"))
                .Add("stat.rating_conversion", E("stat.rating_conversion",
                    "[{\"id\":\"stat.rating.core_180_followup\",\"entries\":[{\"level\":1,\"points_per_percent\":10},{\"level\":10,\"points_per_percent\":5}]}]"))
                .Add("arch.power_type", E("arch.power_type",
                    "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"" + MaxHealth.Value + "\"},\"start_full\":true}]"))
                .Add("arch.class", E("arch.class",
                    "[{\"id\":\"" + Class.Value + "\",\"name_key\":\"l10n.class\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}," +
                    "{\"id\":\"" + ClassA.Value + "\",\"name_key\":\"l10n.class.a\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{\"" + StatClassPower.Value + "\":10},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}," +
                    "{\"id\":\"" + ClassB.Value + "\",\"name_key\":\"l10n.class.b\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{\"" + StatClassPower.Value + "\":20},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}]"))
                .Add("arch.race", E("arch.race",
                    "[{\"id\":\"" + RaceA.Value + "\",\"name_key\":\"l10n.race.a\",\"stat_mods\":{\"" + StatRacePower.Value + "\":10},\"passive_auras\":[\"" + AuraA.Value + "\"]}," +
                    "{\"id\":\"" + RaceB.Value + "\",\"name_key\":\"l10n.race.b\",\"stat_mods\":{\"" + StatRacePower.Value + "\":20},\"passive_auras\":[\"" + AuraB.Value + "\"]}]"))
                .Add("skill.aura_def", E("skill.aura_def",
                    "[{\"id\":\"" + AuraA.Value + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + StatRacePower.Value + "\",\"op\":\"flat\",\"value\":50}}]}," +
                    "{\"id\":\"" + AuraB.Value + "\",\"duration\":60,\"max_stacks\":1,\"effects\":[{\"kind\":\"mod_stat\",\"params\":{\"stat\":\"" + StatRacePower.Value + "\",\"op\":\"flat\",\"value\":70}}]}]"))
                .Add("prog.level_curve", E("prog.level_curve", "[{\"id\":\"" + Curve.Value + "\",\"max_level\":10,\"entries\":[" + levels + "]}]"))
                .Add("item.slot_definition", E("item.slot_definition", "[{\"id\":\"" + Slot.Value + "\",\"name_key\":\"l10n.slot\"}]"))
                .Add("item.quality_definition", E("item.quality_definition", "[{\"id\":\"" + Quality.Value + "\",\"name_key\":\"l10n.quality\"}]"))
                .Add("item.template", E("item.template",
                    "[{\"id\":\"" + Item.Value + "\",\"slot\":\"" + Slot.Value + "\",\"quality\":\"" + Quality.Value + "\",\"item_level\":1," +
                    "\"display_ref\":\"display.core_180_followup\",\"stack_size\":1,\"name_key\":\"l10n.item\",\"requirements\":{\"level\":2}," +
                    "\"stats\":[{\"stat\":\"" + MaxHealth.Value + "\",\"op\":\"flat\",\"value\":100}]}]"))
                .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":200}]}]"))
                .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", E("combat.resist_curve", "[]"));
        }
    }
}

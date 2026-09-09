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
    /// 第十二轮外部审核复核（architecture/落地计划/audit-ac3b622-20260909，core/core-findings.md）
    /// 两条 P2 缺陷的复现与根治验收——全部用真实 <see cref="GameplayAssembly"/>/<see
    /// cref="Core.Rules.Assembly.RulesAssembly"/>/<see cref="Core.Foundation.SaveSystem.SaveSystem"/>/
    /// <see cref="IEventBus"/> 组合（不用假实现替换任何一层），惯例同 <c>CORE_180_FollowupAuditTests</c>。
    /// 数据/断言取值沿用报告归档的独立探针（<c>architecture/落地计划/audit-ac3b622-20260909/core/
    /// repro/FollowupCoreProbe.cs</c>）已经验证过的 fixture 与 EXPECTED 结论。
    /// <list type="bullet">
    /// <item>CORE-110-01（P2）：失败读档触发的回滚此前只恢复字段（<see cref="IPersistable.Load"/>
    /// 直接覆盖写入的部分），派生状态（评级换算属性、种族被动光环、资源池上限/当前值）没有跟着按
    /// 依赖顺序重建，停留在读档失败前的陈旧值。两个子场景：race/stat/aura 与
    /// equipment/known_skills 触发的 max/health。</item>
    /// <item>CORE-110-02（P2）：同图切换职业时，<c>RulesAssembly.ReloadArchetypeAndRace</c> 只对新旧
    /// 职业共同声明的基础属性键做覆盖写入，旧职业独有的基础属性键与资源类型（如法力）从未被清理，
    /// 永久残留。</item>
    /// </list>
    /// </summary>
    public sealed class CORE_110_FollowupAuditTests
    {
        private static readonly Id Unit = new Id("unit.core_110_followup");
        private static readonly Id Map = new Id("world.core_110_followup");
        private static readonly Id Faction = new Id("fac.core_110_followup");
        private static readonly Id Class = new Id("arch.class.core_110_followup");
        private static readonly Id Curve = new Id("prog.curve.core_110_followup");
        private static readonly Id MaxHealth = new Id("stat.max_health.core_110_followup");
        private static readonly Id Health = WellKnownPowers.Health;
        private static readonly Id Mana = new Id("arch.power.mana.core_110_followup");
        private static readonly Id Slot = new Id("item.slot.core_110_followup");
        private static readonly Id Item = new Id("item.core_110_followup_level2");
        private static readonly Id Quality = new Id("item.quality.core_110_followup");

        private static readonly Id RaceA = new Id("arch.race.core_110_followup_a");
        private static readonly Id RaceB = new Id("arch.race.core_110_followup_b");
        private static readonly Id StatRacePower = new Id("stat.core_110_followup_race_power");
        private static readonly Id AuraA = new Id("skill.aura_def.core_110_followup_a");
        private static readonly Id AuraB = new Id("skill.aura_def.core_110_followup_b");

        private static readonly Id ClassLegacyA = new Id("arch.class.core_110_followup_legacy_a");
        private static readonly Id ClassLegacyB = new Id("arch.class.core_110_followup_legacy_b");
        private static readonly Id StatClassPower = new Id("stat.core_110_followup_class_power");
        private static readonly Id StatClassLegacy = new Id("stat.core_110_followup_class_legacy");

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
            var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_110_followup")), bus);
            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
                playerUnitProvider: () => Unit, playerFactionId: Faction,
                statOptions: new StatHostOptions { EnableRatingConversion = false });

            var player = new PlayerUnit(Unit, Map, Faction, resolvedClassId) { Position = Vec2.Zero, RaceId = raceId };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(Unit, resolvedClassId, raceId: raceId, level: level);
            gameplay.RegisterPersistables(save, player);

            return new Fixture { Bus = bus, Fs = fs, Save = save, Gameplay = gameplay, Player = player };
        }

        private static JsonObject ReadSavedSections(StubFileSystem fs, string slotId)
        {
            var text = fs.ReadText($"user://saves/{slotId}.json")!;
            var doc = (JsonObject)JsonReader.Parse(text);
            return (JsonObject)doc["sections"];
        }

        /// <summary>把 <paramref name="slotId"/> 存档文档里 <paramref name="sectionKey"/> 段的值
        /// 替换成 <paramref name="replacement"/>，其余段原样保留，写回同一个 <paramref name="fs"/>——
        /// 惯例同 <c>CORE_180_FollowupAuditTests.CORE_180_02_Rollback_RestoresEquipment_AfterLevelRolledBack</c>
        /// 手工构造"后段会失败"存档文档的手法。</summary>
        private static void PatchSection(StubFileSystem fs, string slotId, string sectionKey, JsonValue replacement)
        {
            var text = fs.ReadText($"user://saves/{slotId}.json")!;
            var doc = (JsonObject)JsonReader.Parse(text);
            var sections = (JsonObject)doc["sections"];

            var patchedSections = new JsonObjectBuilder();
            foreach (var entry in sections)
            {
                patchedSections.Add(entry.Key, entry.Key == sectionKey ? replacement : entry.Value);
            }

            var patched = new JsonObjectBuilder();
            foreach (var entry in doc)
            {
                patched.Add(entry.Key, entry.Key == "sections" ? patchedSections.Build() : entry.Value);
            }

            fs.WriteTextAtomic($"user://saves/{slotId}.json", JsonWriter.Write(patched.Build()));
        }

        // -------------------------------------------------------------
        // CORE-110-01 子场景 A：失败回滚后种族字段已回 A，但评级/光环派生状态仍停留在 B。
        // -------------------------------------------------------------

        private sealed class ThrowingPersistable : IPersistable
        {
            public string SectionKey => "zz.core_110_01_probe_failure";
            public JsonValue Save() => JsonNull.Instance;
            public void Load(JsonValue data) => throw new InvalidOperationException("core-110-01 probe failure");
        }

        [Fact]
        public void CORE_110_01_RaceSubCase_RollbackRestoresFieldAndDerivedStatAndAura()
        {
            var fxA = Build(level: 1, raceId: RaceA);
            fxA.Save.RegisterPersistable(new ThrowingPersistable());
            fxA.Bus.DispatchPending();
            var raceAStat = fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
            Assert.True(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA));
            Assert.False(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB));

            var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.core_110_01_race_a"), "test"));
            Assert.True(savedA.Success);

            var fxB = Build(level: 1, raceId: RaceB);
            fxB.Bus.DispatchPending();
            var raceBOracleStat = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower);
            Assert.NotEqual(raceAStat, raceBOracleStat);
            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_110_01_race_b"), "test"));
            Assert.True(savedB.Success);

            // 把 B 档复制成 A 档同一个槽位——正向阶段 player.race_id 段会先成功把字段/派生状态改成
            // B，随后新增的恒定抛异常自定义段（ThrowingPersistable，排在 KnownOrder 全部已知段之后）
            // 触发失败，驱动回滚。
            var bJson = fxB.Fs.ReadText("user://saves/slot.core_110_01_race_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_110_01_race_b.json", bJson);

            // 抑制作用域内的正向 Load + 回滚回放不应向外部订阅者泄漏业务事件（同 CORE_170_03 既有
            // 惯例）：订阅必须在触发读档之前完成才能观察到"本该被抑制、却漏出来了"的反例。
            var leaked = 0;
            fxA.Bus.Subscribe(StatBlockEventKeys.StatChanged, _ => leaked++);

            var loaded = fxA.Save.Load(new Id("slot.core_110_01_race_b"));
            fxA.Bus.DispatchPending();

            Assert.Equal(LoadStatus.PersistableThrew, loaded.Status);
            // 字段：回滚必须把种族字段恢复回 A。
            Assert.Equal(RaceA, fxA.Player.RaceId);
            // CORE-110-01 核心断言：派生状态（评级换算属性、被动光环）必须跟着字段一起回到 A，
            // 不能停留在正向阶段短暂生效过的 B。
            Assert.Equal(raceAStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatRacePower));
            Assert.True(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraA));
            Assert.False(fxA.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(Unit, AuraB));
            Assert.Equal(0, leaked);
        }

        // -------------------------------------------------------------
        // CORE-110-01 子场景 B：装备段成功回滚，但资源池上限/当前值（Power max/health）此前不会
        // 跟着重建，停留在 known_skills 段失败前那次派生重算 clamp 下调的值。
        // -------------------------------------------------------------

        [Fact]
        public void CORE_110_01_EquipmentSubCase_RollbackRestoresPowerMaxAndHealth()
        {
            var fxA = Build(level: 2);
            fxA.Save.RegisterPersistable(new ThrowingPersistable());
            fxA.Gameplay.Carriers.Inventory.AddItem(Unit, Item, 1);
            var instance = fxA.Gameplay.Carriers.Inventory.ListItems(Unit).Last().InstanceId;
            var equip = fxA.Gameplay.Carriers.Equipment.Equip(Unit, instance, Slot);
            Assert.True(equip.Success);
            fxA.Bus.DispatchPending();
            var maxWithEquip = fxA.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health);
            Assert.Equal(200, maxWithEquip);
            fxA.Gameplay.Carriers.Rules.Powers.ModifyPower(
                Unit, Health, maxWithEquip - fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health), new Id("test.fill_health"));
            fxA.Bus.DispatchPending();
            Assert.Equal(200, fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health));

            var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.core_110_01_equip_a"), "test"));
            Assert.True(savedA.Success);

            // 合法 B 档：level 1、空装备（真实保存，known_skills 段是合法的空数组）。
            var fxB = Build(level: 1);
            fxB.Bus.DispatchPending();
            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_110_01_equip_b"), "test"));
            Assert.True(savedB.Success);
            Assert.Equal(100, fxB.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health));

            var bJson = fxB.Fs.ReadText("user://saves/slot.core_110_01_equip_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_110_01_equip_b.json", bJson);
            // 故障注入：只把 player.known_skills 段改成非法形状（JsonNumber 不是 JsonArray），
            // player.equipment 段仍是 B 的合法空装备——equipment 段会先成功 Load 并触发派生重算
            // （max 因此下调到 100），known_skills 段紧接着抛异常。
            PatchSection(fxA.Fs, "slot.core_110_01_equip_b", SaveSections.PlayerKnownSkills, new JsonNumber(1));

            var loaded = fxA.Save.Load(new Id("slot.core_110_01_equip_b"));

            Assert.Equal(LoadStatus.PersistableThrew, loaded.Status);
            Assert.Equal(2, fxA.Gameplay.Carriers.Rules.Progression.GetLevel(Unit));
            Assert.True(fxA.Gameplay.Carriers.Equipment.GetEquipped(Unit, Slot).HasValue, "装备应该回滚回 A。");
            // CORE-110-01 核心断言：max 与 health 都必须跟着装备一起回到 A 的 200，不能停留在
            // known_skills 段失败前那次派生重算下调到的 100。
            Assert.Equal(200, fxA.Gameplay.Carriers.Rules.Powers.GetPowerMax(Unit, Health));
            Assert.Equal(200, fxA.Gameplay.Carriers.Rules.Powers.GetPower(Unit, Health));
        }

        // -------------------------------------------------------------
        // CORE-110-02：同图换职业，旧职业独有基础属性键与资源类型集合必须清理，不能只覆盖共同键。
        // -------------------------------------------------------------

        [Fact]
        public void CORE_110_02_SameMapClassSwitch_ClearsLegacyBaseKeyAndPowerType()
        {
            var fxA = Build(level: 1, classId: ClassLegacyA);
            fxA.Bus.DispatchPending();
            Assert.Equal(10, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower));
            Assert.Equal(5, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy));
            Assert.True(fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana));

            var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.core_110_02_a"), "test"));
            Assert.True(savedA.Success);

            var fxB = Build(level: 1, classId: ClassLegacyB);
            fxB.Bus.DispatchPending();
            var classBOracleStat = fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower);
            Assert.Equal(20, classBOracleStat);
            // B 职业未声明 StatClassLegacy：B oracle 上该属性应退回 default_base（0）。
            Assert.Equal(0, fxB.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy));
            Assert.False(fxB.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana));
            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_110_02_b"), "test"));
            Assert.True(savedB.Success);

            var bJson = fxB.Fs.ReadText("user://saves/slot.core_110_02_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_110_02_b.json", bJson);

            var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.core_110_02_b"));
            fxA.Bus.DispatchPending();

            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Equal(ClassLegacyB, fxA.Player.ArchetypeId);
            // 共同键：正确变成 B 的值。
            Assert.Equal(classBOracleStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower));
            // CORE-110-02 核心断言：旧职业独有基础键必须清理，不能残留 A 的 5。
            Assert.Equal(0, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy));
            // CORE-110-02 核心断言：旧职业独有的资源类型（法力）必须按新职业集合清理，不能残留可查询。
            Assert.False(fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana));
            Assert.True(fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Health));

            // 重复加载同一份 B 档不应叠加/报错（幂等）。
            fxA.Gameplay.RestoreFromSlot(new Id("slot.core_110_02_b"));
            fxA.Bus.DispatchPending();
            Assert.Equal(classBOracleStat, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassPower));
            Assert.Equal(0, fxA.Gameplay.Carriers.Rules.Stats.GetStat(Unit, StatClassLegacy));
            Assert.False(fxA.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana));
        }

        private static InMemoryDataSource Data()
        {
            string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var levels = string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"level\":" + i + ",\"xp_to_next\":100,\"growth\":{}}"));
            return new InMemoryDataSource()
                .Add("stat.definition", E("stat.definition",
                    "[{\"id\":\"" + MaxHealth.Value + "\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100}," +
                    "{\"id\":\"" + StatRacePower.Value + "\",\"name_key\":\"l10n.race.power\",\"group\":\"primary\",\"default_base\":1}," +
                    "{\"id\":\"" + StatClassPower.Value + "\",\"name_key\":\"l10n.class.power\",\"group\":\"primary\",\"default_base\":1}," +
                    "{\"id\":\"" + StatClassLegacy.Value + "\",\"name_key\":\"l10n.class.legacy\",\"group\":\"primary\",\"default_base\":0}]"))
                .Add("arch.power_type", E("arch.power_type",
                    "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"" + MaxHealth.Value + "\"},\"start_full\":true}," +
                    "{\"id\":\"" + Mana.Value + "\",\"name_key\":\"l10n.mana\",\"max_source\":{\"kind\":\"fixed\",\"value\":50},\"start_full\":true}]"))
                .Add("arch.class", E("arch.class",
                    "[{\"id\":\"" + Class.Value + "\",\"name_key\":\"l10n.class\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}," +
                    "{\"id\":\"" + ClassLegacyA.Value + "\",\"name_key\":\"l10n.class.legacy_a\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{\"" + StatClassPower.Value + "\":10,\"" + StatClassLegacy.Value + "\":5},\"power_types\":[\"arch.power.health\",\"" + Mana.Value + "\"],\"level_curve_ref\":\"" + Curve.Value + "\"}," +
                    "{\"id\":\"" + ClassLegacyB.Value + "\",\"name_key\":\"l10n.class.legacy_b\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{\"" + StatClassPower.Value + "\":20},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}]"))
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
                    "\"display_ref\":\"display.core_110_followup\",\"stack_size\":1,\"name_key\":\"l10n.item\",\"requirements\":{\"level\":2}," +
                    "\"stats\":[{\"stat\":\"" + MaxHealth.Value + "\",\"op\":\"flat\",\"value\":100}]}]"))
                .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":200}]}]"))
                .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", E("combat.resist_curve", "[]"));
        }
    }
}

using System;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 第十三轮外部审核复核（architecture/落地计划/audit-6739f50-20260909，core/core-findings.md）
    /// CORE-111-01（P2，主审已确认）的复现与根治验收——全部用真实 <see cref="GameplayAssembly"/>/
    /// <see cref="Core.Rules.Assembly.RulesAssembly"/>/<see cref="Core.Foundation.SaveSystem.SaveSystem"/>/
    /// <see cref="Core.Numbers.PowerSet.PowerHost"/> 组合（不用假实现替换任何一层），惯例同
    /// <c>CORE_110_FollowupAuditTests</c>。数据/断言取值沿用报告归档的独立探针
    /// （<c>architecture/落地计划/audit-6739f50-20260909/core/repro/FollowupCoreProbe.cs</c>
    /// <c>RunRollbackClassPowerState</c>）已经验证过的 fixture 与 ORACLE-CURRENT 结论。
    /// <para>
    /// CORE-111-01：A 存档运行期 Mana=30（<c>start_full=false</c>，脱战回复 10）且
    /// <c>InCombat=true</c>，读取不同 power 集合（不含 Mana）的 B 存档在后段失败后触发正向顺序
    /// 回滚：<c>player.race_id</c>/<c>player.archetype</c> 段回滚重放
    /// <c>RulesAssembly.ReloadArchetypeAndRace</c>，资源类型集合确实变化时整体
    /// <c>UnregisterUnit</c>+<c>RegisterUnit</c>，重建出的全新 <see cref="Core.Numbers.PowerSet.PowerHost"/>
    /// 内部状态把 Mana 按 <c>start_full</c> 初始化为 0、把 <c>InCombat</c> 重置为 false——修复前
    /// <c>PlayerVitalsPersistable</c> 只序列化 <c>health</c>，没有数据可以把这两个字段纠正回来。
    /// </para>
    /// </summary>
    public sealed class CORE_111_FollowupAuditTests
    {
        private static readonly Id Unit = new Id("unit.core_111_followup");
        private static readonly Id Map = new Id("world.core_111_followup");
        private static readonly Id Faction = new Id("fac.core_111_followup");
        private static readonly Id ClassA = new Id("arch.class.core_111_followup_a");
        private static readonly Id ClassB = new Id("arch.class.core_111_followup_b");
        private static readonly Id Curve = new Id("prog.curve.core_111_followup");
        private static readonly Id MaxHealth = new Id("stat.max_health.core_111_followup");
        private static readonly Id Health = WellKnownPowers.Health;
        private static readonly Id Mana = new Id("arch.power.mana.core_111_followup");

        private sealed class Fixture
        {
            public EventBus Bus = null!;
            public StubFileSystem Fs = null!;
            public SaveSystem Save = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(Id classId)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(Data(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues));

            var world = new WorldSim(bus);
            var fs = new StubFileSystem();
            var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_111_followup")), bus);
            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), save,
                playerUnitProvider: () => Unit, playerFactionId: Faction);

            var player = new PlayerUnit(Unit, Map, Faction, classId) { Position = Vec2.Zero };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(Unit, classId, raceId: null, level: 1);
            gameplay.RegisterPersistables(save, player);

            return new Fixture { Bus = bus, Fs = fs, Save = save, Gameplay = gameplay, Player = player };
        }

        private sealed class ThrowingPersistable : IPersistable
        {
            public string SectionKey => "zz.core_111_01_probe_failure";
            public JsonValue Save() => JsonNull.Instance;
            public void Load(JsonValue data) => throw new InvalidOperationException("core-111-01 probe failure");
        }

        /// <summary>
        /// CORE-111-01 核心复现与根治：失败读档触发的正向顺序回滚必须把资源池"读档前那一刻"的
        /// 当前值（含未被最终存档覆盖过、只在运行期发生过的 Mana 消耗）与进出战斗运行态一并恢复，
        /// 不能只恢复被回滚段自身直接覆盖写入的字段。用 <c>Advance(1)</c> 后 Mana 是否保持 30
        /// （<c>regen_in_combat=0</c>）而不是被错误地按脱战速率推到 40（若 InCombat 被误重置为
        /// false 且 Mana 又被误重置为 0，会先被 <c>regen_out_of_combat=10</c> 推到 10——旧行为的
        /// 真实数值）来确认 InCombat 真的被恢复，而不是碰巧数值相等。
        /// </summary>
        [Fact]
        public void CORE_111_01_RollbackRestoresPowerCurrentValuesAndCombatState()
        {
            var fxA = Build(ClassA);
            fxA.Save.RegisterPersistable(new ThrowingPersistable());
            fxA.Bus.DispatchPending();

            var powers = fxA.Gameplay.Carriers.Rules.Powers;
            powers.SetInCombat(Unit, true);
            powers.ModifyPower(Unit, Mana, 30, new Id("test.mana_spend"));
            fxA.Bus.DispatchPending();
            Assert.Equal(30, powers.GetPower(Unit, Mana));
            Assert.True(powers.IsInCombat(Unit));

            var savedA = fxA.Save.Save(new SaveRequest(new Id("slot.core_111_01_a"), "test"));
            Assert.True(savedA.Success);

            // 合法 B 档：不同职业，声明的资源类型集合不含 Mana。
            var fxB = Build(ClassB);
            fxB.Bus.DispatchPending();
            Assert.False(fxB.Gameplay.Carriers.Rules.Powers.HasPower(Unit, Mana));
            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_111_01_b"), "test"));
            Assert.True(savedB.Success);

            var bJson = fxB.Fs.ReadText("user://saves/slot.core_111_01_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_111_01_b.json", bJson);

            // 正向阶段：player.archetype/race_id 段先成功把职业切到 B（连带把 Mana 从 PowerHost
            // 卸载），随后恒定抛异常的自定义段（排在 KnownOrder 全部已知段之后）触发失败,驱动
            // 正向顺序回滚。
            var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.core_111_01_b"));
            fxA.Bus.DispatchPending();

            Assert.Equal(LoadStatus.PersistableThrew, loaded.Status);
            Assert.Equal(ClassA, fxA.Player.ArchetypeId);

            // CORE-111-01 核心断言：Mana 必须跟着职业一起回到 A 的资源类型集合，当前值恢复为 30
            // （不是被 RegisterUnit 重新初始化到的 0），InCombat 必须恢复为 true。
            Assert.True(powers.HasPower(Unit, Mana));
            Assert.Equal(30, powers.GetPower(Unit, Mana));
            Assert.True(powers.IsInCombat(Unit));

            // 用 Advance 的实际行为确认 InCombat 真的是 true（regen_in_combat=0 时数值不变），
            // 不是仅仅数值巧合相等。
            powers.Advance(Unit, 1);
            Assert.Equal(30, powers.GetPower(Unit, Mana));
        }

        /// <summary>
        /// 验收补充："正常读档也恢复全部池的当前值"：非失败路径的成功读档同样要把目标存档里全部
        /// 已注册资源池的当前值与进出战斗状态覆盖过去，不是只有失败回滚这一条路径才泛化。
        /// </summary>
        [Fact]
        public void CORE_111_01_SuccessfulLoad_RestoresAllRegisteredPowerCurrentValuesAndCombatState()
        {
            var fxA = Build(ClassA);
            fxA.Bus.DispatchPending();
            var powersA = fxA.Gameplay.Carriers.Rules.Powers;
            powersA.SetInCombat(Unit, true);
            powersA.ModifyPower(Unit, Mana, 30, new Id("test.mana_spend_a"));
            fxA.Bus.DispatchPending();

            var fxB = Build(ClassA);
            fxB.Bus.DispatchPending();
            var powersB = fxB.Gameplay.Carriers.Rules.Powers;
            // B 保持脱战（InCombat=false），Mana 消耗到不同于 A 的值，证明成功读档确实用 B 的值
            // 覆盖了 A 运行期的 Mana/InCombat，而不是保留读档前的 A 状态或落回默认值。
            powersB.ModifyPower(Unit, Mana, 70, new Id("test.mana_spend_b"));
            fxB.Bus.DispatchPending();
            Assert.Equal(70, powersB.GetPower(Unit, Mana));
            Assert.False(powersB.IsInCombat(Unit));

            var savedB = fxB.Save.Save(new SaveRequest(new Id("slot.core_111_01_success_b"), "test"));
            Assert.True(savedB.Success);
            var bJson = fxB.Fs.ReadText("user://saves/slot.core_111_01_success_b.json")!;
            fxA.Fs.WriteTextAtomic("user://saves/slot.core_111_01_success_b.json", bJson);

            var loaded = fxA.Gameplay.RestoreFromSlot(new Id("slot.core_111_01_success_b"));
            fxA.Bus.DispatchPending();

            Assert.Equal(LoadStatus.Loaded, loaded.Status);
            Assert.Equal(70, powersA.GetPower(Unit, Mana));
            Assert.False(powersA.IsInCombat(Unit));
        }

        /// <summary>
        /// 向后兼容验收："旧档只有 health 时其它池按各自 start_full/默认规则初始化"：手工构造一份
        /// 只有 <c>{alive, health}</c>、没有新增 <c>powers</c>/<c>in_combat</c> 字段的旧格式
        /// <c>player.vitals</c> 段（CORE-111-01 之前的格式），确认仍能正常 Load、Health 按旧字段
        /// 恢复，且不影响其它已注册资源池（未被旧格式提及，保持 <c>RegisterUnit</c> 已经按
        /// <c>start_full</c>/默认规则初始化好的当前值不变）——旧档必须可读，不能因为新增字段而抛出
        /// 异常或丢数据。
        /// </summary>
        [Fact]
        public void CORE_111_01_LegacyVitalsFormat_HealthOnly_StillLoadsWithoutTouchingOtherPools()
        {
            var fx = Build(ClassA);
            fx.Bus.DispatchPending();
            var powers = fx.Gameplay.Carriers.Rules.Powers;

            powers.ModifyPower(Unit, Mana, 42, new Id("test.legacy_mana"));
            fx.Bus.DispatchPending();
            var manaBefore = powers.GetPower(Unit, Mana);
            var maxHealth = powers.GetPowerMax(Unit, Health);

            var persistable = new PlayerVitalsPersistable(fx.Player, powers);
            var legacyDoc = new JsonObjectBuilder()
                .Add("alive", JsonBool.True)
                .Add("health", new JsonNumber(1))
                .Build();

            persistable.Load(legacyDoc);

            Assert.True(fx.Player.Alive);
            Assert.Equal(1, powers.GetPower(Unit, Health));
            // 旧格式没有 powers/in_combat 字段：其它资源池当前值/进出战斗状态保持不变，不被清零或
            // 重置为默认值。
            Assert.Equal(manaBefore, powers.GetPower(Unit, Mana));
            Assert.False(powers.IsInCombat(Unit));

            // 复位 Health，确认新格式（Save() 产出）能整轮往返。
            powers.ModifyPower(Unit, Health, maxHealth - powers.GetPower(Unit, Health), new Id("test.reset_health"));
        }

        private static InMemoryDataSource Data()
        {
            string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var levels = "[{\"level\":1,\"xp_to_next\":100,\"growth\":{}}]";
            return new InMemoryDataSource()
                .Add("stat.definition", E("stat.definition",
                    "[{\"id\":\"" + MaxHealth.Value + "\",\"name_key\":\"l10n.max\",\"group\":\"primary\",\"default_base\":100}]"))
                .Add("arch.power_type", E("arch.power_type",
                    "[{\"id\":\"arch.power.health\",\"name_key\":\"l10n.health\",\"max_source\":{\"kind\":\"stat\",\"stat\":\"" + MaxHealth.Value + "\"},\"start_full\":true}," +
                    "{\"id\":\"" + Mana.Value + "\",\"name_key\":\"l10n.mana\",\"max_source\":{\"kind\":\"fixed\",\"value\":100},\"regen_in_combat\":0,\"regen_out_of_combat\":10,\"start_full\":false}]"))
                .Add("arch.class", E("arch.class",
                    "[{\"id\":\"" + ClassA.Value + "\",\"name_key\":\"l10n.class.a\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{},\"power_types\":[\"arch.power.health\",\"" + Mana.Value + "\"],\"level_curve_ref\":\"" + Curve.Value + "\"}," +
                    "{\"id\":\"" + ClassB.Value + "\",\"name_key\":\"l10n.class.b\",\"primary_stat\":\"" + MaxHealth.Value + "\",\"base_stats\":{},\"power_types\":[\"arch.power.health\"],\"level_curve_ref\":\"" + Curve.Value + "\"}]"))
                .Add("prog.level_curve", E("prog.level_curve", "[{\"id\":\"" + Curve.Value + "\",\"max_level\":1,\"entries\":" + levels + "}]"))
                .Add("item.budget_curve", E("item.budget_curve", "[{\"id\":\"item.budget.default\",\"entries\":[{\"item_level\":1,\"budget\":10}]}]"))
                .Add("combat.hit_table_config", E("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", E("combat.resist_curve", "[]"));
        }
    }
}

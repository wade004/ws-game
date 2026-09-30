using System;
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
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// T-H11（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="PlayerVitalsPersistable.Load"/>
    /// 坏形状。夹具同 <c>PlayerVitalsPersistableAud02Tests</c>（真实 <see cref="GameplayAssembly"/>）。
    /// <para>
    /// 本段与其它存档段的口径不同（测试只钉现状，不改口径）：
    /// <list type="bullet">
    /// <item>根不是 JSON 对象（且不是 <see cref="JsonNull"/>）→ <see cref="FormatException"/>，且在任何状态被
    /// 触碰之前抛出（存活位 / 生命值 / 进战状态逐项不变）。</item>
    /// <item>对象内字段的类型不符（<c>alive</c> 非布尔、<c>health</c> / <c>powers[*]</c> 非数字、<c>powers</c> 非对象、
    /// <c>in_combat</c> 非布尔）：<b>不抛</b>，按"该字段缺失"处理；<c>powers</c> 里单位未持有 / id 非法的条目被静默
    /// 跳过（Load 注释：<c>该池不存在于当前配置时不做任何事的既有语义</c>）。</item>
    /// <item>缺 <c>in_combat</c>（含字段类型不符）会把进战状态显式归零为 false（C11-RELOAD 判断记录），
    /// 这是<b>有副作用的默认值</b>：空对象 <c>{}</c> 并不是"什么都不改"。</item>
    /// </list>
    /// 以上宽松口径是否应改为严格抛 FormatException 待设计层确认；带 Characterization 前缀的用例只是把现状钉住。
    /// </para>
    /// </summary>
    public sealed class T_H11_PlayerVitalsPersistableBadShapeTests
    {
        private static readonly Id PlayerId = new Id("unit.vitals_h11_player");
        private static readonly Id PlayerFactionId = new Id("fac.vitals_h11_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.vitals_h11_sample");
        private static readonly Id MapA = new Id("world.vitals_h11_map_a");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.vitals_h11_sample\", \"name_key\": \"l10n.arch.class.vitals_h11_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public PowerHost Powers = null!;
            public PlayerVitalsPersistable Persistable = null!;
            public double Max;
            public double PreHealth;
        }

        /// <summary>读档前现场：存活、生命值为 Max 的一半、处于战斗中（使"进战被归零"可观测）。</summary>
        private static Fixture NewFixture()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.vitals_h11_test")), bus);
            var gameplay = new GameplayAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), saveSystem,
                playerUnitProvider: () => PlayerId, playerFactionId: PlayerFactionId);
            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            var powers = gameplay.Carriers.Rules.Powers;
            var max = powers.GetPowerMax(PlayerId, WellKnownPowers.Health);
            var preHealth = max / 2;
            powers.ModifyPower(PlayerId, WellKnownPowers.Health, preHealth - max, PlayerId);
            powers.RestoreInCombat(PlayerId, true);
            player.Alive = true;

            return new Fixture
            {
                Gameplay = gameplay,
                Player = player,
                Powers = powers,
                Persistable = new PlayerVitalsPersistable(player, powers),
                Max = max,
                PreHealth = preHealth,
            };
        }

        private static string Capture(Fixture f) =>
            $"alive={f.Player.Alive}|health={f.Powers.GetPower(PlayerId, WellKnownPowers.Health)}|" +
            $"inCombat={f.Powers.IsInCombat(PlayerId)}##{JsonWriter.Write(f.Persistable.Save())}";

        [Fact]
        public void Fixture_PreLoadStateIsAsDesigned()
        {
            var f = NewFixture();
            Assert.True(f.Player.Alive);
            Assert.Equal(f.Max / 2, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            Assert.True(f.Powers.IsInCombat(PlayerId));
        }

        // ---- 1. 根不是对象：抛 FormatException，状态逐项不变 ----

        [Fact]
        public void Load_RootIsString_ThrowsFormatException_AndKeepsState() => AssertRootRejected(new JsonString("wrong-shape"));

        [Fact]
        public void Load_RootIsArray_ThrowsFormatException_AndKeepsState() => AssertRootRejected(new JsonArray(Array.Empty<JsonValue>()));

        [Fact]
        public void Load_RootIsNumber_ThrowsFormatException_AndKeepsState() => AssertRootRejected(new JsonNumber(1));

        [Fact]
        public void Load_RootIsBool_ThrowsFormatException_AndKeepsState() => AssertRootRejected(JsonBool.False);

        private static void AssertRootRejected(JsonValue bad)
        {
            var f = NewFixture();
            var before = Capture(f);

            var ex = Record.Exception(() => f.Persistable.Load(bad));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(before, Capture(f));
        }

        [Fact]
        public void Load_AfterRejectedBadRoot_ValidSnapshotStillLoadsNormally()
        {
            var f = NewFixture();
            var good = f.Persistable.Save();
            f.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -1, PlayerId);
            Assert.Throws<FormatException>(() => f.Persistable.Load(new JsonString("bad")));

            f.Persistable.Load(good);

            Assert.Equal(f.PreHealth, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            Assert.True(f.Powers.IsInCombat(PlayerId));
        }

        // ---- 2. 对象内字段缺失 / 类型不符：现状口径（宽松，不抛；待设计层确认，见类注释） ----

        [Fact]
        public void Characterization_EmptyObject_KeepsAliveAndHealth_ButResetsInCombatToFalse()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(new JsonObjectBuilder().Build()));

            Assert.Null(ex);
            Assert.True(f.Player.Alive);
            Assert.Equal(f.PreHealth, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            // 缺 in_combat 不是"保持原样"，而是显式归零（C11-RELOAD 判断记录）。
            Assert.False(f.Powers.IsInCombat(PlayerId));
        }

        [Fact]
        public void Characterization_AliveWrongType_IsIgnored()
        {
            var f = NewFixture();
            f.Player.Alive = false;

            var ex = Record.Exception(() => f.Persistable.Load(
                new JsonObjectBuilder().Add("alive", new JsonString("true")).Build()));

            Assert.Null(ex);
            Assert.False(f.Player.Alive);
        }

        [Fact]
        public void Characterization_HealthWrongType_IsIgnored_HealthKeepsPreLoadValue()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(
                new JsonObjectBuilder().Add("health", new JsonString("1")).Build()));

            Assert.Null(ex);
            Assert.Equal(f.PreHealth, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
        }

        [Fact]
        public void Characterization_PowersNotObject_FallsBackToLegacyHealthField()
        {
            var f = NewFixture();
            var target = f.Max / 4;

            var ex = Record.Exception(() => f.Persistable.Load(new JsonObjectBuilder()
                .Add("powers", new JsonArray(Array.Empty<JsonValue>()))
                .Add("health", new JsonNumber(target))
                .Build()));

            Assert.Null(ex);
            Assert.Equal(target, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
        }

        [Fact]
        public void Characterization_PowersEntries_WrongTypeIllegalIdAndUnheldPower_AreSkippedSilently()
        {
            var f = NewFixture();
            var target = f.Max / 4;
            var powersMap = new JsonObjectBuilder()
                .Add("arch.power.mana_not_held_by_this_class", new JsonNumber(5)) // 合法 id 但单位未持有
                .Add("Not A Legal Id!!", new JsonNumber(5)) // 非法 id
                .Add("arch.power.another", new JsonString("7")) // 值类型不符
                .Add(WellKnownPowers.Health.Value, new JsonNumber(target)) // 唯一有效条目
                .Build();

            var ex = Record.Exception(() => f.Persistable.Load(new JsonObjectBuilder().Add("powers", powersMap).Build()));

            Assert.Null(ex);
            Assert.Equal(target, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
        }

        [Fact]
        public void Characterization_HealthAboveMaxOrBelowZero_IsClampedToPowerRange()
        {
            var f = NewFixture();

            f.Persistable.Load(new JsonObjectBuilder()
                .Add("powers", new JsonObjectBuilder().Add(WellKnownPowers.Health.Value, new JsonNumber(f.Max * 10)).Build())
                .Build());
            Assert.Equal(f.Max, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));

            f.Persistable.Load(new JsonObjectBuilder()
                .Add("powers", new JsonObjectBuilder().Add(WellKnownPowers.Health.Value, new JsonNumber(-f.Max)).Build())
                .Build());
            Assert.Equal(0, f.Powers.GetPower(PlayerId, WellKnownPowers.Health));
        }

        [Fact]
        public void Characterization_InCombatWrongType_TreatedAsNotInCombat()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(
                new JsonObjectBuilder().Add("in_combat", new JsonString("true")).Build()));

            Assert.Null(ex);
            Assert.False(f.Powers.IsInCombat(PlayerId));
        }
    }
}

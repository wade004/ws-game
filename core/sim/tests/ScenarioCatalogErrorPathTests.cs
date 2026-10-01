using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 收口遗留修复 A9（T-M10 S2 遗留）：<see cref="Core.Sim.ScenarioCatalog"/> 解析错误路径。
    /// 正式 <c>sim.scenario</c> schema 在加载期已阻断字段缺失/类型错误，这些分支在正式 schema 下触达不到；
    /// 这里改注册一份放宽的测试 schema（全部可选、<c>kind</c> 为自由字符串、<c>player.level</c> 不登记以便塞入非数值）让坏数据
    /// 通过加载期校验，直接验证 <see cref="Core.Sim.ScenarioCatalog"/> 自身的防御分支：
    /// 缺字段/类型不符 → <see cref="DataFieldException"/>（带表名/字段名），未知 kind → <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    public sealed class ScenarioCatalogErrorPathTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        /// <summary>放宽的 sim.scenario：除 id 外全部可选；kind 为自由字符串；player/opponent 子结构不登记 level（登记外字段不被类型校验，便于塞非数值）。</summary>
        private static TableSchema RelaxedScenarioSchema() => new TableSchema(
            "sim.scenario", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("kind", FieldKind.String, required: false),
                new FieldSchema("player", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("class_id", FieldKind.String, required: false),
                }),
                new FieldSchema("opponent", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("creature_id", FieldKind.String, required: false),
                }),
                new FieldSchema("runs", FieldKind.Int, required: false),
                new FieldSchema("base_seed", FieldKind.Int, required: false),
                new FieldSchema("max_ticks", FieldKind.Int, required: false),
            });

        private static DataRegistry Load(params string[] rowJson)
        {
            var source = new InMemoryDataSource()
                .Add("sim.scenario",
                    "{\"table\": \"sim.scenario\", \"schema_version\": 1, \"rows\": [" + string.Join(", ", rowJson) + "]}");
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(RelaxedScenarioSchema());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private const string ValidKind = "\"kind\": \"arena\"";
        private const string ValidPlayer = "\"player\": {\"class_id\": \"arch.class.a\", \"level\": 5}";
        private const string ValidOpponent = "\"opponent\": {\"creature_id\": \"creature.beast\"}";
        private const string ValidTail = "\"runs\": 1, \"base_seed\": 1, \"max_ticks\": 10";

        /// <summary>拼一行 sim.scenario：每一段传空字符串即省略该段，缺省为合法值。</summary>
        private static string Row(
            string id = "sim.scenario.bad", string kind = ValidKind, string player = ValidPlayer,
            string opponent = ValidOpponent, string tail = ValidTail)
        {
            var parts = new List<string> { "\"id\": \"" + id + "\"" };
            foreach (var part in new[] { kind, player, opponent, tail })
            {
                if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            return "{" + string.Join(", ", parts) + "}";
        }

        [Fact]
        public void ControlRow_WithEveryRequiredFieldPresent_ParsesSuccessfully()
        {
            // 对照组：放宽 schema 下的完整合法行能被解析——证明下面各用例的异常确由那一处缺陷触发。
            var registry = Load(Row());

            var scenario = Assert.Single(new Core.Sim.ScenarioCatalog(registry).All);

            Assert.Equal(Core.Sim.ScenarioKind.Arena, scenario.Kind);
            Assert.Equal(5, scenario.Player.Level);
        }

        [Fact]
        public void MissingKind_ThrowsDataFieldException_NamingTheField()
        {
            var registry = Load(Row(kind: ""));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("kind", ex.Field);
        }

        [Fact]
        public void UnknownKind_ThrowsArgumentOutOfRange_WithOffendingValue()
        {
            var registry = Load(Row(kind: "\"kind\": \"bogus\""));

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("bogus", ex.ActualValue);
        }

        [Fact]
        public void MissingPlayerObject_ThrowsDataFieldException()
        {
            var registry = Load(Row(player: ""));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("player", ex.Field);
        }

        [Fact]
        public void MissingOpponentObject_ThrowsDataFieldException()
        {
            var registry = Load(Row(opponent: ""));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("opponent", ex.Field);
        }

        [Fact]
        public void PlayerMissingClassId_ThrowsDataFieldException_NamingSubObjectField()
        {
            var registry = Load(Row(player: "\"player\": {\"level\": 5}"));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("sim.scenario", ex.Table);
            Assert.Equal("class_id", ex.Field);
        }

        [Fact]
        public void PlayerLevelNotNumeric_ThrowsDataFieldException()
        {
            // 放宽 schema 不登记 player.level，"abc" 通过加载期校验，解析时遇到非数值。
            var registry = Load(Row(player: "\"player\": {\"class_id\": \"arch.class.a\", \"level\": \"abc\"}"));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("level", ex.Field);
        }

        [Fact]
        public void OpponentMissingCreatureId_ThrowsDataFieldException()
        {
            var registry = Load(Row(opponent: "\"opponent\": {\"level\": \"3\"}"));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal("creature_id", ex.Field);
        }

        [Theory]
        [InlineData("\"base_seed\": 1, \"max_ticks\": 10", "runs")]
        [InlineData("\"runs\": 1, \"max_ticks\": 10", "base_seed")]
        [InlineData("\"runs\": 1, \"base_seed\": 1", "max_ticks")]
        public void MissingRequiredScalar_ThrowsDataFieldException_NamingTheField(string tail, string expectedField)
        {
            var registry = Load(Row(tail: tail));

            var ex = Assert.Throws<DataFieldException>(() => new Core.Sim.ScenarioCatalog(registry));

            Assert.Equal(expectedField, ex.Field);
        }

        [Fact]
        public void ParseError_InSecondRow_AbortsConstruction_NoPartialCatalog()
        {
            // 构造函数在解析任何一行失败时整体抛出，不会得到一个只含前面行的半成品目录。
            var registry = Load(Row(id: "sim.scenario.ok"), Row(id: "sim.scenario.bad", kind: "\"kind\": \"bogus\""));

            Assert.Throws<ArgumentOutOfRangeException>(() => new Core.Sim.ScenarioCatalog(registry));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Assembly;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Presentation.Assembly;
using Xunit;
using WorldStateImpl = Core.Gameplay.WorldState.WorldState;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// ADR-0173 任务目标指引：<see cref="QuestGuideHost.Evaluate"/> 的步骤选择、进度、目标选择、跨地图通道寻路与优先级用例，
    /// 以及 <c>quest.def.guide</c> 的数据解析/校验与默认定位器（<see cref="RegistryQuestGuideLocator"/>）。
    /// 任务状态一律走真实 <see cref="QuestHost"/>，世界标志走真实 <see cref="WorldStateImpl"/>，期望值由数据算出（不写死裸坐标）。
    /// </summary>
    public sealed class QuestGuideHostTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly IExprSchema Schema = QuestExprSchemaEntries.BuildParsingSchema();

        private static readonly Id Town = new Id("world.g_town");
        private static readonly Id Cave1 = new Id("world.g_cave1");
        private static readonly Id Cave2 = new Id("world.g_cave2");
        private static readonly Id MainQuest = new Id("quest.g_main");
        private static readonly Id SideQuest = new Id("quest.g_side");

        private static ExprNode E(string text) => ExprParser.Parse(text, Schema);

        private static string Flag(int i) => "world.g_cleared_" + i;

        private sealed class FakeLocator : IQuestGuideLocator
        {
            public readonly Dictionary<Id, QuestGuideAnchor> Areas = new Dictionary<Id, QuestGuideAnchor>();
            public readonly Dictionary<Id, QuestGuideAnchor> Spawns = new Dictionary<Id, QuestGuideAnchor>();
            public readonly List<QuestGuideTransition> Gates = new List<QuestGuideTransition>();

            public bool TryResolveArea(Id areaId, out QuestGuideAnchor anchor) => Areas.TryGetValue(areaId, out anchor);

            public bool TryResolveSpawn(Id spawnId, out QuestGuideAnchor anchor) => Spawns.TryGetValue(spawnId, out anchor);

            public IReadOnlyList<QuestGuideTransition> Transitions => Gates;
        }

        private sealed class Rig
        {
            public readonly Harness H;
            public readonly WorldStateImpl World;
            public readonly FakeLocator Locator = new FakeLocator();
            public readonly QuestGuideHost Guide;

            public Rig(params QuestDefinition[] definitions)
            {
                H = new Harness(definitions);
                World = new WorldStateImpl(new EventBus(EventCatalog.FromDefinitions(new[]
                {
                    new EventDefinition(WorldStateEventKeys.FlagChanged, "world", new[] { "flagKey", "oldValue", "newValue", "writerId" }),
                })));
                var factory = new TestExprHostFactory(
                    new QuestExprGroupProvider(H.Host, () => Player),
                    new PlayerExprGroupProvider(H.Inventory, H.Progression, () => Player),
                    new WorldExprGroupProvider(World));
                Guide = new QuestGuideHost(() => H.Host.Definitions, factory, World, Locator);
            }

            public void SetFlag(string key) => World.Set(new Id(key), ExprValue.OfBool(true), Player);
        }

        private static QuestDefinition Quest(Id id, IReadOnlyList<QuestGuideStep>? steps, int priority = 0) => new QuestDefinition(
            id,
            new[] { new QuestObjective(QuestObjectiveType.Kill, new Id("creature.g_boss"), 1) },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None,
            null, null, null, null, null, steps, priority);

        private static QuestGuideTargetDef AreaTarget(string area, string? visibleIf = null) =>
            new QuestGuideTargetDef(new Id(area), null, null, null, visibleIf == null ? null : E(visibleIf));

        // ---------------------------------------------------------------
        // 缺省关闭
        // ---------------------------------------------------------------

        [Fact]
        public void NoQuestDeclaresGuide_HasNoGuideAndEvaluatesToNull()
        {
            var rig = new Rig(Quest(MainQuest, null));
            Assert.False(rig.Guide.HasAnyGuide);
            Assert.Null(rig.Guide.Evaluate(Player, Town, Vec2.Zero));
        }

        // ---------------------------------------------------------------
        // 阶段切换：同一任务按状态/标志依次命中不同步骤，目标与进度由数据决定
        // ---------------------------------------------------------------

        [Fact]
        public void Steps_FollowQuestStateAndWorldFlags_AndTargetsComeFromData()
        {
            var captain = new Id("spawn.g_captain");
            var archway = new Id("area.g_town_to_cave1");
            var stairs = new Id("area.g_cave1_to_cave2");
            var bossRoom = new Id("area.g_enter_boss");
            var rooms = new[] { new Id("area.g_enter_r1"), new Id("area.g_enter_r2"), new Id("area.g_enter_r3") };
            var q = MainQuest.Value;
            var steps = new[]
            {
                new QuestGuideStep(E($"quest.is_available({q})"), new Id("l10n.g.talk"), new[] { new QuestGuideTargetDef(null, captain, null, null, null) }, null),
                new QuestGuideStep(E($"quest.is_active({q}) and not world.has({Flag(3)})"), new Id("l10n.g.clear"),
                    rooms.Select((r, i) => AreaTarget(r.Value, $"not world.has({Flag(i + 1)})")).ToArray(),
                    new[] { new Id(Flag(1)), new Id(Flag(2)), new Id(Flag(3)) }),
                new QuestGuideStep(E($"quest.is_active({q})"), new Id("l10n.g.boss"), new[] { AreaTarget(bossRoom.Value) }, null),
                new QuestGuideStep(E($"quest.is_objectives_complete({q})"), new Id("l10n.g.return"), new[] { new QuestGuideTargetDef(null, captain, null, null, null) }, null),
            };
            var rig = new Rig(Quest(MainQuest, steps));
            rig.Locator.Spawns[captain] = new QuestGuideAnchor(Town, new Vec2(19, 16), captain);
            rig.Locator.Areas[archway] = new QuestGuideAnchor(Town, new Vec2(16, 19), archway);
            rig.Locator.Areas[stairs] = new QuestGuideAnchor(Cave1, new Vec2(45, 27), stairs);
            rig.Locator.Areas[bossRoom] = new QuestGuideAnchor(Cave2, new Vec2(42, 22), bossRoom);
            rig.Locator.Areas[rooms[0]] = new QuestGuideAnchor(Cave1, new Vec2(25, 7), rooms[0]);
            rig.Locator.Areas[rooms[1]] = new QuestGuideAnchor(Cave1, new Vec2(25, 22), rooms[1]);
            rig.Locator.Areas[rooms[2]] = new QuestGuideAnchor(Cave1, new Vec2(42, 22), rooms[2]);
            rig.Locator.Gates.Add(new QuestGuideTransition(archway, Town, Cave1, new Vec2(16, 19), null));
            rig.Locator.Gates.Add(new QuestGuideTransition(stairs, Cave1, Cave2, new Vec2(45, 27), E($"world.has({Flag(3)})")));
            rig.Locator.Gates.Add(new QuestGuideTransition(new Id("area.g_cave2_to_cave1"), Cave2, Cave1, new Vec2(5, 9), null));
            rig.Locator.Gates.Add(new QuestGuideTransition(new Id("area.g_cave1_to_town"), Cave1, Town, new Vec2(5, 9), null));

            // 阶段 1：任务可接 -> 去找队长（同图，箭头直指队长）。
            var g = rig.Guide.Evaluate(Player, Town, new Vec2(10, 10))!;
            Assert.Equal(0, g.StepIndex);
            Assert.Equal(new Id("l10n.g.talk"), g.TextKey);
            Assert.Equal(rig.Locator.Spawns[captain].Position, g.Target!.ArrowPosition);
            Assert.Null(g.Target.ViaTransitionId);
            Assert.Null(g.ProgressTotal);

            // 阶段 2：已接取，一层未清完 -> 在小镇时箭头指向拱门（通往一层的通道），进度 0/3。
            Assert.True(rig.H.Host.Accept(Player, MainQuest));
            g = rig.Guide.Evaluate(Player, Town, new Vec2(10, 10))!;
            Assert.Equal(1, g.StepIndex);
            Assert.Equal(archway, g.Target!.ViaTransitionId);
            Assert.Equal(rig.Locator.Areas[archway].Position, g.Target.ArrowPosition);
            Assert.Equal(Cave1, g.Target.MapId);
            Assert.Equal(0, g.ProgressDone);
            Assert.Equal(3, g.ProgressTotal);
            Assert.Equal("已清 0/3", g.FormatText("已清 {done}/{total}"));

            // 在一层：箭头直指最近的未清房间；清掉一间后进度加一、该房间不再是候选。
            g = rig.Guide.Evaluate(Player, Cave1, new Vec2(24, 8))!;
            Assert.Equal(rooms[0], g.Target!.SourceId);
            Assert.Null(g.Target.ViaTransitionId);
            rig.SetFlag(Flag(1));
            g = rig.Guide.Evaluate(Player, Cave1, new Vec2(24, 8))!;
            Assert.Equal(1, g.ProgressDone);
            Assert.NotEqual(rooms[0], g.Target!.SourceId);
            Assert.Equal(rooms[1], g.Target.SourceId);   // 剩下两间里 r2 离 (24,8) 更近

            // 阶段 3：楼梯条件成立（第三间清掉）-> 通往二层的通道开了，步骤落到"前往首领"，箭头在一层指向楼梯。
            rig.SetFlag(Flag(3));
            g = rig.Guide.Evaluate(Player, Cave1, new Vec2(40, 20))!;
            Assert.Equal(2, g.StepIndex);
            Assert.Equal(stairs, g.Target!.ViaTransitionId);
            Assert.Equal(rig.Locator.Areas[stairs].Position, g.Target.ArrowPosition);
            Assert.Equal(rig.Locator.Areas[bossRoom].Position, g.Target.Position);
            // 在二层：箭头直指首领房。
            g = rig.Guide.Evaluate(Player, Cave2, new Vec2(9, 6))!;
            Assert.Equal(bossRoom, g.Target!.SourceId);
            Assert.Null(g.Target.ViaTransitionId);
        }

        [Fact]
        public void StepWithOnlyClosedRoute_IsSkippedAndFallsThroughToNextStep()
        {
            var q = MainQuest.Value;
            var gate = new Id("area.g_gate");
            var steps = new[]
            {
                new QuestGuideStep(E($"quest.is_active({q})"), new Id("l10n.g.far"), new[] { new QuestGuideTargetDef(null, null, Cave2, new Vec2(1, 1), null) }, null),
                new QuestGuideStep(E($"quest.is_active({q})"), new Id("l10n.g.text_only"), null, null),
            };
            var rig = new Rig(Quest(MainQuest, steps));
            rig.Locator.Gates.Add(new QuestGuideTransition(gate, Town, Cave2, new Vec2(3, 3), E($"world.has({Flag(9)})")));
            Assert.True(rig.H.Host.Accept(Player, MainQuest));

            var closed = rig.Guide.Evaluate(Player, Town, Vec2.Zero)!;
            Assert.Equal(1, closed.StepIndex);
            Assert.Null(closed.Target);

            rig.SetFlag(Flag(9));
            var open = rig.Guide.Evaluate(Player, Town, Vec2.Zero)!;
            Assert.Equal(0, open.StepIndex);
            Assert.Equal(gate, open.Target!.ViaTransitionId);
            Assert.Equal(new Vec2(3, 3), open.Target.ArrowPosition);
            Assert.Equal(new Vec2(1, 1), open.Target.Position);
        }

        [Fact]
        public void MultiHopRoute_PointsAtFirstGateOnShortestOpenPath()
        {
            var q = MainQuest.Value;
            var steps = new[]
            {
                new QuestGuideStep(E($"quest.is_available({q})"), new Id("l10n.g.far"), new[] { new QuestGuideTargetDef(null, null, Town, new Vec2(7, 7), null) }, null),
            };
            var rig = new Rig(Quest(MainQuest, steps));
            var g21 = new Id("area.g_c2_to_c1");
            rig.Locator.Gates.Add(new QuestGuideTransition(g21, Cave2, Cave1, new Vec2(5, 9), null));
            rig.Locator.Gates.Add(new QuestGuideTransition(new Id("area.g_c1_to_town"), Cave1, Town, new Vec2(6, 9), null));
            var g = rig.Guide.Evaluate(Player, Cave2, new Vec2(30, 30))!;
            Assert.Equal(g21, g.Target!.ViaTransitionId);
            Assert.Equal(Town, g.Target.MapId);
        }

        [Fact]
        public void HigherPriorityQuestWinsEvenWhenDeclaredLater()
        {
            var qa = MainQuest.Value;
            var qb = SideQuest.Value;
            var stepsA = new[] { new QuestGuideStep(E($"quest.is_available({qa})"), new Id("l10n.g.a"), null, null) };
            var stepsB = new[] { new QuestGuideStep(E($"quest.is_available({qb})"), new Id("l10n.g.b"), null, null) };
            var rig = new Rig(Quest(MainQuest, stepsA, 0), Quest(SideQuest, stepsB, 5));
            Assert.Equal(SideQuest, rig.Guide.Evaluate(Player, Town, Vec2.Zero)!.QuestId);
        }

        // ---------------------------------------------------------------
        // 数据：解析、校验、默认定位器
        // ---------------------------------------------------------------

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + rowsJson + "]}";

        private static InMemoryDataSource Source(string guideJson) => new InMemoryDataSource()
            .Add("l10n.locale", Envelope("l10n.locale", "{\"id\":\"l10n.locale.zh_cn\",\"is_default\":true}"))
            .Add("l10n.text", Envelope("l10n.text",
                "{\"key\":\"l10n.quest.g.title\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"T\"}," +
                "{\"key\":\"l10n.quest.g.step\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"S {done}/{total}\"}"))
            .Add("area.trigger_def", Envelope("area.trigger_def",
                "{\"id\":\"area.g_a\",\"map_id\":\"world.g_town\",\"shape\":{\"kind\":\"rect\",\"center\":{\"x\":16.0,\"y\":19.4},\"length\":4.0,\"width\":1.6},\"trigger_type\":\"map_transition\",\"one_shot\":false," +
                "\"params\":{\"target_map\":\"world.g_cave1\"}}," +
                "{\"id\":\"area.g_b\",\"map_id\":\"world.g_cave1\",\"shape\":{\"kind\":\"rect\",\"center\":{\"x\":45.4,\"y\":27.5},\"length\":2.2,\"width\":1.8},\"trigger_type\":\"map_transition\",\"one_shot\":false," +
                "\"params\":{\"target_map\":\"world.g_cave2\"},\"condition\":\"world.has(world.g_cleared_3)\"}"))
            .Add("spawn.table", Envelope("spawn.table",
                "{\"id\":\"spawn.g_captain\",\"map_id\":\"world.g_town\",\"content_ref\":\"creature.g_captain\",\"position\":{\"x\":19.0,\"y\":16.3},\"facing\":0,\"respawn_policy\":\"never\"}"))
            .Add("quest.def", Envelope("quest.def",
                "{\"id\":\"quest.g_data\",\"title_key\":\"l10n.quest.g.title\"," +
                "\"objectives\":[{\"type\":\"event\",\"target_ref\":\"event.g_data\",\"count\":1}]," +
                "\"start_method\":\"auto\",\"turn_in_method\":\"auto\",\"repeatable\":\"none\"," +
                "\"guide_priority\":3,\"guide\":" + guideJson + "}"));

        private const string GoodGuide =
            "[{\"when\":\"quest.is_active(quest.g_data)\",\"text_key\":\"l10n.quest.g.step\"," +
            "\"targets\":[{\"area_ref\":\"area.g_b\",\"visible_if\":\"not world.has(world.g_cleared_3)\"},{\"spawn_ref\":\"spawn.g_captain\"},{\"map_id\":\"world.g_town\",\"position\":{\"x\":1,\"y\":2}}]," +
            "\"progress_flags\":[\"world.g_cleared_1\",\"world.g_cleared_3\"]}]";

        [Fact]
        public void GuideField_ValidData_PassesValidationAndParses()
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(GoodGuide) });
            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));

            var registry = (DataRegistry)ContentValidationAssembly.CreateRegistry(Source(GoodGuide), new ContentValidationOptions(), out _);
            registry.LoadAll();
            var def = QuestDefinition.FromRecord(registry.Get("quest.def", "quest.g_data")!, GameplaySchemaCatalog.FullExprSchema);
            Assert.Equal(3, def.GuidePriority);
            var step = Assert.Single(def.GuideSteps);
            Assert.NotNull(step.When);
            Assert.Equal(new Id("l10n.quest.g.step"), step.TextKey);
            Assert.Equal(3, step.Targets.Count);
            Assert.Equal(new Id("area.g_b"), step.Targets[0].AreaRef);
            Assert.NotNull(step.Targets[0].VisibleIf);
            Assert.Equal(new Id("spawn.g_captain"), step.Targets[1].SpawnRef);
            Assert.Equal(new Vec2(1, 2), step.Targets[2].Position);
            Assert.Equal(new[] { new Id("world.g_cleared_1"), new Id("world.g_cleared_3") }, step.ProgressFlags);
        }

        [Theory]
        [InlineData("[{\"text_key\":\"l10n.quest.g.step\",\"targets\":[{}]}]")]
        [InlineData("[{\"text_key\":\"l10n.quest.g.step\",\"targets\":[{\"area_ref\":\"area.g_a\",\"spawn_ref\":\"spawn.g_captain\"}]}]")]
        [InlineData("[{\"text_key\":\"l10n.quest.g.step\",\"targets\":[{\"map_id\":\"world.g_town\"}]}]")]
        [InlineData("[{\"text_key\":\"l10n.quest.g.step\",\"targets\":[{\"position\":{\"x\":1,\"y\":2}}]}]")]
        public void GuideField_BadTargetForm_IsReported(string guide)
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(guide) });
            Assert.True(run.Report.IsBlocking);
            Assert.Contains(run.Report.Issues, i => i.Check == "quest_guide_target_form");
        }

        [Fact]
        public void GuideField_DanglingReferenceAndMissingTextKey_AreBlocking()
        {
            var dangling = "[{\"text_key\":\"l10n.quest.g.step\",\"targets\":[{\"area_ref\":\"area.g_missing\"}]}]";
            Assert.True(ContentValidationAssembly.Run(new IDataSource[] { Source(dangling) }).Report.IsBlocking);
            var noKey = "[{\"text_key\":\"l10n.quest.g.not_there\"}]";
            Assert.True(ContentValidationAssembly.Run(new IDataSource[] { Source(noKey) }).Report.IsBlocking);
        }

        [Fact]
        public void RegistryLocator_ResolvesAreaSpawnAndTransitionsFromData()
        {
            var registry = (DataRegistry)ContentValidationAssembly.CreateRegistry(Source(GoodGuide), new ContentValidationOptions(), out _);
            registry.LoadAll();
            var locator = new RegistryQuestGuideLocator(registry, GameplaySchemaCatalog.FullExprSchema);

            Assert.True(locator.TryResolveArea(new Id("area.g_a"), out var area));
            Assert.Equal(new Id("world.g_town"), area.MapId);
            Assert.Equal(new Vec2(16.0, 19.4), area.Position);
            Assert.True(locator.TryResolveSpawn(new Id("spawn.g_captain"), out var spawn));
            Assert.Equal(new Vec2(19.0, 16.3), spawn.Position);
            Assert.False(locator.TryResolveArea(new Id("area.g_none"), out _));
            Assert.False(locator.TryResolveSpawn(new Id("spawn.g_none"), out _));

            Assert.Equal(2, locator.Transitions.Count);
            var stairs = locator.Transitions.Single(t => t.AreaId.Equals(new Id("area.g_b")));
            Assert.Equal(new Id("world.g_cave1"), stairs.FromMapId);
            Assert.Equal(new Id("world.g_cave2"), stairs.ToMapId);
            Assert.NotNull(stairs.Condition);
            Assert.Null(locator.Transitions.Single(t => t.AreaId.Equals(new Id("area.g_a"))).Condition);
        }
    }
}

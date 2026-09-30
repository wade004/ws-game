using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SaveSystem
{
    /// <summary>
    /// 测试覆盖梳理 D19（docs/复盘/测试覆盖梳理-2026-10-01.md 2.3 节；ADR-0125 D19 拍板：保留并补测试，
    /// 不标 Obsolete）：<see cref="ReplayRecorder.RecordEndTurn"/> / <see cref="ReplayData.EndTurns"/> /
    /// <see cref="ReplayEndTurnRecord"/> 是回合制时间模型（ADR-0013）回放记录的"结束回合"入口，
    /// <see cref="ReplayPlayer"/> 离散重放读取它，但此前录制侧全仓库零测试。本文件固定：记录 → 导出 →
    /// 序列化 → 反序列化往返；与输入、步记录的相对顺序；tick 计数；重置与前置条件；旧格式兼容。
    /// </summary>
    public sealed class ReplayEndTurnTests
    {
        private const double StepSeconds = 1.0;

        private static readonly Id ActorA = new Id("unit.et_a");
        private static readonly Id ActorB = new Id("unit.et_b");

        private static readonly IReadOnlyDictionary<string, RngStreamState> NoSeeds =
            new Dictionary<string, RngStreamState>(StringComparer.Ordinal);

        private static ReplayRecorder Began(ulong masterSeed = 0UL)
        {
            var recorder = new ReplayRecorder(StepSeconds);
            recorder.BeginRecording(masterSeed, NoSeeds);
            return recorder;
        }

        private static ReplayData RoundTrip(ReplayData data)
        {
            var text = JsonWriter.Write(data.ToJson());
            return ReplayData.FromJson((JsonObject)JsonReader.Parse(text));
        }

        private static ReplayInputRecord Input(long tick, Id actor, string kind) =>
            new ReplayInputRecord(tick, actor, kind, new JsonObjectBuilder().Build());

        // ---- 往返 -----------------------------------------------------------------

        [Fact]
        public void RecordEndTurn_ExportContainsRecordsInCallOrder()
        {
            var recorder = Began();
            var script = new[] { (Tick: 3L, Actor: ActorA), (Tick: 5L, Actor: ActorB), (Tick: 8L, Actor: ActorA) };
            foreach (var (tick, actor) in script)
            {
                recorder.RecordEndTurn(tick, actor);
            }

            var data = recorder.Export();

            Assert.Equal(script.Length, data.EndTurns.Count);
            for (var i = 0; i < script.Length; i++)
            {
                Assert.Equal(script[i].Tick, data.EndTurns[i].Tick);
                Assert.Equal(script[i].Actor, data.EndTurns[i].ActorId);
            }
        }

        [Fact]
        public void EndTurns_RecordToJsonAndBack_PreservesEveryRecord()
        {
            var recorder = Began();
            var script = new[] { (Tick: 1L, Actor: ActorA), (Tick: 2L, Actor: ActorB), (Tick: 2L, Actor: ActorA) };
            foreach (var (tick, actor) in script)
            {
                recorder.RecordEndTurn(tick, actor);
            }

            var original = recorder.Export();
            var roundTripped = RoundTrip(original);

            Assert.Equal(original.EndTurns.Count, roundTripped.EndTurns.Count);
            for (var i = 0; i < original.EndTurns.Count; i++)
            {
                Assert.Equal(original.EndTurns[i].Tick, roundTripped.EndTurns[i].Tick);
                Assert.Equal(original.EndTurns[i].ActorId, roundTripped.EndTurns[i].ActorId);
            }

            Assert.Equal(original.FormatVersion, roundTripped.FormatVersion);
        }

        [Fact]
        public void EndTurns_WrittenTextIsStable_ReserializingTheRoundTripGivesIdenticalText()
        {
            var recorder = Began();
            recorder.RecordEndTurn(4, ActorB);
            recorder.RecordEndTurn(9, ActorA);

            var first = JsonWriter.Write(recorder.Export().ToJson());
            var second = JsonWriter.Write(ReplayData.FromJson((JsonObject)JsonReader.Parse(first)).ToJson());

            Assert.Equal(first, second);
        }

        [Fact]
        public void EndTurnRecord_HugeTick_RoundTripsExactly()
        {
            const long hugeTick = 9007199254740993L; // 2^53 + 1：经 double 中转会丢精度。
            var recorder = Began();
            recorder.RecordEndTurn(hugeTick, ActorA);

            var text = JsonWriter.Write(recorder.Export().ToJson());
            Assert.Contains(hugeTick.ToString(System.Globalization.CultureInfo.InvariantCulture), text);

            var roundTripped = ReplayData.FromJson((JsonObject)JsonReader.Parse(text));

            Assert.Equal(hugeTick, Assert.Single(roundTripped.EndTurns).Tick);
            Assert.Equal(hugeTick, roundTripped.TickCount); // RecordEndTurn 同时抬高总 tick 数。
        }

        [Fact]
        public void EndTurnRecord_ToJsonFromJson_SingleRecord()
        {
            var record = new ReplayEndTurnRecord(17, ActorB);

            var back = ReplayEndTurnRecord.FromJson((JsonObject)JsonReader.Parse(JsonWriter.Write(record.ToJson())));

            Assert.Equal(17, back.Tick);
            Assert.Equal(ActorB, back.ActorId);
        }

        // ---- 与其它记录的相对顺序 -------------------------------------------------------

        [Fact]
        public void InterleavedInputsStepsAndEndTurns_EachListKeepsItsOwnInsertionOrder_ThroughRoundTrip()
        {
            // 一段"脚本化"的录制：输入、步、结束回合按 tick 交错；同一 tick 内也有多条，顺序即调用顺序。
            var ops = new List<(string Kind, long Tick, Id Actor)>
            {
                ("step", 1, ActorA),
                ("input", 2, ActorA),
                ("endturn", 2, ActorA),
                ("step", 3, ActorB),
                ("input", 4, ActorB),
                ("input", 4, ActorA),
                ("endturn", 4, ActorB),
                ("step", 5, ActorA),
                ("endturn", 6, ActorA),
            };

            var recorder = Began();
            foreach (var (kind, tick, actor) in ops)
            {
                switch (kind)
                {
                    case "step":
                        recorder.RecordStep(tick, SimStep.Discrete(actor, StepPhase.Act));
                        break;
                    case "input":
                        recorder.RecordInput(tick, Input(tick, actor, "move"));
                        break;
                    case "endturn":
                        recorder.RecordEndTurn(tick, actor);
                        break;
                }
            }

            var roundTripped = RoundTrip(recorder.Export());

            // 期望值由脚本按种类过滤得出。
            var expectedEndTurns = ops.Where(o => o.Kind == "endturn").ToList();
            var expectedInputs = ops.Where(o => o.Kind == "input").ToList();
            var expectedSteps = ops.Where(o => o.Kind == "step").ToList();

            Assert.Equal(expectedEndTurns.Select(o => (o.Tick, o.Actor)), roundTripped.EndTurns.Select(e => (e.Tick, e.ActorId)));
            Assert.Equal(expectedInputs.Select(o => (o.Tick, o.Actor)), roundTripped.Inputs.Select(i => (i.Tick, i.ActorId)));
            Assert.Equal(expectedSteps.Select(o => o.Tick), roundTripped.Steps.Select(s => s.Tick));

            // 三个列表互不串台：EndTurns 不会混进 Inputs/Steps，反之亦然。
            Assert.Equal(expectedEndTurns.Count, roundTripped.EndTurns.Count);
            Assert.Equal(expectedInputs.Count, roundTripped.Inputs.Count);
            Assert.Equal(expectedSteps.Count, roundTripped.Steps.Count);
        }

        [Fact]
        public void EndTurnsAreNotSortedByTick_TheRecordedOrderIsKept()
        {
            // 录制层不重排：调用方传入的顺序（即使 tick 不单调）原样保留，排序语义属于回放层。
            var recorder = Began();
            recorder.RecordEndTurn(9, ActorA);
            recorder.RecordEndTurn(2, ActorB);
            recorder.RecordEndTurn(5, ActorA);

            var roundTripped = RoundTrip(recorder.Export());

            Assert.Equal(new long[] { 9, 2, 5 }, roundTripped.EndTurns.Select(e => e.Tick));
        }

        [Fact]
        public void SerializedJson_PlacesEndTurnsAfterInputsAndSteps_AsSeparateArray()
        {
            var recorder = Began();
            recorder.RecordInput(1, Input(1, ActorA, "move"));
            recorder.RecordStep(1, SimStep.Continuous(StepSeconds));
            recorder.RecordEndTurn(1, ActorA);

            var json = recorder.Export().ToJson();
            var keys = json.Select(p => p.Key).ToList();

            Assert.True(keys.IndexOf("inputs") < keys.IndexOf("steps"));
            Assert.True(keys.IndexOf("steps") < keys.IndexOf("end_turns"));
            Assert.Single((JsonArray)json["end_turns"]);
            Assert.Single((JsonArray)json["inputs"]);
            Assert.Single((JsonArray)json["steps"]);
        }

        // ---- tick 计数 -------------------------------------------------------------

        [Fact]
        public void RecordEndTurn_RaisesTickCountToItsTick_NeverLowersIt()
        {
            var recorder = Began();
            recorder.RecordInput(10, Input(10, ActorA, "move"));

            recorder.RecordEndTurn(4, ActorA);
            Assert.Equal(10, recorder.Export().TickCount); // 较小 tick 不拉低。

            recorder.RecordEndTurn(12, ActorA);
            Assert.Equal(12, recorder.Export().TickCount); // 较大 tick 抬高。
        }

        // ---- 生命周期与前置条件 -----------------------------------------------------------

        [Fact]
        public void RecordEndTurn_BeforeBeginRecording_Throws()
        {
            var recorder = new ReplayRecorder(StepSeconds);

            Assert.Throws<InvalidOperationException>(() => recorder.RecordEndTurn(1, ActorA));
        }

        [Fact]
        public void BeginRecording_AgainClearsPreviouslyRecordedEndTurns()
        {
            var recorder = Began();
            recorder.RecordEndTurn(3, ActorA);
            Assert.Single(recorder.Export().EndTurns);

            recorder.BeginRecording(0UL, NoSeeds);

            var data = recorder.Export();
            Assert.Empty(data.EndTurns);
            Assert.Equal(0, data.TickCount);
        }

        [Fact]
        public void Export_IsASnapshot_LaterRecordEndTurnDoesNotMutateEarlierExport()
        {
            var recorder = Began();
            recorder.RecordEndTurn(1, ActorA);
            var earlier = recorder.Export();

            recorder.RecordEndTurn(2, ActorB);

            Assert.Single(earlier.EndTurns);
            Assert.Equal(2, recorder.Export().EndTurns.Count);
        }

        [Fact]
        public void Export_WithoutAnyEndTurn_HasEmptyEndTurns_AndStillWritesEmptyArray()
        {
            var recorder = Began();
            recorder.RecordInput(1, Input(1, ActorA, "move"));

            var data = recorder.Export();
            var json = data.ToJson();

            Assert.Empty(data.EndTurns);
            Assert.Empty((JsonArray)json["end_turns"]);
        }

        // ---- 旧格式兼容 / 非法数据 ------------------------------------------------------

        [Fact]
        public void FromJson_DocumentWithoutEndTurnsField_YieldsEmptyEndTurns()
        {
            var recorder = Began();
            recorder.RecordEndTurn(1, ActorA);
            var withField = recorder.Export().ToJson();

            // 模拟 format_version < 3 的旧录像：没有 end_turns 字段。
            var builder = new JsonObjectBuilder();
            foreach (var pair in withField)
            {
                if (pair.Key != "end_turns")
                {
                    builder.Add(pair.Key, pair.Value);
                }
            }

            var parsed = ReplayData.FromJson(builder.Build());

            Assert.Empty(parsed.EndTurns);
        }

        [Fact]
        public void EndTurnRecord_FromJson_NullJson_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ReplayEndTurnRecord.FromJson(null!));
        }

        [Fact]
        public void EndTurnRecord_FromJson_NonIntegerTick_ThrowsFormatException()
        {
            var json = new JsonObjectBuilder()
                .Add("tick", new JsonNumber(1.5))
                .Add("actorId", new JsonString(ActorA.Value))
                .Build();

            Assert.Throws<FormatException>(() => ReplayEndTurnRecord.FromJson(json));
        }

        [Fact]
        public void ReplayData_ConstructedWithoutEndTurns_DefaultsToEmpty_NotNull()
        {
            var data = new ReplayData(NoSeeds, Array.Empty<ReplayInputRecord>(), StepSeconds, 0);

            Assert.NotNull(data.EndTurns);
            Assert.Empty(data.EndTurns);
        }
    }
}

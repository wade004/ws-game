using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Gameplay.Perf
{
    /// <summary>
    /// 性能基线测试（11_工程规范与测试.md 第 5、6 节"性能约定""性能基线、回放回归"）：固定场景
    /// （种子固定、N=200 单位混战）测 (a) 一次 tick 平均耗时、(b) 大规模空间查询耗时、(c) 存档
    /// 序列化耗时，三项都与 <c>perf_baseline.json</c> 记录的阈值（本机首次实测中位数 × 5）比较，
    /// 超阈值即失败；同时把实测值打印到测试输出，供慢机器排查"是否只是机器慢而非真回归"。
    /// <para>
    /// 判断记录（合成场景而非完整战斗管线）：本测试的"N 单位混战"场景用最小必要组件合成
    /// （<see cref="WorldSim"/> + 若干个模拟 O(N) 工作量的 <see cref="ITickPhaseHandler"/> +
    /// <see cref="StubSpatialQuery"/> 登记），不经 <c>RulesAssembly</c>/<c>GameplayAssembly</c> 的
    /// 完整技能/战斗/AI 管线——性能基线关心的是"每 tick 处理 N 个对象的量级开销"，不是"具体战斗
    /// 规则算出的数值是否正确"（后者由其余测试覆盖），合成场景能更精确地控制"每 tick 确实做了
    /// O(N) 工作"这一测量前提，不受具体游戏内容（技能数值、AI 优先级表分支）影响测量结果的稳定性。
    /// </para>
    /// </summary>
    [Trait("Category", "Perf")]
    public sealed class PerfBaselineTests
    {
        private const int UnitCount = 200;
        private const int WarmupTicks = 20;
        private const int SampleTicks = 200;
        private const int SpatialQuerySamples = 1000;
        private const ulong Seed = 20260905UL;

        private static PerfBaseline LoadBaseline()
        {
            var path = FindBaselineFilePath();
            var json = File.ReadAllText(path);
            var root = (JsonObject)JsonReader.Parse(json);
            return new PerfBaseline
            {
                TickMedianMs = ((JsonNumber)root["tick_median_ms"]).Value,
                TickThresholdMs = ((JsonNumber)root["tick_threshold_ms"]).Value,
                SpatialQueryMedianMs = ((JsonNumber)root["spatial_query_median_ms"]).Value,
                SpatialQueryThresholdMs = ((JsonNumber)root["spatial_query_threshold_ms"]).Value,
                SaveMedianMs = ((JsonNumber)root["save_median_ms"]).Value,
                SaveThresholdMs = ((JsonNumber)root["save_threshold_ms"]).Value,
            };
        }

        private static string FindBaselineFilePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空");
            return Path.Combine(dir, "perf_baseline.json");
        }

        private struct PerfBaseline
        {
            public double TickMedianMs;
            public double TickThresholdMs;
            public double SpatialQueryMedianMs;
            public double SpatialQueryThresholdMs;
            public double SaveMedianMs;
            public double SaveThresholdMs;
        }

        // -----------------------------------------------------------------
        // (a) 一次 tick 平均耗时
        // -----------------------------------------------------------------

        [Fact]
        public void TickCost_MedianOfSampledTicks_WithinBaselineThreshold()
        {
            var world = BuildWorldWithUnits(UnitCount, out _);

            for (var i = 0; i < WarmupTicks; i++)
            {
                world.Tick(SimStep.Continuous(1.0 / 60.0));
            }

            var samples = new List<double>(SampleTicks);
            var sw = new Stopwatch();
            for (var i = 0; i < SampleTicks; i++)
            {
                sw.Restart();
                world.Tick(SimStep.Continuous(1.0 / 60.0));
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            var median = Median(samples);
            var baseline = LoadBaseline();

            Assert.True(median <= baseline.TickThresholdMs,
                $"tick 中位耗时 {median:F4}ms 超过基线阈值 {baseline.TickThresholdMs:F4}ms" +
                $"（基线中位数 {baseline.TickMedianMs:F4}ms，见 perf_baseline.json）");
        }

        // -----------------------------------------------------------------
        // (b) 大规模目标查询耗时
        // -----------------------------------------------------------------

        [Fact]
        public void SpatialQueryCost_MedianOf1000Queries_WithinBaselineThreshold()
        {
            var spatial = new StubSpatialQuery();
            var rng = new Random((int)Seed);
            for (var i = 0; i < UnitCount; i++)
            {
                var id = new Id($"unit.perf_target_{i}");
                var pos = new Vec2(rng.NextDouble() * 100, rng.NextDouble() * 100);
                spatial.Register(id, pos, 0.5, Array.Empty<string>());
            }

            // 预热。
            for (var i = 0; i < 20; i++)
            {
                spatial.QueryRadius(new Vec2(50, 50), 30, QueryFilter.None);
            }

            var samples = new List<double>(SpatialQuerySamples);
            var sw = new Stopwatch();
            for (var i = 0; i < SpatialQuerySamples; i++)
            {
                var center = new Vec2((i % 100), (i * 7) % 100);
                sw.Restart();
                spatial.QueryRadius(center, 30, QueryFilter.None);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            var median = Median(samples);
            var baseline = LoadBaseline();

            Assert.True(median <= baseline.SpatialQueryThresholdMs,
                $"空间查询中位耗时 {median:F4}ms 超过基线阈值 {baseline.SpatialQueryThresholdMs:F4}ms" +
                $"（基线中位数 {baseline.SpatialQueryMedianMs:F4}ms，见 perf_baseline.json）");
        }

        // -----------------------------------------------------------------
        // (c) 存档序列化耗时
        // -----------------------------------------------------------------

        [Fact]
        public void SaveSerializationCost_MedianOfRepeatedSaves_WithinBaselineThreshold()
        {
            var fs = new StubFileSystem();
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.perf_baseline")), bus);
            saveSystem.RegisterPersistable(new SyntheticUnitsPersistable(UnitCount));

            var slot = new Id("slot.perf_baseline");
            var request = new SaveRequest(slot, "2026-09-05T00:00:00Z");

            for (var i = 0; i < 5; i++)
            {
                saveSystem.Save(request);
            }

            const int sampleCount = 50;
            var samples = new List<double>(sampleCount);
            var sw = new Stopwatch();
            for (var i = 0; i < sampleCount; i++)
            {
                sw.Restart();
                saveSystem.Save(request);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            var median = Median(samples);
            var baseline = LoadBaseline();

            Assert.True(median <= baseline.SaveThresholdMs,
                $"存档序列化中位耗时 {median:F4}ms 超过基线阈值 {baseline.SaveThresholdMs:F4}ms" +
                $"（基线中位数 {baseline.SaveMedianMs:F4}ms，见 perf_baseline.json）");
        }

        // -----------------------------------------------------------------
        // 帮助方法
        // -----------------------------------------------------------------

        private static double Median(List<double> samples)
        {
            var sorted = samples.OrderBy(x => x).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }

        private static WorldSim BuildWorldWithUnits(int count, out List<TestEntity> entities)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var list = new List<TestEntity>(count);
            var rng = new Random((int)Seed);

            for (var i = 0; i < count; i++)
            {
                var entity = new TestEntity(new Id($"perf.inst_{i}"))
                {
                    Position = new Vec2(rng.NextDouble() * 100, rng.NextDouble() * 100),
                };
                world.AddEntity(entity);
                list.Add(entity);
            }

            entities = list;

            // 模拟每 tick 对全部单位做 O(N) 工作（近邻距离和/简单状态刷新），代表混战场景下典型的
            // 逐单位遍历开销——不引入真实战斗规则，只保证"确有 O(N) 工作被执行"这一测量前提。
            world.RegisterPhaseHandler(TickPhase.CombatResolution, new SyntheticWorkloadHandler());

            return world;
        }

        private sealed class TestEntity : Entity
        {
            public TestEntity(Id entityId) : base(entityId, mapId: default)
            {
            }

            public override string Kind => "perf_test_unit";
        }

        private sealed class SyntheticWorkloadHandler : ITickPhaseHandler
        {
            public void Execute(SimStep step, IWorldSim world)
            {
                var entities = world.QueryEntities(new EntityFilter());
                double acc = 0;
                for (var i = 0; i < entities.Count; i++)
                {
                    acc += entities[i].Position.X * entities[i].Position.Y;
                }

                // 避免 JIT 把整段计算当死代码优化掉：写回一个不影响测量语义的只读局部。
                _ = acc;
            }
        }

        /// <summary>合成一份"N 个单位状态"的存档段，代表典型的批量单位存档负载（位置/生命值/一个
        /// 标签数组），供 (c) 项测量真实的 JSON 序列化耗时。</summary>
        private sealed class SyntheticUnitsPersistable : IPersistable
        {
            private readonly int _count;

            public SyntheticUnitsPersistable(int count)
            {
                _count = count;
            }

            public string SectionKey => "perf.synthetic_units";

            public JsonValue Save()
            {
                var array = new List<JsonValue>(_count);
                for (var i = 0; i < _count; i++)
                {
                    array.Add(new JsonObjectBuilder()
                        .Add("id", new JsonString($"unit.perf_{i}"))
                        .Add("x", new JsonNumber(i * 1.5))
                        .Add("y", new JsonNumber(i * 0.5))
                        .Add("hp", new JsonNumber(100 - (i % 100)))
                        .Add("hp_max", new JsonNumber(100))
                        .Add("tags", new JsonArray(new JsonValue[] { new JsonString("tag.a"), new JsonString("tag.b") }))
                        .Build());
                }
                return new JsonArray(array);
            }

            public void Load(JsonValue data)
            {
                // 性能基线测试不需要读档往返。
            }
        }
    }
}

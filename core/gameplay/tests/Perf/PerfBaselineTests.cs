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
using Core.Rules.Common;
using Xunit;
using Tests.Gameplay.EndToEnd;

namespace Tests.Gameplay.Perf
{
    /// <summary>
    /// 性能基线测试（11_工程规范与测试.md 第 5、6 节"性能约定""性能基线、回放回归"）：固定场景
    /// （种子固定、N=200 单位混战）测 (a) 一次 tick 平均耗时（合成场景 + 完整管线两条，见判断
    /// 记录）、(b) 大规模空间查询耗时、(c) 存档序列化耗时，均与 <c>perf_baseline.json</c> 记录的
    /// 阈值（本机首次实测中位数 × 5）比较，超阈值即失败；同时把实测值打印到测试输出，供慢机器
    /// 排查"是否只是机器慢而非真回归"。
    /// <para>
    /// 判断记录（合成场景 + 完整管线两条 tick 用例并存，收边任务补齐）：
    /// <see cref="TickCost_MedianOfSampledTicks_WithinBaselineThreshold"/>（合成场景）用最小必要
    /// 组件合成（<see cref="WorldSim"/> + 若干个模拟 O(N) 工作量的 <see cref="ITickPhaseHandler"/> +
    /// <see cref="StubSpatialQuery"/> 登记），不经 <c>RulesAssembly</c>/<c>GameplayAssembly</c> 的
    /// 完整技能/战斗/AI 管线，作为"纯 tick 循环开销、不受具体游戏内容影响"的对照基准，继续保留；
    /// <see cref="TickCost_FullPipeline_MedianOfSampledTicks_WithinBaselineThreshold"/>（完整管线）
    /// 改用 <c>Tests.Gameplay.EndToEnd.GameWorldFixture</c> 装配的真实 <c>GameplayAssembly</c>
    /// （N=200 单位，含 AI 决策/施法管线/战斗结算），衡量"真实游戏一次 tick 的量级开销"——两者互为
    /// 补充：合成场景数值更稳定、便于隔离"tick 循环骨架本身"的回归，完整管线数值更贴近真实游戏
    /// 但会随游戏内容（技能数值、AI 优先级表分支）的演进而波动，两条基线各自独立，互不替代。
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
                FullPipelineTickMedianMs = ((JsonNumber)root["full_pipeline_tick_median_ms"]).Value,
                FullPipelineTickThresholdMs = ((JsonNumber)root["full_pipeline_tick_threshold_ms"]).Value,
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
            public double FullPipelineTickMedianMs;
            public double FullPipelineTickThresholdMs;
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
        // (a') 一次 tick 平均耗时——完整管线（收边任务补齐：GameWorldFixture 装配的真实
        // GameplayAssembly，N=200 单位含 AI 与技能，见类型判断记录"合成场景 vs 完整管线"）。
        // 上面的 TickCost_* 用例（合成场景）保留作对照，见该用例判断记录。
        // -----------------------------------------------------------------

        [Fact]
        public void TickCost_FullPipeline_MedianOfSampledTicks_WithinBaselineThreshold()
        {
            var fx = BuildFullPipelineWorld(creatureCount: UnitCount - 1);

            for (var i = 0; i < WarmupTicks; i++)
            {
                TopOffPlayerHealth(fx);
                fx.World.Tick(SimStep.Continuous(1.0 / 60.0));
            }

            var samples = new List<double>(SampleTicks);
            var sw = new Stopwatch();
            for (var i = 0; i < SampleTicks; i++)
            {
                TopOffPlayerHealth(fx); // 见判断记录"保持玩家存活"：不计入测量窗口。
                sw.Restart();
                fx.World.Tick(SimStep.Continuous(1.0 / 60.0));
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            var median = Median(samples);
            var baseline = LoadBaseline();

            Assert.True(median <= baseline.FullPipelineTickThresholdMs,
                $"完整管线 tick 中位耗时 {median:F4}ms 超过基线阈值 {baseline.FullPipelineTickThresholdMs:F4}ms" +
                $"（基线中位数 {baseline.FullPipelineTickMedianMs:F4}ms，见 perf_baseline.json）");
        }

        /// <summary>
        /// 装配一整套真实 <c>GameplayAssembly</c>（<see cref="GameWorldFixture"/>，与
        /// <c>EndToEndTests</c> 同一夹具），额外生成 <paramref name="creatureCount"/> 个
        /// <c>creature.sample_beast</c>（自带 <c>ai_behavior_ref</c>/<c>rotation</c>，见
        /// <c>data/_sample/creature/creature.template.json</c>），散布在玩家周围
        /// <c>ai.profile.sample_melee.perception_radius</c>（20）以内的一个螺旋阵列上，确保每个
        /// 都能感知到玩家并按 <c>ai.rotation.sample_melee</c> 释放技能（<c>skill.sample_strike</c>/
        /// <c>skill.sample_burn</c>）——真正走完 AI 决策 + 施法管线 + 战斗结算的完整 O(N) 路径。
        /// <para>
        /// 判断记录（不做"两派互殴"）：<c>data/_sample</c> 的阵营关系只声明了
        /// <c>fac.player</c> 与 <c>fac.wildlife</c> 互为敌对（见 <c>fac.reaction_matrix</c>），
        /// 野生生物彼此中立——要让全部 N 个生物同时有战斗可打，最简单的现成数据配置就是让它们全部
        /// 敌视同一个玩家，而不是新增一套"两派野生生物互相敌对"的示例数据（超出本次性能任务范围）。
        /// 副作用是玩家会被 <paramref name="creatureCount"/> 个生物围攻，若不干预会在采样窗口早期
        /// 死亡（见 <see cref="TopOffPlayerHealth"/> 判断记录）。
        /// </para>
        /// </summary>
        private static GameWorldFixture.Fixture BuildFullPipelineWorld(int creatureCount)
        {
            var fx = GameWorldFixture.Build();

            var perTurn = 2.0 * Math.PI / 12.0;
            for (var i = 0; i < creatureCount; i++)
            {
                // 螺旋阵列：半径从 2 缓慢增长到 <perception_radius（20），角度均匀分布，保证 N 个
                // 生物两两不重叠、且全部落在玩家的 AI 感知半径内。
                var radius = 2.0 + (18.0 * i / Math.Max(1, creatureCount - 1));
                var angle = i * perTurn;
                var position = new Vec2(radius * Math.Cos(angle), radius * Math.Sin(angle));
                fx.Gameplay.Carriers.Creatures.Spawn(GameWorldFixture.CreatureBeast, GameWorldFixture.MapId, position, facing: 0);
            }

            // 生成阶段只 Enqueue 了 entity.created；EntitySpatialSyncHost 要处理完这些事件才能把
            // 全部生物登记进空间索引，AI 的"感知范围内敌对单位"查询才查得到它们。
            fx.Bus.DispatchPending();

            return fx;
        }

        /// <summary>
        /// 判断记录（保持玩家存活）：<paramref name="fx"/> 的玩家会被 <see cref="BuildFullPipelineWorld"/>
        /// 生成的全部生物围攻，若放任战斗自然发展，玩家会在采样窗口的前几个 tick 内死亡——死亡后
        /// AI 找不到有效目标，后续 tick 的施法/战斗结算工作量会显著低于"N 个单位持续混战"这一
        /// 测量目标本身，采样值会失真为"AI 徒劳搜索目标"而非"完整管线在真实负载下的开销"。本方法
        /// 在每次采样前把玩家生命值打满（不计入本次采样的计时窗口，见调用点），让围攻在整个采样
        /// 窗口内持续发生，AI 决策间隔（0.5s）内仍会不断评估 Rotation 条件并释放技能。</summary>
        private static void TopOffPlayerHealth(GameWorldFixture.Fixture fx)
        {
            var powers = fx.Gameplay.Carriers.Rules.Powers;
            if (!powers.HasPower(GameWorldFixture.PlayerId, WellKnownPowers.Health))
            {
                return;
            }

            var missing = powers.GetPowerMax(GameWorldFixture.PlayerId, WellKnownPowers.Health) -
                          powers.GetPower(GameWorldFixture.PlayerId, WellKnownPowers.Health);
            if (missing > 0)
            {
                powers.ModifyPower(GameWorldFixture.PlayerId, WellKnownPowers.Health, missing, GameWorldFixture.PlayerId);
            }
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

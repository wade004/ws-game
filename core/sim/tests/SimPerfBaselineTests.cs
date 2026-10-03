using System;
using System.Diagnostics;
using System.IO;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Tests.Gameplay.Perf;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Sim
{
    /// <summary>
    /// 仿真装配根的性能基线用例（T-M8，2026-10-01 测试覆盖第四批拍板：墙钟阈值用例改到 Perf 类别，
    /// 断言改为相对基线倍数，与 <c>core/gameplay/tests/Perf</c> 现有 Perf 诊断行同口径，不只输出）。
    /// <para>
    /// 取代原 <c>FightRunnerTests.Probe_BuildTiming_WellUnder50MsThreshold</c>（绝对 <c>minMs &lt; 100</c>）：
    /// <c>FightRunner</c> 判断记录"隔离方案"（每场战斗新建一整套世界）成立的前提是
    /// <see cref="Core.Sim.HeadlessWorldBuilder.Build"/> 的固有开销是个位数到十几毫秒量级；绝对阈值在慢机/忙机上误报
    /// （1.88.0 发布时并行执行误报两次），改成"基线最小耗时 × 5 × 机器系数"后，机器慢只放大阈值，
    /// 真回归（耗时相对基线成倍增长）仍会失败。
    /// </para>
    /// <para>
    /// 口径（沿用 <c>Tests.Gameplay.Perf.PerfBaselineTests</c>，见该目录 README"机器归一化口径"）：
    /// <c>factor = clamp(本机 PerfMachineCalibration.ReferenceMs ÷ baseline.reference_workload_ms, 1, calibration_factor_max)</c>，
    /// <c>measured &lt;= threshold × factor</c>；参考负载实现经 <c>Tests.Sim.csproj</c> 链接同一份源文件，不复制。
    /// 判定量沿用原用例的"逐次最小值"（并行负载下平均值量到的是调度等待而不是 Build 自身开销），输出行里的
    /// <c>median=</c> 字段承载的就是这个最小值——字段名保持 <c>median</c> 是为了让 <c>toolchain/_gate_test_floors.ps1</c>
    /// 的 <c>Get-PerfDiagnosticLines</c> 正则（<c>perf \S+_WithinBaselineThreshold median=.*factor=.*reference=</c>）
    /// 一并收进门禁日志。
    /// </para>
    /// <para>
    /// 判断记录（2026-10-03，门禁连续三次被本用例偶发红挡住，纯测试缺陷、产品无回归）：此前参考负载在进程内一次性缓存，
    /// 与"20 次 Build 取最小"不在同一时刻采样，负载起伏时系数不描述被测量值当时的机器状态；同进程里其它测试类并行
    /// 运行也让 Build（分配密集）比纯计算的参考负载慢得更多。根治两处：① 参考负载与 Build <b>交错采样成相邻样本对</b>、逐对归一化取最佳一对
    /// （纯函数在 <see cref="PerfNormalizedDecision"/>，原理与复现用例在 <c>SimPerfNormalizedDecisionTests</c>）；
    /// ② 本类放进禁用并行化的 <c>Perf serial</c> 集合，xunit 在全部并行集合跑完之后才单独运行它，同进程内无别的用例争用。
    /// 阈值、系数上限、基线文件均不变——真回归（耗时相对基线成倍增长而参考负载不变）仍然失败。
    /// </para>
    /// </summary>
    [Trait("Category", "Perf")]
    [Collection(PerfSerialCollection.Name)]
    public sealed class SimPerfBaselineTests
    {
        /// <summary>交错采样轮数：每轮各采一次参考负载与一次 Build，组成一对。</summary>
        private const int Rounds = 30;
        private const int WarmupIterations = 3;

        private readonly ITestOutputHelper _output;

        public SimPerfBaselineTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string BaselineFilePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空");
            return Path.Combine(dir, "perf_baseline.json");
        }

        private static Core.Sim.HeadlessWorld BuildOnce(System.Collections.Generic.IReadOnlyList<Core.Foundation.DataRegistry.IDataSource> dataSources, int i) =>
            Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = dataSources,
                Seed = (ulong)(9000 + i),
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
            });

        [Fact]
        public void HeadlessBuild_MinTiming_WithinBaselineThreshold()
        {
            var root = (JsonObject)JsonReader.Parse(File.ReadAllText(BaselineFilePath()));
            var baselineMinMs = ((JsonNumber)root["headless_build_min_ms"]).Value;
            var thresholdMs = ((JsonNumber)root["headless_build_threshold_ms"]).Value;
            var baselineReferenceMs = ((JsonNumber)root["reference_workload_ms"]).Value;
            var factorMax = ((JsonNumber)root["calibration_factor_max"]).Value;

            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();
            for (var i = 0; i < WarmupIterations; i++)
            {
                BuildOnce(dataSources, i);
            }

            // 预热参考负载（JIT），不计入采样。
            PerfMachineCalibration.MeasureWorkloadOnceMs();

            var sw = new Stopwatch();
            var seed = WarmupIterations;
            var pairs = PerfNormalizedDecision.SampleInterleavedPairs(
                Rounds,
                PerfMachineCalibration.MeasureWorkloadOnceMs,
                () =>
                {
                    sw.Restart();
                    BuildOnce(dataSources, seed++);
                    sw.Stop();
                    return sw.Elapsed.TotalMilliseconds;
                });

            var decision = PerfNormalizedDecision.EvaluateBestPair(pairs, thresholdMs, baselineReferenceMs, factorMax);
            var minMs = decision.MeasuredMs;
            var referenceMs = decision.ReferenceMs;
            var factor = decision.Factor;
            var effectiveThreshold = decision.EffectiveThresholdMs;

            _output.WriteLine(
                $"perf HeadlessBuild_MinTiming_WithinBaselineThreshold median={minMs:F4} threshold={thresholdMs:F4} " +
                $"factor={factor:F2} effective_threshold={effectiveThreshold:F4} reference={referenceMs:F4}");

            Assert.True(decision.Passed,
                $"HeadlessWorldBuilder.Build 最小耗时 {minMs:F4}ms 超过机器归一化后的阈值 {effectiveThreshold:F4}ms" +
                $"（基线阈值 {thresholdMs:F4}ms × 机器系数 {factor:F2}，基线最小耗时 {baselineMinMs:F4}ms，" +
                $"本机参考负载 {referenceMs:F4}ms，基线机参考负载 {baselineReferenceMs:F4}ms，见 perf_baseline.json）");
        }
    }

    /// <summary>禁用并行化的性能用例集合：集合内用例要等所有并行集合跑完才逐个运行，进程内没有别的测试与之争用
    /// CPU/内存/GC（原因见 <see cref="SimPerfBaselineTests"/> 判断记录）。</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class PerfSerialCollection
    {
        public const string Name = "Perf serial";
    }
}

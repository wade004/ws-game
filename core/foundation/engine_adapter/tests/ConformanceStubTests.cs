// ConformanceStubTests：对 adapters/stub/StubEngine 逐场景跑 adapters/conformance 下的
// 契约一致性场景（见 architecture/11_工程规范与测试.md 第 6 节、adapters/conformance/README.md）。
//
// 每个接口一个 [Theory]（用场景名做 MemberData，测试结果里能看到具体是哪条场景失败），方法体
// 按名字取回场景、构造一个每次测试都全新的 StubEngine 与对应实现的 ConformanceContext 钩子，
// 用 Pump 展开场景体（IEnumerator，可能含嵌套 yield，见 ConformanceContext.cs 判断记录），
// 断言异常统一转译成 xUnit 的失败（见 XunitConformanceAssert）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Adapters.Conformance;
using Adapters.Stub;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>IConformanceAssert 的 xUnit 落地：失败即触发 Xunit.Assert 系的标准失败异常，
    /// Skip 请求转译为 ConformanceSkipException，由调用方（本文件各 [Theory] 方法）捕获后
    /// 判为通过（xUnit 2.5.3 没有运行期动态跳过 API，见 adapters/conformance/README.md
    /// 判断记录 6）。</summary>
    internal sealed class XunitConformanceAssert : IConformanceAssert
    {
        public void True(bool condition, string message) => Assert.True(condition, message);

        public void False(bool condition, string message) => Assert.False(condition, message);

        public void Equal<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
            {
                Assert.Fail($"{message}\n期望: {Describe(expected)}\n实际: {Describe(actual)}");
            }
        }

        public void NotEqual<T>(T notExpected, T actual, string message)
        {
            if (Equals(notExpected, actual))
            {
                Assert.Fail($"{message}\n不应等于: {Describe(notExpected)}\n实际: {Describe(actual)}");
            }
        }

        public void Throws<TException>(Action action, string message) where TException : Exception
        {
            var ex = Record.Exception(action);
            if (ex == null)
            {
                Assert.Fail($"{message}\n期望抛出 {typeof(TException).Name}，但未抛出任何异常");
                return;
            }

            if (!(ex is TException))
            {
                Assert.Fail($"{message}\n期望抛出 {typeof(TException).Name}，实际抛出 {ex.GetType().Name}: {ex.Message}");
            }
        }

        public void DoesNotThrow(Action action, string message)
        {
            var ex = Record.Exception(action);
            if (ex != null)
            {
                Assert.Fail($"{message}\n不应抛出异常，实际抛出 {ex.GetType().Name}: {ex.Message}");
            }
        }

        public void IsNull(object? value, string message) => Assert.True(value == null, message);

        public void NotNull(object? value, string message) => Assert.True(value != null, message);

        public void Skip(string reason) => throw new ConformanceSkipException(reason);

        private static string Describe<T>(T value) => value == null ? "<null>" : value.ToString() ?? "<null>";
    }

    public class ConformanceStubTests
    {
        /// <summary>驱动一个场景到结束：不含 yield 的场景在第一次 MoveNext 内就跑完；
        /// 含 `yield return ctx.AdvanceTime(...)` 的场景，其产出的嵌套 IEnumerator 需要递归展开
        /// （Unity 侧由引擎协程调度器原生展开，xUnit 侧没有引擎协程调度器，本方法手写等价逻辑，
        /// 见 adapters/conformance/ConformanceContext.cs 顶部判断记录）。</summary>
        private static void Pump(IEnumerator it)
        {
            while (it.MoveNext())
            {
                if (it.Current is IEnumerator nested)
                {
                    Pump(nested);
                }
            }
        }

        private static readonly AsyncLocal<List<(string Scenario, string Reason)>?> SkipSink =
            new AsyncLocal<List<(string, string)>?>();

        private static void Run<TImpl>(IReadOnlyList<ConformanceScenario<TImpl>> all, string scenarioName, TImpl impl, ConformanceContext ctx)
        {
            var scenario = all.First(s => s.Name == scenarioName);
            var assert = new XunitConformanceAssert();
            try
            {
                Pump(scenario.Run(impl, assert, ctx));
            }
            catch (ConformanceSkipException skip)
            {
                // 见类型顶部判断记录：xUnit 2.5.3 没有运行期动态跳过 API，退化为记录一条诊断输出
                // 后判为通过；桩实现按设计不应该真正触发 Skip（全部钩子桩侧都能提供），命中这里
                // 通常意味着某个钩子在桩侧被漏配置，值得在测试输出里留痕。
                Console.WriteLine($"[Skip] {scenarioName}: {skip.Message}");
                // 测试覆盖第四批 T-M10：被跳过的场景此前只剩一行控制台输出、报告里仍显示“通过”，
                // 跳过数不可见。这里额外把 (场景名, 原因) 记进当前调用链的收集器（仅
                // StubSkippedScenarios_EqualTheKnownList 会装上收集器），由那条用例断言桩侧 Skip 集合
                // 恰等于已知清单——多出一个（某钩子被漏配）或少一个（已知跳过被悄悄修好却没更新清单）都红。
                SkipSink.Value?.Add((scenarioName, skip.Message));
            }
        }

        private static ConformanceContext NewStubContext(StubClock? clock = null, StubWindow? window = null, StubFileSystem? fileSystem = null)
        {
            var ctx = new ConformanceContext();
            if (clock != null)
            {
                ctx.AdvanceTime = seconds =>
                {
                    clock.Advance(seconds);
                    return ConformanceContext.EmptyStep();
                };
            }
            if (window != null)
            {
                ctx.TriggerWindowClose = window.RequestCloseForTest;
            }
            if (fileSystem != null)
            {
                ctx.SimulateNextWriteFailure = fileSystem.FailNextWrite;
            }
            ctx.SupportsRenderer3D = true; // 桩实现按正常路径工作，见 StubRenderer3D。
            return ctx;
        }

        public static IEnumerable<object[]> WindowNames() => WindowScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(WindowNames))]
        public void Window(string scenarioName)
        {
            var window = new StubWindow();
            Run(WindowScenarios.All, scenarioName, window, NewStubContext(window: window));
        }

        public static IEnumerable<object[]> ClockNames() => ClockScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(ClockNames))]
        public void Clock(string scenarioName)
        {
            var clock = new StubClock();
            Run(ClockScenarios.All, scenarioName, clock, NewStubContext(clock: clock));
        }

        public static IEnumerable<object[]> Renderer2DNames() => Renderer2DScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(Renderer2DNames))]
        public void Renderer2D(string scenarioName)
        {
            var renderer = new StubRenderer2D();
            Run(Renderer2DScenarios.All, scenarioName, renderer, NewStubContext());
        }

        public static IEnumerable<object[]> AudioNames() => AudioScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(AudioNames))]
        public void Audio(string scenarioName)
        {
            var audio = new StubAudio();
            Run(AudioScenarios.All, scenarioName, audio, NewStubContext());
        }

        public static IEnumerable<object[]> InputNames() => InputScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(InputNames))]
        public void Input(string scenarioName)
        {
            var input = new StubInput();
            Run(InputScenarios.All, scenarioName, input, NewStubContext());
        }

        public static IEnumerable<object[]> FileSystemNames() => FileSystemScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(FileSystemNames))]
        public void FileSystem(string scenarioName)
        {
            var fileSystem = new StubFileSystem();
            Run(FileSystemScenarios.All, scenarioName, fileSystem, NewStubContext(fileSystem: fileSystem));
        }

        public static IEnumerable<object[]> ResourceLoaderNames() => ResourceLoaderScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(ResourceLoaderNames))]
        public void ResourceLoader(string scenarioName)
        {
            var loader = new StubResourceLoader();
            Run(ResourceLoaderScenarios.All, scenarioName, loader, NewStubContext());
        }

        public static IEnumerable<object[]> Navigation2DNames() => Navigation2DScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(Navigation2DNames))]
        public void Navigation2D(string scenarioName)
        {
            var nav = new StubNavigation2D();
            Run(Navigation2DScenarios.All, scenarioName, nav, NewStubContext());
        }

        public static IEnumerable<object[]> SpatialQueryNames() => SpatialQueryScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(SpatialQueryNames))]
        public void SpatialQuery(string scenarioName)
        {
            var query = new StubSpatialQuery();
            Run(SpatialQueryScenarios.All, scenarioName, query, NewStubContext());
        }

        public static IEnumerable<object[]> UISurfaceNames() => UISurfaceScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(UISurfaceNames))]
        public void UISurface(string scenarioName)
        {
            var ui = new StubUISurface();
            Run(UISurfaceScenarios.All, scenarioName, ui, NewStubContext());
        }

        public static IEnumerable<object[]> PlatformNames() => PlatformScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(PlatformNames))]
        public void Platform(string scenarioName)
        {
            var platform = new StubPlatform();
            Run(PlatformScenarios.All, scenarioName, platform, NewStubContext());
        }

        public static IEnumerable<object[]> Renderer3DNames() => Renderer3DScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(Renderer3DNames))]
        public void Renderer3D(string scenarioName)
        {
            var renderer = new StubRenderer3D();
            var ctx = NewStubContext();
            // H5b 根治新增：桩侧同步立即判定完成，见 StubRenderer3D.CompleteAnimForTest 判断记录。
            ctx.CompleteNonLoopAnim = handle =>
            {
                renderer.CompleteAnimForTest(handle);
                return ConformanceContext.EmptyStep();
            };
            Run(Renderer3DScenarios.All, scenarioName, renderer, ctx);
        }

        public static IEnumerable<object[]> CameraNames() => CameraScenarios.All.Select(s => new object[] { s.Name });
        [Theory]
        [MemberData(nameof(CameraNames))]
        public void Camera(string scenarioName)
        {
            var camera = new StubCamera();
            Run(CameraScenarios.All, scenarioName, camera, NewStubContext());
        }

        /// <summary>桩侧已知会 Skip 的场景清单（场景名）：桩的 <see cref="StubNavigation2D"/> 是直线导航，
        /// 不做绕障规划，两条要求“绕障路径”的场景在桩上恒 Skip（原因见
        /// <c>Navigation2DScenarios</c> 两处 <c>assert.Skip</c> 与 StubNavigation2D 类型顶部判断记录）。
        /// 其余三处 Skip（写入失败模拟、阻挡版本追踪、窗口关闭触发）桩侧钩子齐全，不应触发。</summary>
        private static readonly string[] KnownStubSkips =
        {
            "FindPath_双矩形拐角工况_每段Raycast均不受阻",
            "FindPath_薄墙窄于采样间距存在绕路_每段Raycast均不受阻",
        };

        /// <summary>每个接口一行：(接口名, 场景名枚举, 对单个场景名执行对应 [Theory] 方法体)。</summary>
        private static IEnumerable<(string Interface, IEnumerable<string> Names, Action<ConformanceStubTests, string> Run)> AllInterfaceRuns() =>
            new (string, IEnumerable<string>, Action<ConformanceStubTests, string>)[]
            {
                ("Window", WindowScenarios.All.Select(s => s.Name), (t, n) => t.Window(n)),
                ("Clock", ClockScenarios.All.Select(s => s.Name), (t, n) => t.Clock(n)),
                ("Renderer2D", Renderer2DScenarios.All.Select(s => s.Name), (t, n) => t.Renderer2D(n)),
                ("Audio", AudioScenarios.All.Select(s => s.Name), (t, n) => t.Audio(n)),
                ("Input", InputScenarios.All.Select(s => s.Name), (t, n) => t.Input(n)),
                ("FileSystem", FileSystemScenarios.All.Select(s => s.Name), (t, n) => t.FileSystem(n)),
                ("ResourceLoader", ResourceLoaderScenarios.All.Select(s => s.Name), (t, n) => t.ResourceLoader(n)),
                ("Navigation2D", Navigation2DScenarios.All.Select(s => s.Name), (t, n) => t.Navigation2D(n)),
                ("SpatialQuery", SpatialQueryScenarios.All.Select(s => s.Name), (t, n) => t.SpatialQuery(n)),
                ("UISurface", UISurfaceScenarios.All.Select(s => s.Name), (t, n) => t.UISurface(n)),
                ("Platform", PlatformScenarios.All.Select(s => s.Name), (t, n) => t.Platform(n)),
                ("Renderer3D", Renderer3DScenarios.All.Select(s => s.Name), (t, n) => t.Renderer3D(n)),
                ("Camera", CameraScenarios.All.Select(s => s.Name), (t, n) => t.Camera(n)),
            };

        [Fact]
        public void StubSkippedScenarios_EqualTheKnownList_NoMoreNoFewer()
        {
            var skips = new List<(string Scenario, string Reason)>();
            SkipSink.Value = skips;
            try
            {
                var self = new ConformanceStubTests();
                foreach (var (_, names, run) in AllInterfaceRuns())
                {
                    foreach (var name in names)
                    {
                        run(self, name);
                    }
                }
            }
            finally
            {
                SkipSink.Value = null;
            }

            var actual = skips.Select(s => s.Scenario).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var expected = KnownStubSkips.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
            // 防止清单里的名字改名后与场景脱节而“恰好都为空”：已知跳过必须是真实存在的场景。
            var allNames = AllInterfaceRuns().SelectMany(r => r.Names).ToHashSet(StringComparer.Ordinal);
            Assert.All(KnownStubSkips, n => Assert.Contains(n, allNames));
            // 跳过原因必须是“桩不支持绕障路径规划”这一已知语义，而不是别的缺口借同名位置混入。
            Assert.All(skips, s => Assert.Contains("绕障", s.Reason));
        }
    }
}

#nullable enable
// ConformanceUnityTests：对 UnityEngineHost 各引擎适配层实现逐场景跑 adapters/conformance 下的
// 契约一致性场景（见该目录 README.md）。覆盖除 IFileSystem/IPlatform（放 Tests/Editor，见
// ConformanceUnityEditorTests.cs）之外的全部 11 个接口。
//
// 实例来源（见包 README 判断记录 4）：
//   - IClock/IAudio/ICamera/IResourceLoader —— 必须用 UnityEngineHost.Ensure() 共享单例，
//     因为其 Tick 系列方法只由该宿主的 Update/FixedUpdate 驱动，独立 new 出来的实例永远不会
//     被引擎循环推进。
//   - IRenderer2D/IRenderer3D/IInput/IUISurface —— 构造依赖 Transform/ResourceLoader 等，
//     改用共享单例，场景写法只做加法不做全局清空。
//   - IWindow/INavigation2D/ISpatialQuery —— 独立构造，避免场景内的 SetFullscreen/Clear()
//     之类操作波及同一次 -runTests 运行里其它用例的前置状态。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapters.Conformance;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>IConformanceAssert 的 NUnit 落地。与
    /// Tests/Editor/ConformanceUnityEditorTests.cs 里的同名类型逐字重复——两者分属
    /// Adapter.Unity.Tests.Runtime 与 Adapter.Unity.Tests.Editor 两个独立程序集
    /// （后者 includePlatforms 限定 Editor，前者不限定，不能互相引用），这个类型足够小、
    /// 足够稳定，重复一份比新增一个只为共享它而存在的第三个程序集更简单（同 UnityUISurface/
    /// UnityResourceLoader 两处各自维护一份 StripCategoryPrefix 的既有惯例）。</summary>
    internal sealed class NUnitConformanceAssert : IConformanceAssert
    {
        public void True(bool condition, string message) => NUnit.Framework.Assert.IsTrue(condition, message);

        public void False(bool condition, string message) => NUnit.Framework.Assert.IsFalse(condition, message);

        public void Equal<T>(T expected, T actual, string message) => NUnit.Framework.Assert.AreEqual(expected, actual, message);

        public void NotEqual<T>(T notExpected, T actual, string message) => NUnit.Framework.Assert.AreNotEqual(notExpected, actual, message);

        public void Throws<TException>(Action action, string message) where TException : Exception
        {
            Exception? caught = null;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            if (caught == null)
            {
                NUnit.Framework.Assert.Fail($"{message}\n期望抛出 {typeof(TException).Name}，但未抛出任何异常");
                return;
            }

            if (!(caught is TException))
            {
                NUnit.Framework.Assert.Fail($"{message}\n期望抛出 {typeof(TException).Name}，实际抛出 {caught.GetType().Name}: {caught.Message}");
            }
        }

        public void DoesNotThrow(Action action, string message)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                NUnit.Framework.Assert.Fail($"{message}\n不应抛出异常，实际抛出 {ex.GetType().Name}: {ex.Message}");
            }
        }

        public void IsNull(object? value, string message) => NUnit.Framework.Assert.IsNull(value, message);

        public void NotNull(object? value, string message) => NUnit.Framework.Assert.IsNotNull(value, message);

        public void Skip(string reason) => NUnit.Framework.Assert.Ignore(reason);
    }

    public sealed class ConformanceUnityTests : PlayModeTestBase
    {
        /// <summary>推进一次"时钟/帧"：真实等待 <paramref name="seconds"/> 秒的不缩放真实时间
        /// （<c>WaitForSecondsRealtime</c>），让 UnityEngineHost.Update/FixedUpdate 在这段真实时间内
        /// 自然运行若干次，从而真正推进 UnityClock 的帧回调/固定步累积器（见
        /// adapters/conformance/ConformanceContext.cs 顶部判断记录：Unity 没有任何同步 API 能在
        /// 方法调用内部伪造一次引擎帧，只能真的等）。</summary>
        private static IEnumerator AdvanceRealtime(double seconds)
        {
            yield return new WaitForSecondsRealtime((float)Math.Max(seconds, 0.0));
        }

        private static IEnumerator RunScenario<TImpl>(IReadOnlyList<ConformanceScenario<TImpl>> all, string scenarioName, TImpl impl, ConformanceContext ctx)
        {
            var scenario = all.First(s => s.Name == scenarioName);
            yield return scenario.Run(impl, new NUnitConformanceAssert(), ctx);
        }

        private static ConformanceContext NewContext() => new ConformanceContext();

        // ---------------------------------------------------------------------------------
        // IWindow —— 独立构造。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> WindowNames() => WindowScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Window([ValueSource(nameof(WindowNames))] string scenarioName)
        {
            var window = new UnityWindow();
            var ctx = NewContext();
            ctx.TriggerWindowClose = window.RequestCloseForTest;
            yield return RunScenario(WindowScenarios.All, scenarioName, window, ctx);
        }

        // ---------------------------------------------------------------------------------
        // IClock —— 必须用共享宿主实例（见类型顶部注释）。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> ClockNames() => ClockScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Clock([ValueSource(nameof(ClockNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            var ctx = NewContext();
            ctx.AdvanceTime = AdvanceRealtime;
            yield return RunScenario(ClockScenarios.All, scenarioName, (IClock)host.Clock, ctx);
        }

        // ---------------------------------------------------------------------------------
        // IRenderer2D —— 共享宿主实例。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> Renderer2DNames() => Renderer2DScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Renderer2D([ValueSource(nameof(Renderer2DNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            yield return RunScenario(Renderer2DScenarios.All, scenarioName, (IRenderer2D)host.Renderer2D, NewContext());
        }

        // ---------------------------------------------------------------------------------
        // IAudio —— 共享宿主实例。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> AudioNames() => AudioScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Audio([ValueSource(nameof(AudioNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            yield return RunScenario(AudioScenarios.All, scenarioName, (IAudio)host.Audio, NewContext());
        }

        // ---------------------------------------------------------------------------------
        // IInput —— 共享宿主实例。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> InputNames() => InputScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Input([ValueSource(nameof(InputNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            yield return RunScenario(InputScenarios.All, scenarioName, (IInput)host.Input, NewContext());
        }

        // ---------------------------------------------------------------------------------
        // IResourceLoader —— 必须用共享宿主实例（Tick 驱动后台加载完成回调）。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> ResourceLoaderNames() => ResourceLoaderScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator ResourceLoader([ValueSource(nameof(ResourceLoaderNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            var ctx = NewContext();
            ctx.AdvanceTime = AdvanceRealtime;
            yield return RunScenario(ResourceLoaderScenarios.All, scenarioName, (IResourceLoader)host.ResourceLoader, ctx);
        }

        // ---------------------------------------------------------------------------------
        // INavigation2D —— 独立构造。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> Navigation2DNames() => Navigation2DScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Navigation2D([ValueSource(nameof(Navigation2DNames))] string scenarioName)
        {
            var nav = new UnityNavigation2D();
            yield return RunScenario(Navigation2DScenarios.All, scenarioName, (INavigation2D)nav, NewContext());
        }

        // ---------------------------------------------------------------------------------
        // ISpatialQuery —— 独立构造（Clear() 不应波及共享单例）。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> SpatialQueryNames() => SpatialQueryScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator SpatialQuery([ValueSource(nameof(SpatialQueryNames))] string scenarioName)
        {
            var query = new UnitySpatialQuery();
            yield return RunScenario(SpatialQueryScenarios.All, scenarioName, (ISpatialQuery)query, NewContext());
        }

        // ---------------------------------------------------------------------------------
        // IUISurface —— 共享宿主实例。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> UISurfaceNames() => UISurfaceScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator UISurface([ValueSource(nameof(UISurfaceNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            yield return RunScenario(UISurfaceScenarios.All, scenarioName, (IUISurface)host.UISurface, NewContext());
        }

        // ---------------------------------------------------------------------------------
        // IRenderer3D —— 共享宿主实例；W6-B 收口，不再整体声明降级，走真实实现路径（见
        // UnityRenderer3D.cs 类型注释、Renderer3DScenarios.ModelId 现指向占位模型
        // model.placeholder_biped）。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> Renderer3DNames() => Renderer3DScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Renderer3D([ValueSource(nameof(Renderer3DNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            var ctx = NewContext();
            ctx.SupportsRenderer3D = true;
            yield return RunScenario(Renderer3DScenarios.All, scenarioName, (IRenderer3D)host.Renderer3D, ctx);
        }

        // ---------------------------------------------------------------------------------
        // ICamera —— 共享宿主实例。
        // ---------------------------------------------------------------------------------
        private static IEnumerable<string> CameraNames() => CameraScenarios.All.Select(s => s.Name);
        [UnityTest]
        public IEnumerator Camera([ValueSource(nameof(CameraNames))] string scenarioName)
        {
            var host = UnityEngineHost.Ensure();
            yield return RunScenario(CameraScenarios.All, scenarioName, (ICamera)host.Camera, NewContext());
        }
    }
}

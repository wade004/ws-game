#nullable enable
// DiagnosticsHubCompositionPlayModeTests：测试覆盖第四批 T-M47——DiagnosticsHubComposition.RegisterCoreSources
// 是三个生产装配根（GameFoundationBootstrap/FrameworkResidentHost/games/_template.GameBootstrap）共享的
// “把核心层诊断来源逐个登记进 DiagnosticsHub”的清单，类型头判断记录明确它不在 dotnet test 编译范围，
// 此前只有静态字符串 pytest 与 ADR-0086 的 EventBus 单源 Editor 用例间接触达，“某来源漏登记”没有任何
// 行为级用例。本文件用真实的 GameplayAssembly/PresentationAssembly/EventBus/SaveSystem/SceneRouter
// 装配出一份与生产装配同形的实例，调用生产方法 RegisterCoreSources，再从两个方向验证：
//  ① 清单→行为：普查清单里每一个来源，往其真实诊断实例写一条 Warn（及可写 Error 的来源再写一条 Error），
//     经 Hub.Pump 后录制 sink 必须收到带该来源名前缀的转发文本，且条数恰好等于清单规则算出的数量
//     （漏登记、重复登记、串名都会让条数或文本对不上）；
//  ② 实例→清单（防新增遗漏）：沿五个装配根的公开属性图反射枚举全部 InMemory*Diagnostics 实例，每一个都必须
//     出现在上面清单里，或在“结构性排除”白名单里并写明理由——今后新增了一个对外暴露的 InMemory 诊断实例却
//     忘了登记进 Hub，本用例会红并点名该类型。
//
// 判断记录（用 PlayMode 而不是 EditMode）：装配 PresentationAssembly 需要真实的 UnityEngineHost 提供
// Renderer2D/Camera/Audio/FileSystem 等引擎适配实现与 UnityViewFactory，EditMode 下无法 Ensure 宿主；
// 夹具与 AuditBlockersPlayModeTests.BuildFixture 同款（同判断记录：不经完整 FrameworkResidentHost/ShellRoot，
// 直接构造装配），去掉玩家/Shell 部分，只保留诊断登记所需的五个对象。
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using Adapter.Unity.Diagnostics;
using Adapter.Unity.Presentation;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class DiagnosticsHubCompositionPlayModeTests : PlayModeTestBase
    {
        /// <summary>netstandard2.1 没有 ReferenceEqualityComparer，自带一个引用相等比较器。</summary>
        private sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();

            bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

            int IEqualityComparer<object>.GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class RecordingSink : IPresentationDiagnosticsConsoleSink
        {
            public readonly List<string> Messages = new List<string>();

            public void Warn(string message) => Messages.Add(message);
        }

        private sealed class Fixture
        {
            public AssemblyFixture Asm = null!;
            public DiagnosticsHub Hub = null!;
            public RecordingSink Sink = null!;
        }

        private Fixture? _fixture;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null)
            {
                _fixture.Asm.Dispose();
                _fixture = null;
            }

            yield return null;
        }

        private static Fixture BuildFixture()
        {
            var asm = AssemblyFixture.Build(20261001UL);
            var sink = new RecordingSink();
            var hub = new DiagnosticsHub(sink);
            // 生产方法：与 GameFoundationBootstrap/FrameworkResidentHost 同一调用形态（hub 先 new，再登记）。
            DiagnosticsHubComposition.RegisterCoreSources(hub, asm.Gameplay, asm.Presentation, asm.Bus, asm.SaveSystem, asm.SceneRouter);

            // 装配期自己可能已经产生过诊断（缺省数据等）；先排空并清掉，后面只数本用例探针产生的转发。
            hub.Pump();
            sink.Messages.Clear();
            return new Fixture { Asm = asm, Hub = hub, Sink = sink };
        }

        private enum ErrorShape
        {
            None,           // 该来源只登记 Warnings
            PlainString,    // Errors 为 IReadOnlyList<string>，Error(string)
            ExceptionAware, // Errors 经 ExceptionAwareErrorProjection，Error(string, Exception?)
        }

        /// <summary>普查清单：来源名 + 取得其真实诊断实例的访问路径 + 是否登记了 Errors。访问路径与
        /// DiagnosticsHubComposition.RegisterCoreSources 的取法逐行对应（同一批公开属性）。清单本身就是
        /// ADR-0042 普查表在测试里的镜像，新增来源时要同时改这里与生产登记。</summary>
        private static readonly (string Source, Func<Fixture, object?> Diagnostics, ErrorShape Errors)[] Census =
        {
            ("Core.Foundation.EventBus", f => f.Asm.Bus.Diagnostics, ErrorShape.ExceptionAware),
            ("Core.Foundation.HookRegistry", f => f.Asm.Gameplay.HooksDiagnostics, ErrorShape.ExceptionAware),
            ("Core.Foundation.AppLifecycle", f => f.Asm.Gameplay.AppStateDiagnostics, ErrorShape.ExceptionAware),
            ("Core.Foundation.SaveSystem", f => f.Asm.SaveSystem.Diagnostics, ErrorShape.ExceptionAware),
            ("Core.Foundation.SceneRouter", f => f.Asm.SceneRouter.Diagnostics, ErrorShape.ExceptionAware),
            ("Core.Foundation.InputMap", f => f.Asm.Presentation.InputMapDiagnostics, ErrorShape.None),
            ("Core.Foundation.Localization", f => f.Asm.Presentation.L10nDiagnostics, ErrorShape.None),
            ("Core.Carriers.Gobj", f => f.Asm.Gameplay.Carriers.GameObjectInteractions.Diagnostics, ErrorShape.PlainString),
            ("Core.Carriers.Creature", f => f.Asm.Gameplay.Carriers.CreatureInteractions.Diagnostics, ErrorShape.PlainString),
            ("Core.Carriers.Item", f => f.Asm.Gameplay.Carriers.Equipment.Diagnostics, ErrorShape.None),
            ("Core.Carriers.Projectile", f => f.Asm.Gameplay.Carriers.Projectiles.Diagnostics, ErrorShape.PlainString),
            ("Core.Numbers.PowerSet", f => f.Asm.Gameplay.Carriers.Rules.PowerDiagnostics, ErrorShape.None),
            ("Core.Numbers.Progression", f => f.Asm.Gameplay.Carriers.Rules.Progression.Diagnostics, ErrorShape.None),
            ("Core.Rules.Combat", f => f.Asm.Gameplay.Carriers.Rules.Combat.Diagnostics, ErrorShape.None),
            ("Core.Rules.Skill", f => f.Asm.Gameplay.Carriers.Rules.Skill.Diagnostics, ErrorShape.PlainString),
            ("Core.Gameplay.AreaTrigger", f => f.Asm.Gameplay.AreaTrigger.Diagnostics, ErrorShape.PlainString),
            ("Core.Gameplay.Common.Reward", f => f.Asm.Gameplay.Reward.Diagnostics, ErrorShape.None),
            ("Core.Gameplay.Death", f => f.Asm.Gameplay.Death.Diagnostics, ErrorShape.PlainString),
            ("Core.Gameplay.Dialog", f => f.Asm.Gameplay.Dialog.Diagnostics, ErrorShape.None),
            ("Core.Gameplay.Spawn", f => f.Asm.Gameplay.Spawn.Diagnostics, ErrorShape.PlainString),
            ("Core.Gameplay.WorldState", f => f.Asm.Gameplay.WorldState.Diagnostics, ErrorShape.None),
            ("Core.Gameplay.ProgressionBridge", f => f.Asm.Gameplay.ProgressionBridgeDiagnostics, ErrorShape.None),
            ("Core.Gameplay.Loot", f => f.Asm.Gameplay.LootDiagnostics, ErrorShape.None),
            ("Presentation.Ui", f => f.Asm.Presentation.UiDiagnostics, ErrorShape.None),
        };

        private static void Invoke(object target, string method, params object?[] args)
        {
            var m = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            Assert.IsNotNull(m, $"{target.GetType().FullName} 没有 {args.Length} 参数的 {method}");
            m!.Invoke(target, args);
        }

        // ------------------------------------------------------------------
        // ① 清单 → 行为
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EveryCensusSource_ForwardsItsWarningsAndErrors_UnderItsOwnSourceName_ExactlyOnce()
        {
            var fx = BuildFixture();
            _fixture = fx;
            yield return null;

            foreach (var (source, getDiag, errorShape) in Census)
            {
                var diag = getDiag(fx);
                Assert.IsNotNull(diag, $"{source}：装配后诊断实例不应为 null（生产登记用 `is InMemory...` 匹配，null 会被静默跳过）");
                Invoke(diag!, "Warn", $"probe-warn::{source}");
                switch (errorShape)
                {
                    case ErrorShape.PlainString:
                        Invoke(diag!, "Error", $"probe-error::{source}");
                        break;
                    case ErrorShape.ExceptionAware:
                        Invoke(diag!, "Error", $"probe-error::{source}", new InvalidOperationException($"probe-exception::{source}"));
                        break;
                }
            }

            fx.Hub.Pump();

            // 期望条数由清单规则算出：每个来源 1 条 warning，带 Errors 的来源再多 1 条。
            var expectedCount = Census.Sum(c => 1 + (c.Errors == ErrorShape.None ? 0 : 1));
            Assert.AreEqual(expectedCount, fx.Sink.Messages.Count,
                "转发条数应恰好等于清单规则算出的数量（漏登记会少、重复登记会多）。实际消息：\n" + string.Join("\n", fx.Sink.Messages));

            foreach (var (source, _, errorShape) in Census)
            {
                var warnLine = $"[{source}] probe-warn::{source}";
                Assert.AreEqual(1, fx.Sink.Messages.Count(m => m == warnLine), $"{source}：warning 应以来源名前缀恰好转发一次");

                if (errorShape == ErrorShape.PlainString)
                {
                    var errorLine = $"[{source}][error] probe-error::{source}";
                    Assert.AreEqual(1, fx.Sink.Messages.Count(m => m == errorLine), $"{source}：error 应恰好转发一次");
                }
                else if (errorShape == ErrorShape.ExceptionAware)
                {
                    var prefix = $"[{source}][error] probe-error::{source}";
                    var hits = fx.Sink.Messages.Where(m => m.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                    Assert.AreEqual(1, hits.Count, $"{source}：带异常的 error 应恰好转发一次");
                    StringAssert.Contains(typeof(InvalidOperationException).FullName!, hits[0], $"{source}：转发文本应带异常类型全名（ADR-0086）");
                    StringAssert.Contains($"probe-exception::{source}", hits[0], $"{source}：转发文本应带异常消息");
                }
            }
        }

        [UnityTest]
        public IEnumerator SourceNames_AreUnique_AndEachDiagnosticsInstanceIsDistinct()
        {
            var fx = BuildFixture();
            _fixture = fx;
            yield return null;

            Assert.AreEqual(Census.Length, Census.Select(c => c.Source).Distinct(StringComparer.Ordinal).Count(), "来源名不得重复（重复会让两个来源的转发无法区分）");
            var instances = Census.Select(c => c.Diagnostics(fx)).ToList();
            Assert.AreEqual(instances.Count, instances.Distinct(RefEq.Instance).Count(),
                "清单里每个来源应对应各自独立的诊断实例；两个来源共享同一实例意味着登记写重或取错了属性");
        }

        [UnityTest]
        public IEnumerator Hub_Disabled_DropsEverythingSilently_AndNothingIsReplayedAfterReEnable_ForAllSourceShapes()
        {
            var fx = BuildFixture();
            _fixture = fx;
            yield return null;

            // 三种登记形状各取一个来源：仅 Warnings / 带 string Errors / 带 ExceptionAware Errors。
            var shapes = new[]
            {
                Census.First(c => c.Errors == ErrorShape.None),
                Census.First(c => c.Errors == ErrorShape.PlainString),
                Census.First(c => c.Errors == ErrorShape.ExceptionAware),
            };

            fx.Hub.Enabled = false;
            foreach (var (source, getDiag, errorShape) in shapes)
            {
                Invoke(getDiag(fx)!, "Warn", $"off-warn::{source}");
                if (errorShape == ErrorShape.PlainString)
                {
                    Invoke(getDiag(fx)!, "Error", $"off-error::{source}");
                }
                else if (errorShape == ErrorShape.ExceptionAware)
                {
                    Invoke(getDiag(fx)!, "Error", $"off-error::{source}", new InvalidOperationException("off"));
                }
            }

            fx.Hub.Pump();
            Assert.AreEqual(0, fx.Sink.Messages.Count, "关闭开关期间任何登记形状都不应转发（含 ExceptionAware 通道）：" + System.Environment.NewLine + string.Join(System.Environment.NewLine, fx.Sink.Messages));

            fx.Hub.Enabled = true;
            fx.Hub.Pump();
            Assert.AreEqual(0, fx.Sink.Messages.Count, "游标在关闭期间照常推进：重新开启后不会补发关闭期间的消息（与 dotnet 侧 Enabled_CanBeToggledAtRuntime 同一语义）");

            // 开启后的新消息正常转发；关闭期间的同文本不被当作“已见过”（不记账）。
            var (src, diag, _) = shapes[0];
            Invoke(diag(fx)!, "Warn", $"off-warn::{src}");
            fx.Hub.Pump();
            Assert.AreEqual(new[] { $"[{src}] off-warn::{src}" }, fx.Sink.Messages.ToArray());
        }

        // ------------------------------------------------------------------
        // ② 实例 → 清单（防新增遗漏）
        // ------------------------------------------------------------------

        /// <summary>结构性排除白名单：沿公开属性图可达、但按设计不经 Hub 登记的 InMemory*Diagnostics 类型
        /// （全名 → 理由）。登记在此处的每一项都要有 ADR/判断记录依据；新增遗漏不得靠往这里加条目“过关”，
        /// 除非有同等强度的设计依据。当前为空：普查结论是所有公开可达的 InMemory 诊断实例都已登记。</summary>
        private static readonly Dictionary<string, string> StructuralExclusions = new Dictionary<string, string>();

        [UnityTest]
        public IEnumerator EveryPubliclyReachableInMemoryDiagnosticsInstance_IsInTheCensus_OrStructurallyExcluded()
        {
            var fx = BuildFixture();
            _fixture = fx;
            yield return null;

            var census = new HashSet<object>(Census.Select(c => c.Diagnostics(fx)).Where(d => d != null)!, RefEq.Instance);
            var found = new Dictionary<object, string>(RefEq.Instance);
            var visited = new HashSet<object>(RefEq.Instance);
            var roots = new (string Name, object Root)[]
            {
                ("gameplay", fx.Asm.Gameplay), ("presentation", fx.Asm.Presentation), ("bus", fx.Asm.Bus),
                ("saveSystem", fx.Asm.SaveSystem), ("sceneRouter", fx.Asm.SceneRouter),
            };
            foreach (var (name, root) in roots)
            {
                Walk(root, name, depth: 0, visited, found);
            }

            Assert.Greater(found.Count, 0, "反射遍历应至少发现一个 InMemory*Diagnostics 实例（遍历逻辑失效时不能静默通过）");
            Assert.GreaterOrEqual(found.Count, Census.Length - 1, "遍历发现的实例数不应远少于清单（否则遍历深度/过滤条件有问题）");

            var unregistered = found
                .Where(kv => !census.Contains(kv.Key) && !StructuralExclusions.ContainsKey(kv.Key.GetType().FullName!))
                .Select(kv => $"{kv.Key.GetType().FullName}（经 {kv.Value} 可达）")
                .ToList();
            Assert.IsEmpty(unregistered,
                "以下公开可达的 InMemory*Diagnostics 实例既不在 Hub 登记清单里、也不在结构性排除白名单里（漏登记？）：\n" + string.Join("\n", unregistered));
        }

        private const int MaxWalkDepth = 4;

        private static void Walk(object node, string path, int depth, HashSet<object> visited, Dictionary<object, string> found)
        {
            if (!visited.Add(node))
            {
                return;
            }

            var type = node.GetType();
            var fullName = type.FullName ?? type.Name;
            if (type.Name.StartsWith("InMemory", StringComparison.Ordinal) && type.Name.EndsWith("Diagnostics", StringComparison.Ordinal))
            {
                found[node] = path;
                return;
            }

            if (depth >= MaxWalkDepth)
            {
                return;
            }

            // 只沿框架自己的类型继续往下走，避免钻进 BCL/Unity 对象图。
            if (!(fullName.StartsWith("Core.", StringComparison.Ordinal) || fullName.StartsWith("Presentation.", StringComparison.Ordinal)))
            {
                return;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (prop.GetIndexParameters().Length != 0 || !prop.CanRead)
                {
                    continue;
                }

                var pt = prop.PropertyType;
                if (pt.IsValueType || pt == typeof(string) || typeof(Delegate).IsAssignableFrom(pt) || typeof(IEnumerable).IsAssignableFrom(pt))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = prop.GetValue(node);
                }
                catch (Exception)
                {
                    continue;   // 个别属性在当前装配状态下读取会抛（例如要求先进入某状态），不属于诊断可达性问题
                }

                if (value != null)
                {
                    Walk(value, path + "." + prop.Name, depth + 1, visited, found);
                }
            }
        }
    }
}

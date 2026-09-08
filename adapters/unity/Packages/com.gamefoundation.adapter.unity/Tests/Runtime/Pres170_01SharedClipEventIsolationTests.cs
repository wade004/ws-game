#nullable enable
// Pres170_01SharedClipEventIsolationTests：PRES-170-01 根治验收（architecture/落地计划/
// audit-8160178-20260908/presentation/presentation-findings.md「PRES-17-01」，AUDIT_REPORT.md
// 汇总编号 PRES-170-01）——空 display.anim_set.clips[*].events 配置与跨 UnityViewFactory 生命周期
// 污染共享 AnimationClip 的根治验收。
//
// 判断记录一（取代同目录审核归档 PresentationClipSharedStateProbe.cs 的"故障现状断言"）：该探针的
// 三条用例（顺序 A 非空后空 / 顺序 B 空后非空 / 跨 factory）全部用 Assert.IsTrue(actual, "probe: ...
// 共享了 A 的数据事件")断言"污染确实发生"——测试通过 = 复现故障，不代表产品行为正确，不能作为回归
// 门槛长期保留。本文件把同样的三种触发顺序改写成正确性断言（"B 不应该看到 A 的事件"），并按任务书
// 要求追加"场景重建"“实体销毁重建”两组，共五组用例，全部反向断言：任何一种顺序/生命周期下，一个
// anim_set 都不应该看到另一个 anim_set 的事件。
//
// 判断记录二（用 Animator 实际播放中的事件集合而不是静态读 AnimationClip.events 作为最终断言）：
// UnityViewFactory.RegisterModelClipEvents 修复后对非空配置一律走
// UnityRenderer3D.ApplyAnimClipOverride（AnimatorOverrideController），静态读某个 AnimationClip 对象
// 的 events 字段查不出"这个具体实例的 Animator 实际会播放哪一份"——AnimatorOverrideController 的替换
// 只在 Animator 内部生效，不改变原始 AnimationClip 对象本身的 events。本文件改为经
// IRenderer3D.PlayAnim 真正驱动 Animator 播放 attack 状态、经 IRenderer3D.OnAnimEvent 的真实回调
// （AnimationEvent 的 SendMessage 落点 ModelAnimEventRelay -> UnityRenderer3D.RaiseAnimEvent，同
// HitFrameSyncEndToEndTests.cs 一贯手法）收集这次播放期间真正触发的事件裸名集合，逐条 Contains/
// !Contains 断言——这是"这个实例的 Animator 实际播放时到底会不会执行到这个回调"的终局判定，不可能被
// 任何静态分析或对象引用巧合欺骗。
//
// 判断记录三（不用集合整体相等断言，改用逐条 Contains）：非循环剪辑自然播放完成后，
// UnityRenderer3D.Tick 会额外经同一条 OnAnimEvent 通路发出一次裸名为 "finished" 的完成事件（见该
// 方法判断记录），与本文件要验证的 PRES-170-01 隔离逻辑无关；用整体集合相等断言需要额外扣除这个
// 框架自带的事件，容易在维护时被误改成"扣除逻辑本身有 bug、意外放行"，改用逐条断言"这几个事件确实
// 触发了/那几个事件确实没有触发"更直接、也更不容易被拖累。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class Pres170_01SharedClipEventIsolationTests : PlayModeTestBase
    {
        private static readonly Id AttackClipRef = new Id("anim.attack");

        private UnityEngineHost _host = null!;
        private readonly List<ModelHandle> _createdHandles = new List<ModelHandle>();

        [SetUp]
        public void SetUp()
        {
            _host = UnityEngineHost.Ensure();
        }

        /// <summary>兜底清理：正常路径下每条用例末尾已经逐一 DestroyModelInstance，这里只覆盖"用例
        /// 中途 Assert 失败、后续清理代码没有执行到"的异常路径，避免残留模型实例污染下一条用例（同
        /// PlayModeTestBase 统一隔离惯例的补充，本类型自己创建的模型实例不属于任何
        /// GameFoundationBootstrap/World，PlayModeIsolation.TearDownAfterTest 管不到，需要自己收尾）。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var handle in _createdHandles)
            {
                _host.Renderer3D.DestroyModelInstance(handle);
            }
            _createdHandles.Clear();
            yield return null;
        }

        private static UnityViewFactory NewFactory(UnityEngineHost host) =>
            new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), new NullDisplayInfoRegistry(),
                host.ResourceLoader, renderer3D: host.Renderer3D);

        private (ModelHandle Handle, Animator Animator) CreateModel()
        {
            var handle = _host.Renderer3D.CreateModelInstance(new Id("model.placeholder_biped"));
            _createdHandles.Add(handle);
            var animator = _host.Renderer3D.GetModelVisualRoot(handle)!.GetComponentInChildren<Animator>();
            Assert.IsNotNull(animator, "占位模型应当带有 Animator 组件");
            return (handle, animator!);
        }

        private void DestroyModel(ModelHandle handle)
        {
            _host.Renderer3D.DestroyModelInstance(handle);
            _createdHandles.Remove(handle);
        }

        private static bool HasOverride(Animator animator) => animator.runtimeAnimatorController is AnimatorOverrideController;

        /// <summary>见文件顶部判断记录二：真正经 PlayAnim 驱动 <paramref name="handle"/> 对应实例播放
        /// attack 状态（占位 attack.anim 长度固定 0.5 秒，见 GeneratePlaceholderModelAssets.cs），
        /// 经 IRenderer3D.OnAnimEvent 收集期间实际触发（AnimationEvent 真实 SendMessage 派发）的事件
        /// 裸名集合。deadline 用真实时间轮询（同 HitFrameSyncEndToEndTests.cs 一贯手法），足够覆盖
        /// 0.5 秒 * speed=1.0 的整段播放。</summary>
        private IEnumerator PlayAndCollectFiredBareNames(ModelHandle handle, HashSet<string> firedBareNames)
        {
            const string prefix = "anim_event.";
            using (_host.Renderer3D.OnAnimEvent(handle, (h, eventId) =>
                   {
                       var raw = eventId.Value;
                       firedBareNames.Add(raw.StartsWith(prefix) ? raw.Substring(prefix.Length) : raw);
                   }))
            {
                _host.Renderer3D.PlayAnim(handle, AttackClipRef, loop: false, speed: 1.0, blendSeconds: 0.0);
                var deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
        }

        private sealed class NullDisplayInfoRegistry : IDisplayInfoRegistry
        {
            public Core.Foundation.DisplayInfo.DisplayInfo? Lookup(Id logicalId) => null;
            public System.Collections.Generic.IReadOnlyList<Core.Foundation.DisplayInfo.DisplayInfo> LookupByCategory(DisplayCategory category) => System.Array.Empty<Core.Foundation.DisplayInfo.DisplayInfo>();
            public System.Collections.Generic.IReadOnlyList<Core.Foundation.DisplayInfo.DisplayInfo> All => System.Array.Empty<Core.Foundation.DisplayInfo.DisplayInfo>();
            public void Reload() { }
        }

        /// <summary>顺序一（对应审核探针 EmptyAnimSetAfterConfiguredAnimSet_...）：同一 factory 内，
        /// 非空配置 A（probe_a）先注册，空配置 B 后注册。修复前 B 会经共享 baseClip.events 直接看到
        /// A 写入的 probe_a；修复后 B 只应该看到 authored 事件（hit_frame），不应该有任何覆盖控制器。</summary>
        [UnityTest]
        public IEnumerator Order1_NonEmptyThenEmpty_SameFactory_EmptyStaysAuthoredOnly()
        {
            var factory = NewFactory(_host);
            var a = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);
            var b = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, System.Array.Empty<AnimClipEventSpec>()), b.Handle);

            Assert.IsTrue(HasOverride(a.Animator), "非空配置 A 应当经运行期克隆剪辑隔离，不应该再写共享资产");
            Assert.IsFalse(HasOverride(b.Animator), "空配置 B 天生等于 authored 基线，不需要任何覆盖控制器");

            var firedA = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(a.Handle, firedA);
            Assert.IsTrue(firedA.Contains("probe_a"), "A 自己配置的数据事件应当在真实播放中触发");
            Assert.IsTrue(firedA.Contains("hit_frame"), "A 不应该丢失美术自带的 hit_frame（合并而非整体覆盖）");

            var firedB = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(b.Handle, firedB);
            Assert.IsFalse(firedB.Contains("probe_a"),
                "PRES-170-01 核心断言：空配置 B 的真实播放不应该触发 A 的数据事件 probe_a（此前会经共享 baseClip.events 泄漏）");
            Assert.IsTrue(firedB.Contains("hit_frame"), "B 仍应正常播放美术自带的 hit_frame（authored 基线本身不受影响）");

            DestroyModel(a.Handle);
            DestroyModel(b.Handle);
        }

        /// <summary>顺序二（对应审核探针 EmptyAnimSetBeforeConfiguredAnimSet_...）：同一 factory 内，
        /// 空配置 B 先注册，非空配置 A 后注册。修复前 A 之后的写入会污染共享资产，连带影响已经创建、
        /// 已经在播的 B；修复后顺序不应该改变任何结论——B 仍然只应该看到 authored 事件。</summary>
        [UnityTest]
        public IEnumerator Order2_EmptyThenNonEmpty_SameFactory_EmptyStaysAuthoredOnly()
        {
            var factory = NewFactory(_host);
            var b = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, System.Array.Empty<AnimClipEventSpec>()), b.Handle);
            var a = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);

            Assert.IsFalse(HasOverride(b.Animator), "B 先于 A 注册，注册顺序不应该改变'空配置不需要覆盖控制器'这一结论");
            Assert.IsTrue(HasOverride(a.Animator), "A（非空配置）应当经运行期克隆剪辑隔离");

            var firedB = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(b.Handle, firedB);
            Assert.IsFalse(firedB.Contains("probe_a"),
                "PRES-170-01 核心断言：B 已经创建在先，A 之后的注册不应该反过来污染 B 的真实播放");
            Assert.IsTrue(firedB.Contains("hit_frame"));

            var firedA = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(a.Handle, firedA);
            Assert.IsTrue(firedA.Contains("probe_a"));
            Assert.IsTrue(firedA.Contains("hit_frame"));

            DestroyModel(a.Handle);
            DestroyModel(b.Handle);
        }

        /// <summary>顺序三（对应审核探针 DifferentUnityViewFactory_...，同时是"场景重建"最贴近生产
        /// 触发链的最小复现——UnityViewFactory 由场景组合根在每次场景重进时重新构造一个新实例，见该
        /// 类型 s_authoredClipEvents 判断记录"一"）：factory A 用签名 probe_a 注册，随后场景"重进"，
        /// 全新的 factory B（不共享任何实例字段）用不同签名 probe_b 注册同一个 resource_ref。修复前
        /// factory B 会把 factory A 已经写进共享资产的 probe_a 当成"美术自带基线"一并保留；修复后
        /// authored 基线是进程级静态注册表，不因为新建 factory 而重新捕获成"当前已被污染的状态"。</summary>
        [UnityTest]
        public IEnumerator Order3_DifferentFactory_CrossFactoryAuthoredBaselineNotPolluted()
        {
            var factoryA = NewFactory(_host);
            var a = CreateModel();
            factoryA.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);

            // 场景重建的关键动作：不复用 factoryA，构造一个全新的 UnityViewFactory 实例——同生产装配
            // 根每次场景重进时的真实构造方式（见文件顶部判断记录）。
            var factoryB = NewFactory(_host);
            var b = CreateModel();
            factoryB.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_b", 0.8) }), b.Handle);

            Assert.IsTrue(HasOverride(a.Animator));
            Assert.IsTrue(HasOverride(b.Animator));

            var firedA = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(a.Handle, firedA);
            Assert.IsTrue(firedA.Contains("probe_a"));
            Assert.IsFalse(firedA.Contains("probe_b"), "factory A 创建的实例不应该看到 factory B 之后才声明的 probe_b");
            Assert.IsTrue(firedA.Contains("hit_frame"));

            var firedB = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(b.Handle, firedB);
            Assert.IsTrue(firedB.Contains("probe_b"));
            Assert.IsFalse(firedB.Contains("probe_a"),
                "PRES-170-01 核心断言：新 factory B 不应该把 factory A 已经写入的数据事件当成 authored 基线保留下来");
            Assert.IsTrue(firedB.Contains("hit_frame"), "factory B 的 authored 基线仍应包含美术自带的 hit_frame");

            DestroyModel(a.Handle);
            DestroyModel(b.Handle);
        }

        /// <summary>场景重建（第四组）：在"跨 factory"的基础上再叠加"旧场景的实例已经随场景卸载被
        /// 销毁"这一更完整的重进语义——factory A 及其创建的实例先整体销毁，再构造全新 factory B 与
        /// 全新实体。验证 authored 基线与运行期覆盖缓存都以 resource_ref/签名为键、与已销毁的旧实例
        /// 生命周期无关，销毁旧实例不会连带清空这两张进程级缓存，也不会让新场景看到旧场景任何
        /// 实体专属的数据事件。</summary>
        [UnityTest]
        public IEnumerator SceneRebuild_OldFactoryAndEntitiesDestroyed_NewFactoryStaysIsolated()
        {
            var factoryA = NewFactory(_host);
            var a = CreateModel();
            factoryA.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);
            DestroyModel(a.Handle); // 模拟场景卸载：旧场景的模型实例整体销毁。

            var factoryB = NewFactory(_host); // 模拟场景重进：组合根重新构造一个新的 UnityViewFactory。
            var b = CreateModel();
            factoryB.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, System.Array.Empty<AnimClipEventSpec>()), b.Handle);

            Assert.IsFalse(HasOverride(b.Animator), "新场景里空配置的实体不应该被旧场景已销毁实体的数据事件牵连出任何覆盖控制器");

            var firedB = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(b.Handle, firedB);
            Assert.IsFalse(firedB.Contains("probe_a"),
                "PRES-170-01 核心断言：旧场景（factory A + 其实体）已经整体销毁，新场景新实体仍不应该看到旧场景的数据事件");
            Assert.IsTrue(firedB.Contains("hit_frame"));

            DestroyModel(b.Handle);
        }

        /// <summary>实体销毁重建（第五组）：同一 factory 内，先创建非空配置实体 A 并销毁，再创建
        /// 空配置实体 B——验证"实体被销毁"本身不会让下一个复用同一 resource_ref 的空配置实体意外看到
        /// 被销毁实体的数据事件；随后再创建一个与 A 签名相同的实体 C，验证运行期覆盖剪辑缓存在原实体
        /// 已销毁后仍能正确复用并套用到新实体的 Animator 上（不依赖已销毁实例的任何残留状态）。</summary>
        [UnityTest]
        public IEnumerator EntityDestroyAndRecreate_PreservesIsolationAndReusesOverrideCache()
        {
            var factory = NewFactory(_host);
            var probeADef = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) });

            var a = CreateModel();
            factory.RegisterModelClipEventsForTest(probeADef, a.Handle);
            DestroyModel(a.Handle); // 实体销毁重建：A 先被销毁。

            var b = CreateModel(); // 重建：同一 factory 上创建的新实体，空配置。
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, System.Array.Empty<AnimClipEventSpec>()), b.Handle);

            Assert.IsFalse(HasOverride(b.Animator));
            var firedB = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(b.Handle, firedB);
            Assert.IsFalse(firedB.Contains("probe_a"),
                "PRES-170-01 核心断言：已销毁实体 A 的数据事件不应该出现在重建后的空配置实体 B 上");
            Assert.IsTrue(firedB.Contains("hit_frame"));

            var c = CreateModel(); // 再重建：同一签名 probe_a，验证覆盖缓存在 A 销毁后仍可正确复用。
            factory.RegisterModelClipEventsForTest(probeADef, c.Handle);
            Assert.IsTrue(HasOverride(c.Animator), "C 与已销毁的 A 签名相同，仍应正确套用（复用）运行期覆盖剪辑");

            var firedC = new HashSet<string>();
            yield return PlayAndCollectFiredBareNames(c.Handle, firedC);
            Assert.IsTrue(firedC.Contains("probe_a"), "C 应当正确播放到 probe_a——覆盖缓存复用不应该因为原实体已销毁而失效");
            Assert.IsTrue(firedC.Contains("hit_frame"));

            DestroyModel(b.Handle);
            DestroyModel(c.Handle);
        }
    }
}

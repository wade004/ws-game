#nullable enable
// ModelClipEventIsolationTests：动画剪辑事件登记契约差异根治验收（architecture/落地计划/
// audit-85f1f4f-20260908/presentation/presentation-findings.md"静态契约差异"，第九方审核）。
//
// 12 §5 二次勘误（本文件断言随实现一起更新——architecture/落地计划/audit-8160178-20260908/
// presentation/presentation-findings.md「PRES-17-01」，AUDIT_REPORT.md 汇总编号 PRES-170-01）：
// 上一轮"首个 anim_set 直接合并写在共享 AnimationClip.events 上、不需要任何覆盖控制器"的立场被
// 第十轮审核指出是污染源本身——任何写共享资产的分支，不论是不是"第一个"，都会让后来新建的
// UnityViewFactory（典型触发：场景重进）把这份写入误当成"美术自带基线"（见
// Pres170_01SharedClipEventIsolationTests.cs 完整复现 + 根治验收）。本文件覆盖的三处根治结论调整为：
//   一、经 IResourceLoader（ResourceKind.AnimationClip）取剪辑，不再直接 Resources.Load——不变；
//   二、合并（不整体覆盖）美术自带 events——不变，只是合并结果写进运行期克隆剪辑，不再写共享资产；
//   三、同一 resource_ref 被不同 anim_set（不同事件配置）引用时按 anim_set 隔离——不变的是"互不
//      影响"，变化的是隔离手段：现在不分"第一个 vs 后续"，任何非空配置一律经运行期克隆
//      （AnimatorOverrideController）隔离，共享的 baseClip.events 永远保持 authored 原样，
//      不会被本类型写入。
//
// 判断记录（用 RegisterModelClipEventsForTest 内部测试钩子，不经完整 display.anim_set 数据集装配）：
// RegisterModelClipEvents 是 UnityViewFactory 私有方法，真正装配需要两条引用同一 resource_ref、但
// events 不同的 display.anim_set 记录——为此在 data/_sample 里新增两条记录反而让测试依赖磁盘布局，
// 不如直接用真实的 anim.attack 剪辑资源（已有美术自带 hit_frame@0.5 事件，见
// adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs）+ 手工构造两份不同的 AnimClipDef，
// 直接验证"合并 + 隔离"这段逻辑本身，同 UnityRenderer3DTests 系列"隔离验证底层机制"的一贯惯例。
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
    public sealed class ModelClipEventIsolationTests : PlayModeTestBase
    {
        private static readonly Id AttackClipRef = new Id("anim.attack");

        private UnityEngineHost _host = null!;
        private UnityViewFactory _factory = null!;

        /// <summary>判断记录（隔离全局共享资产）：<c>anim.attack.anim</c> 经 <see cref="UnityEngineHost.Ensure"/>
        /// 在整个 Unity 测试会话内只加载一次、跨全部 PlayMode 测试类共享同一个对象实例。二次勘误后
        /// <see cref="RegisterModelClipEvents"/> 本身不再写这份共享资产（见文件顶部判断记录），本
        /// 快照/复原只作为"万一某条用例的断言前置条件写错、真的动了共享资产"的最后一道防线，不再是
        /// 被测行为要求的必需清理。</summary>
        private AnimationEvent[] _originalAttackClipEvents = null!;

        [SetUp]
        public void SetUp()
        {
            _host = UnityEngineHost.Ensure();
            _factory = new UnityViewFactory(
                _host.Renderer2D, new RenderConventionHost(), new NullDisplayInfoRegistry(), _host.ResourceLoader,
                renderer3D: _host.Renderer3D);
            _originalAttackClipEvents = LoadedBaseClip().events;
        }

        [TearDown]
        public void TearDown()
        {
            LoadedBaseClip().events = _originalAttackClipEvents;
        }

        /// <summary>本文件不需要真实数据集（只用内部测试钩子直接调用私有的事件登记逻辑），但
        /// UnityViewFactory 构造函数要求一个 <see cref="IDisplayInfoRegistry"/>，给一个永远查不到任何
        /// 记录的最小实现即可——本文件从不经 <see cref="UnityViewFactory.CreateView"/>，不依赖它的
        /// 查询结果。</summary>
        private sealed class NullDisplayInfoRegistry : IDisplayInfoRegistry
        {
            public Core.Foundation.DisplayInfo.DisplayInfo? Lookup(Id logicalId) => null;

            public System.Collections.Generic.IReadOnlyList<Core.Foundation.DisplayInfo.DisplayInfo> LookupByCategory(DisplayCategory category) =>
                System.Array.Empty<Core.Foundation.DisplayInfo.DisplayInfo>();

            public System.Collections.Generic.IReadOnlyList<Core.Foundation.DisplayInfo.DisplayInfo> All =>
                System.Array.Empty<Core.Foundation.DisplayInfo.DisplayInfo>();

            public void Reload()
            {
            }
        }

        private (ModelHandle Handle, Animator Animator) CreateInstance()
        {
            var handle = _host.Renderer3D.CreateModelInstance(new Id("model.placeholder_biped"));
            var animator = _host.Renderer3D.GetModelVisualRoot(handle)!.GetComponentInChildren<Animator>();
            Assert.IsNotNull(animator, "占位模型应当带有 Animator 组件");
            return (handle, animator!);
        }

        private AnimationClip LoadedBaseClip()
        {
            Assert.IsTrue(_host.ResourceLoader.TryLoadAnimationClipSync(AttackClipRef, out var clip),
                "control: anim.attack 应当能经 IResourceLoader 成功解析");
            return clip;
        }

        private static AnimationEvent? FindEvent(AnimationClip clip, string name)
        {
            foreach (var evt in clip.events)
            {
                if (evt.functionName == UnityRenderer3D.AnimEventFunctionName && evt.stringParameter == name)
                {
                    return evt;
                }
            }
            return null;
        }

        /// <summary>判断记录：<see cref="AnimationClip.events"/> 的取值器每次调用都返回一份新拷贝的
        /// <see cref="AnimationEvent"/> 数组（Unity 既有行为），而 <see cref="AnimationEvent"/> 是引用
        /// 类型且未见得重写结构化 <c>Equals</c>——直接用 <see cref="CollectionAssert"/> 比较两次取值器
        /// 结果的对象引用并不可靠。改用本方法把每条事件投影成一个可结构化比较的元组
        /// (functionName, stringParameter, time) 序列，用来判定"事件集合内容是否逐字相同"。</summary>
        private static (string FunctionName, string StringParameter, float Time)[] Snapshot(AnimationClip clip)
        {
            var events = clip.events;
            var snapshot = new (string, string, float)[events.Length];
            for (var i = 0; i < events.Length; i++)
            {
                snapshot[i] = (events[i].functionName, events[i].stringParameter, events[i].time);
            }
            return snapshot;
        }

        /// <summary>根治一（经 IResourceLoader）+ 根治二（合并、不覆盖）验收：唯一引用 anim.attack 的
        /// anim_set 声明一个与美术自带事件不同名的新事件（footstep）。二次勘误后：合并结果只写进运行期
        /// 克隆出的覆盖剪辑，共享 baseClip 必须保持 authored 原样（不出现 footstep，hit_frame 时间点
        /// 不变）；覆盖剪辑上应当同时看到未被触碰的 hit_frame@0.5 与新增的 footstep。</summary>
        [Test]
        public void RegisterModelClipEvents_SingleAnimSet_MergesWithAuthoredEvents_ViaOverride_DoesNotTouchSharedClip()
        {
            var baseClip = LoadedBaseClip();
            var authoredHitFrame = FindEvent(baseClip, "hit_frame");
            Assert.IsNotNull(authoredHitFrame, "control: attack.anim 应当自带一条 hit_frame（占位生成器烘焙）");
            var authoredTime = authoredHitFrame!.time;

            var (handle, animator) = CreateInstance();
            var clipDef = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("footstep", 0.2) });

            _factory.RegisterModelClipEventsForTest(clipDef, handle);

            // 二次勘误核心断言：共享资产必须逐字不变——本类型自此不再写它。
            Assert.IsNull(FindEvent(baseClip, "footstep"),
                "PRES-170-01 根治验收：数据驱动新增的 footstep 不应该出现在共享资产上，只应该出现在运行期覆盖剪辑上");
            var sharedHitFrame = FindEvent(baseClip, "hit_frame");
            Assert.IsNotNull(sharedHitFrame);
            Assert.AreEqual(authoredTime, sharedHitFrame!.time, 0.0001f,
                "共享资产上美术自带的 hit_frame 时间点不应该被本类型的任何写入改动");

            var overrideController = animator.runtimeAnimatorController as AnimatorOverrideController;
            Assert.IsNotNull(overrideController,
                "任何非空 anim_set（含唯一一个）都应当经 AnimatorOverrideController 拿到运行期克隆剪辑，不再有\"第一个可以直接写共享资产\"的特例");
            var overrideClip = overrideController![baseClip];
            Assert.IsNotNull(overrideClip);
            Assert.AreNotSame(baseClip, overrideClip, "覆盖剪辑必须是运行期克隆出的私有副本，不能就是共享资产本身");

            var overrideHitFrame = FindEvent(overrideClip!, "hit_frame");
            Assert.IsNotNull(overrideHitFrame, "美术自带的 hit_frame 事件应当被保留，不应该被数据驱动整体覆盖丢弃");
            Assert.AreEqual(authoredTime, overrideHitFrame!.time, 0.0001f,
                "这次数据没有重新定义 hit_frame，美术自带的时间点不应该被改动");
            var overrideFootstep = FindEvent(overrideClip, "footstep");
            Assert.IsNotNull(overrideFootstep, "数据驱动新增的 footstep 事件应当被合并进运行期覆盖剪辑");
            Assert.AreEqual(0.2 * overrideClip!.length, overrideFootstep!.time, 0.0001f);

            _host.Renderer3D.DestroyModelInstance(handle);
        }

        /// <summary>根治三（按 anim_set 隔离）验收核心：同一 resource_ref（anim.attack）被两个不同签名
        /// （A 新增 footstep；B 重新定义 hit_frame 的时间点）引用——二者都必须经运行期克隆隔离，共享
        /// 资产全程保持 authored 原样，互不影响。</summary>
        [Test]
        public void RegisterModelClipEvents_TwoDifferentSignatures_BothIsolatedViaOverride_SharedClipUntouched()
        {
            var baseClip = LoadedBaseClip();
            var sharedSnapshotBefore = Snapshot(baseClip);

            var (handleA, animatorA) = CreateInstance();
            var clipDefA = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("footstep", 0.2) });
            _factory.RegisterModelClipEventsForTest(clipDefA, handleA);

            CollectionAssert.AreEqual(sharedSnapshotBefore, Snapshot(baseClip),
                "PRES-170-01 根治验收：A（哪怕是第一个被注册的非空 anim_set）也不应该写共享资产");

            var (handleB, animatorB) = CreateInstance();
            // 第二个 anim_set：同一 resource_ref，但重新定义 hit_frame 的时间点（0.9，不同于美术自带的
            // 0.5，也不同于 A 完全没有触碰 hit_frame 这件事本身）——签名与 A 不同，各自独立隔离。
            var clipDefB = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("hit_frame", 0.9) });
            _factory.RegisterModelClipEventsForTest(clipDefB, handleB);

            // 一，共享资产在 B 注册前后必须完全一致——两个 anim_set 都不写它。
            CollectionAssert.AreEqual(sharedSnapshotBefore, Snapshot(baseClip),
                "共享资产上的事件集合在 A、B 均注册之后必须与最初 authored 状态完全一致");
            var sharedHitFrame = FindEvent(baseClip, "hit_frame");
            Assert.IsNotNull(sharedHitFrame);
            Assert.AreEqual(0.5 * baseClip.length, sharedHitFrame!.time, 0.0001f,
                "共享资产上的 hit_frame 应当仍是美术自带的 0.5，不应该被 B 的 0.9 覆盖");

            // 二，A、B 都必须经各自的 AnimatorOverrideController 拿到私有克隆，互不影响。
            var overrideControllerA = animatorA.runtimeAnimatorController as AnimatorOverrideController;
            Assert.IsNotNull(overrideControllerA, "A 也应当经运行期克隆剪辑隔离，不再有\"第一个直接写共享资产\"的特例");
            var overrideClipA = overrideControllerA![baseClip];
            Assert.IsNotNull(overrideClipA);
            Assert.IsNotNull(FindEvent(overrideClipA!, "footstep"), "A 的覆盖剪辑应当看到自己声明的 footstep");
            var overrideHitFrameA = FindEvent(overrideClipA!, "hit_frame");
            Assert.IsNotNull(overrideHitFrameA, "A 未重新定义 hit_frame，覆盖剪辑上仍应保留美术自带的 hit_frame");
            Assert.AreEqual(0.5 * overrideClipA!.length, overrideHitFrameA!.time, 0.0001f,
                "A 未重新定义 hit_frame，覆盖剪辑上的 hit_frame 不应该变成 B 的 0.9");

            var overrideControllerB = animatorB.runtimeAnimatorController as AnimatorOverrideController;
            Assert.IsNotNull(overrideControllerB, "B（第二个、不同签名的 anim_set）应当被套上一层 AnimatorOverrideController 实现隔离");
            var overrideClipB = overrideControllerB![baseClip];
            Assert.IsNotNull(overrideClipB, "覆盖控制器应当为 baseClip 登记一条覆盖映射");
            Assert.AreNotSame(baseClip, overrideClipB, "覆盖剪辑必须是运行期克隆出的私有副本，不能就是共享资产本身");
            Assert.AreNotSame(overrideClipA, overrideClipB, "A、B 签名不同，必须是两份独立的运行期克隆，不能共用一份");

            var overrideHitFrameB = FindEvent(overrideClipB!, "hit_frame");
            Assert.IsNotNull(overrideHitFrameB);
            Assert.AreEqual(0.9 * overrideClipB!.length, overrideHitFrameB!.time, 0.0001f,
                "B 的私有覆盖剪辑上，hit_frame 应当是 B 自己声明的 0.9，不受共享资产/A 的配置影响");
            Assert.IsNull(FindEvent(overrideClipB, "footstep"), "B 的私有覆盖剪辑不应该出现 A 专属声明的 footstep 事件");

            _host.Renderer3D.DestroyModelInstance(handleA);
            _host.Renderer3D.DestroyModelInstance(handleB);
        }

        /// <summary>同一 resource_ref、同一套事件配置（相同签名）被两个不同实体共同引用（最常见场景，
        /// 例如两个共享同一 display.anim_set 的实体）：两个实体都应当经 AnimatorOverrideController 拿到
        /// 覆盖剪辑（不再有"无覆盖直接播共享资产"的特例），但必须复用同一份运行期克隆（同一个
        /// AnimationClip 对象引用），不应该为同一签名重复 Instantiate；共享资产全程不受影响。</summary>
        [Test]
        public void RegisterModelClipEvents_SameSignatureTwice_ReusesSameOverrideClipInstance_SharedClipUntouched()
        {
            var baseClip = LoadedBaseClip();
            var sharedSnapshotBefore = Snapshot(baseClip);

            var (handleA, animatorA) = CreateInstance();
            var (handleB, animatorB) = CreateInstance();
            var events = new[] { new AnimClipEventSpec("footstep", 0.2) };

            _factory.RegisterModelClipEventsForTest(new AnimClipDef(AttackClipRef, events), handleA);
            _factory.RegisterModelClipEventsForTest(new AnimClipDef(AttackClipRef, events), handleB);

            CollectionAssert.AreEqual(sharedSnapshotBefore, Snapshot(baseClip),
                "PRES-170-01 根治验收：相同签名重复引用同样不应该写共享资产");
            Assert.IsNull(FindEvent(baseClip, "footstep"));

            var overrideControllerA = animatorA.runtimeAnimatorController as AnimatorOverrideController;
            var overrideControllerB = animatorB.runtimeAnimatorController as AnimatorOverrideController;
            Assert.IsNotNull(overrideControllerA);
            Assert.IsNotNull(overrideControllerB);

            var overrideClipA = overrideControllerA![baseClip];
            var overrideClipB = overrideControllerB![baseClip];
            Assert.IsNotNull(overrideClipA);
            Assert.AreSame(overrideClipA, overrideClipB,
                "相同签名应当复用同一份运行期克隆剪辑对象，不应该为每个实体各自重复 Instantiate");
            Assert.IsNotNull(FindEvent(overrideClipA!, "footstep"));

            _host.Renderer3D.DestroyModelInstance(handleA);
            _host.Renderer3D.DestroyModelInstance(handleB);
        }
    }
}

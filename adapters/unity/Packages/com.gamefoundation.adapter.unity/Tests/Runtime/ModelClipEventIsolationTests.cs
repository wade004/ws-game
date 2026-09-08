#nullable enable
// ModelClipEventIsolationTests：动画剪辑事件登记契约差异根治验收（architecture/落地计划/
// audit-85f1f4f-20260908/presentation/presentation-findings.md"静态契约差异"，第九方审核）。
//
// 覆盖三处根治（见 UnityViewFactory.RegisterModelClipEvents 判断记录"12 §5 勘误判断记录"）：
//   一、经 IResourceLoader（ResourceKind.AnimationClip）取剪辑，不再直接 Resources.Load；
//   二、合并（不整体覆盖）美术自带 events；
//   三、同一 resource_ref 被不同 anim_set（不同事件配置）引用时按 anim_set 隔离，不让首个 anim_set
//      决定其它 anim_set 看到的事件。
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
        /// 在整个 Unity 测试会话内只加载一次、跨全部 PlayMode 测试类共享同一个对象实例（同
        /// <see cref="RegisterModelClipEvents"/> 判断记录"Resources.Load 对同一路径返回同一个对象"）；
        /// 本文件的用例会真的往这个共享资产上写 <c>events</c>（这正是被测行为本身，不能改用替身规避）。
        /// 为了不让本文件的写入残留到同一会话内跑在它之后的其它测试类（如 ModelViewTests 对
        /// hit_frame 计数的精确断言），<see cref="SetUp"/> 记下调用任何注册逻辑之前的原始 <c>events</c>
        /// 快照，<see cref="TearDown"/> 无条件复原——即便某个用例中途失败也会执行（NUnit
        /// <c>[TearDown]</c> 惯例）。</summary>
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

        /// <summary>根治一（经 IResourceLoader）+ 根治二（合并、不覆盖）验收：第一个引用 anim.attack 的
        /// anim_set 声明一个与美术自带事件不同名的新事件（footstep）——合并后共享资产上应当同时保留
        /// 美术自带的 hit_frame@0.5（未被触碰）与数据驱动新增的 footstep。</summary>
        [Test]
        public void RegisterModelClipEvents_FirstAnimSet_MergesWithAuthoredEvents_DoesNotOverwrite()
        {
            var baseClip = LoadedBaseClip();
            var authoredHitFrame = FindEvent(baseClip, "hit_frame");
            Assert.IsNotNull(authoredHitFrame, "control: attack.anim 应当自带一条 hit_frame（占位生成器烘焙）");
            var authoredTime = authoredHitFrame!.time;

            var (handle, animator) = CreateInstance();
            var clipDef = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("footstep", 0.2) });

            _factory.RegisterModelClipEventsForTest(clipDef, handle);

            var afterHitFrame = FindEvent(baseClip, "hit_frame");
            var afterFootstep = FindEvent(baseClip, "footstep");
            Assert.IsNotNull(afterHitFrame, "美术自带的 hit_frame 事件应当被保留，不应该被数据驱动整体覆盖丢弃");
            Assert.AreEqual(authoredTime, afterHitFrame!.time, 0.0001f,
                "这次数据没有重新定义 hit_frame，美术自带的时间点不应该被改动");
            Assert.IsNotNull(afterFootstep, "数据驱动新增的 footstep 事件应当被合并进共享剪辑资产");
            Assert.AreEqual(0.2 * baseClip.length, afterFootstep!.time, 0.0001f);

            // 第一个 anim_set 不需要任何运行期覆盖——Animator 应当仍然直接使用共享的原始 RuntimeAnimatorController。
            Assert.IsNull(animator.runtimeAnimatorController as AnimatorOverrideController,
                "首个 anim_set 应当直接合并写在共享资产上，不需要 AnimatorOverrideController 这层间接");

            _host.Renderer3D.DestroyModelInstance(handle);
        }

        /// <summary>根治三（按 anim_set 隔离）验收核心：同一 resource_ref（anim.attack）被第二个 anim_set
        /// 以不同签名（重新定义 hit_frame 的时间点）引用时，不能覆盖共享资产——必须运行期克隆出一份私有
        /// 覆盖剪辑只应用到第二个实例的 Animator 上；第一个 anim_set 的实例与共享资产必须完全不受影响。</summary>
        [Test]
        public void RegisterModelClipEvents_SecondAnimSet_DifferentSignature_IsolatedViaOverride_DoesNotAffectFirst()
        {
            var baseClip = LoadedBaseClip();

            var (handleA, animatorA) = CreateInstance();
            var clipDefA = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("footstep", 0.2) });
            _factory.RegisterModelClipEventsForTest(clipDefA, handleA);

            var baseClipEventsAfterA = Snapshot(baseClip);

            var (handleB, animatorB) = CreateInstance();
            // 第二个 anim_set：同一 resource_ref，但重新定义 hit_frame 的时间点（0.9，不同于美术自带的
            // 0.5，也不同于 A 完全没有触碰 hit_frame 这件事本身）——签名与 A 不同，触发隔离分支。
            var clipDefB = new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("hit_frame", 0.9) });
            _factory.RegisterModelClipEventsForTest(clipDefB, handleB);

            // 一，A 的实例与共享资产必须完全不受 B 这次注册影响（不多任何 hit_frame@0.9，也不多 B 没有
            // 声明的任何东西；集合本身也不应该变化）。
            Assert.IsNull(animatorA.runtimeAnimatorController as AnimatorOverrideController,
                "A（首个 anim_set）不应该因为 B 之后才发生的注册而被套上任何覆盖控制器");
            CollectionAssert.AreEqual(baseClipEventsAfterA, Snapshot(baseClip),
                "共享资产上的事件集合在 B 注册前后必须完全一致——B 不能污染共享资产，A 也不能被 B 影响");
            var sharedHitFrame = FindEvent(baseClip, "hit_frame");
            Assert.IsNotNull(sharedHitFrame);
            Assert.AreEqual(0.5 * baseClip.length, sharedHitFrame!.time, 0.0001f,
                "共享资产上的 hit_frame 应当仍是美术自带的 0.5，不应该被 B 的 0.9 覆盖");

            // 二，B 必须经 AnimatorOverrideController 拿到一份私有克隆，克隆上只看到 B 自己声明的
            // hit_frame@0.9（不覆盖，因为这次 B 的数据里就是 hit_frame 本身，替换的是美术自带的那一条），
            // 且不应该出现 A 专属的 footstep（A、B 互不影响的另一半）。
            var overrideController = animatorB.runtimeAnimatorController as AnimatorOverrideController;
            Assert.IsNotNull(overrideController, "B（第二个、不同签名的 anim_set）应当被套上一层 AnimatorOverrideController 实现隔离");
            var overrideClip = overrideController![baseClip];
            Assert.IsNotNull(overrideClip, "覆盖控制器应当为 baseClip 登记一条覆盖映射");
            Assert.AreNotSame(baseClip, overrideClip, "覆盖剪辑必须是运行期克隆出的私有副本，不能就是共享资产本身");

            var overrideHitFrame = FindEvent(overrideClip!, "hit_frame");
            Assert.IsNotNull(overrideHitFrame);
            Assert.AreEqual(0.9 * overrideClip!.length, overrideHitFrame!.time, 0.0001f,
                "B 的私有覆盖剪辑上，hit_frame 应当是 B 自己声明的 0.9，不受共享资产/A 的配置影响");
            Assert.IsNull(FindEvent(overrideClip, "footstep"), "B 的私有覆盖剪辑不应该出现 A 专属声明的 footstep 事件");

            _host.Renderer3D.DestroyModelInstance(handleA);
            _host.Renderer3D.DestroyModelInstance(handleB);
        }

        /// <summary>同一 resource_ref、同一套事件配置（相同签名）被两个不同实体共同引用（最常见场景，
        /// 例如两个共享同一 display.anim_set 的实体）：应当直接复用共享资产，完全不需要任何覆盖控制器，
        /// 不产生不必要的运行期克隆。</summary>
        [Test]
        public void RegisterModelClipEvents_SameSignatureTwice_ReusesSharedClip_NoOverrideCreated()
        {
            var baseClip = LoadedBaseClip();

            var (handleA, animatorA) = CreateInstance();
            var (handleB, animatorB) = CreateInstance();
            var events = new[] { new AnimClipEventSpec("footstep", 0.2) };

            _factory.RegisterModelClipEventsForTest(new AnimClipDef(AttackClipRef, events), handleA);
            _factory.RegisterModelClipEventsForTest(new AnimClipDef(AttackClipRef, events), handleB);

            Assert.IsNull(animatorA.runtimeAnimatorController as AnimatorOverrideController);
            Assert.IsNull(animatorB.runtimeAnimatorController as AnimatorOverrideController);
            Assert.IsNotNull(FindEvent(baseClip, "footstep"));

            _host.Renderer3D.DestroyModelInstance(handleA);
            _host.Renderer3D.DestroyModelInstance(handleB);
        }
    }
}

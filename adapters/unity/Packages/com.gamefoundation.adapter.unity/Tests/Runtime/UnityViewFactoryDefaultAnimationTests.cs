#nullable enable
// UnityViewFactoryDefaultAnimationTests：外部审核阻塞项 3 收口验收（见
// architecture/落地计划/audit-20260907/followup-2026-09-07.md"外部审核阻塞项处理"一节）——
// UnityViewFactory.CreateView 默认为"生物"型 sprite 视图挂接 UnityFrameAnimPlayer +
// AnimClipResolver，使 Rig.PlayClip 在不需要 FrameworkResidentHost/GameFoundationBootstrap/
// games/_template.GameBootstrap 任何一方手工接线的情况下就能正确工作：移动/施法/受击三类状态
// 切换各自解析并播放到正确的剪辑（断言 IFrameAnimPlayer.CurrentClipId）。
//
// 判断记录（不经完整 FrameworkResidentHost/ShellRoot 装配，直接构造 UnityViewFactory 本体）：同
// AnimationLayerTests.cs 顶部判断记录——本文件验证的是 UnityViewFactory 这一个单元"默认动画接线"
// 这一具体行为，不需要完整游戏世界装配。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Core.Foundation.SimLoop;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

// 判断记录：UnityEngine 自身也有一个名为 DisplayInfo 的结构体（显示器信息，与本模块的
// Core.Foundation.DisplayInfo.DisplayInfo 外形信息完全是两回事），本文件同时 using
// Core.Foundation.DisplayInfo（命名空间，取 DisplayCategory/DisplayKind/SpriteInfo/ShadowMode
// 等其余成员）与 UnityEngine 两个命名空间时，裸写 "DisplayInfo" 会触发 CS0104 二义性——用
// 别名区分，惯例同 core/gameplay/tests/EndToEnd/GameWorldFixture.cs 顶部对 RealSaveSystem 的处理。
using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>只返回预先构造好的单条 <see cref="DisplayInfo"/> 的最小 <see cref="IDisplayInfoRegistry"/>
    /// 测试替身，不依赖真实 <c>DataRegistry</c>/<c>display.map</c> 数据（同本包既有测试"最小夹具"
    /// 惯例，见 <c>DiscreteCombatTests.cs</c> 一类完整装配之外，本文件只关心 UnityViewFactory 这一个
    /// 单元本身）。</summary>
    internal sealed class FakeDisplayInfoRegistryForAnim : IDisplayInfoRegistry
    {
        private readonly Dictionary<Id, DisplayInfo> _byLogicalId = new Dictionary<Id, DisplayInfo>();

        public void Add(DisplayInfo info) => _byLogicalId[info.LogicalId] = info;

        public DisplayInfo? Lookup(Id logicalId) => _byLogicalId.TryGetValue(logicalId, out var info) ? info : null;

        public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category)
        {
            var result = new List<DisplayInfo>();
            foreach (var info in _byLogicalId.Values)
            {
                if (info.Category == category) result.Add(info);
            }
            return result;
        }

        public IReadOnlyList<DisplayInfo> All => new List<DisplayInfo>(_byLogicalId.Values);

        public void Reload()
        {
        }
    }

    public sealed class UnityViewFactoryDefaultAnimationTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("UnityViewFactoryDefaultAnimationTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_rootGo);
        }

        private static (IEventBus Bus, DisplayInfo Info, Id EntityId, FakeDisplayInfoRegistryForAnim DisplayInfo) BuildFixture()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var info = DisplayInfoTestSupportForAnimTests.CreateSpriteInfo();
            var displayInfo = new FakeDisplayInfoRegistryForAnim();
            displayInfo.Add(info);

            return (bus, info, new Id("unit.default_anim_test_entity"), displayInfo);
        }

        /// <summary>核心验收：CreateView 默认给"生物"分类的 sprite 视图挂上
        /// UnityFrameAnimPlayer + AnimClipResolver，AnimStateMachine 收到移动/施法/受击三类状态
        /// 切换事件后，附着在该实体上的播放器应当分别切到 move/cast/hit 三个不同的默认剪辑
        /// （未提供 dataRegistry，全部走"单帧退化"路径——见 UnityViewFactory.RegisterDefaultClips
        /// 判断记录，退化路径同样应当为每个状态生成互不相同的 clipId，PlayClip 依然"默认可用"）。</summary>
        [Test]
        public void CreateView_ForCreatureCategory_MoveCastHit_PlayDistinctDefaultClips()
        {
            var (bus, info, entityId, displayInfo) = BuildFixture();
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);

            var view = factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            Assert.IsInstanceOf<UnitySpriteView>(view, "生物分类 + kind=sprite 应当创建 UnitySpriteView，不退化为 NullView");
            var spriteView = (UnitySpriteView)view;

            var root = _renderer.GetSpriteRoot(spriteView.EngineHandle);
            Assert.IsNotNull(root, "UnitySpriteView 构造完成后应当已经建好精灵根节点");
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "CreateView 默认应当给生物分类的 sprite 视图挂上 UnityFrameAnimPlayer 组件（外部审核阻塞项 3）");

            // 移动：unit.state_changed（MovementTickHandler 惯例，Idle -> Walk）应解出并播放 move 剪辑。
            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            var moveClipId = player!.CurrentClipId;
            Assert.IsTrue(moveClipId.HasValue, "移动状态切换后 IFrameAnimPlayer 应当已经在播放某个剪辑");
            Assert.IsTrue(moveClipId!.Value.Value.EndsWith(".move", StringComparison.Ordinal), $"应播放 move 剪辑，实际：{moveClipId.Value}");

            // 施法：skill.cast_start（castTime > 0）应解出并播放 cast 剪辑。
            bus.PublishImmediate(new Core.Rules.Common.SkillCastStartEvent(entityId, new Id("skill.default_anim_test_fireball"), castTime: 1.5));
            var castClipId = player.CurrentClipId;
            Assert.IsTrue(castClipId.HasValue);
            Assert.IsTrue(castClipId!.Value.Value.EndsWith(".cast", StringComparison.Ordinal), $"应播放 cast 剪辑，实际：{castClipId.Value}");
            Assert.AreNotEqual(moveClipId!.Value, castClipId.Value, "cast 剪辑应与 move 剪辑是不同的 clipId（默认接线按状态各自生成一条剪辑）");

            // 受击：combat.damage_dealt（targetId 命中本实体）应解出并播放 hit 剪辑。
            bus.PublishImmediate(new Core.Rules.Common.CombatDamageDealtEvent(
                new Id("unit.default_anim_test_attacker"), entityId, new Id("school.physical"), 5.0,
                isCrit: false, Core.Rules.Common.HitResult.Hit));
            var hitClipId = player.CurrentClipId;
            Assert.IsTrue(hitClipId.HasValue);
            Assert.IsTrue(hitClipId!.Value.Value.EndsWith(".hit", StringComparison.Ordinal), $"应播放 hit 剪辑，实际：{hitClipId.Value}");
            Assert.AreNotEqual(castClipId.Value, hitClipId.Value, "hit 剪辑应与 cast 剪辑是不同的 clipId");
        }

        /// <summary>非生物分类（item/gobj/projectile 一类）不应挂接默认动画——见
        /// UnityViewFactory.CreateView 判断记录"只给生物挂默认动画"。</summary>
        [Test]
        public void CreateView_ForNonCreatureCategory_DoesNotAttachAnimPlayer()
        {
            var (bus, _, entityId, displayInfo) = BuildFixture();

            var itemInfo = new DisplayInfo(
                id: new Id("display.map.default_anim_test_item"),
                category: DisplayCategory.Item,
                logicalId: new Id("item.default_anim_test_item"),
                kind: DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: new SpriteInfo(spriteSetId: "sprite.default_anim_test_item", directionCount: 4, paperdollLayers: new List<string>()),
                model: null);
            displayInfo.Add(itemInfo);

            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);
            var view = factory.CreateView(ViewKind.Unit, itemInfo.LogicalId, entityId);
            var spriteView = (UnitySpriteView)view;

            var root = _renderer.GetSpriteRoot(spriteView.EngineHandle);
            Assert.IsNotNull(root);
            Assert.IsNull(root!.GetComponentInChildren<UnityFrameAnimPlayer>(), "非生物分类不应该挂接默认动画（item/gobj/projectile 不会收到任何状态切换事件）");
        }

        // -----------------------------------------------------------------
        // U04 根治与回归（第五轮外部审核 audit-5e779c6-20260907/AUDIT_REPORT.md）：默认序列帧动画
        // 此前直接挂在精灵根物体自身上，其 SpriteRenderer 既不是 LayersRoot 的子物体（不受
        // SetTransform 的 height 偏移平移）也不在 LayerRenderers 集合里（ApplyColor 遍历不到，
        // flash/fade 不生效）。断言默认动画渲染器与纸娃娃层一样响应 height/flash/fade 三个原语，
        // 影子（本用例 DisplayInfo.Shadow=None，不涉及）仍按既有设计独立处理。
        // -----------------------------------------------------------------

        /// <summary>核心复现/回归：默认序列帧动画（AnimRoot）渲染器应当与纸娃娃层（body 层）同步响应
        /// height 偏移、flash 过曝、fade 透明度——三者任一失效即为 U04 复发。</summary>
        [Test]
        public void CreateView_AnimRootRenderer_RespondsToHeightFlashFade_LikePaperdollLayers()
        {
            var (bus, info, entityId, displayInfo) = BuildFixture();
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);
            var view = factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            var spriteView = (UnitySpriteView)view;
            // SyncPose 要求 IsAlive（Bind 之后才为真），见 SpriteViewBase.EnsureAlive、
            // AnimationLayerTests.CreateView 同款判断记录——CreateView 本身不自动 Bind。
            spriteView.Bind(entityId);

            // 合成出纸娃娃层（CreateSpriteInfo 声明了 1 层 "body"）——SyncPose 覆写在层集合非空时
            // 会自动调用基类 SetPaperdollLayers（见 UnitySpriteView.SyncPose 判断记录，同
            // AnimationLayerTests 一贯做法）。
            spriteView.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);

            var root = _renderer.GetSpriteRoot(spriteView.EngineHandle);
            Assert.IsNotNull(root);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");
            var animRenderer = player!.SpriteRenderer;

            var layersRoot = root.transform.Find("LayersRoot");
            Assert.IsNotNull(layersRoot, "应当已建好 LayersRoot 子物体");
            // 按 UnityRenderer2D.SetLayers 的命名约定（"Layer_{i}"）定位，而不是按兄弟顺序索引——
            // AttachDefaultAnimation 在 CreateView 期间早于本用例显式调用的 SyncPose 就已经把
            // "AnimRoot" 子物体建在 LayersRoot 下，body 层是随后才追加的兄弟节点，兄弟顺序不等于
            // 语义顺序。
            var bodyLayerTransform = layersRoot!.Find("Layer_0");
            Assert.IsNotNull(bodyLayerTransform, "body 纸娃娃层应当已合成");
            var bodyLayerRenderer = bodyLayerTransform!.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(bodyLayerRenderer);

            // U04 核心断言 1：AnimRoot 渲染器应当挂在 LayersRoot 下（不是根物体自己的平级组件），
            // 这样才能像纸娃娃层一样天然继承 height 偏移，不需要额外代码单独搬运它。
            Assert.AreEqual(
                layersRoot, animRenderer.transform.parent,
                "U04：默认动画渲染器应当挂在 LayersRoot 下才能吃到 height 偏移，不能停留在根物体自身");

            // U04 核心断言 2（height）：SetTransform 只平移 LayersRoot 的本地 Y——两个子渲染器
            // （body 层、AnimRoot）因此都随 Unity 变换层级一起移动，不需要分别断言世界坐标。
            _renderer.SetTransform(
                spriteView.EngineHandle, Vec2.Zero, height: 32.0, sortY: 0.0, layer: 0,
                rotation: 0.0, scale: 1.0, flipX: false);
            var expectedWorldOffset = 32.0f / _renderer.PixelsPerUnit;
            Assert.AreEqual(
                expectedWorldOffset, layersRoot.localPosition.y, 0.01f,
                "height 偏移应当平移 LayersRoot（AnimRoot 作为其子物体随之一起移动）");

            // U04 核心断言 3（flash）：SetShaderParam("flash_intensity", ...) 应当同时作用于纸娃娃层
            // 与 AnimRoot 渲染器，两者取同一份合成颜色。
            _renderer.SetShaderParam(spriteView.EngineHandle, "flash_intensity", 1.0);
            Assert.Greater(bodyLayerRenderer.color.r, 1.0f, "纸娃娃层应当过曝");
            Assert.Greater(animRenderer.color.r, 1.0f, "U04：AnimRoot 渲染器也应当同步过曝，不能不受 flash 影响");
            Assert.AreEqual(bodyLayerRenderer.color.r, animRenderer.color.r, 0.001f, "两者应当是同一份合成颜色");

            // U04 核心断言 4（fade）：SetShaderParam("fade_alpha", ...) 同理。
            _renderer.SetShaderParam(spriteView.EngineHandle, "fade_alpha", 0.4);
            Assert.AreEqual(0.4f, bodyLayerRenderer.color.a, 0.01f, "纸娃娃层应当淡出到目标透明度");
            Assert.AreEqual(0.4f, animRenderer.color.a, 0.01f, "U04：AnimRoot 渲染器也应当同步淡出，不能不受 fade 影响");
        }

        // -----------------------------------------------------------------
        // GP-02 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        // 默认动画此前只接了 StateChanged -> Play 这一半，播放完成从不回头通知 AnimStateMachine，
        // 也不订阅 unit.respawned/entity.destroyed 清理——受击/死亡后角色永久锁死，复活/销毁后旧
        // 播放器引用与状态记账悬空残留。
        // -----------------------------------------------------------------

        /// <summary>受击（Hit，瞬态优先级锁）播放完成后必须能再次进入 Move——修复前
        /// UnityFrameAnimPlayer.OnComplete 从不调用 AnimStateMachine.NotifyTransientStateFinished，
        /// Hit 状态会永久锁住后续全部状态切换（含本用例断言的 move）。</summary>
        [UnityTest]
        public IEnumerator CreateView_AfterHitClipCompletes_CanTransitionToMove()
        {
            var (bus, info, entityId, displayInfo) = BuildFixture();
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);
            var view = factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            var spriteView = (UnitySpriteView)view;
            var root = _renderer.GetSpriteRoot(spriteView.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();

            // 单帧剪辑（dataRegistry:null 走单帧退化路径）用 RegisterSingleFrameClip 登记，
            // frameRate 固定 1.0（见该方法判断记录），即单帧播放时长约 1 秒才会触发 OnComplete
            // ——用自己的订阅精确等待完成，不用固定帧数猜时长（不同机器/负载下每帧真实耗时不同，
            // 固定帧数在慢机器上可能等不到 1 秒）。
            var hitCompleted = false;
            player.OnComplete(() => hitCompleted = true);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.gp02_attacker"), entityId, new Id("school.physical"), 5.0,
                isCrit: false, HitResult.Hit));
            var hitClipId = player.CurrentClipId;
            Assert.IsTrue(hitClipId.HasValue);
            Assert.IsTrue(hitClipId!.Value.Value.EndsWith(".hit", StringComparison.Ordinal));

            var deadline = Time.realtimeSinceStartup + 5f;
            while (!hitCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.IsTrue(hitCompleted, "Hit 单帧剪辑（约 1 秒时长）应当在 5 秒超时前触发 OnComplete");

            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            var moveClipId = player.CurrentClipId;

            Assert.IsTrue(moveClipId.HasValue);
            Assert.IsTrue(
                moveClipId!.Value.Value.EndsWith(".move", StringComparison.Ordinal),
                $"Hit 剪辑播放完成后应当已经回落到 locomotion，move 状态切换应当生效（GP-02 根治），实际仍是：{moveClipId.Value}");
        }

        /// <summary>entity.destroyed 后同一个 entityId 被新实体复用（重新 CreateView）：新实例必须从
        /// Idle 开始，不能继承旧实体死亡前的终态锁——修复前本工厂从不订阅 entity.destroyed，
        /// AnimStateMachine 里的旧记账会一直残留。</summary>
        [UnityTest]
        public IEnumerator EntityDestroyed_SameIdReused_NewViewStartsFromIdle_NotStuckOnOldDeathLock()
        {
            var (bus, info, entityId, displayInfo) = BuildFixture();
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);

            var firstView = factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            var firstRoot = _renderer.GetSpriteRoot(((UnitySpriteView)firstView).EngineHandle);
            var firstPlayer = firstRoot!.GetComponentInChildren<UnityFrameAnimPlayer>();

            bus.PublishImmediate(new UnitDiedEvent(entityId, killerId: null));
            Assert.IsTrue(firstPlayer.CurrentClipId!.Value.Value.EndsWith(".death", StringComparison.Ordinal));

            bus.PublishImmediate(new EntityDestroyedEvent(entityId));
            firstView.Destroy();

            // 同一个 entityId 被一个全新实体复用（同真实场景"旧实体销毁、新实体在同一帧/后续帧
            // 生成、entityId 由 WorldSim 复用"）。
            var secondView = factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            var secondRoot = _renderer.GetSpriteRoot(((UnitySpriteView)secondView).EngineHandle);
            var secondPlayer = secondRoot!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.AreNotSame(firstPlayer, secondPlayer, "新实体应当挂接一个全新的播放器组件");

            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            var moveClipId = secondPlayer.CurrentClipId;

            Assert.IsTrue(moveClipId.HasValue);
            Assert.IsTrue(
                moveClipId!.Value.Value.EndsWith(".move", StringComparison.Ordinal),
                $"新实体应当能正常进入 move 状态（AnimStateMachine 记账已随 entity.destroyed 清空），实际：{moveClipId.Value}");

            yield break;
        }

        /// <summary>unit.respawned 后同一个播放器组件必须能重新进入 Move（不是"销毁重建"，是原地
        /// 复用）——修复前本工厂从不订阅 unit.respawned，死亡是终态锁，复活后角色会永久卡在死亡
        /// 姿势。</summary>
        [Test]
        public void UnitRespawned_SamePlayerInstance_CanTransitionToMoveAgain()
        {
            var (bus, info, entityId, displayInfo) = BuildFixture();
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);
            var view = factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            var root = _renderer.GetSpriteRoot(((UnitySpriteView)view).EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();

            bus.PublishImmediate(new UnitDiedEvent(entityId, killerId: null));
            Assert.IsTrue(player.CurrentClipId!.Value.Value.EndsWith(".death", StringComparison.Ordinal));

            bus.PublishImmediate(new UnitRespawnedEvent(entityId, RespawnPolicy.RespawnPoint));
            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));

            var moveClipId = player.CurrentClipId;
            Assert.IsTrue(moveClipId.HasValue);
            Assert.IsTrue(
                moveClipId!.Value.Value.EndsWith(".move", StringComparison.Ordinal),
                $"复活后同一播放器应当能正常进入 move 状态（GP-02 根治），实际仍是：{moveClipId.Value}");
        }

        // -----------------------------------------------------------------
        // GP-06 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        // display.anim_set 声明了 resource_ref、但 UnityResourceLoader 尚未加载进缓存（冷启动）时，
        // 修复前直接登记单帧 fallback、从不 LoadAsync，纸娃娃永久退化成白点。用真实
        // data/_sample/display/display.anim_set.sample_hero（引用的 anim.sample_hero_* 资源文件
        // 当前占位资产集里确实不存在，见该表判断记录）验证：冷启动仍然立即可用（不抛异常、
        // CurrentClipId 有值），且确实发起了一次真正的加载请求（不是"什么都不做的永久 fallback"）。
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator CreateView_ColdAnimSetResource_ImmediatelyUsable_AndTriggersRealLoadRequest()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var displayInfo = new DisplayInfoRegistry(registry, bus);
            var entityId = new Id("unit.gp06_cold_start_entity");
            var factory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), displayInfo, host.ResourceLoader, bus: bus, dataRegistry: registry);

            var view = factory.CreateView(ViewKind.Unit, new Id("creature.sample_hero"), entityId);
            var spriteView = (UnitySpriteView)view;
            var root = host.Renderer2D.GetSpriteRoot(spriteView.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();

            // 冷启动立即可用：不抛异常，Idle 状态已经登记了某个 clipId（单帧 fallback 或真实剪辑，
            // 取决于本次运行是否恰好已有缓存），Rig.PlayClip 不会因为资源没加载完就整个哑掉。
            Assert.IsTrue(player.HasClip(new Id($"anim.default.display.map.sample_hero.idle")));

            // GP-06 核心断言：确实发起了一次真正的加载请求（PendingLoadCount 短暂 > 0，或者最终
            // 收到加载完成/失败的诊断），不是"TryGetEffect 未命中就登记 fallback、此后再也不管"。
            var sawPendingLoad = host.ResourceLoader.PendingLoadCount > 0;
            var timeout = 5f;
            while (!sawPendingLoad && timeout > 0f)
            {
                sawPendingLoad = host.ResourceLoader.PendingLoadCount > 0;
                host.ResourceLoader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsTrue(
                sawPendingLoad,
                "GP-06 根治：display.anim_set 声明了 resource_ref 但缓存未命中时，必须实际发起一次 LoadAsync，" +
                "不能只登记单帧 fallback 就再也不管——PendingLoadCount 应当在加载排队期间短暂大于 0");

            view.Destroy();
        }
    }
}

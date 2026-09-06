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
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

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
            var player = root!.GetComponent<UnityFrameAnimPlayer>();
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
            Assert.IsNull(root!.GetComponent<UnityFrameAnimPlayer>(), "非生物分类不应该挂接默认动画（item/gobj/projectile 不会收到任何状态切换事件）");
        }
    }
}

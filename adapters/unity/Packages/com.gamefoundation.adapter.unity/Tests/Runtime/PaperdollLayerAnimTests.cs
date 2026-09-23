#nullable enable
// PaperdollLayerAnimTests：ADR-0072 决策 2 验收——纸娃娃层逐层动画。消费方反馈第十八批自我指出的
// 第二个"已知简化"：display.anim_set.clips.<state>.resource_ref 此前只驱动整身 AnimRoot 一份序列帧
// （UnityViewFactory.RegisterDefaultClips/AttachDefaultAnimation），纸娃娃层（身体/装备）在任何状态
// 切换期间都只有静态图，AnimRoot 的排序恒在全部纸娃娃层之上（UnityRenderer2D.ApplySortingOrders），
// 播放攻击/待机一类真正带多帧美术的状态时会整个盖住装备。本文件用真实 data/_sample/display 数据
// （display.anim_set.sample_hero 的 idle 状态 resource_ref 经 toolchain/import_sample_assets.py 的
// PER_LAYER_SPRITE_ANIM_CLIPS 生成了两层真正的多帧占位资源：head 层 tier1（方向+层名）、hand_main
// 层 tier2（仅层名），body 层刻意不给任何逐层资源）+ 真实 UnityViewFactory 完整生产装配路径验证：
//   1) 至少一层命中逐层剪辑时，整身 AnimRoot 渲染器应当被隐藏（不再"盖住"纸娃娃层）；
//   2) 命中逐层剪辑的层应当随共享时间轴帧号推进真的切换 SpriteRenderer.sprite；
//   3) 没有命中逐层剪辑的层（body）应当维持静态可见，不被触碰；
//   4) 没有任何一层命中逐层剪辑的状态（move）应当维持决策 2 之前就有的整身兜底行为，AnimRoot 保持
//      可见——不因为本次改动而对"没有逐层美术的游戏/状态"产生任何可观察差异。
// 断言直接读 Unity 场景对象（Transform.Find 定位 UnityRenderer2D.SetLayers/AttachDefaultAnimation
// 建的 "AnimRoot"/"Layer_<i>" 子物体，同 EquipmentVisualReplayTests.FindDeep 一贯的白盒 PlayMode
// 断言手法），不经任何中间层封装——满足"必须经真实生产装配入口验证"的要求。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class PaperdollLayerAnimTests : PlayModeTestBase
    {
        private static readonly Id SpriteHeroLogicalId = new Id("creature.sample_hero");

        // 与 toolchain/import_sample_assets.py PER_LAYER_SPRITE_ANIM_CLIPS 逐字节对应：head 层
        // tier1（方向 side_r + 层名 head），creature.sample_hero direction_count=8 时
        // UnityViewFactory.TryAttachPerLayerAnimation 挂接期用的默认朝向 Direction.FromQuantized(0,8)
        // 恰好解析到 side_r（同 SpriteViewBaseTests.RebuildEquippedLayers_AcrossThreeDirections_
        // ResolvesThreeDistinctResourceIds 验证过的同一套方向换算），不需要额外 SyncPose 到别的朝向。
        private static readonly Id HeadTier1ClipId = new Id("sprite_anim.sample_hero_idle__side_r__head");

        private (IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) BuildFixture()
        {
            // 同 SpriteEquipVisualWiringTests.BuildFixture 逐字节一致的既有夹具惯例。
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
            return (bus, registry, displayInfo, host);
        }

        private static Transform? FindChild(Transform root, string name)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        private static IEnumerator WaitUntilOrFail(Func<bool> condition, string failureMessage, float timeoutSeconds = 5f)
        {
            var elapsed = 0f;
            while (!condition())
            {
                if (elapsed >= timeoutSeconds)
                {
                    Assert.Fail(failureMessage);
                }
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        [UnityTest]
        public IEnumerator IdleState_HeadAndHandMainLayersResolve_HidesAnimRootAndAnimatesLayerFrames()
        {
            var fx = BuildFixture();
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry);

            var entityId = new Id("unit.per_layer_anim_idle_test");
            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);
            // creature.sample_hero direction_count=8（同 SpriteEquipVisualWiringTests 既有惯例）。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            // 等待逐层探测异步链路结算完成：UnityViewFactory.ProbeLayersSequential 按 PaperdollLayers
            // 顺序（body -> hand_main -> head）严格串行探测，head 是最后一层，它的 tier1 命中即代表
            // 全部层（含 body 的两级探测失败、hand_main 的 tier2 命中）都已经处理完毕。
            yield return WaitUntilOrFail(
                () => fx.Host.ResourceLoader.GetLoadProgress(HeadTier1ClipId) >= 1.0,
                "head 层 tier1 逐层剪辑应当在合理时间内异步加载完成（UnityViewFactory.ProbeLayersSequential）");

            // 真实触发一次 Idle 状态切换：AnimStateMachine.SetState 对"同状态重入"是空操作（新实体
            // 默认已经是 Idle），先切到 Move 再切回 Idle 才能真正触发一次
            // StateChangedWithSkill -> AnimClipResolver -> player.Play("idle" 对应 clipId, ...)。
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(entityId, "Idle", "Walk"));
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(entityId, "Walk", "Idle"));
            yield return null;

            Assert.IsInstanceOf<UnityRenderer2D>(fx.Host.Renderer2D);
            var concreteRenderer = (UnityRenderer2D)fx.Host.Renderer2D;
            var layersRoot = concreteRenderer.GetLayersRoot(view.EngineHandle);
            Assert.IsNotNull(layersRoot, "sample_hero 应当已经建好 LayersRoot 子物体");

            var animRootTransform = FindChild(layersRoot!, "AnimRoot");
            Assert.IsNotNull(animRootTransform, "AttachDefaultAnimation 应当已经挂接 AnimRoot 子物体");
            var animRootRenderer = animRootTransform!.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(animRootRenderer);
            Assert.IsFalse(animRootRenderer.enabled,
                "ADR-0072 决策 2 验收标准 2：idle 状态至少一层（head/hand_main）命中逐层剪辑时，" +
                "整身 AnimRoot 渲染器应当被隐藏，不再盖住纸娃娃层");

            // PaperdollLayers=["body","hand_main","head"]，SetLayers 按这一顺序建子物体
            // "Layer_0"/"Layer_1"/"Layer_2"（UnityRenderer2D.SetLayers 命名惯例），body=0、
            // hand_main=1、head=2。
            var headLayerTransform = FindChild(layersRoot!, "Layer_2");
            Assert.IsNotNull(headLayerTransform, "head 应当是纸娃娃层合成顺序里的第 3 层（下标 2）");
            var headRenderer = headLayerTransform!.GetComponent<SpriteRenderer>();
            var firstHeadSprite = headRenderer.sprite;
            Assert.IsNotNull(firstHeadSprite, "head 层逐层剪辑第 0 帧应当已经写入 SpriteRenderer.sprite");

            var handMainLayerTransform = FindChild(layersRoot!, "Layer_1");
            Assert.IsNotNull(handMainLayerTransform, "hand_main 应当是纸娃娃层合成顺序里的第 2 层（下标 1）");
            var handMainRenderer = handMainLayerTransform!.GetComponent<SpriteRenderer>();
            var firstHandMainSprite = handMainRenderer.sprite;

            // idle 剪辑 fps=4（frame_duration=0.25s，见 PER_LAYER_SPRITE_ANIM_CLIPS 生成器）：head
            // 2 帧循环、hand_main 3 帧循环，共享同一条时间轴（hand_main 帧数更多，被
            // ProbeLayersSequential 选为权威时间轴来源，见该方法判断记录）。判断记录（改用轮询等待、
            // 不用固定 sleep 时长再一次性断言）：canonical 时间轴帧号是 0/1/2/0/1/2/... 循环推进的，
            // head 按 2 取模、hand_main 按 3 取模——若用固定等待时长再断言，帧号恰好落在"对 2 取模后
            // 仍是 0"的时刻（如帧号=2）会让 head 断言假失败，这不是生产代码缺陷，是测试等待时长与
            // 取模周期撞在了一起的巧合（首次跑到过这个坑，故记录）。改为轮询直到贴图变化为止，
            // 超时时间给足一整圈以上（时间轴总时长 0.75s，含关键帧回绕），不依赖任何具体帧号落点。
            yield return WaitUntilOrFail(
                () => headRenderer.sprite != firstHeadSprite,
                "ADR-0072 决策 2 验收标准 2：head 层应当随共享时间轴帧号推进切换贴图（tier1 命中）",
                timeoutSeconds: 3f);
            yield return WaitUntilOrFail(
                () => handMainRenderer.sprite != firstHandMainSprite,
                "ADR-0072 决策 2 验收标准 2：hand_main 层应当随共享时间轴帧号推进切换贴图（tier2 命中）",
                timeoutSeconds: 3f);

            // 协调者第 2 项验收（2026-09-23）：逐层动画路径新写的帧应用逻辑
            // （UnityRenderer2D.SetLayerSprite）只写 SpriteRenderer.sprite，不碰 flipX——镜像方向
            // 档位下装备/纸娃娃层是否仍然跟着翻转是真会回归的点（另起一份贴图赋值逻辑很容易漏掉已有
            // 的 flipX 状态）。本用例整个只调用过一次 SyncPose，朝向是 Direction.FromQuantized(0.0, 8)
            // ——creature.sample_hero 的 display.map 显式登记了 mirror_pairs
            // {"direction_slot": "dir.side_l", "mirror_of": "dir.side_r", "flip_x": true}，quantized
            // 角度 0 对应的 canonical 档位正是 "dir.side_l"（8 方向量化表，见
            // SpriteViewBaseTests.RebuildEquippedLayers_AcrossThreeDirections_ResolvesThreeDistinctResourceIds
            // 同一断言过的角度换算），镜像回退解析到裸资源名 "side_r"（与本文件其余用例"side_r"资源
            // 断言一致，flipX 不编码进资源 Id，见 SpriteViewBase.ResolveLayerResourceId 判断记录）+
            // flipX=true——本用例因此天然就在镜像档位下运行，不需要额外切一次朝向。head 层正在被
            // 逐层动画驱动（上面两条断言已确认帧号在推进），此处断言它的 SpriteRenderer.flipX 仍然是
            // true，证明 SetLayerSprite 的帧写入没有绕过/重置 UnityRenderer2D.SetTransform 早先设好的
            // flipX 状态。
            Assert.IsTrue(headRenderer.flipX,
                "ADR-0072 决策 2：镜像方向档位下，命中逐层剪辑正在播放的装备/纸娃娃层 flipX 应当仍然" +
                "生效（逐层动画帧写入不应绕开既有的 SetTransform flipX 状态）");

            // 验收标准 3：body 层（下标 0）没有任何逐层剪辑（tier1/tier2 均未落地对应资产）——应当
            // 维持 SetLayers 落地的静态帧，保持可见，不被本次逐层动画驱动触碰。
            var bodyLayerTransform = FindChild(layersRoot!, "Layer_0");
            Assert.IsNotNull(bodyLayerTransform);
            var bodyRenderer = bodyLayerTransform!.GetComponent<SpriteRenderer>();
            Assert.IsTrue(bodyRenderer.enabled, "body 层没有逐层剪辑时应当保持可见（决策 2 第三级：维持静态）");
            Assert.IsNotNull(bodyRenderer.sprite, "body 层应当仍然显示 SetLayers 落地的静态纸娃娃层图");

            // 验收标准 4（不回归既有表现通道，一项代表性断言即可）：flash_intensity 经既有
            // IRenderer2D.SetShaderParam 通道应当继续对参与逐层动画的层生效——SetLayerSprite 复用的
            // 是 SetLayers 建的同一份 SpriteRenderer 对象，ApplyColor 遍历 LayerRenderers 时天然包含
            // 它，不需要任何额外接线。
            fx.Host.Renderer2D.SetShaderParam(view.EngineHandle, "flash_intensity", 2.0);
            Assert.Greater(headRenderer.color.r, 1.0f,
                "flash_intensity 应当继续对参与逐层动画的 head 层生效（决策 2 不新建平行渲染通道）");

            view.Destroy();
        }

        [UnityTest]
        public IEnumerator MoveState_NoLayerResolves_KeepsWholeBodyFallbackAnimRootVisible()
        {
            var fx = BuildFixture();
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry);

            var entityId = new Id("unit.per_layer_anim_move_fallback_test");
            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            // move 状态的 resource_ref（sprite_anim.sample_hero_move）没有配套 PER_LAYER_SPRITE_ANIM_CLIPS
            // 占位资产——三个层各自的 tier1/tier2 六次探测理应全部落空（决策 2 第三级）。给异步探测链路
            // 充分时间跑完（每层两级、三层共六次 LoadAsync 往返，均为确定的"文件不存在"快速失败）。
            yield return new WaitForSecondsRealtime(0.5f);

            fx.Bus.PublishImmediate(new UnitStateChangedEvent(entityId, "Idle", "Walk"));
            yield return null;

            var concreteRenderer = (UnityRenderer2D)fx.Host.Renderer2D;
            var layersRoot = concreteRenderer.GetLayersRoot(view.EngineHandle);
            Assert.IsNotNull(layersRoot);
            var animRootTransform = FindChild(layersRoot!, "AnimRoot");
            Assert.IsNotNull(animRootTransform);
            var animRootRenderer = animRootTransform!.GetComponent<SpriteRenderer>();

            Assert.IsTrue(animRootRenderer.enabled,
                "ADR-0072 决策 2：move 状态没有任何一层命中逐层剪辑时应当维持决策 2 之前就有的整身兜底" +
                "行为，AnimRoot 保持可见——不因为纸娃娃层逐层动画这一新能力而改变没有逐层美术的状态/游戏" +
                "的既有表现");

            view.Destroy();
        }
    }
}

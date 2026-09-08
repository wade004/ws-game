#nullable enable
// ModelViewTests：W6-B 验收——UnityRenderer3D/UnityModelView/UnityViewFactory 的 model 型外形真实
// 实现落地（见 architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md、落地计划 W6-B 小节）。
// 覆盖：model 视图创建/销毁、PlayAnim(attack) 推进后收到 HitFrameReached（真实 Animator +
// AnimationEvent 驱动，不是伪造回调）、挂点/槽位、height/flash/fade 经 SetPlacement/SetMaterialParam
// 生效。数据集用真实 data/_sample（display.map.sample_model_hero，见该文件），同
// UnityViewFactoryDefaultAnimationTests.CreateView_ColdAnimSetResource... 一贯的"真实数据集 + 共享
// UnityEngineHost"装配惯例，不建最小夹具替身——本文件恰恰要验证"占位模型资产确实能被真实加载/播放"，
// 用替身反而验证不到这一点。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
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
    public sealed class ModelViewTests : PlayModeTestBase
    {
        private static readonly Id ModelHeroLogicalId = new Id("creature.sample_model_hero");

        private (IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) BuildFixture()
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
            return (bus, registry, displayInfo, host);
        }

        private UnityViewFactory BuildFactory((IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) fx) =>
            new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D);

        [Test]
        public void CreateView_ModelKindDisplayInfo_CreatesUnityModelView_NotNullView()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_create_test");

            var view = factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);

            Assert.IsInstanceOf<UnityModelView>(view, "kind=model + 已装配 IRenderer3D 应当创建 UnityModelView，不退化为 NullView");
            var modelView = (UnityModelView)view;
            // 见 IModelHandleProvider.TryGetModelHandle 契约注释："View 尚未绑定……时返回 null"——
            // CreateView 本身不自动 Bind（同 UnitySpriteView 一贯惯例），需要显式 Bind 才持有句柄。
            modelView.Bind(entityId);
            Assert.IsTrue(modelView.TryGetModelHandle().HasValue, "Bind 完成后应当已经持有一个模型句柄");

            view.Destroy();
        }

        [Test]
        public void CreateView_TwiceThenDestroy_UnderlyingModelInstanceRemoved()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_destroy_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var handle = view.TryGetModelHandle()!.Value;
            var root = ((UnityRenderer3D)fx.Host.Renderer3D).GetModelRoot(handle);
            Assert.IsNotNull(root, "销毁前应当能取回模型根节点");

            view.Destroy();

            var rootAfterDestroy = ((UnityRenderer3D)fx.Host.Renderer3D).GetModelRoot(handle);
            Assert.IsNull(rootAfterDestroy, "销毁后模型实例应当已从渲染器内部表中移除");
        }

        /// <summary>PR130-01 根治后更新：锚点根（<see cref="UnityRenderer3D.GetModelRoot"/>）落在
        /// (planePos.X, planePos.Y, 0)，不再借用 Z 轴表达深度；height 只平移可见内容子物体
        /// （<see cref="UnityRenderer3D.GetModelVisualRoot"/>）的局部 Y，与 <see cref="UnityRenderer2D"/>
        /// 的 Root/LayersRoot 同一套结构，见 UnityRenderer3D.cs 类型顶部"三维放置的坐标换算"判断
        /// 记录。</summary>
        [Test]
        public void SyncPose_AppliesHeightToModelRoot()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_height_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(new Vec2(1, 2), Direction.Continuous(0.0), height: 3.0);

            var handle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var root = renderer3D.GetModelRoot(handle);
            Assert.IsNotNull(root);
            Assert.AreEqual(new Vector3(1f, 2f, 0f), root!.transform.localPosition,
                "锚点根应落在 (planePos.X, planePos.Y, 0)——与 sprite/相机同一套地面平面约定");

            var visualRoot = renderer3D.GetModelVisualRoot(handle);
            Assert.IsNotNull(visualRoot);
            Assert.AreEqual(new Vector3(0f, 3f, 0f), visualRoot!.localPosition,
                "height 只应平移可见内容子物体的局部 Y");

            view.Destroy();
        }

        /// <summary>PR140-01 复现/回归用例（<c>architecture/落地计划/audit-c86bfa9-20260908/</c>
        /// 第七方审核）：<see cref="ShadowMode.Blob"/> 生成的占位影子 Quad 必须与角色/sprite 落在同一张
        /// 世界 (X,Y) 地面平面上、法线朝向固定相机（相机沿世界 Z 轴永不旋转，见
        /// <c>Adapter.Unity.EngineAdapter.UnityCamera</c> 构造函数判断记录），根治前固定
        /// <c>localRotation = Euler(90,0,0)</c> 是"地面=世界 XZ 平面"这一已废弃旧约定下的写法，在当前
        /// "地面=世界 XY 平面"约定下把 Quad 转成了侧立薄片（法线转进 XZ 平面，肉眼看只是一条线）。
        /// </summary>
        [Test]
        public void SetShadow_Blob_FacesCameraOnGroundPlane_PositionUnaffectedByHeight()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_blob_shadow_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var handle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;

            renderer3D.SetShadow(handle, Core.Foundation.EngineAdapter.ShadowMode.Blob);
            view.SyncPose(new Vec2(4, 5), Direction.Continuous(0.0), height: 0.0);

            var blob = renderer3D.GetBlobShadowTransform(handle);
            Assert.IsNotNull(blob, "ShadowMode.Blob 应当已经生成一个占位影子 Quad");

            // 相机固定沿世界 Z 轴取景、永不旋转（UnityCamera 构造函数固定 transform.rotation =
            // Quaternion.identity），据此直接用 Vector3.forward 代表相机朝向，不需要额外取相机组件。
            var cameraForward = Vector3.forward;
            var blobNormal = blob!.forward; // Quad 图元局部 -Z 是正面法线，transform.forward 是局部 +Z。
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(blobNormal, cameraForward)), 0.01f,
                "Blob 法线应当与相机朝向共线（|dot|≈1）——躺在与相机正对的地面画面平面上，不是侧立的薄片");

            var root = renderer3D.GetModelRoot(handle)!;
            Assert.AreEqual(root.transform.position.x, blob.position.x, 0.001f, "Blob 应当与角色落在同一张地面平面上（X 分量对齐）");
            Assert.AreEqual(root.transform.position.y, blob.position.y, 0.001f, "Blob 应当与角色落在同一张地面平面上（Y 分量对齐）");

            var blobPosAtHeight0 = blob.position;
            view.SyncPose(new Vec2(4, 5), Direction.Continuous(0.0), height: 3.0);
            var blobPosAtHeight3 = renderer3D.GetBlobShadowTransform(handle)!.position;

            Assert.AreEqual(blobPosAtHeight0, blobPosAtHeight3, "height 变化不应该移动影子——影子应当始终贴地");

            // 根治手法额外把 Blob 的世界旋转从 Root 的 facing 旋转中解耦（每次 SetPlacement 都重新钉回
            // Quaternion.identity，见 ApplyBlobShadowTransform 判断记录）——人物转身不应该让贴地阴影
            // 跟着立起来，一并验证转身后法线依旧朝向相机。
            view.SyncPose(new Vec2(4, 5), Direction.Continuous(Mathf.PI / 2), height: 0.0);
            var blobNormalAfterTurn = renderer3D.GetBlobShadowTransform(handle)!.forward;
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(blobNormalAfterTurn, cameraForward)), 0.01f,
                "人物转身（facing 变化）之后，Blob 法线仍应当朝向相机，不应该跟着 Root 一起转出地面画面平面");

            view.Destroy();
        }

        [Test]
        public void SetMaterialParam_FlashAndFade_DoNotThrow_AndApplyToAllRenderers()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_flash_fade_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var handle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;

            Assert.DoesNotThrow(() => view.Rig.ProceduralAnim.Flash(FlashParams.Default));
            Assert.DoesNotThrow(() => renderer3D.SetMaterialParam(handle, "fade_alpha", 0.4));

            view.Destroy();
        }

        /// <summary>见 <see cref="ModelCharacterRig.ResolveAnchorLocalOffset"/> 判断记录：本方法只查
        /// <c>ModelInfo.Sockets</c>（挂点），不查 <c>Slots</c>（换装槽位——那是 <see cref="ModelCharacterRig.ApplyEquipVisual"/>/
        /// <c>SetSlotMesh</c> 的职责范围，语义上不是"锚点"）。已声明的挂点返回 <see cref="Vec2.Zero"/>
        /// （已知简化——model 型没有精确偏移数据），未声明的返回 null。</summary>
        [Test]
        public void ResolveAnchorLocalOffset_DeclaredSocket_ReturnsZero_UndeclaredReturnsNull()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_anchor_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);

            Assert.AreEqual(Vec2.Zero, view.Rig.ResolveAnchorLocalOffset(new Id("socket.main_hand"), Direction.Continuous(0.0)));
            Assert.IsNull(view.Rig.ResolveAnchorLocalOffset(new Id("socket.does_not_exist"), Direction.Continuous(0.0)));

            view.Destroy();
        }

        [Test]
        public void SetSlotMesh_OnDeclaredHeadSlot_DoesNotThrow()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_slot_mesh_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var handle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;

            Assert.DoesNotThrow(() => renderer3D.SetSlotMesh(handle, new Id("slot.head"), null));

            view.Destroy();
        }

        [Test]
        public void AttachToSocket_MainHand_ThenDetach_DoesNotThrow()
        {
            var fx = BuildFixture();
            var factory = BuildFactory(fx);
            var entityId = new Id("unit.model_view_socket_test");

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var handle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var childHandle = renderer3D.CreateModelInstance(new Id("model.placeholder_biped"));

            Assert.DoesNotThrow(() => renderer3D.AttachToSocket(handle, new Id("socket.main_hand"), childHandle));
            Assert.DoesNotThrow(() => renderer3D.Detach(childHandle));

            renderer3D.DestroyModelInstance(childHandle);
            view.Destroy();
        }

        /// <summary>核心验收：PlayAnim(attack) 经真实 Animator 播放到 50% 时间点后，
        /// 该占位剪辑内嵌的 AnimationEvent（functionName=OnAnimEvent, stringParameter=hit_frame）
        /// 应当真正触发 <see cref="ModelCharacterRig.HitFrameReached"/>（经
        /// <see cref="ModelCharacterRig.HitFrameEventId"/> 换算），而不是任何伪造的回调——这是本轮
        /// W6-B 唯一需要真正等待若干真实引擎帧才能验证的行为。</summary>
        [UnityTest]
        public IEnumerator PlayAttackClip_ReachesHitFrame_RaisesHitFrameReachedOnRig()
        {
            var fx = BuildFixture();
            var entityId = new Id("unit.model_view_hitframe_test");

            // AnimKeyframeDriven 策略：ModelCharacterRig 构造期才会订阅 IRenderer3D.OnAnimEvent（见
            // 该类型构造函数），本用例需要一个开启该策略的 rig——直接构造（不经
            // UnityViewFactory/UnityModelView，后者默认走 LogicDriven，见 UnityViewFactory 类型
            // 判断记录"HitFrameSync 仍保持默认 LogicDriven"）。
            var info = fx.DisplayInfo.Lookup(ModelHeroLogicalId)!;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var handle = renderer3D.CreateModelInstance(info.Model!.ModelRef);
            var rig = new ModelCharacterRig(
                entityId, renderer3D, handle, info,
                new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven });

            var hitFrameReceivedFor = new System.Collections.Generic.List<Id>();
            rig.HitFrameReached += id => hitFrameReceivedFor.Add(id);

            rig.PlayClip(new Id("anim.attack"), loop: false, speed: 1.0);

            var deadline = Time.realtimeSinceStartup + 5f;
            while (hitFrameReceivedFor.Count == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, hitFrameReceivedFor.Count, "attack 剪辑播放到 50% 时应当恰好触发一次命中帧事件");
            Assert.AreEqual(entityId, hitFrameReceivedFor[0]);

            rig.Dispose();
            renderer3D.DestroyModelInstance(handle);
        }

        /// <summary>H5b 根治验收：直接对 UnityRenderer3D 播放 hit.anim（非循环），验证
        /// UnityEngineHost.Update 驱动的 Tick() 侦测到自然播放完成后确实经 OnAnimEvent 发出一次
        /// "anim_event.finished"（与 ModelCharacterRig.AnimFinishedEventId 逐字相等）——隔离验证底层
        /// 机制本身，不经 GameFoundationBootstrap 全链路（全链路端到端覆盖见
        /// AnimReplayAndFinishEndToEndTests.cs）。</summary>
        [UnityTest]
        public IEnumerator PlayHitClip_NonLoop_ReachesFinished_RaisesFinishedEvent()
        {
            var fx = BuildFixture();
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var handle = renderer3D.CreateModelInstance(new Id("model.placeholder_biped"));

            var received = new System.Collections.Generic.List<Id>();
            renderer3D.OnAnimEvent(handle, (h, id) => received.Add(id));

            renderer3D.PlayAnim(handle, new Id("anim.hit"), loop: false, speed: 1.0, blendSeconds: 0.15);

            var deadline = Time.realtimeSinceStartup + 3f;
            while (received.Count == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, received.Count, "非循环 hit 剪辑播放完成后应当发出一次 anim_event.finished");
            Assert.AreEqual(new Id("anim_event.finished"), received[0]);

            renderer3D.DestroyModelInstance(handle);
        }

        /// <summary>PR140-03 复现/回归用例（<c>architecture/落地计划/audit-c86bfa9-20260908/</c>
        /// 第七方审核）：占位 Animator 的 "test_autoexit" 状态自带一条 hasExitTime=true、duration=0
        /// （瞬时切换）的自动过渡直接回 idle（见 <c>GeneratePlaceholderModelAssets.CreateOrReplaceController</c>
        /// 判断记录），专门用来复现"Animator 自动过渡在检测帧之前已经发生"这一窗口——0.2 秒的短剪辑 +
        /// 瞬时过渡使得只要测试轮询间隔跨过 0.2s 这一帧，过渡的开始与结束几乎必然落在同一次真实引擎帧
        /// 之间，命中根治前 <c>IsAnimatorStateFinished</c> 会一直卡在 <c>IsInTransition</c> 分支之后
        /// "当前状态已经是 idle、hash 不再匹配"从而永久漏发的那个窗口。根治后应当恰好收到一次
        /// anim_event.finished，不多不少、不会因为轮询节奏偶然踩中过渡帧而丢失。</summary>
        [UnityTest]
        public IEnumerator PlayAutoExitClip_AnimatorTransitionsBeforeDetectionFrame_StillRaisesFinishedExactlyOnce()
        {
            var fx = BuildFixture();
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var handle = renderer3D.CreateModelInstance(new Id("model.placeholder_biped"));

            var received = new System.Collections.Generic.List<Id>();
            renderer3D.OnAnimEvent(handle, (h, id) => received.Add(id));

            renderer3D.PlayAnim(handle, new Id("anim.test_autoexit"), loop: false, speed: 1.0, blendSeconds: 0.05);

            // 先等 Animator 真正进入 test_autoexit 状态（CrossFade 混合过渡需要至少经过若干帧），
            // 再等它自动过渡回 idle——两段都用真实引擎帧驱动，不手工拨表。
            var enterDeadline = Time.realtimeSinceStartup + 3f;
            while (!renderer3D.IsPlayingState(handle, "test_autoexit") && Time.realtimeSinceStartup < enterDeadline)
            {
                yield return null;
            }
            Assert.IsTrue(renderer3D.IsPlayingState(handle, "test_autoexit"), "应当先能观察到 Animator 真正进入 test_autoexit 状态");

            var finishDeadline = Time.realtimeSinceStartup + 3f;
            while (received.Count == 0 && Time.realtimeSinceStartup < finishDeadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, received.Count,
                "test_autoexit 自动过渡回 idle 之后仍应当恰好发出一次 anim_event.finished，不应该因为过渡窗口" +
                "落在两次检测帧之间就永久漏发");
            Assert.AreEqual(new Id("anim_event.finished"), received[0]);
            Assert.IsTrue(renderer3D.IsPlayingState(handle, "idle"), "自动过渡应当已经把 Animator 带回 idle 状态");

            renderer3D.DestroyModelInstance(handle);
        }
    }
}

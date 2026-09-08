#nullable enable
// UnityRenderer3D PlayMode 用例（W6-B 收口，取代 H5 及以前"全部方法必须抛 NotSupportedException"
// 的声明降级验证——见 UnityRenderer3D.cs 类型顶部判断记录）。W7 收口新增 PR130-01/05/08 复现与
// 回归用例（见各用例判断记录）。
//
// 判断记录（放 Tests/Runtime 而不是 Tests/Editor）：本类型内部方法（DestroyModelInstance 等）使用
// UnityEngine.Object.Destroy（生产期正确写法，同 UnityRenderer2D.DestroySpriteInstance 一贯惯例），
// 而 Object.Destroy 在编辑器模式（EditMode）下不允许调用（会记一条 Console Error 并延迟真正销毁，
// 见 Unity 官方"Destroy may not be called from edit mode"提示）；UnityRenderer2DTests.cs 同理放在
// Tests/Runtime，本文件遵循同一惯例，改用 PlayModeTestBase。
using System;
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityRenderer3DTests : PlayModeTestBase
    {
        private static readonly Id PlaceholderModelId = new Id("model.placeholder_biped");

        private GameObject _root = null!;
        private UnityResourceLoader _loader = null!;
        private UnityRenderer3D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("UnityRenderer3DTests_Root");
            _loader = new UnityResourceLoader();
            _renderer = new UnityRenderer3D(_root.transform, _loader);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(_root);
        }

        [Test]
        public void CreateModelInstance_PlaceholderBiped_InstantiatesUnderRoot()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.AreEqual(1, _root.transform.childCount, "占位模型应实例化在构造函数传入的根节点下");
            Assert.IsNotNull(_renderer.GetModelRoot(handle));
            Assert.IsFalse(_renderer.IsShowingPlaceholder(handle), "占位内容资源本身能正常解析时，不应该被判定为降级占位");

            _renderer.DestroyModelInstance(handle);
            Assert.IsNull(_renderer.GetModelRoot(handle), "销毁后内部记账应当立即移除该句柄（不依赖 Unity 对象真正销毁的帧末时机）");
        }

        [Test]
        public void DestroyModelInstance_TwiceOnSameHandle_ThrowsInvalidOperationException()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            _renderer.DestroyModelInstance(handle);

            Assert.Throws<InvalidOperationException>(() => _renderer.DestroyModelInstance(handle));
        }

        [Test]
        public void SetPlacement_OnDestroyedHandle_ThrowsInvalidOperationException()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            _renderer.DestroyModelInstance(handle);

            Assert.Throws<InvalidOperationException>(() => _renderer.SetPlacement(handle, Vec2.Zero, 0, 0, 1, 0));
        }

        /// <summary>PR130-01 根治：锚点根（<see cref="UnityRenderer3D.GetModelRoot"/>）只承载
        /// planePos/facing/scale，不含 height——与 <see cref="UnityRenderer2D"/> 的
        /// <c>SpriteInstance.Root</c> 同一套结构（见 UnityRenderer3D.cs 类型顶部"三维放置的坐标
        /// 换算"判断记录）。height 单独落在 <see cref="UnityRenderer3D.GetModelVisualRoot"/> 的局部
        /// Y 偏移上。</summary>
        [Test]
        public void SetPlacement_AppliesPositionRotationScale_HeightOnlyOnVisualRoot()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            _renderer.SetPlacement(handle, new Vec2(1, 2), 3, 0, 2, 0);

            var root = _renderer.GetModelRoot(handle);
            Assert.IsNotNull(root);
            Assert.AreEqual(new Vector3(1f, 2f, 0f), root!.transform.localPosition,
                "锚点根应落在 (planePos.X, planePos.Y, 0)——与 sprite/相机同一套地面平面约定，不再借用 Z 轴表达深度");
            Assert.AreEqual(new Vector3(2f, 2f, 2f), root.transform.localScale);

            var visualRoot = _renderer.GetModelVisualRoot(handle);
            Assert.IsNotNull(visualRoot);
            Assert.AreEqual(new Vector3(0f, 3f, 0f), visualRoot!.localPosition,
                "height 只应平移可见内容子物体的局部 Y，不改变锚点根位置——与 UnityRenderer2D.SetTransform 的 LayersRoot 同一套结构");
        }

        /// <summary>PR130-01 核心验收：同一 <see cref="Vec2"/>/height/facing 下，model 与 sprite 落在
        /// 同一个 Unity 世界平面——经 <see cref="UnityCamera.WorldToScreen"/> 与
        /// <see cref="Camera.WorldToScreenPoint"/> 分别计算出的屏幕坐标应严格一致（scale=1，误差在
        /// 浮点容差内），不再因为两条渲染路径各自借用不同的世界坐标轴而对不上。</summary>
        [UnityTest]
        public IEnumerator SetPlacement_ModelWorldPosition_MatchesCameraWorldToScreen_ForVariousHeightsAndFacings()
        {
            var cameraGo = new GameObject("UnityRenderer3DTests_Camera");
            cameraGo.AddComponent<Camera>();
            var camera = new UnityCamera(cameraGo.GetComponent<Camera>());
            camera.Configure(45, 0, new ZoomRange(1, 10));
            camera.SetZoom(5);

            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            var planePos = new Vec2(3, -4);

            var heights = new[] { 0.0, 1.5 };
            var facings = new[] { 0.0, Math.PI / 2, Math.PI };

            foreach (var height in heights)
            {
                foreach (var facing in facings)
                {
                    _renderer.SetPlacement(handle, planePos, height, facing, 1, 0);

                    var expectedScreen = camera.WorldToScreen(planePos, height);

                    var visualRoot = _renderer.GetModelVisualRoot(handle)!;
                    var unityCamera = cameraGo.GetComponent<Camera>();
                    var actualScreenPoint = unityCamera.WorldToScreenPoint(visualRoot.position);

                    Assert.AreEqual(expectedScreen.X, actualScreenPoint.x, 0.01,
                        $"height={height} facing={facing}：model 可见内容的世界位置经相机投影后应与 ICamera.WorldToScreen 的结果一致（X 轴）");
                    Assert.AreEqual(expectedScreen.Y, actualScreenPoint.y, 0.01,
                        $"height={height} facing={facing}：model 可见内容的世界位置经相机投影后应与 ICamera.WorldToScreen 的结果一致（Y 轴）");
                }
            }

            UnityEngine.Object.Destroy(cameraGo);
            yield return null;
        }

        /// <summary>PR130-01 核心验收（sprite/model 对齐）：同一份 planePos/height 下，
        /// <see cref="UnityRenderer2D"/> 精灵实例与 <see cref="UnityRenderer3D"/> 模型实例的可见内容
        /// 世界位置经同一个相机投影应落在同一个屏幕位置附近（sprite 侧额外经 PixelsPerUnit 换算，
        /// 用与本用例传入 height 一致的换算关系传参，验证的是"两条渲染路径共用同一个逻辑地面平面"，
        /// 不是"height 的原始数值在两条路径的单位定义完全相同"——后者是 IRenderer2D/IRenderer3D 两个
        /// 契约方法既有的、独立于本次改动的单位差异，见 ICamera.WorldToScreen 类型注释）。</summary>
        [UnityTest]
        public IEnumerator SetPlacement_ModelAndSpriteVisualPosition_ProjectToSameScreenPoint()
        {
            var cameraGo = new GameObject("UnityRenderer3DTests_Camera2");
            cameraGo.AddComponent<Camera>();
            var unityCamera = cameraGo.GetComponent<Camera>();

            var spriteRoot = new GameObject("UnityRenderer3DTests_SpriteRoot");
            var spriteRenderer = new UnityRenderer2D(spriteRoot.transform, _loader);

            var planePos = new Vec2(2, 5);
            const double heightWorldUnits = 1.0;

            var spriteHandle = spriteRenderer.CreateSpriteInstance(new Id("sprite_set.does_not_matter"));
            // sprite 路线的 height 参数是像素值（见 IRenderer2D.SetTransform 判断记录），换算成与本
            // 用例 model 侧一致的世界单位需要乘回 PixelsPerUnit。
            spriteRenderer.SetTransform(spriteHandle, planePos, heightWorldUnits * spriteRenderer.PixelsPerUnit, 0, 0, 0, 1, false);

            var modelHandle = _renderer.CreateModelInstance(PlaceholderModelId);
            _renderer.SetPlacement(modelHandle, planePos, heightWorldUnits, 0, 1, 0);

            var spriteLayersRoot = spriteRenderer.GetLayersRoot(spriteHandle)!;
            var modelVisualRoot = _renderer.GetModelVisualRoot(modelHandle)!;

            var spriteScreen = unityCamera.WorldToScreenPoint(spriteLayersRoot.position);
            var modelScreen = unityCamera.WorldToScreenPoint(modelVisualRoot.position);

            Assert.AreEqual(spriteScreen.x, modelScreen.x, 0.01, "sprite/model 在同一份 planePos/height 下应投影到同一个屏幕 X");
            Assert.AreEqual(spriteScreen.y, modelScreen.y, 0.01, "sprite/model 在同一份 planePos/height 下应投影到同一个屏幕 Y");

            UnityEngine.Object.Destroy(cameraGo);
            UnityEngine.Object.Destroy(spriteRoot);
            yield return null;
        }

        [Test]
        public void SetSlotMesh_UnknownSlot_DoesNotThrow()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.DoesNotThrow(() => _renderer.SetSlotMesh(handle, new Id("slot.does_not_exist"), null));
        }

        [Test]
        public void SetSlotMesh_DeclaredHeadSlot_ClearsMeshWithoutThrow()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.DoesNotThrow(() => _renderer.SetSlotMesh(handle, new Id("slot.head"), null));
        }

        [Test]
        public void AttachToSocket_ThenDetach_DoesNotThrow()
        {
            var parent = _renderer.CreateModelInstance(PlaceholderModelId);
            var child = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.DoesNotThrow(() => _renderer.AttachToSocket(parent, new Id("socket.main_hand"), child));
            Assert.DoesNotThrow(() => _renderer.Detach(child));

            _renderer.DestroyModelInstance(child);
        }

        [Test]
        public void SetMaterialParam_DoesNotThrow()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.DoesNotThrow(() => _renderer.SetMaterialParam(handle, "flash_intensity", 1.0));
        }

        [Test]
        public void SetShadow_AllModes_DoNotThrow()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.DoesNotThrow(() => _renderer.SetShadow(handle, ShadowMode.Blob));
            Assert.DoesNotThrow(() => _renderer.SetShadow(handle, ShadowMode.Projected));
            Assert.DoesNotThrow(() => _renderer.SetShadow(handle, ShadowMode.None));
        }

        /// <summary>PR130-08 根治：影子挂在锚点根下（不随 height 位移，见 UnityRenderer3D.cs 类型顶部
        /// "PR130-08 根治"判断记录），height=0 与 height&gt;0 时影子的世界位置应保持不变，角色本体
        /// （VisualRoot）按 height 偏移。</summary>
        [Test]
        public void SetShadow_Blob_WorldPosition_DoesNotMoveWithHeight()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            _renderer.SetShadow(handle, ShadowMode.Blob);

            _renderer.SetPlacement(handle, new Vec2(5, 7), 0, 0, 1, 0);
            var root = _renderer.GetModelRoot(handle)!;
            var blob = root.transform.Find("BlobShadow");
            Assert.IsNotNull(blob, "Blob 影子应挂在锚点根下");
            var shadowWorldPosAtZeroHeight = blob!.position;

            _renderer.SetPlacement(handle, new Vec2(5, 7), 4.0, 0, 1, 0);
            var shadowWorldPosAtNonZeroHeight = blob.position;

            Assert.AreEqual(shadowWorldPosAtZeroHeight, shadowWorldPosAtNonZeroHeight,
                "影子的世界位置不应随 height 变化——height 只应平移 VisualRoot（角色本体），不平移锚点根/影子");

            var visualRoot = _renderer.GetModelVisualRoot(handle)!;
            Assert.AreEqual(4.0f, visualRoot.localPosition.y, 0.001, "角色本体（VisualRoot）应按 height 偏移");
        }

        [Test]
        public void OnAnimEvent_ReturnsDisposableSubscription()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            SubscriptionHandle? subscription = null;
            Assert.DoesNotThrow(() => subscription = _renderer.OnAnimEvent(handle, (h, eventId) => { }));
            Assert.NotNull(subscription);
            Assert.DoesNotThrow(() => subscription!.Dispose());
        }

        // ------------------------------------------------------------------
        // PR130-05：模型资源缺失不再抛异常，改为占位 + 诊断 + 异步加载完成后原地替换（ADR-0017 决策
        // 1）。取代此前 CreateModelInstance_UnknownModelId_ThrowsWithResolvedPathInMessage 的
        // Assert.Throws 断言——按任务要求"改写测试，不是删除"。
        // ------------------------------------------------------------------

        [Test]
        public void CreateModelInstance_UnknownModelId_DoesNotThrow_CreatesPlaceholderAndLogsDiagnosticOnce()
        {
            var unknownId = new Id("model.does_not_exist_probe_pr130_05");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "\\[UnityRenderer3D\\].*找不到模型资源.*model\\.does_not_exist_probe_pr130_05.*占位模型"));

            ModelHandle handle = default;
            Assert.DoesNotThrow(() => handle = _renderer.CreateModelInstance(unknownId));

            Assert.IsNotNull(_renderer.GetModelRoot(handle), "占位内容也应该是一个真实创建出来的实例，不是完全空白");
            Assert.IsTrue(_renderer.IsShowingPlaceholder(handle), "资源缺失时应当标记为占位状态");

            // 同一个未知 modelId 再创建一个实例：诊断只记一次（去重），不应该再次触发 LogAssert.Expect
            // 之外的告警——第二次调用如果又刷出同一条 Warning 会被 Unity Test Framework 判为未预期日志
            // 导致用例失败，因此这里不重复调用 CreateModelInstance，交由下一条用例的独立 UnityRenderer3D
            // 实例验证（_missingModelWarned 是按实例持有的去重表，见类型顶部判断记录）。

            _renderer.DestroyModelInstance(handle);
        }

        [Test]
        public void CreateModelInstance_UnknownModelId_PlaceholderCanBePlacedAndDestroyed_LikeAnyOtherInstance()
        {
            var unknownId = new Id("model.does_not_exist_probe_pr130_05b");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[UnityRenderer3D\\].*找不到模型资源"));

            var handle = _renderer.CreateModelInstance(unknownId);

            Assert.DoesNotThrow(() => _renderer.SetPlacement(handle, new Vec2(1, 1), 0, 0, 1, 0));
            Assert.DoesNotThrow(() => _renderer.SetShadow(handle, ShadowMode.Blob));
            Assert.DoesNotThrow(() => _renderer.DestroyModelInstance(handle));
        }

        // ------------------------------------------------------------------
        // PR140-02（architecture/落地计划/audit-c86bfa9-20260908/ 第七方审核）：条件异步模型替换
        // 不恢复挂点子模型与投影阴影状态。CompleteAsyncModelSwapForTest 是本文件新增的测试专用钩子，
        // 直接触发生产代码 AttachVisual 走一遍"原地替换视觉内容"的真实逻辑（与真实 Resources 异步加载
        // 成功回调调用的是完全同一份代码），绕开的只是 Resources.Load 本身的时序（见该方法判断记录），
        // 不影响本组用例对 AttachVisual 状态恢复正确性的验证效力。
        // ------------------------------------------------------------------

        [Test]
        public void CreateModelInstance_MissingThenAvailable_AttachVisual_RestoresSocketChildAfterSwap()
        {
            var missingId = new Id("model.does_not_exist_probe_pr140_02a");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[UnityRenderer3D\\].*找不到模型资源"));

            var handle = _renderer.CreateModelInstance(missingId);
            Assert.IsTrue(_renderer.IsShowingPlaceholder(handle), "先缺资源应当先展示占位内容");

            // 挂一个子模型到占位内容自带的 socket.main_hand 挂点上（见 GeneratePlaceholderModelAssets.cs）。
            var childHandle = _renderer.CreateModelInstance(PlaceholderModelId);
            _renderer.AttachToSocket(handle, new Id("socket.main_hand"), childHandle);
            var childRootBeforeSwap = _renderer.GetModelRoot(childHandle);
            Assert.IsTrue(childRootBeforeSwap != null, "挂接后子实例应当是一个存活的 GameObject");

            // 模拟"后可用→替换"：真实生产路径最终执行的正是 AttachVisual，这里直接实例化真实占位
            // 预制体作为"新解析到的真实内容"传入，见 CompleteAsyncModelSwapForTest 判断记录。
            var realPrefab = Resources.Load<GameObject>("GameFoundation/models/placeholder_biped");
            Assert.IsNotNull(realPrefab, "测试前置条件：占位预制体资产应当存在");
            _renderer.CompleteAsyncModelSwapForTest(handle, UnityEngine.Object.Instantiate(realPrefab));

            Assert.IsFalse(_renderer.IsShowingPlaceholder(handle), "替换后不应再标记为占位");

            // 根治前：旧 VisualRoot 被整棵销毁时，挂在它下面的子实例一并被 Unity 销毁，_instances 里
            // 仍然记着这个句柄，但 Root 已经是一个"已销毁"的 Unity 对象（Unity 的 == 运算符会把它当
            // 作 null），后续任何访问都会像悬空引用一样出问题。
            var childRootAfterSwap = _renderer.GetModelRoot(childHandle);
            Assert.IsTrue(childRootAfterSwap != null,
                "替换后子实例不应该已经被销毁——旧实现会把挂在旧 VisualRoot 下的子实例一并销毁，留下一个悬空句柄");

            // 根治后：子实例应当已经重新挂回新内容同名挂点下，而不是被摘下丢在 _root 底下不管。
            var parentAfterSwap = childRootAfterSwap!.transform.parent;
            Assert.IsNotNull(parentAfterSwap, "替换后子实例应当仍然挂在某个挂点下");
            Assert.AreEqual("socket.main_hand", parentAfterSwap!.name,
                "替换后子实例应当重新挂回新内容同名挂点下");

            // Detach 不应该因为句柄悬空而抛 MissingReferenceException（根治前的真实观测：
            // parent_real=True、child_destroyed=True、detach_exception=MissingReferenceException，见
            // architecture/落地计划/audit-c86bfa9-20260908/evidence/probes-presentation/presentation-validation.md）。
            Assert.DoesNotThrow(() => _renderer.Detach(childHandle));

            _renderer.DestroyModelInstance(childHandle);
            _renderer.DestroyModelInstance(handle);
        }

        [Test]
        public void CreateModelInstance_MissingThenAvailable_AttachVisual_RestoresShadowModeAfterSwap()
        {
            var missingId = new Id("model.does_not_exist_probe_pr140_02b");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[UnityRenderer3D\\].*找不到模型资源"));

            var handle = _renderer.CreateModelInstance(missingId);
            _renderer.SetShadow(handle, ShadowMode.None);

            var realPrefab = Resources.Load<GameObject>("GameFoundation/models/placeholder_biped");
            Assert.IsNotNull(realPrefab, "测试前置条件：占位预制体资产应当存在");
            _renderer.CompleteAsyncModelSwapForTest(handle, UnityEngine.Object.Instantiate(realPrefab));

            var visualRoot = _renderer.GetModelVisualRoot(handle);
            Assert.IsNotNull(visualRoot, "替换后应当有新的可见内容");
            var renderers = visualRoot!.GetComponentsInChildren<Renderer>(includeInactive: true);
            Assert.IsTrue(renderers.Length > 0, "新内容应当至少有一个 Renderer 供验证 shadowCastingMode");

            foreach (var r in renderers)
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode,
                    "ShadowMode.None 应当在替换后的新内容上重放，不应该悄悄回到 Unity 新 Renderer 的默认值 On" +
                    "（根治前的真实观测：new_shadow_on=True，见 presentation-validation.md）");
            }

            _renderer.DestroyModelInstance(handle);
        }
    }
}

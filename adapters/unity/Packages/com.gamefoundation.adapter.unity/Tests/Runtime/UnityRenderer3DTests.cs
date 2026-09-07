#nullable enable
// UnityRenderer3D PlayMode 用例（W6-B 收口，取代 H5 及以前"全部方法必须抛 NotSupportedException"
// 的声明降级验证——见 UnityRenderer3D.cs 类型顶部判断记录）。
//
// 判断记录（放 Tests/Runtime 而不是 Tests/Editor）：本类型内部方法（DestroyModelInstance 等）使用
// UnityEngine.Object.Destroy（生产期正确写法，同 UnityRenderer2D.DestroySpriteInstance 一贯惯例），
// 而 Object.Destroy 在编辑器模式（EditMode）下不允许调用（会记一条 Console Error 并延迟真正销毁，
// 见 Unity 官方"Destroy may not be called from edit mode"提示）；UnityRenderer2DTests.cs 同理放在
// Tests/Runtime，本文件遵循同一惯例，改用 PlayModeTestBase。
using System;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

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
        public void CreateModelInstance_UnknownModelId_ThrowsWithResolvedPathInMessage()
        {
            var unknownId = new Id("model.does_not_exist_probe");

            var ex = Assert.Throws<InvalidOperationException>(() => _renderer.CreateModelInstance(unknownId));
            StringAssert.Contains("GameFoundation/models/does_not_exist_probe", ex!.Message);
        }

        [Test]
        public void CreateModelInstance_PlaceholderBiped_InstantiatesUnderRoot()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            Assert.AreEqual(1, _root.transform.childCount, "占位模型应实例化在构造函数传入的根节点下");
            Assert.IsNotNull(_renderer.GetModelRoot(handle));

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

        [Test]
        public void SetPlacement_AppliesPositionRotationScale()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            _renderer.SetPlacement(handle, new Vec2(1, 2), 3, 0, 2, 0);

            var root = _renderer.GetModelRoot(handle);
            Assert.IsNotNull(root);
            Assert.AreEqual(new Vector3(1f, 3f, 2f), root!.transform.localPosition);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), root.transform.localScale);
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

        [Test]
        public void OnAnimEvent_ReturnsDisposableSubscription()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);

            SubscriptionHandle? subscription = null;
            Assert.DoesNotThrow(() => subscription = _renderer.OnAnimEvent(handle, (h, eventId) => { }));
            Assert.NotNull(subscription);
            Assert.DoesNotThrow(() => subscription!.Dispose());
        }
    }
}

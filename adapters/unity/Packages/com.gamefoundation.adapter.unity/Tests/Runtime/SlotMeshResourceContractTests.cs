#nullable enable
// SlotMeshResourceContractTests：AUD-05 复现与根治验收（architecture/落地计划/audit-85f1f4f-20260908/
// presentation/presentation-findings.md "PRES-85-01"，第九方审核）。
//
// 判断记录（取代该报告 probe-slotmesh.cs 里的"现状断言"探针）：报告里的独立探针 Assert.IsNull(after,
// "audit probe: current SetSlotMesh cannot resolve the sample mesh_ref") 是故意断言"当前的错误行为"，
// 用来在只读审计里稳定复现问题、不代表产品应有行为。本文件覆盖同一触发条件，但断言改成"正确性断言"
// ——mesh_ref 引用一个 prefab 型 Model 资源时，槽位网格必须被替换成从该预制体提取出的真实网格，不能
// 是 null（见 UnityRenderer3D.ApplySlotMesh/UnityResourceLoader.TryGetOrLoadSlotMesh 判断记录"AUD-05
// 根治"：mesh_ref 的资源合同是"引用 ResourceKind.Model 资源，若是模型预制体则从中提取网格，若是独立
// 网格资源也可直接使用"，见 02 第 1.7 节勘误）。
//
// 判断记录（放 Tests/Runtime 而不是 Tests/Editor）：同 UnityRenderer3DTests.cs 顶部判断记录——
// UnityRenderer3D.DestroyModelInstance 使用 UnityEngine.Object.Destroy，EditMode 下不允许调用。
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class SlotMeshResourceContractTests : PlayModeTestBase
    {
        private static readonly Id PlaceholderModelId = new Id("model.placeholder_biped");
        private static readonly Id HeadSlotId = new Id("slot.head");

        private GameObject _root = null!;
        private UnityResourceLoader _loader = null!;
        private UnityRenderer3D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SlotMeshResourceContractTests_Root");
            _loader = new UnityResourceLoader();
            _renderer = new UnityRenderer3D(_root.transform, _loader);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(_root);
        }

        /// <summary>递归按精确名字查找子物体，返回 null 表示当前这一支子树没有找到——不能在这里直接
        /// 抛异常：抛异常会在第一个不含目标节点的子树处立即中断整个递归，导致根本不会去尝试其它兄弟
        /// 子树（同 <see cref="UnityRenderer3D"/> 同名私有方法一贯写法，调用方自行决定"找不到"要不要
        /// 报错）。</summary>
        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private MeshFilter HeadMeshFilter(ModelHandle handle)
        {
            var visualRoot = _renderer.GetModelVisualRoot(handle)!;
            var slot = FindDeep(visualRoot, HeadSlotId.Value);
            Assert.IsNotNull(slot, $"没有找到子对象 \"{HeadSlotId.Value}\"——placeholder_biped 应当带有该槽位子对象");
            var filter = slot!.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "placeholder_biped 的 slot.head 应当是一个 MeshFilter 槽位");
            return filter!;
        }

        /// <summary>AUD-05 复现 + 根治验收（对应报告 probe-slotmesh.cs 的完整触发路径：先经
        /// IResourceLoader.LoadAsync 把 model.placeholder_biped 作为 ResourceKind.Model 加载进缓存，
        /// 再对真实 slot.head 调用 ModelCharacterRig.ApplyEquipVisual，传入与样例
        /// display.equip_visual.sample_model_helmet 完全一致的 EquipVisualDef：mode=slot_mesh，
        /// mesh_ref=model.placeholder_biped——prefab 型引用）。根治前 sharedMesh 变为 null；根治后
        /// 应当解析出该预制体自身 slot.head 子对象上的真实网格，非空且就是同一份网格资产。</summary>
        [Test]
        public void AUD05_ApplyEquipVisual_SlotMesh_PrefabModelRef_ResolvesRealMesh_NotNull()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            var meshFilter = HeadMeshFilter(handle);
            var authoredMesh = meshFilter.sharedMesh;
            Assert.IsNotNull(authoredMesh, "control: slot.head 应当带着预制体自带的网格启动");

            var callbackSuccess = false;
            _loader.LoadAsync(PlaceholderModelId, ResourceKind.Model, (_, success) => callbackSuccess = success);
            _loader.Tick();
            Assert.IsTrue(callbackSuccess, "control: model.placeholder_biped 应当能经 IResourceLoader 成功加载");

            var display = new Core.Foundation.DisplayInfo.DisplayInfo(
                new Id("display.map.aud05_slot_mesh"), DisplayCategory.Creature,
                new Id("creature.aud05_slot_mesh"), DisplayKind.Model, null, null, null,
                1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                new ModelInfo(PlaceholderModelId, new Id("display.anim_set.placeholder_biped"),
                    sockets: new[] { new Id("socket.main_hand") },
                    slots: new[] { HeadSlotId }));
            var rig = new ModelCharacterRig(new Id("unit.aud05_slot_mesh"), _renderer, handle, display);
            var equip = new EquipVisualDef(
                new Id("display.equip_visual.aud05_sample_helmet"),
                new Id("item.aud05_sample_helmet"), EquipVisualMode.SlotMesh,
                HeadSlotId, PlaceholderModelId, null, null);

            rig.ApplyEquipVisual(equip);

            var afterMesh = meshFilter.sharedMesh;
            Assert.IsNotNull(afterMesh,
                "AUD-05 根治：prefab 型 mesh_ref 必须解析出真实网格，不能因为把 model 当 Mesh 直读而被清空为 null");
            Assert.AreEqual(authoredMesh, afterMesh,
                "根治后的提取规则是：从该预制体的同名槽位子对象取网格——这里 mesh_ref 与 model_ref 引用的是同一个" +
                "预制体，提取出来的应当就是 slot.head 自己原有的那份网格资产");

            rig.Dispose();
            _renderer.DestroyModelInstance(handle);
        }

        [Test]
        public void SetSlotMesh_PrefabMeshRefAlreadyCached_ReplacesWithExtractedSlotMesh()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            var meshFilter = HeadMeshFilter(handle);
            var authoredMesh = meshFilter.sharedMesh;

            // CreateModelInstance 本身已经把 PlaceholderModelId 同步加载进 _loader 缓存（TryLoadModelSync），
            // 这里直接复用同一个 id 作为 mesh_ref，验证"已缓存"这条命中路径。
            _renderer.SetSlotMesh(handle, HeadSlotId, PlaceholderModelId);

            Assert.AreEqual(authoredMesh, meshFilter.sharedMesh,
                "mesh_ref 与 model_ref 引用同一预制体时，提取结果应当就是该预制体 slot.head 自己的网格");

            _renderer.DestroyModelInstance(handle);
        }

        /// <summary>验收清单第 3 条"缺失...不把装备意图静默变成不可见"：mesh_ref 指向一个确实不存在的
        /// 资源时，槽位应当保留当前网格（不清空为 null），不抛异常。</summary>
        [Test]
        public void SetSlotMesh_MissingMeshRef_KeepsCurrentSlotMesh_DoesNotThrow()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            var meshFilter = HeadMeshFilter(handle);
            var originalMesh = meshFilter.sharedMesh;
            Assert.IsNotNull(originalMesh);

            Assert.DoesNotThrow(() =>
                _renderer.SetSlotMesh(handle, HeadSlotId, new Id("model.aud05_missing_mesh_probe")));

            Assert.AreEqual(originalMesh, meshFilter.sharedMesh,
                "缺失的 mesh_ref 不应该清空槽位网格——应当保留当前网格，等异步加载结果（本用例里资源确实" +
                "不存在，因此会一直保留），而不是把装备意图静默变成不可见");

            _renderer.DestroyModelInstance(handle);
        }

        /// <summary>验收清单第 2 条"自定义 loader 记录首次请求证明渲染器不自行读路径"：mesh_ref 缺失/
        /// 未加载时，UnityRenderer3D 必须经 <see cref="UnityResourceLoader.LoadAsync"/> 发起一次真正的
        /// 请求（用 <see cref="UnityResourceLoader.PendingLoadCount"/> 这一既有测试/诊断钩子作为证据：
        /// 若渲染器绕开加载器直接调用 Resources.Load，这个计数不会变化），而不是自己悄悄调用
        /// UnityEngine.Resources.Load。</summary>
        [Test]
        public void SetSlotMesh_MissingMeshRef_IssuesRealLoadAsyncRequestThroughLoader()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            var missingId = new Id("model.aud05_pending_probe");
            Assert.IsFalse(_loader.IsLoaded(missingId));
            Assert.AreEqual(0, _loader.PendingLoadCount, "control: 尚未对这个 id 发起过任何加载请求");

            _renderer.SetSlotMesh(handle, HeadSlotId, missingId);

            Assert.AreEqual(1, _loader.PendingLoadCount,
                "SetSlotMesh 应当经 IResourceLoader.LoadAsync 为这个未缓存的 mesh_ref 发起一次真正的请求" +
                "（直接旁路 Resources.Load 不会让这个计数变化）");

            _loader.Tick();
            Assert.AreEqual(0, _loader.PendingLoadCount, "Tick 后这次（注定失败的）请求应当已经完成");
            Assert.IsFalse(_loader.IsLoaded(missingId));

            _renderer.DestroyModelInstance(handle);
        }

        [Test]
        public void SetSlotMesh_ExplicitNullAfterMesh_ClearsSlot_RepeatedNullIsIdempotent()
        {
            var handle = _renderer.CreateModelInstance(PlaceholderModelId);
            var meshFilter = HeadMeshFilter(handle);

            _renderer.SetSlotMesh(handle, HeadSlotId, PlaceholderModelId);
            Assert.IsNotNull(meshFilter.sharedMesh);

            _renderer.SetSlotMesh(handle, HeadSlotId, null);
            Assert.IsNull(meshFilter.sharedMesh, "meshId: null 是显式卸下，必须清空，不受缺失时保留网格这条降级策略影响");

            Assert.DoesNotThrow(() => _renderer.SetSlotMesh(handle, HeadSlotId, null));
            Assert.IsNull(meshFilter.sharedMesh, "重复卸下同一槽位应当幂等，不抛异常");

            _renderer.DestroyModelInstance(handle);
        }

        [Test]
        public void SetSlotMesh_AfterDestroyAndRecreateInstance_StillAppliesExtractedMeshOnNewHandle()
        {
            var handle1 = _renderer.CreateModelInstance(PlaceholderModelId);
            _renderer.SetSlotMesh(handle1, HeadSlotId, PlaceholderModelId);
            Assert.IsNotNull(HeadMeshFilter(handle1).sharedMesh);
            _renderer.DestroyModelInstance(handle1);

            var handle2 = _renderer.CreateModelInstance(PlaceholderModelId);
            var meshFilter2 = HeadMeshFilter(handle2);
            Assert.DoesNotThrow(() => _renderer.SetSlotMesh(handle2, HeadSlotId, PlaceholderModelId));
            Assert.IsNotNull(meshFilter2.sharedMesh,
                "销毁重建出的新句柄应当仍能正常提取/应用 mesh_ref，不受此前句柄缓存状态影响");

            _renderer.DestroyModelInstance(handle2);
        }
    }
}

#nullable enable
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
    public sealed class PresentationAudit85Tests : PlayModeTestBase
    {
        [Test]
        public void ModelSlotMesh_ModelResourceLoadedThroughLoader_StillResolvesNullBecauseRendererLoadsMeshDirectly()
        {
            var root = new GameObject("PresentationAudit85_Root");
            var loader = new UnityResourceLoader();
            var renderer = new UnityRenderer3D(root.transform, loader);
            var modelId = new Id("model.placeholder_biped");
            var handle = renderer.CreateModelInstance(modelId);
            var slot = FindDeep(renderer.GetModelVisualRoot(handle)!, "slot.head");
            Assert.IsNotNull(slot, "placeholder_biped must expose the declared slot.head object");
            var meshFilter = slot!.GetComponent<MeshFilter>();
            Assert.IsNotNull(meshFilter, "slot.head must be a MeshFilter slot");
            Assert.IsNotNull(meshFilter!.sharedMesh, "control: slot.head starts with its authored mesh");

            var callbackSuccess = false;
            loader.LoadAsync(modelId, ResourceKind.Model, (_, success) => callbackSuccess = success);
            loader.Tick();
            Assert.IsTrue(callbackSuccess, "control: the model id is available through IResourceLoader");
            Assert.IsTrue(loader.TryGetModelPrefab(modelId, out _), "control: loader cache contains the model prefab");

            var display = new Core.Foundation.DisplayInfo.DisplayInfo(
                new Id("display.map.sample_model_hero"), DisplayCategory.Creature,
                new Id("creature.sample_model_hero"), DisplayKind.Model, null, null, null,
                1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                new ModelInfo(modelId, new Id("display.anim_set.placeholder_biped"),
                    sockets: new[] { new Id("socket.main_hand") },
                    slots: new[] { new Id("slot.head") }));
            var rig = new ModelCharacterRig(new Id("unit.presentation_audit_85"), renderer, handle, display);
            var equip = new EquipVisualDef(
                new Id("display.equip_visual.sample_model_helmet"),
                new Id("item.sample_model_helmet"), EquipVisualMode.SlotMesh,
                new Id("slot.head"), modelId, null, null);
            rig.ApplyEquipVisual(equip);
            var after = meshFilter.sharedMesh;
            Debug.Log($"PRESENTATION85 slot_mesh rig_apply=true loader_loaded={loader.IsLoaded(modelId)} loader_prefab=true expected_mesh_ref_applied=true actual_mesh_null={after == null}");

            // Probe assertion deliberately captures the current failure: model ResourceKind loads a GameObject,
            // but SetSlotMesh bypasses that cache and Resources.Load<Mesh>(the prefab path) returns null.
            Assert.IsNull(after, "audit probe: current SetSlotMesh cannot resolve the sample mesh_ref");
            rig.Dispose();
            renderer.DestroyModelInstance(handle);
            Object.Destroy(root);
        }

        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var result = FindDeep(root.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }
}

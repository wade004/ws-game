#nullable enable
// 外部审计副本专用真实链路探针：GameBootstrap + EquipmentHost + SaveSystem + 既有 UnityModelView。
// A 为真实装备状态，B 为空装备存档；LoadB 后要求 EquipmentHost 为空且 View identity 不变，
// 再读取同一个 model socket 的 childCount，区分“状态恢复”与“外观已刷新”。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Template;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;

namespace Game.Template.Tests
{
    public sealed class ExistingViewEquipmentSaveLoadAuditTests
    {
        private const string ShellSceneName = "GameTemplateShell";
        private const string SwordTemplateId = "item.sample_model_sword";
        private const string MainHandSlotId = "item.slot.sample_main_hand";
        private const string ModelPlayerTemplateId = "creature.sample_model_hero";
        private const string SampleMapId = "world.sample_field";
        private const string SampleDifficultyId = "diff.sample_story";

        private static IEnumerator LoadShellScene()
        {
            SceneManager.LoadScene(ShellSceneName);
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static IEnumerator PumpFrames(GameBootstrap bootstrap, int count = 6)
        {
            for (var i = 0; i < count; i++)
            {
                bootstrap.Presentation.Shell.Update();
                yield return null;
            }
        }

        private static IEnumerator WaitForSocketCount(GameBootstrap bootstrap, Transform socket, int expected)
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && socket.childCount != expected)
            {
                bootstrap.Presentation.Shell.Update();
                yield return new WaitForFixedUpdate();
            }

            Debug.Log($"[ExistingViewEquipmentSaveLoadAudit] socket_wait expected={expected};actual={socket.childCount}");
        }

        private static IEnumerator ReplaceSceneBootstrapWithModelFixture()
        {
            var old = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (old != null)
            {
                UnityEngine.Object.Destroy(old.gameObject);
                yield return null;
            }

            var go = new GameObject("AuditExistingViewEquipmentBootstrap");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.Options.StartMapId = SampleMapId;
            bootstrap.Options.PlayerTemplateId = ModelPlayerTemplateId;
            bootstrap.Options.PlayerFactionId = "fac.player";
            bootstrap.Options.PlayerClassId = "arch.class.sample_a";
            bootstrap.Options.DefaultDifficultyId = SampleDifficultyId;
            bootstrap.Options.GameId = "game.audit_existing_view_equipment";
            bootstrap.Options.EquipmentSlotIds = new[] { MainHandSlotId };
            bootstrap.Options.EnableDataHotReload = false;

            // GameBootstrap 的模板默认 game root 不含 sample 数据；本外部副本只把 root 改成
            // 已同步的 data/_sample，仍由生产 GameBootstrap 自己完成 DataRegistry.LoadAll。
            var gameRoot = typeof(GameBootstrap).GetField("_gameDatasetRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(gameRoot, "应能定位 GameBootstrap 的 game dataset root 配置字段");
            gameRoot!.SetValue(bootstrap, "data/_sample");

            go.SetActive(true);
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(bootstrap.BootstrapFailed, "模型外观真实探针的 GameBootstrap 应装配成功");
            bootstrap.Presentation.Shell.Start();
            Assert.IsTrue(bootstrap.RequestNewGame(
                new Id("slot.audit_existing_view_equipment"), new Id(SampleDifficultyId)),
                "生产 ShellHost.NewGame 应接受探针槽位");

            var guard = 900;
            while (bootstrap.Presentation.Shell.Page != Presentation.Shell.ShellPage.InWorld && guard-- > 0)
            {
                bootstrap.Presentation.Shell.Update();
                yield return null;
            }

            Assert.AreEqual(Presentation.Shell.ShellPage.InWorld, bootstrap.Presentation.Shell.Page,
                "真实 GameBootstrap 新游戏应在有限帧内进入 InWorld");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private static Transform FindDeep(Transform root, string name)
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

            return null!;
        }

        [UnityTest]
        public IEnumerator ExistingModelView_RealEquipmentSaveA_SaveB_LoadB_StaysStale()
        {
            yield return LoadShellScene();
            yield return ReplaceSceneBootstrapWithModelFixture();

            var bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap);
            var b = bootstrap!;
            Assert.IsTrue(b.Presentation.ViewBinder.TryGetView(b.PlayerId, out var beforeView),
                "进入 InWorld 后应存在玩家 View");
            Assert.IsInstanceOf<UnityModelView>(beforeView,
                "本探针配置 creature.sample_model_hero，必须走真实 UnityModelView");
            var view = (UnityModelView)beforeView!;
            var initialHandle = view.TryGetModelHandle();
            Assert.IsTrue(initialHandle.HasValue, "玩家 View 应持有存活 ModelHandle");
            var modelRenderer = (UnityRenderer3D)b.Host.Renderer3D;
            var visualRoot = modelRenderer.GetModelVisualRoot(initialHandle!.Value);
            Assert.IsNotNull(visualRoot, "真实 UnityRenderer3D 应返回模型外观根");
            var socket = visualRoot!.Find("socket.main_hand") ?? FindDeep(visualRoot, "socket.main_hand");
            Assert.IsNotNull(socket, "sample_model_hero 应声明 socket.main_hand");

            var saveB = b.Presentation.SaveSystem.Save(new SaveRequest(
                new Id("slot.audit_existing_view_equipment_b"), "audit-B"));
            Assert.IsTrue(saveB.Success, "Save B（空装备）应成功：" + saveB.Message);

            var player = b.Gameplay.Carriers.Inventory;
            Assert.IsTrue(player.AddItem(b.PlayerId, new Id(SwordTemplateId), 1),
                "真实 InventoryHost 应接受外部副本的合法 sample_model_sword fixture");
            var instance = player.ListItems(b.PlayerId).Single(x => x.TemplateId.Equals(new Id(SwordTemplateId)));
            var equipResult = b.Gameplay.Carriers.Equipment.Equip(
                b.PlayerId, instance.InstanceId, new Id(MainHandSlotId));
            Assert.IsTrue(equipResult.Success, "真实 EquipmentHost.Equip 应成功：" + equipResult.Reason);
            yield return WaitForSocketCount(b, socket!, 1);
            Debug.Log($"[ExistingViewEquipmentSaveLoadAudit] after_equip instance={instance.InstanceId};equipment={b.Gameplay.Carriers.Equipment.GetAllEquippedInstances(b.PlayerId).Count};socket={socket!.childCount}");
            Assert.AreEqual(1, socket!.childCount,
                "装备 A 后真实既有 View 的 socket.main_hand 应有一个子模型");

            var saveA = b.Presentation.SaveSystem.Save(new SaveRequest(
                new Id("slot.audit_existing_view_equipment_a"), "audit-A"));
            Assert.IsTrue(saveA.Success, "Save A 应成功：" + saveA.Message);
            Assert.IsTrue(b.Presentation.ViewBinder.TryGetView(b.PlayerId, out var sameViewBeforeLoad));
            Assert.AreSame(view, sameViewBeforeLoad, "Load B 前后应始终使用同一 View 对象");

            var loadB = b.Presentation.SaveSystem.Load(new Id("slot.audit_existing_view_equipment_b"));
            Assert.IsTrue(loadB.Status == LoadStatus.Loaded || loadB.Status == LoadStatus.LoadedFromBackup,
                "Load B 应成功：" + loadB.Status);
            yield return PumpFrames(b, 12);

            var equippedAfterLoad = b.Gameplay.Carriers.Equipment.GetAllEquippedInstances(b.PlayerId);
            Assert.IsTrue(b.Presentation.ViewBinder.TryGetView(b.PlayerId, out var afterView),
                "Load B 后玩家应仍有 View");
            Assert.IsNotNull(afterView);
            var afterHandle = ((UnityModelView)afterView!).TryGetModelHandle();
            Assert.IsTrue(afterHandle.HasValue, "Load B 后同一 View 应仍持有 ModelHandle");
            var currentRoot = modelRenderer.GetModelVisualRoot(afterHandle!.Value);
            Assert.IsNotNull(currentRoot, "Load B 后应能重新读取同一模型根");
            var currentSocket = currentRoot!.Find("socket.main_hand") ?? FindDeep(currentRoot, "socket.main_hand");
            Assert.IsNotNull(currentSocket, "Load B 后应能重新读取 socket.main_hand");
            yield return WaitForSocketCount(b, currentSocket!, 0);
            Debug.Log($"[ExistingViewEquipmentSaveLoadAudit] saveA={saveA.Success};saveB={saveB.Success};loadB={loadB.Status};" +
                      $"equipment_after_load={equippedAfterLoad.Count};socket_childCount_before={socket.childCount};socket_childCount_after={currentSocket!.childCount};" +
                      $"view_same={ReferenceEquals(view, afterView)};socket_same={ReferenceEquals(socket, currentSocket)}");
            Assert.AreEqual(0, equippedAfterLoad.Count, "正确 oracle：Load B 后真实 EquipmentHost 应为空装备");
            Assert.AreSame(view, afterView, "正确 oracle：Load B 不应重建玩家 View");
            Assert.AreEqual(0, currentSocket.childCount,
                "正确 oracle：Load B 后既有 View 的主手外观应清除；若仍为 1 即确认同图读档外观缺陷");
        }
    }
}

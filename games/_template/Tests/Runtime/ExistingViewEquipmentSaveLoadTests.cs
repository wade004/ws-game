#nullable enable
// ExistingViewEquipmentSaveLoadTests：第十四轮审核（audit-c9ff301-20260909）P2-08 根治验收——
// 真实链路：GameBootstrap + EquipmentHost + SaveSystem + 既有 UnityModelView。A 为真实装备状态，
// B 为空装备存档；LoadB 后要求 EquipmentHost 为空且 View identity 不变，再读取同一个 model socket
// 的 childCount，区分"状态恢复"（EquipmentHost 查询已经是空）与"外观已刷新"（socket 子物体确实被
// 清空）——根治前二者不一致（前者已是 0、后者仍是 1），是本用例存在的意义。原始探针由外部审计
// 副本产出（见 architecture/落地计划/audit-c9ff301-20260909/presentation/presentation-findings.md
// "同图已有 View 读档外观"一节），断言本身已是正确行为 oracle，收口时未改动断言，只把它从审计
// 归档迁入模板作为常设 PlayMode 回归；依赖的 fixture 物品 item.sample_model_sword 已作为常设内容
// 补入 data/_sample/item/item.template.json（此前该物品只有 display.equip_visual/display.map 两张
// 表引用了它，item.template 一直缺对应行）。
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
    public sealed class ExistingViewEquipmentSaveLoadTests
    {
        private const string ShellSceneName = "GameTemplateShell";
        private const string SwordTemplateId = "item.sample_model_sword";
        private const string MainHandSlotId = "item.slot.sample_main_hand";
        private const string ModelPlayerTemplateId = "creature.sample_model_hero";
        private const string SampleMapId = "world.sample_field";
        private const string SampleDifficultyId = "diff.sample_story";
        private const string FixtureBootstrapName = "AuditExistingViewEquipmentBootstrap";

        /// <summary>本用例的断言在中途失败（`Assert.*` 抛异常）会让协程立即中止，
        /// <see cref="ReplaceSceneBootstrapWithModelFixture"/> 创建的自定义
        /// <c>AuditExistingViewEquipmentBootstrap</c> GameObject 因此可能没机会走到方法末尾的清理——
        /// 与既有 <c>GlobalTemplateTestSetup</c>（清理"game.template."前缀存档槽，只在整批用例开始前
        /// 跑一次）同一个道理，本清理需要"每条用例结束后都执行"，用 <c>[UnityTearDown]</c> 保证不管
        /// 用例通过/失败/提前中止，本条用例创建的临时 GameObject 都不会残留到下一条用例（下一条用例
        /// 通常会重新 <c>SceneManager.LoadScene</c>，但那只保证同名旧场景对象被替换，不保证发生在本
        /// 类型触发失败的那一刻——两者时序上并不天然衔接）。
        /// <para>
        /// 判断记录（本用例主动删除自己创建的两个存档槽）：<c>GlobalPlayModeTestSetup</c>
        /// （工作台既有隔离基础设施）只在整批 <c>-runTests</c> 调用最开始把用户数据根重定向到一个
        /// 专属空目录，同一次调用内先后跑的全部 PlayMode 用例共用这一个目录、共用同一份
        /// <c>SaveSystemOptions.MaxSlots</c>（默认 20）配额，中途不会再清空。本用例每次运行会创建
        /// 两个新槽位（Save B、Save A），若不在用例结束时主动删除，会一直占着配额直到整次
        /// <c>-runTests</c> 调用结束，挤压同一次调用里排在后面、同样需要新建存档槽的其它用例（如
        /// <c>GameTemplateSmokeTests</c> 的冒烟用例）——这不是这些用例自身的缺陷，是本用例作为"后来者"
        /// 应该对自己新增的存档槽负责收尾，不能只靠"反正下次整次调用会重新清空"来打平账。</para></summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var stray = GameObject.Find(FixtureBootstrapName);
            if (stray != null)
            {
                var strayBootstrap = stray.GetComponent<GameBootstrap>();
                if (strayBootstrap != null && !strayBootstrap.BootstrapFailed)
                {
                    strayBootstrap.Presentation.SaveSystem.DeleteSlot(new Id("game.template.audit_existing_view_equipment"));
                    strayBootstrap.Presentation.SaveSystem.DeleteSlot(new Id("game.template.audit_existing_view_equipment_a"));
                    strayBootstrap.Presentation.SaveSystem.DeleteSlot(new Id("game.template.audit_existing_view_equipment_b"));
                }
                UnityEngine.Object.DestroyImmediate(stray);
            }
            yield break;
        }

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
                new Id("game.template.audit_existing_view_equipment"), new Id(SampleDifficultyId)),
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
                new Id("game.template.audit_existing_view_equipment_b"), "audit-B"));
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

            // 判断记录（不再另存一份"存档 A"）：原始探针额外把这一刻的已装备状态存成独立存档
            // "audit-A"，但那份存档从未被后续任何一步 Load 过——真正驱动断言的是"刚刚在这个仍存活的
            // 实时 EquipmentHost/View 上完成了装备"这一实时状态，不需要额外落一份盘。PlayMode 测试
            // 全套共用同一个按 SaveSystemOptions.MaxSlots（默认 20）限额、且只在整次 -runTests 调用
            // 开始时清空一次的存档目录（见 GlobalPlayModeTestSetup 判断记录），本用例因此改为只保留
            // 真正被 Load 消费的这一个新增存档槽（"_b"），减少不必要的存档槽占用。
            Assert.IsTrue(b.Presentation.ViewBinder.TryGetView(b.PlayerId, out var sameViewBeforeLoad));
            Assert.AreSame(view, sameViewBeforeLoad, "Load B 前后应始终使用同一 View 对象");

            var loadB = b.Presentation.SaveSystem.Load(new Id("game.template.audit_existing_view_equipment_b"));
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
            Debug.Log($"[ExistingViewEquipmentSaveLoadAudit] saveB={saveB.Success};loadB={loadB.Status};" +
                      $"equipment_after_load={equippedAfterLoad.Count};socket_childCount_before={socket.childCount};socket_childCount_after={currentSocket!.childCount};" +
                      $"view_same={ReferenceEquals(view, afterView)};socket_same={ReferenceEquals(socket, currentSocket)}");
            Assert.AreEqual(0, equippedAfterLoad.Count, "正确 oracle：Load B 后真实 EquipmentHost 应为空装备");
            Assert.AreSame(view, afterView, "正确 oracle：Load B 不应重建玩家 View");
            Assert.AreEqual(0, currentSocket.childCount,
                "正确 oracle：Load B 后既有 View 的主手外观应清除；若仍为 1 即确认同图读档外观缺陷");
        }
    }
}

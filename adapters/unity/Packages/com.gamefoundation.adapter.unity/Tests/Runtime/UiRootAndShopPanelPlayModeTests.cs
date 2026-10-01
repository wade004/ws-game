#nullable enable
// UiRootAndShopPanelPlayModeTests：测试覆盖第四批 T-L15——UiRoot（画布本体）与 ShopPanel（商店面板）
// 此前没有直接用例：UiSuiteTests 经完整 Shell 场景只在“十一面板都能 Toggle”与一条购买链路里间接经过
// 它们，画布配置（渲染模式/缩放/内容区拉伸/EventSystem 唯一性）与面板自身行为（行数跟随视图模型、
// 未打开时的占位、购买按钮可用性、点击落到真实经济扣款、关闭后复位）都没有断言。
//
// 判断记录（不走 Shell 场景，用 AssemblyFixture + 手工 UiRoot/UiPanelHost）：Shell 场景会拉起常驻的
// FrameworkResidentHost 与 ShellRoot 全套，既慢又让 UiRoot/EventSystem 的创建发生在被测代码之外；本文件
// 要断言的恰恰是 UiRoot.Create 与 UiPanelHost.Initialize 自己做了什么，所以直接调用它们。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Adapter.Unity.Ui;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.Common;
using NUnit.Framework;
using Presentation.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:ui")]
    public sealed class UiRootAndShopPanelPlayModeTests : PlayModeTestBase
    {
        private const string VendorId = "econ.vendor.sample_hunter";
        private const string ItemId = "item.sample_tonic";
        private const string CurrencyId = "econ.currency.sample_coin";

        private AssemblyFixture? _asm;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _spawned.Clear();
            if (_asm != null)
            {
                _asm.Dispose();
                _asm = null;
            }

            yield return null;
        }

        private UiRoot CreateRoot(string name = "UiRootUnderTest")
        {
            var root = UiRoot.Create(name);
            _spawned.Add(root.gameObject);
            return root;
        }

        private (UiPanelHost Host, UiRoot Root, AssemblyFixture Asm) BuildHost()
        {
            _asm = AssemblyFixture.Build(20261001UL);
            var root = CreateRoot();
            var hostGo = new GameObject("UiPanelHostUnderTest");
            _spawned.Add(hostGo);
            var host = hostGo.AddComponent<UiPanelHost>();
            host.Initialize(
                root.Content, _asm.Registry, _asm.Presentation,
                saveSlotCandidates: Array.Empty<Id>(),
                onSaveSlotLoad: _ => { },
                onSaveSlotSaveOrOverwrite: _ => { },
                onSaveSlotDelete: _ => { },
                onPauseOptionClicked: _ => { });
            return (host, root, _asm);
        }

        // ------------------------------------------------------------------
        // UiRoot
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator UiRoot_Create_ConfiguresOverlayCanvas_ScalerAndStretchedContent()
        {
            var root = CreateRoot("UiRootConfig");
            yield return null;

            Assert.AreEqual("UiRootConfig", root.gameObject.name);
            Assert.AreSame(root.GetComponent<Canvas>(), root.Canvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, root.Canvas.renderMode);
            Assert.IsNotNull(root.GetComponent<GraphicRaycaster>(), "画布需要 GraphicRaycaster 才能接收点击");

            var scaler = root.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler);
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(new Vector2(1280, 720), scaler.referenceResolution);
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight);

            Assert.AreEqual("Content", root.Content.name);
            Assert.AreSame(root.transform, root.Content.parent, "Content 是画布的直接子节点");
            Assert.AreEqual(Vector2.zero, root.Content.anchorMin);
            Assert.AreEqual(Vector2.one, root.Content.anchorMax);
            Assert.AreEqual(Vector2.zero, root.Content.offsetMin);
            Assert.AreEqual(Vector2.zero, root.Content.offsetMax);
        }

        [UnityTest]
        public IEnumerator UiRoot_Create_EnsuresExactlyOneEventSystemWithInputSystemModule_EvenWhenCalledTwice()
        {
            var first = CreateRoot("UiRootA");
            yield return null;
            var second = CreateRoot("UiRootB");
            yield return null;

            var eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            Assert.AreEqual(1, eventSystems.Length, "无论创建几个 UiRoot，场景里的 EventSystem 都只能有一个（多于一个时 Unity 会持续告警）");
            Assert.IsNotNull(eventSystems[0].GetComponent<InputSystemUIInputModule>(), "输入模块应为 InputSystemUIInputModule（项目只启用新 Input System）");
            Assert.IsNull(eventSystems[0].GetComponent<StandaloneInputModule>(), "不应同时挂旧输入模块");
            Assert.AreNotSame(first.Canvas, second.Canvas, "两个根各自独立持有自己的画布");
        }

        // ------------------------------------------------------------------
        // ShopPanel
        // ------------------------------------------------------------------

        private static TextMeshProUGUI[] AllLabels(Component panelRoot) => panelRoot.GetComponentsInChildren<TextMeshProUGUI>(true);

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            throw new AssertionException($"在 {root.name} 下找不到名为 {name} 的节点");
        }

        private static List<Transform> RowsOf(Transform content, string listName) =>
            FindDeep(content, listName).Cast<Transform>().ToList();

        [UnityTest]
        public IEnumerator ShopPanel_BeforeOpeningAnyVendor_ShowsPlaceholderAndNoBuyRows()
        {
            var (host, root, _) = BuildHost();
            yield return null;

            host.Shop.RefreshUi();

            Assert.IsFalse(host.Shop.IsOpen, "UiPanelHost 构造后商店默认关闭");
            Assert.IsTrue(AllLabels(root.Content).Any(l => l.text == "（未打开商店）"), "未打开任何商人时余额位应显示占位文案");
            Assert.AreEqual(0, RowsOf(root.Content, "BuyList").Count, "未打开商店时出售清单为空");
        }

        [UnityTest]
        public IEnumerator ShopPanel_OpenVendor_BuyRowsMatchViewModel_AndBuyClickCharges_ExactlyTheDisplayedPrice()
        {
            var (host, root, asm) = BuildHost();
            yield return null;

            var vendorId = new Id(VendorId);
            var itemId = new Id(ItemId);
            var currencyId = new Id(CurrencyId);
            asm.Gameplay.Economy.Add(asm.PlayerId, currencyId, 100, sourceId: currencyId);

            host.Shop.OpenVendor(vendorId);
            yield return null;
            host.Shop.RefreshUi();

            Assert.IsTrue(host.Shop.IsOpen, "OpenVendor 同时显示面板");
            var vm = asm.Presentation.Shop;
            Assert.AreEqual(vendorId, vm.CurrentVendorId);
            Assert.Greater(vm.SellItems.Count, 0, "样例商人应有出售清单");

            // 清单行数跟随视图模型；每行文本按规则由视图模型字段拼出（短 id 取最后一个 '.' 之后的段）。
            var buyRows = RowsOf(root.Content, "BuyList");
            Assert.AreEqual(vm.SellItems.Count, buyRows.Count);
            for (var i = 0; i < vm.SellItems.Count; i++)
            {
                var item = vm.SellItems[i];
                var label = buyRows[i].Find("Label").GetComponent<TextMeshProUGUI>().text;
                var stockText = item.Stock.HasValue ? item.Stock.Value.ToString() : "不限";
                var expected = $"{Short(item.ItemId)}  {item.PriceAmount}{Short(item.PriceCurrencyId)}  库存:{stockText}";
                Assert.AreEqual(expected, label, $"第 {i} 行文本");
                Assert.AreEqual(!item.Stock.HasValue || item.Stock.Value > 0, buyRows[i].Find("Buy").GetComponent<Button>().interactable,
                    $"第 {i} 行购买按钮可用性 = 有库存或不限量");
            }

            var balanceLabel = FindDeep(root.Content, "Balance").GetComponent<TextMeshProUGUI>().text;
            StringAssert.StartsWith("货币：", balanceLabel);
            StringAssert.Contains(Short(currencyId) + ":100", balanceLabel, "余额位应展示出售清单用到的币种与当前余额");

            // 点击第一行“购买”：与真实点击同路径（Button.onClick），期望扣款额取视图模型展示的单价。
            var target = vm.SellItems.First(s => s.ItemId == itemId);
            var targetIndex = vm.SellItems.ToList().IndexOf(target);
            var balanceBefore = asm.Gameplay.Economy.GetBalance(asm.PlayerId, currencyId);
            var countBefore = asm.Gameplay.Carriers.Inventory.CountOf(asm.PlayerId, itemId);

            buyRows[targetIndex].Find("Buy").GetComponent<Button>().onClick.Invoke();
            asm.Bus.DispatchPending();
            yield return null;

            Assert.AreEqual(balanceBefore - target.PriceAmount, asm.Gameplay.Economy.GetBalance(asm.PlayerId, currencyId), "余额应恰好扣除展示单价");
            Assert.AreEqual(countBefore + 1, asm.Gameplay.Carriers.Inventory.CountOf(asm.PlayerId, itemId), "背包应恰好多一件");
        }

        [UnityTest]
        public IEnumerator ShopPanel_BuyClick_WithInsufficientFunds_ChangesNothing()
        {
            var (host, root, asm) = BuildHost();
            yield return null;

            var vendorId = new Id(VendorId);
            var itemId = new Id(ItemId);
            var currencyId = new Id(CurrencyId);
            // 不发放任何货币：余额为 0，任何有价商品都买不起。
            host.Shop.OpenVendor(vendorId);
            yield return null;

            var vm = asm.Presentation.Shop;
            var index = vm.SellItems.ToList().FindIndex(s => s.ItemId == itemId);
            Assert.GreaterOrEqual(index, 0);
            Assert.Greater(vm.SellItems[index].PriceAmount, 0, "前置：该商品有价");

            var balanceBefore = asm.Gameplay.Economy.GetBalance(asm.PlayerId, currencyId);
            var countBefore = asm.Gameplay.Carriers.Inventory.CountOf(asm.PlayerId, itemId);

            RowsOf(root.Content, "BuyList")[index].Find("Buy").GetComponent<Button>().onClick.Invoke();
            asm.Bus.DispatchPending();
            yield return null;

            Assert.AreEqual(balanceBefore, asm.Gameplay.Economy.GetBalance(asm.PlayerId, currencyId), "余额不足时不应扣款");
            Assert.AreEqual(countBefore, asm.Gameplay.Carriers.Inventory.CountOf(asm.PlayerId, itemId), "余额不足时背包不应增加");
        }

        [UnityTest]
        public IEnumerator ShopPanel_OpenUnknownVendor_DegradesToEmptyShelf_WithoutThrowing()
        {
            var (host, root, asm) = BuildHost();
            yield return null;

            Assert.DoesNotThrow(() => host.Shop.OpenVendor(new Id("econ.vendor.does_not_exist")));
            yield return null;

            Assert.AreEqual(0, asm.Presentation.Shop.SellItems.Count);
            Assert.AreEqual(0, RowsOf(root.Content, "BuyList").Count, "未知商人退化为空货架（视图模型约定：不向上抛异常中断 UI 刷新）");
            Assert.IsTrue(host.Shop.IsOpen, "面板仍然按调用显示");
        }

        [UnityTest]
        public IEnumerator ShopPanel_CloseVendorAndHide_ClearsViewModelVendor_AndHidesPanel()
        {
            var (host, _, asm) = BuildHost();
            yield return null;

            host.Shop.OpenVendor(new Id(VendorId));
            yield return null;
            Assert.IsTrue(asm.Presentation.Shop.CurrentVendorId.HasValue);

            host.Shop.CloseVendorAndHide();

            Assert.IsFalse(asm.Presentation.Shop.CurrentVendorId.HasValue, "关闭后视图模型不再持有当前商人");
            Assert.AreEqual(0, asm.Presentation.Shop.SellItems.Count);
            Assert.IsFalse(host.Shop.IsOpen);
        }

        // ------------------------------------------------------------------
        // 面板关闭后画面是否真的看不见（复现用例）
        // ------------------------------------------------------------------

        private static readonly (UiPanel Panel, string BackgroundName)[] PanelBackgrounds =
        {
            (UiPanel.Hud, "HudPanel"), (UiPanel.ActionBar, "ActionBarPanel"), (UiPanel.Inventory, "InventoryPanel"),
            (UiPanel.QuestLog, "QuestLogPanel"), (UiPanel.Dialog, "DialogPanel"), (UiPanel.SkillBook, "SkillBookPanel"),
            (UiPanel.CharacterStats, "CharacterStatsPanel"), (UiPanel.Settings, "SettingsPanel"),
            (UiPanel.SaveSlots, "SaveSlotsPanel"), (UiPanel.PauseMenu, "PauseMenuPanel"), (UiPanel.Shop, "ShopPanel"),
        };

        [UnityTest]
        public IEnumerator EveryPanel_WhenHidden_HasNoVisiblePanelBackground_AndWhenShown_HasOne()
        {
            var (host, root, _) = BuildHost();
            yield return null;

            Assert.AreEqual(Enum.GetValues(typeof(UiPanel)).Length, PanelBackgrounds.Length, "新增 UiPanel 取值时要同步本表");
            foreach (var (panel, backgroundName) in PanelBackgrounds)
            {
                var uiPanel = host.Panels[panel];
                var background = FindDeep(root.Content, backgroundName);

                uiPanel.Hide();
                Assert.IsFalse(background.gameObject.activeInHierarchy,
                    $"面板 {panel} 已关闭，但其背景节点 {backgroundName} 仍在层级里激活（关闭后画面上还能看到它）");

                uiPanel.Show();
                Assert.IsTrue(background.gameObject.activeInHierarchy, $"面板 {panel} 显示后其背景节点 {backgroundName} 应当可见");
            }
        }

        private static string Short(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
        }
    }
}

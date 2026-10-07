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
using System.IO;
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
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private string? _skinRoot;

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

            // 皮肤用例自己造的资源（先等面板销毁完，宿主 OnDestroy 要用到注入的 UiVisuals）。
            foreach (var d in _disposables)
            {
                d.Dispose();
            }

            _disposables.Clear();
            UiSkin.Reset();
            if (_skinRoot != null && Directory.Exists(_skinRoot))
            {
                Directory.Delete(_skinRoot, true);
            }

            _skinRoot = null;
        }

        private UiRoot CreateRoot(string name = "UiRootUnderTest")
        {
            var root = UiRoot.Create(name);
            _spawned.Add(root.gameObject);
            return root;
        }

        private (UiPanelHost Host, UiRoot Root, AssemblyFixture Asm) BuildHost(Func<AssemblyFixture, UiVisuals>? makeVisuals = null)
        {
            _asm = AssemblyFixture.Build(20261001UL);
            var root = CreateRoot();
            var hostGo = new GameObject("UiPanelHostUnderTest");
            _spawned.Add(hostGo);
            var host = hostGo.AddComponent<UiPanelHost>();
            host.Visuals = makeVisuals?.Invoke(_asm);     // 缺省 null：宿主按数据自建（与改动前一致）
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

            // 清单行数跟随视图模型；每行文本按规则由视图模型字段拼出（物品名走 UiVisuals 本地化名，货币短 id 取最后一个 '.' 之后的段）。
            var buyRows = RowsOf(root.Content, "BuyList");
            Assert.AreEqual(vm.SellItems.Count, buyRows.Count);
            for (var i = 0; i < vm.SellItems.Count; i++)
            {
                var item = vm.SellItems[i];
                var label = buyRows[i].Find("Label").GetComponent<TextMeshProUGUI>().text;
                var stockText = item.Stock.HasValue ? item.Stock.Value.ToString() : "不限";
                var expected = $"{ItemLabel(host, item.ItemId)}  {item.PriceAmount}{Short(item.PriceCurrencyId)}  库存:{stockText}";
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
            (UiPanel.Equipment, "EquipmentPanel"),
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

        // ------------------------------------------------------------------
        // 运行期换皮肤：所有面板统一刷新（ADR-0155）
        // ------------------------------------------------------------------

        private const string ReferenceSkinRef = "skin.reference_fantasy";

        /// <summary>临时内容根：只放占位皮肤包与参考皮肤包（皮肤包从文件读，不依赖适配器资源根里有没有参考包）。</summary>
        private string BuildSkinRoot()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var root = Path.Combine(Application.temporaryCachePath, "ui_skin_refresh_tests", Guid.NewGuid().ToString("N").Substring(0, 8));
            CopyTree(Path.Combine(repoRoot, "assets", "_placeholder", "ui", "skin", "default"), Path.Combine(root, "ui", "skin", "default"));
            CopyTree(Path.Combine(repoRoot, "assets", "_reference_fantasy", "ui", "skin", "reference_fantasy"), Path.Combine(root, "ui", "skin", "reference_fantasy"));
            _skinRoot = root;
            return root;
        }

        private static void CopyTree(string from, string to)
        {
            Assert.IsTrue(Directory.Exists(from), "缺目录 " + from);
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(from))
            {
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

        /// <summary>UiSkin 现在这一代给出的全部皮肤精灵（面板底图、纯色矩形、按钮各状态图）。</summary>
        private static HashSet<Sprite> CurrentSkinSprites()
        {
            var set = new HashSet<Sprite> { UiSkin.PanelSprite, UiSkin.FlatSprite };
            var buttons = UiSkin.ButtonSprites;
            if (buttons != null)
            {
                foreach (var s in new[] { buttons.Normal, buttons.Hover, buttons.Pressed, buttons.Disabled, buttons.Selected })
                {
                    if (s != null)
                    {
                        set.Add(s);
                    }
                }
            }

            return set;
        }

        /// <summary>
        /// 漏网判定（不看登记表，只看界面树里实际持有的精灵）：<paramref name="content"/> 子树里的 Image 与 Button 状态图，凡是属于被换下的旧皮肤包、
        /// 或属于上一代 UiSkin 给出而不在这一代里的，都算漏网。返回"路径: 精灵名"清单（遍历全部子节点含隐藏的，所以调用前要等被替换的旧节点销毁完）。
        /// </summary>
        private static List<string> StaleSkinSprites(Transform content, UiVisuals visuals, HashSet<Sprite> previousGeneration, HashSet<Sprite> currentGeneration)
        {
            // 已被释放的精灵（引用还在、对象已销毁）也算漏网：旧皮肤包释放后仍有部件引用它，画面上就是空白。
            bool IsStale(Sprite? sprite) =>
                (object?)sprite != null && (sprite == null || (!currentGeneration.Contains(sprite) && (visuals.IsRetiredSkinSprite(sprite) || previousGeneration.Contains(sprite))));
            string Name(Sprite? sprite) => sprite == null ? "(已释放)" : sprite.name;

            var leaks = new List<string>();
            foreach (var image in content.GetComponentsInChildren<Image>(true))
            {
                if (IsStale(image.sprite))
                {
                    leaks.Add(PathUnder(image.transform, content) + ": " + Name(image.sprite));
                }
            }

            foreach (var button in content.GetComponentsInChildren<Button>(true))
            {
                var state = button.spriteState;
                foreach (var sprite in new[] { state.highlightedSprite, state.pressedSprite, state.selectedSprite, state.disabledSprite })
                {
                    if (IsStale(sprite))
                    {
                        leaks.Add(PathUnder(button.transform, content) + " (spriteState): " + Name(sprite));
                    }
                }
            }

            return leaks;
        }

        private static string PathUnder(Transform t, Transform root)
        {
            var path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent)
            {
                path = p.name + "/" + path;
            }

            return path;
        }

        [UnityTest]
        public IEnumerator SwitchSkin_PlaceholderToReferenceAndBack_EveryPanelRefreshes()
        {
            var skinRoot = BuildSkinRoot();
            UiSkin.Reset();
            var pack = UiSkinPack.Load(null, skinRoot);
            UiVisuals visuals = null!;
            var (host, root, _) = BuildHost(asm =>
            {
                visuals = new UiVisuals(pack, asm.Registry, asm.Presentation.DisplayInfo);
                _disposables.Add(visuals);
                return visuals;
            });
            yield return null;

            // 遍历枚举与登记表（不写死面板清单）：每个 UiPanel 取值都有一个面板，面板根节点都在内容树里。
            foreach (var panel in (UiPanel[])Enum.GetValues(typeof(UiPanel)))
            {
                Assert.IsTrue(host.Panels.ContainsKey(panel), "UiPanel." + panel + " 没有登记面板——新增枚举值要进宿主的面板登记表");
                Assert.IsTrue(host.Panels[panel].Root.transform.IsChildOf(root.Content), "UiPanel." + panel + " 的面板根不在内容树里");
            }

            Assert.AreEqual(Enum.GetValues(typeof(UiPanel)).Length, host.Panels.Count);

            var stepNames = new[] { "占位→参考", "参考→占位", "占位→参考（再来一轮）" };
            var targets = new string?[] { ReferenceSkinRef, null, ReferenceSkinRef };
            var textColors = new List<Color> { UiSkin.TextColor };
            var staleCountsBefore = new List<int>();
            for (var step = 0; step < targets.Length; step++)
            {
                var previous = CurrentSkinSprites();
                // 换之前：每个面板里"文字色等于当前皮肤文字色"的标签，换之后应当都跟到新皮肤的文字色。
                var oldTextColor = UiSkin.TextColor;
                var labelsOnSkinColor = root.Content.GetComponentsInChildren<TextMeshProUGUI>(true).Where(l => l.color == oldTextColor).ToList();
                Assert.Greater(labelsOnSkinColor.Count, 0, "前置：界面里有取皮肤文字色的标签");

                // 对照（证明漏网判定抓得住）：一个不经 UiWidgets、也没订阅的 Image 持有当前皮肤底图，换皮肤后必须被判成漏网。
                var strayGo = new GameObject("StrayUnboundImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                strayGo.transform.SetParent(host.Hud.transform, false);
                strayGo.GetComponent<Image>().sprite = UiSkin.PanelSprite;

                visuals.SwitchSkin(targets[step], skinRoot);
                yield return null;      // 被面板重建换下的旧节点在帧末销毁

                var current = CurrentSkinSprites();
                var staleBefore = StaleSkinSprites(root.Content, visuals, previous, current);
                staleCountsBefore.Add(staleBefore.Count);
                Assert.AreEqual(1, staleBefore.Count, stepNames[step] + "：对照用的未登记 Image 应恰好被判成漏网，其余都应已换新：" + string.Join("; ", staleBefore));
                StringAssert.Contains("StrayUnboundImage", staleBefore[0]);
                UnityEngine.Object.Destroy(strayGo);
                yield return null;

                var leaks = StaleSkinSprites(root.Content, visuals, previous, current);
                Assert.IsEmpty(leaks, stepNames[step] + "：这些界面部件仍持有旧皮肤的精灵：" + string.Join("; ", leaks));
                Assert.IsEmpty(UiSkinBindings.FindStale(root.Content), stepNames[step] + "：登记过的控件里还有停在旧皮肤取值上的");

                // 每个面板（含 HUD）里至少有一块底图是现行皮肤的面板底图。
                foreach (var panel in (UiPanel[])Enum.GetValues(typeof(UiPanel)))
                {
                    var hasCurrentPanelSprite = host.Panels[panel].Root.GetComponentsInChildren<Image>(true).Any(i => i.sprite == UiSkin.PanelSprite);
                    Assert.IsTrue(hasCurrentPanelSprite, $"{stepNames[step]}：UiPanel.{panel} 里没有任何一块底图用现行皮肤的面板底图");
                }

                // 文字色：换之前取皮肤文字色的标签，现在取新皮肤的文字色。
                var newTextColor = UiSkin.TextColor;
                Assert.AreNotEqual(oldTextColor, newTextColor, "前置：两套皮肤的文字色不同，否则下面的断言是空的");
                foreach (var label in labelsOnSkinColor.Where(l => l != null))
                {
                    Assert.AreEqual(newTextColor, label.color, stepNames[step] + "：标签 " + PathUnder(label.transform, root.Content) + " 的文字色没有跟到新皮肤");
                }

                // 按钮：每个 UiWidgets 按钮的底图现在是新皮肤的取值（参考皮肤 = 九宫格按钮图 Normal，占位皮肤 = 纯色矩形）。
                var expectedButtonSprite = UiSkin.ButtonSprites != null ? UiSkin.ButtonSprites.Normal : UiSkin.FlatSprite;
                var hudButtons = host.Hud.GetComponentsInChildren<Button>(true);
                Assert.Greater(hudButtons.Length, 0, "HUD 里有按钮");
                foreach (var button in hudButtons)
                {
                    Assert.AreSame(expectedButtonSprite, button.GetComponent<Image>().sprite, stepNames[step] + "：HUD 按钮 " + button.name + " 的底图");
                }

                textColors.Add(newTextColor);
                TestContext.Out.WriteLine($"[skin-refresh] {stepNames[step]}: stale-with-control={staleBefore.Count} stale-after={leaks.Count} bindings={UiSkinBindings.Count} textColor={ColorUtility.ToHtmlStringRGB(newTextColor)}");
            }

            // 一个来回之后回到起点：文字色又是占位皮肤的那一个。
            Assert.AreEqual(textColors[0], textColors[2], "占位→参考→占位 之后回到占位皮肤文字色");
        }

        [UnityTest]
        public IEnumerator SwitchSkin_TakesOverTheGlobalSkinOverride_AndDisposeReleasesIt()
        {
            var skinRoot = BuildSkinRoot();
            UiSkin.Reset();
            var foreign = new UiSkinOverride { TextColor = Color.magenta };
            UiSkin.Install(foreign);
            var loader = new Adapter.Unity.EngineAdapter.UnityResourceLoader();
            var visuals = new UiVisuals(UiSkinPack.Load(null, skinRoot), null, null, loader);
            try
            {
                Assert.IsFalse(visuals.OwnsSkinOverride, "前置：覆盖是别人装的");

                visuals.SwitchSkin(ReferenceSkinRef, skinRoot);
                Assert.IsTrue(visuals.OwnsSkinOverride, "换到非占位皮肤后，覆盖归 UiVisuals");
                Assert.AreNotEqual(Color.magenta, UiSkin.TextColor, "别人装的覆盖被接管（换皮肤就是要这套皮肤）");
                Assert.AreEqual(visuals.Pack.ThemeColor("text"), UiSkin.TextColor, "文字色来自新皮肤包 theme.json");

                visuals.SwitchSkin(null, skinRoot);
                Assert.IsFalse(UiSkin.IsOverrideInstalled, "换回占位皮肤：撤掉覆盖，UiSkin 回到缺省");
                Assert.IsFalse(visuals.OwnsSkinOverride);

                visuals.SwitchSkin(ReferenceSkinRef, skinRoot);
                Assert.IsTrue(UiSkin.IsOverrideInstalled);
            }
            finally
            {
                visuals.Dispose();
            }

            Assert.IsFalse(UiSkin.IsOverrideInstalled, "Dispose 撤掉自己持有的覆盖");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwitchSkin_RepeatedSwitches_ReleaseRetiredPacks_AndNeverLeaveReleasedSprites()
        {
            var skinRoot = BuildSkinRoot();
            UiSkin.Reset();
            var pack = UiSkinPack.Load(null, skinRoot);
            UiVisuals visuals = null!;
            var (host, root, _) = BuildHost(asm =>
            {
                visuals = new UiVisuals(pack, asm.Registry, asm.Presentation.DisplayInfo);
                _disposables.Add(visuals);
                return visuals;
            });
            yield return null;
            Assert.AreEqual(1, visuals.LivePackCount);

            const int Switches = 8;
            var maxLive = 1;
            for (var i = 0; i < Switches; i++)
            {
                var previous = CurrentSkinSprites();
                visuals.SwitchSkin(i % 2 == 0 ? ReferenceSkinRef : null, skinRoot);
                maxLive = Math.Max(maxLive, visuals.LivePackCount);
                Assert.LessOrEqual(visuals.LivePackCount, 2, $"第 {i + 1} 次切换后：当前一份 + 刚退役的一份（旧面板子树要到帧末才销毁，还在用它）");
                yield return null;
                yield return null;      // 宿主 Update 在退役之后的帧里释放没人引用的旧包
                Assert.AreEqual(1, visuals.LivePackCount, $"第 {i + 1} 次切换后两帧：旧皮肤包应已释放，只剩当前一份");
                Assert.AreEqual(0, visuals.RetiredPackCount);

                var current = CurrentSkinSprites();
                var leaks = StaleSkinSprites(root.Content, visuals, previous, current);
                Assert.IsEmpty(leaks, $"第 {i + 1} 次切换后：仍引用旧皮肤或已释放精灵的部件：" + string.Join("; ", leaks));
                foreach (var sprite in current)
                {
                    Assert.IsTrue(sprite != null, "现行皮肤的精灵没有被误释放");
                }
            }

            TestContext.Out.WriteLine($"[skin-packs] switches={Switches} maxLivePacks={maxLive} finalLivePacks={visuals.LivePackCount}");
        }

        [UnityTest]
        public IEnumerator SwitchSkin_WithoutAnyDriver_StaysWithinTwoPacks_AndKeepsPacksThatAreStillReferenced()
        {
            // 没有宿主每帧驱动时靠下一次 SwitchSkin 开头的释放兜底（双缓冲）：连续换 N 次（每次隔一帧）持有的包数 ≤ 2。
            var skinRoot = BuildSkinRoot();
            UiSkin.Reset();
            var loader = new Adapter.Unity.EngineAdapter.UnityResourceLoader();
            var visuals = new UiVisuals(UiSkinPack.Load(null, skinRoot), null, null, loader);
            _disposables.Add(visuals);
            for (var i = 0; i < 6; i++)
            {
                visuals.SwitchSkin(i % 2 == 0 ? ReferenceSkinRef : null, skinRoot);
                Assert.LessOrEqual(visuals.LivePackCount, 2, $"第 {i + 1} 次切换后");
                yield return null;
            }

            // 仍有存活部件引用旧包时不释放：造一个图像持有当前包的面板底图，换肤后它就是旧包的引用者。
            visuals.SwitchSkin(ReferenceSkinRef, skinRoot);
            yield return null;
            var holderGo = new GameObject("Holder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _spawned.Add(holderGo);
            var held = visuals.Pack.PanelBackground();
            Assert.IsNotNull(held);
            holderGo.GetComponent<Image>().sprite = held;
            visuals.SwitchSkin(null, skinRoot);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                visuals.ReleaseRetiredPacks();
            }

            Assert.AreEqual(1, visuals.RetiredPackCount, "旧包仍被一个存活图像引用：不释放，该图像的精灵没有变成已释放");
            Assert.IsTrue(holderGo.GetComponent<Image>().sprite != null);

            UnityEngine.Object.Destroy(holderGo);
            for (var i = 0; i < 20 && visuals.RetiredPackCount > 0; i++)
            {
                yield return null;
                visuals.ReleaseRetiredPacks();
            }

            Assert.AreEqual(0, visuals.RetiredPackCount, "引用者销毁后（重试间隔内）旧包释放");
        }

        /// <summary>面板文字里的物品名：宿主有皮肤/展示信息（<c>host.Visuals</c>）时是本地化名（P4 备忘 1），否则退回短 id。</summary>
        private static string ItemLabel(UiPanelHost host, Id id) => host.Visuals != null ? host.Visuals.ItemName(id) : Short(id);

        private static string Short(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
        }
    }
}

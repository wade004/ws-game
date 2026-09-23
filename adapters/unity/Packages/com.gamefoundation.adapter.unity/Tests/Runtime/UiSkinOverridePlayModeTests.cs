#nullable enable
// UiSkinOverridePlayModeTests：ADR-0082（UI 套件皮肤覆盖注入点）验收用例，消费方反馈第二十七批
// （阻塞）——CharacterStatsPanel/DialogPanel（均为 public sealed class，见类型判断记录）此前完全
// 拿不到任何皮肤注入点，本文件用真实 Unity PlayMode 环境钉死：① 未安装覆盖时两个面板的实际组件
// 值（背景精灵/按钮颜色/文字字体）与改动前逐字节一致；② 安装覆盖后新建的面板实际拿到覆盖值；
// ③ 卸载覆盖后新建的面板回到默认值；④ 覆盖安装晚于默认值的惰性缓存生成时仍然生效（UiSkin.cs
// 判断记录"不缓存覆盖结果"专门要钉死的边界）。
//
// 判断记录（每个用例各自用独立的 scratch RectTransform 作为面板 Construct 的 parent，不复用
// shell.UiPanelHost.GameplayGroup）：生产装配（UiPanelHost.Initialize）已经在 GameplayGroup 下建过
// 一份同名的 "CharacterStatsPanel"/"DialogPanel" 可视节点，若本文件的探针面板也建在 GameplayGroup
// 下，Transform.Find 按名字查找会在两个同名兄弟节点之间产生歧义（返回哪一个未在契约中明确保证）。
// 独立 scratch 父节点下只会有本用例自己新建的那一份，定位精确、不与生产面板互相干扰。
//
// 判断记录（复用 shell.Framework.Presentation.CharacterStats/DialogView/UiIntents/L10n，不新建
// 独立视图模型实例）：这两个视图模型的构造都需要真实 IUiDataSource/IDialogHost/IL10nHost，
// FrameworkResidentHost 装配好的现成实例已经是这些依赖的真实实现，直接复用比另起一套装配更简单；
// 视图模型本身只读、不持有任何"每个消费面板专属"的状态，被多个探针面板共享读取不会互相污染。
// 本文件不对这些共享实例调用 Dispose()——它们随 FrameworkResidentHost（DontDestroyOnLoad 单例）
// 跨整个批处理进程存活，供其余测试类复用，本文件只销毁自己新建的 GameObject。
using System.Collections;
using Adapter.Unity.Shell;
using Adapter.Unity.Ui;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.Common;
using NUnit.Framework;
using Presentation.Shell;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UiSkinOverridePlayModeTests : PlayModeTestBase
    {
        private const string SceneName = "Shell";
        private const string SlotId = "game.sample.slot_ui_skin_override";
        private const string TierId = "diff.sample_story";
        private const string PlaceholderFontResourcePath = "Fonts/noto_sans_cjk_sc";

        [UnitySetUp]
        public IEnumerator SkinSetUp()
        {
            // 判断记录：UiSkin 的覆盖是静态/进程级状态，跨用例不自动复位——防御性地确保本文件每条
            // 用例开始前一定是"未安装"状态，不依赖上一条用例的 TearDown 是否忘记复位。
            UiSkin.Reset();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator SkinTearDown()
        {
            UiSkin.Reset();
            yield break;
        }

        private static IEnumerator LoadShellScene()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static ShellRoot RequireShellRoot()
        {
            var root = Object.FindFirstObjectByType<ShellRoot>();
            Assert.IsNotNull(root, "场景里应当有且仅有一个 ShellRoot 实例");
            return root!;
        }

        /// <summary>同 UiSuiteTests.EnterInWorld：把会话推进到 InWorld，供需要"游戏内视图模型有
        /// 真实数据"的用例复用（本文件用独立探针槽位 id，不与其它测试类共享存档）。</summary>
        private static IEnumerator EnterInWorld(ShellRoot shell)
        {
            shell.Framework.Presentation.Shell.ShowSlots();
            shell.Framework.Presentation.Shell.ShowNewGameSetup();
            var ok = shell.Framework.Presentation.Shell.NewGame(new Id(SlotId), new Id(TierId), null);
            Assert.IsTrue(ok, "NewGame 应当返回成功");

            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                yield return null;
            }
            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page, "新游戏后应当能进入 InWorld");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private static RectTransform CreateScratchParent(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            return (RectTransform)go.transform;
        }

        private static CharacterStatsPanel BuildCharacterStatsPanel(ShellRoot shell, RectTransform parent)
        {
            var panel = new GameObject("CharacterStatsProbe", typeof(RectTransform)).AddComponent<CharacterStatsPanel>();
            panel.Construct(parent, shell.Framework.Presentation.CharacterStats);
            return panel;
        }

        private static DialogPanel BuildDialogPanel(ShellRoot shell, RectTransform parent)
        {
            var panel = new GameObject("DialogProbe", typeof(RectTransform)).AddComponent<DialogPanel>();
            panel.Construct(parent, shell.Framework.Presentation.DialogView, shell.Framework.Presentation.UiIntents, shell.Framework.Presentation.L10n);
            return panel;
        }

        /// <summary>真实打开一次 gossip 会话，驱动共享 DialogViewModel（presentation.DialogView）的
        /// Gossip 属性变为非空——DialogPanel.RefreshUi 只有在 Story/Gossip 非空时才会经
        /// RebuildOptions 真的建出按钮（见该方法源码），本文件要断言按钮颜色/精灵，必须先有真实的
        /// 选项数据可渲染，不能只测空态。复用 UiSuiteTests 同款样例数据（npc.sample_hunter /
        /// dialog.sample_hunter），不新增数据文件。</summary>
        private static void OpenSampleGossip(ShellRoot shell)
        {
            var playerId = shell.Framework.PlayerId;
            var gossip = shell.Framework.Gameplay.Dialog.OpenGossip(playerId, new Id("npc.sample_hunter"), new Id("dialog.sample_hunter"));
            Assert.Greater(gossip.Options.Count, 0, "gossip 菜单应当至少有一个可见选项，后续用例才能断言真实渲染出的按钮");
        }

        private static Sprite CreateProbeSprite(string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name + "Texture" };
            var pixels = new Color32[4];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 40, 200, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        /// <summary>生成一个与 UiSkin 默认字体不同引用的 TMP_FontAsset 探针：复用同一条已验证的
        /// "Resources.Load&lt;Font&gt; -> TMP_FontAsset.CreateFontAsset"路径（见 UiSkin.cs 顶部
        /// 判断记录"字体复用 U1 已有方案"），CreateFontAsset 本身是纯函数式生成，每次调用返回一个
        /// 新实例，足以和 UiSkin 内部缓存的默认字体做引用相等性区分。</summary>
        private static TMP_FontAsset CreateProbeFont()
        {
            var osFont = Resources.Load<Font>(PlaceholderFontResourcePath);
            Assert.IsNotNull(osFont, "占位字体资源应当存在（同 UiSkin 默认字体生成路径）");
            var probe = TMP_FontAsset.CreateFontAsset(osFont);
            Assert.IsNotNull(probe, "TMP_FontAsset.CreateFontAsset 应当能生成探针字体资产");
            return probe!;
        }

        private static UiSkinOverride BuildProbeOverride() => new UiSkinOverride
        {
            PanelBackground = new Color(0.11f, 0.22f, 0.33f, 0.44f),
            PanelBorder = new Color(0.55f, 0.66f, 0.77f, 0.88f),
            TextColor = new Color(0.12f, 0.34f, 0.56f, 1f),
            AccentColor = new Color(0.21f, 0.43f, 0.65f, 1f),
            DisabledColor = new Color(0.31f, 0.32f, 0.33f, 1f),
            DangerColor = new Color(0.91f, 0.13f, 0.14f, 1f),
            ButtonIdleColor = new Color(0.41f, 0.42f, 0.43f, 1f),
            ButtonHoverColor = new Color(0.51f, 0.52f, 0.53f, 1f),
            PanelSprite = CreateProbeSprite("ProbePanelSprite"),
            FlatSprite = CreateProbeSprite("ProbeFlatSprite"),
            Font = CreateProbeFont(),
        };

        private static void AssertPanelBackgroundMatches(RectTransform parent, string panelChildName, Sprite expectedSprite)
        {
            var image = parent.Find(panelChildName).GetComponent<Image>();
            Assert.IsNotNull(image, $"{panelChildName} 应当有背景 Image 组件");
            Assert.AreSame(expectedSprite, image.sprite, $"{panelChildName} 背景精灵应当匹配期望值");
            Assert.AreEqual(Image.Type.Sliced, image.type, $"{panelChildName} 背景应当是九宫格切片类型");
            Assert.IsTrue(Color.white == image.color, $"{panelChildName} 背景 Image.color 应当保持白色（颜色已烘进精灵本身）");
        }

        private static void AssertLabelMatches(TextMeshProUGUI label, TMP_FontAsset expectedFont, Color expectedTextColor)
        {
            Assert.AreSame(expectedFont, label.font, "文本控件字体应当匹配期望值");
            Assert.IsTrue(expectedTextColor == label.color, "文本控件颜色应当匹配期望值");
        }

        // -----------------------------------------------------------------
        // 用例 1：未安装覆盖时，两个面板的实际组件值应与改动前逐字节一致。
        // -----------------------------------------------------------------
        [UnityTest]
        public IEnumerator NoOverrideInstalled_CharacterStatsAndDialogPanels_MatchFrameworkDefaults()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);
            OpenSampleGossip(shell);

            var defaultPanelSprite = UiSkin.PanelSprite;
            var defaultFlatSprite = UiSkin.FlatSprite;
            var defaultFont = UiSkin.Font;
            var defaultTextColor = UiSkin.TextColor;
            var defaultButtonIdleColor = UiSkin.ButtonIdleColor;

            var parent = CreateScratchParent("NoOverrideScratchParent");
            try
            {
                var statsPanel = BuildCharacterStatsPanel(shell, parent);
                AssertPanelBackgroundMatches(parent, "CharacterStatsPanel", defaultPanelSprite);
                var statsBody = parent.Find("CharacterStatsPanel/Body").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(statsBody, defaultFont, defaultTextColor);

                var dialogPanel = BuildDialogPanel(shell, parent);
                dialogPanel.RefreshUi();
                AssertPanelBackgroundMatches(parent, "DialogPanel", defaultPanelSprite);
                var dialogBody = parent.Find("DialogPanel/Content/Body").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(dialogBody, defaultFont, defaultTextColor);

                var optionButtonGo = parent.Find("DialogPanel/Content/Options/Option0");
                Assert.IsNotNull(optionButtonGo, "打开真实 gossip 会话后，对话面板应当渲染出至少一个选项按钮");
                var optionImage = optionButtonGo.GetComponent<Image>();
                Assert.AreSame(defaultFlatSprite, optionImage.sprite, "选项按钮精灵应当匹配默认纯色精灵");
                Assert.IsTrue(defaultButtonIdleColor == optionImage.color, "选项按钮颜色应当匹配默认按钮常态色");
                var optionLabel = optionButtonGo.Find("Label").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(optionLabel, defaultFont, defaultTextColor);

                Assert.IsFalse(UiSkin.IsOverrideInstalled, "本用例全程不应安装任何皮肤覆盖");
            }
            finally
            {
                Object.Destroy(parent.gameObject);
            }
        }

        // -----------------------------------------------------------------
        // 用例 2：安装覆盖后，新建的两个面板应当拿到覆盖值；已经建好的面板（用例 1 里已销毁，
        // 本用例场景重新加载后生产装配另建的默认面板）不受影响——本用例只断言"新建的面板拿到
        // 覆盖值"，不断言"旧面板保持默认"（那是 UiPanelHost 生产装配的既有面板，非本文件探针）。
        // -----------------------------------------------------------------
        [UnityTest]
        public IEnumerator InstallOverride_NewlyBuiltPanels_UseOverrideValues()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);
            OpenSampleGossip(shell);

            var probe = BuildProbeOverride();
            UiSkin.Install(probe);
            Assert.IsTrue(UiSkin.IsOverrideInstalled, "安装后 IsOverrideInstalled 应当为真");

            var parent = CreateScratchParent("OverrideScratchParent");
            try
            {
                BuildCharacterStatsPanel(shell, parent);
                AssertPanelBackgroundMatches(parent, "CharacterStatsPanel", probe.PanelSprite!);
                var statsBody = parent.Find("CharacterStatsPanel/Body").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(statsBody, probe.Font!, probe.TextColor!.Value);

                var dialogPanel = BuildDialogPanel(shell, parent);
                dialogPanel.RefreshUi();
                AssertPanelBackgroundMatches(parent, "DialogPanel", probe.PanelSprite!);
                var dialogBody = parent.Find("DialogPanel/Content/Body").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(dialogBody, probe.Font!, probe.TextColor!.Value);

                var optionButtonGo = parent.Find("DialogPanel/Content/Options/Option0");
                Assert.IsNotNull(optionButtonGo, "安装覆盖后仍应正常渲染出选项按钮");
                var optionImage = optionButtonGo.GetComponent<Image>();
                Assert.AreSame(probe.FlatSprite, optionImage.sprite, "选项按钮精灵应当匹配覆盖的纯色精灵");
                Assert.IsTrue(probe.ButtonIdleColor!.Value == optionImage.color, "选项按钮颜色应当匹配覆盖的按钮常态色");
                var optionLabel = optionButtonGo.Find("Label").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(optionLabel, probe.Font!, probe.TextColor!.Value);

                // 决定 5：UiWidgets 里第四个读取 UiSkin 的方法（CreateProgressBar）同样应当自动生效，
                // 即便它没有被 CharacterStatsPanel/DialogPanel 直接使用。
                var (progressRoot, progressFill) = UiWidgets.CreateProgressBar("ProgressProbe", parent, Color.white);
                var progressBg = progressRoot.GetComponent<Image>();
                Assert.AreSame(probe.PanelSprite, progressBg.sprite, "进度条底板精灵应当匹配覆盖的九宫格精灵");
                Assert.AreSame(probe.FlatSprite, progressFill.sprite, "进度条填充精灵应当匹配覆盖的纯色精灵");
            }
            finally
            {
                Object.Destroy(parent.gameObject);
                UiSkin.Reset();
            }
        }

        // -----------------------------------------------------------------
        // 用例 3：卸载覆盖后，新建的面板应当回到框架默认值（确定性复位路径）。
        // -----------------------------------------------------------------
        [UnityTest]
        public IEnumerator ResetAfterOverride_NewlyBuiltPanels_RevertToFrameworkDefaults()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);
            OpenSampleGossip(shell);

            var defaultPanelSprite = UiSkin.PanelSprite;
            var defaultFlatSprite = UiSkin.FlatSprite;
            var defaultFont = UiSkin.Font;
            var defaultTextColor = UiSkin.TextColor;
            var defaultButtonIdleColor = UiSkin.ButtonIdleColor;

            UiSkin.Install(BuildProbeOverride());
            Assert.IsTrue(UiSkin.IsOverrideInstalled);

            UiSkin.Reset();
            Assert.IsFalse(UiSkin.IsOverrideInstalled, "Reset 后 IsOverrideInstalled 应当为假");

            var parent = CreateScratchParent("ResetScratchParent");
            try
            {
                BuildCharacterStatsPanel(shell, parent);
                AssertPanelBackgroundMatches(parent, "CharacterStatsPanel", defaultPanelSprite);
                var statsBody = parent.Find("CharacterStatsPanel/Body").GetComponent<TextMeshProUGUI>();
                AssertLabelMatches(statsBody, defaultFont, defaultTextColor);

                var dialogPanel = BuildDialogPanel(shell, parent);
                dialogPanel.RefreshUi();
                var optionButtonGo = parent.Find("DialogPanel/Content/Options/Option0");
                Assert.IsNotNull(optionButtonGo, "复位后仍应正常渲染出选项按钮");
                var optionImage = optionButtonGo.GetComponent<Image>();
                Assert.AreSame(defaultFlatSprite, optionImage.sprite, "复位后选项按钮精灵应当回到默认纯色精灵");
                Assert.IsTrue(defaultButtonIdleColor == optionImage.color, "复位后选项按钮颜色应当回到默认按钮常态色");
            }
            finally
            {
                Object.Destroy(parent.gameObject);
            }
        }

        // -----------------------------------------------------------------
        // 用例 4（钉死 UiSkin.cs 判断记录"不缓存覆盖结果"）：先访问一次 UiSkin.PanelSprite 触发
        // 默认值的惰性缓存生成，之后再安装覆盖——新建的面板必须拿到覆盖值，而不是被那份默认值缓存
        // 挡住。这是消费方反馈原文的真实使用顺序（面板体系此前已经建过默认皮肤的面板，消费方后来
        // 才想起要换皮）。
        // -----------------------------------------------------------------
        [UnityTest]
        public IEnumerator AccessPanelSpriteBeforeInstall_OverrideStillAppliesToPanelsBuiltAfterwards()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            // 先触发默认九宫格精灵的惰性缓存生成（本用例独立于其它用例执行顺序断言这一点：直接读
            // 一次属性，不依赖"前面某条用例已经读过"这种隐式前提）。
            var cachedDefaultPanelSprite = UiSkin.PanelSprite;
            Assert.IsNotNull(cachedDefaultPanelSprite, "访问 UiSkin.PanelSprite 应当能触发默认值生成");

            var probe = BuildProbeOverride();
            UiSkin.Install(probe);

            // 覆盖安装之后，UiSkin.PanelSprite 属性本身也应当立即切换为覆盖值——不是"只对新建面板
            // 生效、静态属性读数仍是旧缓存"这种不一致状态。
            Assert.AreSame(probe.PanelSprite, UiSkin.PanelSprite, "安装覆盖后 UiSkin.PanelSprite 应当立即返回覆盖精灵，不受此前触发的默认值缓存影响");
            Assert.AreNotSame(cachedDefaultPanelSprite, UiSkin.PanelSprite, "覆盖精灵应当与此前缓存的默认精灵是不同的对象");

            var parent = CreateScratchParent("LateInstallScratchParent");
            try
            {
                BuildCharacterStatsPanel(shell, parent);
                AssertPanelBackgroundMatches(parent, "CharacterStatsPanel", probe.PanelSprite!);
            }
            finally
            {
                Object.Destroy(parent.gameObject);
                UiSkin.Reset();
            }
        }
    }
}

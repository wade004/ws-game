#nullable enable
// ReferenceSkinPlayModeTests：参考皮肤包 skin.reference_fantasy（assets/_reference_fantasy，ADR-0152）的真实美术 PlayMode 验收。
//
// EquipUiSkinPlayModeTests 用"程序生成的纯色皮肤"证明换皮肤链路成立；本类换成真实美术（本地出图 + 后处理的一整套皮肤、物品图标、纸娃娃静态层图），
// 把"真实素材走完整条链路"变成永久回归，目的是让问题在框架里暴露，而不是等具体游戏接入时才发现：
//   - 逐元素完整性：清单展开的每个元素（8 槽位 × 5 品质 × 全部状态/可选项）解析到的精灵像素 = 皮肤包里那张文件的像素，0 回落、0 可选缺失；
//   - 主题：theme.json 的颜色逐个读回，九宫格 border 取 theme 令牌；
//   - 真实面板：装备面板（槽位框、品质框、真实图标、纸娃娃预览五个方向）、背包面板（物品格）、衣橱场景（带皮肤逐件穿戴 + 轮播）零异常；
//   - 本地截图（环境变量 GF_SKIN_SHOTS_DIR 指向目录时输出，不进 git）：皮肤画廊、装备面板、背包面板、衣橱拼图。
//
// 资源内容根：把占位资产（皮肤、图标、静态层）与参考资产叠成一个临时根，经 UnityResourceLoader.RootDirOverrideForTests 指过去；
// 面板用自己新建的资源加载器（不与进程里其它用例共享缓存，避免读到别的用例已缓存的占位图标）。
// 判断记录（范围边界）：纸娃娃的"身体层"（placeholder_hero）与逐层动画剪辑（sprite_anim）仍用占位美术，不在皮肤包范围内，用例不核对它们的像素。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.LabHost;
using Adapter.Unity.Ui;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.Common;
using Lab;
using NUnit.Framework;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:ui")]
    [Category("module:lab")]
    public sealed class ReferenceSkinPlayModeTests
    {
        private const string PackName = "reference_fantasy";
        private const string PackRef = "skin." + PackName;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private string _root = string.Empty;
        private string _packDir = string.Empty;
        private string _referenceDir = string.Empty;
        private string? _previousOverride;

        // ───────── 夹具 ─────────

        [SetUp]
        public void SetUp()
        {
            _referenceDir = Path.Combine(SkinTestKit.RepoRoot, "assets", "_reference_fantasy");
            Assert.IsTrue(Directory.Exists(_referenceDir), "缺参考资产目录 " + _referenceDir);
            var placeholder = Path.Combine(SkinTestKit.RepoRoot, "assets", "_placeholder");
            _root = Path.Combine(Application.temporaryCachePath, "reference_skin_tests", Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
            CopyDirectory(Path.Combine(placeholder, "ui", "skin", "default"), Path.Combine(_root, "ui", "skin", "default"));
            CopyDirectory(Path.Combine(_referenceDir, "ui", "skin", PackName), Path.Combine(_root, "ui", "skin", PackName));
            foreach (var sub in new[] { "icons", "sprites" })
            {
                CopyDirectory(Path.Combine(placeholder, sub), Path.Combine(_root, sub));   // 先铺占位（身体层、未换美术的物品），再叠参考美术
                if (Directory.Exists(Path.Combine(_referenceDir, sub)))
                {
                    CopyDirectory(Path.Combine(_referenceDir, sub), Path.Combine(_root, sub));
                }
            }

            _packDir = Path.Combine(_root, "ui", "skin", PackName);
            _previousOverride = UnityResourceLoader.RootDirOverrideForTests;
            UnityResourceLoader.RootDirOverrideForTests = _root;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiSkin.Reset();
            UnityResourceLoader.RootDirOverrideForTests = _previousOverride;
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _spawned.Clear();
            foreach (var d in _disposables)
            {
                d.Dispose();
            }

            _disposables.Clear();
            SkinTestKit.Delete(_root);
            yield return null;
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(from))
            {
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

        private static string L10nText(WardrobeStage stage, string key)
        {
            var row = stage.Registry.GetAll("l10n.text").FirstOrDefault(r => r.TryGetString("key", out var k) && k == key);
            Assert.IsNotNull(row, "l10n.text 没有键 " + key);
            return row!.GetString("text");
        }

        /// <summary>数据集（data/_equip）里 inventory 布局行的 cell_size（读文件，不写死）。</summary>
        private static float DataInventoryCellSize()
        {
            var text = File.ReadAllText(Path.Combine(SkinTestKit.RepoRoot, "data", "_equip", "ui", "ui_layout_definition.json"));
            var at = text.IndexOf("\"panel\": \"inventory\"", StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "数据里没有 inventory 布局行");
            var match = System.Text.RegularExpressions.Regex.Match(text.Substring(at), @"""cell_size"":\s*([0-9.]+)");
            Assert.IsTrue(match.Success, "inventory 布局行没有 cell_size");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private WardrobeStage NewStage()
        {
            var template = LabHostTestSupport.Script(EquipWardrobeRunner.TemplateScript);
            var stage = WardrobeStage.Create(LabHostTestSupport.Host.Runner, template);
            _disposables.Add(stage);
            return stage;
        }

        private UiRoot NewUiRoot()
        {
            var root = UiRoot.Create("ReferenceSkinTestRoot");
            _spawned.Add(root.gameObject);
            return root;
        }

        private UiSkinPack LoadPack(string name)
        {
            var pack = UiSkinPack.Load("skin." + name, _root);
            _disposables.Add(pack);
            return pack;
        }

        /// <summary>皮肤包里实际带的槽位名与品质名（从文件列出，不写裸清单）。</summary>
        private (List<string> Slots, List<string> Qualities) PackNames()
        {
            List<string> Names(string sub) => Directory.GetFiles(Path.Combine(_packDir, sub), "*.png")
                .Select(f => Path.GetFileNameWithoutExtension(f)!).Where(n => !n.StartsWith("_", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return (Names("slot_frame"), Names("quality_frame"));
        }

        private static Color32[] FilePixels(string path, out int width, out int height)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(texture, File.ReadAllBytes(path)), "读不出 " + path);
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>精灵所在纹理的像素与磁盘文件一致（允许极少量编码/采样差异：每通道差不超过 <paramref name="tolerance"/>，超差像素不超过 0.5%）。</summary>
        private static void AssertPixelsEqualFile(Sprite? sprite, string file, string what, int tolerance = 2)
        {
            Assert.IsNotNull(sprite, what + " 没有精灵：" + file);
            var texture = sprite!.texture;
            var expected = FilePixels(file, out var w, out var h);
            Assert.AreEqual(w, texture.width, $"{what}: 宽度与文件 {file} 不一致");
            Assert.AreEqual(h, texture.height, $"{what}: 高度与文件 {file} 不一致");
            var actual = texture.GetPixels32();
            var bad = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                var a = actual[i];
                var e = expected[i];
                if (Math.Abs(a.a - e.a) > tolerance
                    || (e.a > 0 && (Math.Abs(a.r - e.r) > tolerance || Math.Abs(a.g - e.g) > tolerance || Math.Abs(a.b - e.b) > tolerance)))
                {
                    bad++;
                }
            }

            Assert.LessOrEqual(bad, expected.Length / 200, $"{what}: 精灵像素与文件 {file} 不一致（{bad}/{expected.Length} 个像素超差）");
        }

        private string PackFile(string relative) => Path.Combine(_packDir, relative.Replace('/', Path.DirectorySeparatorChar));

        private static void Pump(UnityResourceLoader loader)
        {
            var watch = Stopwatch.StartNew();
            while (loader.PendingLoadCount > 0 && watch.ElapsedMilliseconds < 8000)
            {
                loader.Tick();
                Thread.Sleep(1);
            }

            loader.Tick();
        }

        private static Color32 Hex(string hex)
        {
            var h = hex.TrimStart('#');
            return new Color32(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16), 255);
        }

        /// <summary>图标资源 id（<c>icon.item.std_sword_1h</c>）→ 仓库里参考资产的文件路径（<c>icons/item/std_sword_1h.png</c>）。</summary>
        private string IconFile(Id iconId)
        {
            var parts = iconId.Value.Split('.');
            Assert.AreEqual("icon", parts[0], "图标资源 id 形状：" + iconId.Value);
            return Path.Combine(_referenceDir, "icons", parts[1], string.Join(".", parts.Skip(2)) + ".png");
        }

        private string LayerFile(string spriteSet, string direction, string layer) =>
            Path.Combine(_referenceDir, "sprites", spriteSet, direction, layer + ".png");

        // ───────── 截图（本地产物，环境变量缺省时不输出） ─────────

        private static string? ShotDir => Environment.GetEnvironmentVariable("GF_SKIN_SHOTS_DIR") is { Length: > 0 } dir ? dir : null;

        private IEnumerator Shot(UiRoot root, string fileName, int width, int height)
        {
            var dir = ShotDir;
            if (dir == null)
            {
                yield break;
            }

            Directory.CreateDirectory(dir);
            var camGo = new GameObject("SkinShotCamera");
            _spawned.Add(camGo);
            var camera = camGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.15f, 1f);
            camera.cullingMask = 1 << root.gameObject.layer;
            camera.orthographic = true;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = texture;
            var canvas = root.Canvas;
            var scaler = root.GetComponent<CanvasScaler>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            picture.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(dir, fileName), ImageConversion.EncodeToPNG(picture));
            UnityEngine.Object.DestroyImmediate(picture);
            camera.targetTexture = null;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            TestContext.Out.WriteLine("[skin-shot] " + Path.Combine(dir, fileName));
        }

        // ───────── 清单逐元素与主题 ─────────

        [Test]
        public void Pack_WalksEveryManifestElement_EveryPixelComesFromThePackFiles_NoFallbacksNoOptionalAbsent()
        {
            var manifest = SkinTestKit.LoadManifest();
            var (slots, qualities) = PackNames();
            Assert.GreaterOrEqual(slots.Count, 8, "参考包覆盖 8 个槽位");
            Assert.GreaterOrEqual(qualities.Count, 5, "参考包覆盖五档品质");
            var pack = LoadPack(PackName);
            var def = LoadPack("default");
            Assert.IsFalse(pack.IsPlaceholder);
            Assert.IsFalse(pack.RefInvalid);

            var files = manifest.Expand(slots, qualities);
            var walked = 0;
            var optionalWalked = 0;
            foreach (var f in files)
            {
                if (!f.Element.IsImage)
                {
                    continue;
                }

                var generic = pack.ElementSprite(f);
                AssertPixelsEqualFile(generic, PackFile(f.Path), "清单元素 " + f.Path, tolerance: 0);
                Assert.IsTrue(UiSkinTypedAccess.TryGet(pack, f, out var typed), "清单元素没有类型化取用：" + f.Element.Id);
                Assert.AreSame(generic, typed, "类型化取用与清单路径不一致：" + f.Path);
                if (f.Element.Requirement == "optional")
                {
                    optionalWalked++;
                }

                // 对照：占位皮肤里同名元素（若有）的像素不同于参考包（证明读到的确实是参考美术，不是占位残留）。
                var placeholder = def.ElementSprite(f);
                if (placeholder != null && placeholder.texture.width == generic!.texture.width && placeholder.texture.height == generic.texture.height)
                {
                    CollectionAssert.AreNotEqual(placeholder.texture.GetPixels32(), generic.texture.GetPixels32(), "与占位皮肤逐像素相同：" + f.Path);
                }

                walked++;
            }

            Assert.AreEqual(files.Count(x => x.Element.IsImage), walked);
            Assert.Greater(optionalWalked, 0, "可选元素也要有（目标 optional_absent 为空）");
            Assert.AreEqual(0, pack.Fallbacks.Count, "参考包是全元素的，不应发生回落：" + string.Join(", ", pack.Fallbacks.Select(x => x.Item)));
            Assert.AreEqual(0, pack.OptionalAbsent.Count, "参考包带全部可选元素：" + string.Join(", ", pack.OptionalAbsent));
            // 包里的 PNG 恰好是清单展开的全集，没有清单之外的多余图片。
            var onDisk = Directory.GetFiles(_packDir, "*.png", SearchOption.AllDirectories)
                .Select(p => p.Substring(_packDir.Length + 1).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var expected = files.Where(x => x.Element.IsImage).Select(x => x.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(expected, onDisk);
            TestContext.Out.WriteLine($"[reference-skin] walked={walked} optional={optionalWalked} slots={slots.Count} qualities={qualities.Count} fallbacks=0 optional-absent=0");
        }

        [Test]
        public void Theme_ColorsReadBackAndNineSliceBordersFollowTheTokens()
        {
            var manifest = SkinTestKit.LoadManifest();
            var pack = LoadPack(PackName);
            var theme = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(File.ReadAllText(PackFile("theme.json")));
            Assert.IsTrue(theme.TryGetValue("colors", out var colorsValue));
            var colors = (Core.Foundation.Common.Json.JsonObject)colorsValue;
            foreach (var key in manifest.ColorTokens)
            {
                Assert.IsTrue(colors.TryGetValue(key, out var value), "theme.json 缺颜色 " + key);
                var c = pack.ThemeColor(key);
                Assert.IsTrue(c.HasValue, "读不到主题颜色 " + key);
                Assert.IsTrue(SkinTestKit.Same(Hex(((Core.Foundation.Common.Json.JsonString)value!).Value), (Color32)c!.Value), "主题颜色读回不一致：" + key);
            }

            foreach (var e in manifest.Elements.Where(x => x.IsNineSlice))
            {
                var border = pack.NineSliceBorder(e.NineSliceToken!, e.NineSliceDefault);
                Assert.AreNotEqual(e.NineSliceDefault, border, $"{e.Id}: 参考包的令牌应当是非缺省值（证明运行期读的是 theme.json）");
            }

            var panel = pack.PanelBackground()!;
            Assert.AreEqual(pack.NineSliceBorder("panel_border", 5), panel.border.x);
            Assert.AreEqual(pack.NineSliceBorder("panel_border", 5), panel.border.w);
            var tooltip = pack.TooltipBackground();
            Assert.AreEqual(pack.NineSliceBorder("tooltip_border", 2), tooltip.border.x);
            var button = pack.Button()!;
            Assert.AreEqual(pack.NineSliceBorder("button_border", 4), button.Normal!.border.x);
            Assert.IsNotNull(pack.CreateOverride(), "非占位皮肤会安装覆盖");
        }

        [Test]
        public void ResolveCellSize_ZeroMeansTheFramesNativeWidth_ForBothPanels()
        {
            var pack = LoadPack(PackName);
            var native = pack.SlotFrameDefault().rect.width;
            Assert.Greater(native, 48f, "参考包的槽位框原生边长不是占位皮肤的 48");
            Assert.AreEqual(native, UiPanelLayout.ResolveCellSize(0f, pack.SlotFrameDefault()), "cell_size=0 取皮肤包槽位框原生宽度（背包曾把它当成 32）");
            Assert.AreEqual(40f, UiPanelLayout.ResolveCellSize(40f, pack.SlotFrameDefault()));
        }

        // ───────── 真实面板 ─────────

        private (EquipmentPanel Panel, UiVisuals Visuals, UnityResourceLoader Loader) BuildEquipmentPanel(WardrobeStage stage, UiSkinPack pack, UiRoot root)
        {
            var loader = new UnityResourceLoader();
            var visuals = new UiVisuals(pack, stage.Registry, stage.DisplayInfo, loader);
            visuals.L10n = stage.L10n;       // 与生产宿主一致（UiPanelHost / 换装场景都给 Visuals 接本地化宿主）：槽位标签、物品名取显示名
            _disposables.Add(visuals);
            var go = UiWidgets.CreateRoot("EquipmentPanelHost", root.Content);
            var panel = go.gameObject.AddComponent<EquipmentPanel>();
            var layout = visuals.LayoutOf(UiPanel.Equipment, EquipmentPanel.DefaultLayout);
            layout = new UiPanelLayout(layout.Anchor, layout.Columns, layout.CellSize, layout.PreviewScale, "placeholder_hero", layout.PreviewDirection);
            panel.Construct(go, stage.Panel, visuals, null, layout);
            return (panel, visuals, loader);
        }

        private static void Settle(EquipmentPanel panel, UnityResourceLoader loader)
        {
            panel.RefreshUi();      // 首次请求发起加载
            Pump(loader);
            panel.RefreshUi();      // 加载完成后的刷新拿到精灵
        }

        [UnityTest]
        public IEnumerator EquipmentPanel_WithRealArt_ShowsPackFramesRealIconsAndPaperdollInEveryDirection()
        {
            var stage = NewStage();
            var pack = LoadPack(PackName);
            UiSkin.Install(pack.CreateOverride()!);
            var root = NewUiRoot();
            var (panel, visuals, loader) = BuildEquipmentPanel(stage, pack, root);

            Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, panel.Background.GetComponent<Image>().type);
            AssertPixelsEqualFile(panel.Background.GetComponent<Image>().sprite, PackFile("panel/background.png"), "面板底图", 0);
            Assert.AreEqual(pack.NineSliceBorder("panel_border", 5), panel.Background.GetComponent<Image>().sprite.border.x);
            AssertPixelsEqualFile(panel.Preview.Background.sprite, PackFile("paperdoll_preview/background.png"), "预览区背景", 0);
            foreach (var cell in panel.Cells)
            {
                AssertPixelsEqualFile(cell.Frame.sprite, PackFile("slot_frame/" + cell.SlotName + ".png"), "槽位框 " + cell.SlotName, 0);
                var state = cell.Button.spriteState;
                AssertPixelsEqualFile(state.highlightedSprite, PackFile("slot_frame/_highlight.png"), "槽位框 hover", 0);
                AssertPixelsEqualFile(state.pressedSprite, PackFile("slot_frame/_pressed.png"), "槽位框 pressed", 0);
                AssertPixelsEqualFile(state.selectedSprite, PackFile("slot_frame/_selected.png"), "槽位框 selected", 0);
                AssertPixelsEqualFile(state.disabledSprite, PackFile("slot_frame/_disabled.png"), "槽位框 disabled", 0);
                Assert.AreEqual(panel.CellSize, cell.Root.sizeDelta.x);

                // 复现：空槽标签曾显示 std_main_hand 这类 id；现在取 slot_definition.name_key 的本地化文案（期望值读数据行）。
                var slotKey = stage.Registry.Get("item.slot_definition", cell.SlotId)!.GetString("name_key");
                Assert.AreEqual(L10nText(stage, slotKey), cell.Label.text, "空槽标签 " + cell.SlotName);
                StringAssert.DoesNotContain("std_", cell.Label.text, "空槽标签不应是 id");
            }

            var iconsChecked = 0;
            var layersChecked = 0;
            var paperdollItems = 0;
            foreach (var entry in stage.Entries)
            {
                Assert.IsTrue(stage.Equip(entry.ItemId), "穿不上 " + entry.ItemId);
                Settle(panel, loader);
                var slotIndex = stage.Panel.Slots.ToList().FindIndex(s => s.SlotId.Value == entry.SlotId);
                var slot = stage.Panel.Slots[slotIndex];
                var cell = panel.Cells[slotIndex];
                Assert.IsTrue(slot.Occupied);

                // 真实图标：像素 = 参考资产里这件物品的图标文件。
                var iconId = visuals.IconOfTemplate(new Id(entry.ItemId));
                Assert.IsTrue(iconId.HasValue, entry.ItemId + " 没有 display.map.icon_id");
                Assert.IsTrue(cell.Icon.enabled, entry.ItemId + " 的图标没出现");
                AssertPixelsEqualFile(cell.Icon.sprite, IconFile(iconId!.Value), "图标 " + entry.ItemId);
                iconsChecked++;

                // 品质框来自皮肤包。
                Assert.IsTrue(cell.Quality.enabled);
                AssertPixelsEqualFile(cell.Quality.sprite, PackFile("quality_frame/" + slot.QualityName + ".png"), "品质框 " + slot.QualityName, 0);

                if (!entry.IsPaperdoll)
                {
                    continue;
                }

                paperdollItems++;
                // 纸娃娃：五个方向逐个核对该件装备的静态层是参考层图。
                foreach (var direction in PaperdollPreview.Directions)
                {
                    panel.Preview.SetDirection(direction);
                    Settle(panel, loader);
                    Assert.AreEqual(stage.Panel.PaperdollLayers.Count, panel.Preview.EquipmentLayerCount);
                    foreach (var (layer, set, image) in panel.Preview.EquipmentLayers)
                    {
                        Assert.IsTrue(image.enabled, $"{entry.ItemId} {direction}/{layer} 没有精灵");
                        if (File.Exists(LayerFile(set, direction, layer)))
                        {
                            AssertPixelsEqualFile(image.sprite, LayerFile(set, direction, layer), $"静态层 {set}/{direction}/{layer}");
                            layersChecked++;
                        }
                    }

                    Assert.IsTrue(panel.Preview.BodyHasSprite, "身体层（占位美术）没有出现");
                }
            }

            Assert.AreEqual(stage.Entries.Count, iconsChecked);
            Assert.Greater(paperdollItems, 0);
            Assert.Greater(layersChecked, 0, "至少核对过一张参考静态层图");
            Assert.AreEqual(0, visuals.FailedCount, "有图标/层图加载失败");
            Assert.AreEqual(0, pack.Fallbacks.Count);
            Assert.AreEqual(0, pack.OptionalAbsent.Count);
            TestContext.Out.WriteLine($"[reference-equipment-panel] cells={panel.Cells.Count} icons={iconsChecked} paperdoll-items={paperdollItems} static-layers={layersChecked} cell={panel.CellSize}");

            panel.Preview.SetDirection("front_side_r");
            Settle(panel, loader);
            yield return Shot(root, "v2_equipment_panel.png", 900, 560);
        }

        [UnityTest]
        public IEnumerator InventoryPanel_WithRealArt_ShowsRealIconsAndPackQualityFrames()
        {
            var stage = NewStage();
            var pack = LoadPack(PackName);
            UiSkin.Install(pack.CreateOverride()!);
            var root = NewUiRoot();
            var loader = new UnityResourceLoader();
            var visuals = new UiVisuals(pack, stage.Registry, stage.DisplayInfo, loader);
            visuals.L10n = stage.L10n;       // 与生产宿主一致：行标签取物品显示名（复现：夹具没接本地化宿主时截图里是 std_bow 这类 id）
            _disposables.Add(visuals);

            // 把全部物品放进背包：逐件穿上再全部卸下（卸下的物品回到背包）。
            foreach (var entry in stage.Entries)
            {
                Assert.IsTrue(stage.Equip(entry.ItemId));
            }

            stage.UnequipAll();
            var slotIds = stage.Panel.Slots.Select(s => s.SlotId).ToList();
            var vm = new InventoryViewModel(stage.Data, slotIds);
            _disposables.Add(vm);
            Assert.AreEqual(stage.Entries.Count, vm.Slots.Count, "背包里应有全部样例物品");

            var go = UiWidgets.CreateRoot("InventoryPanelHost", root.Content);
            var panel = go.gameObject.AddComponent<InventoryPanel>();
            panel.Construct(go, vm, null!, visuals);
            panel.RefreshUi();
            Pump(loader);
            panel.RefreshUi();

            Assert.AreEqual(vm.Slots.Count, panel.Cells.Count);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Background);
            foreach (var cell in panel.Cells)
            {
                // 复现：竖直列表不控制行高、横向布局不控宽，格子曾按矩形缺省 100 x 100 画出，cell_size 形同虚设（面板 260 高只放得下两行）。
                var rect = cell.Frame.rectTransform.rect;
                Assert.AreEqual(panel.CellSize, rect.width, 0.5f, "物品格宽度应取 cell_size");
                Assert.AreEqual(panel.CellSize, rect.height, 0.5f, "物品格高度应取 cell_size");
            }

            AssertPixelsEqualFile(panel.Background.GetComponent<Image>().sprite, PackFile("panel/background.png"), "背包面板底图", 0);
            for (var i = 0; i < vm.Slots.Count; i++)
            {
                var template = vm.Slots[i].TemplateId;
                var cell = panel.Cells[i];
                AssertPixelsEqualFile(cell.Frame.sprite, PackFile("slot_frame/_default.png"), "物品格框", 0);
                Assert.IsTrue(cell.Icon.enabled, template.Value + " 的图标没出现");
                AssertPixelsEqualFile(cell.Icon.sprite, IconFile(visuals.IconOfTemplate(template)!.Value), "背包图标 " + template.Value);
                Assert.IsTrue(cell.Quality.enabled);
                AssertPixelsEqualFile(cell.Quality.sprite, PackFile("quality_frame/" + visuals.QualityNameOf(template) + ".png"), "背包品质框", 0);
            }

            // 格子边长取数据里 inventory 布局行的 cell_size（数据集 data/_equip 声明 48，读文件取值，不写死）；图标矩形 = 格子减两侧 2 px 内边距。
            var dataCell = DataInventoryCellSize();
            Assert.AreEqual(dataCell, panel.CellSize, 0.01f, "背包格子应取数据 cell_size");
            Assert.Greater(dataCell, 32f, "数据声明的格子比缺省 32 px 大，图标才看得清");
            foreach (var cell in panel.Cells)
            {
                Assert.AreEqual(dataCell - 4f, cell.Icon.rectTransform.rect.width, 0.5f, "背包图标宽度 = 数据格子边长 - 4");
                Assert.AreEqual(dataCell - 4f, cell.Icon.rectTransform.rect.height, 0.5f, "背包图标高度 = 数据格子边长 - 4");
            }

            // 行标签是物品显示名（读 l10n.text 数据行），不是 id。
            var labels = panel.LabelTexts;
            for (var i = 0; i < vm.Slots.Count; i++)
            {
                var nameKey = stage.Registry.Get("item.template", vm.Slots[i].TemplateId)!.GetString("name_key");
                StringAssert.StartsWith(L10nText(stage, nameKey) + " x", labels[i], "背包行标签应是显示名");
                StringAssert.DoesNotContain("std_", labels[i], "背包行标签不应是 id");
            }

            Assert.AreEqual(0, visuals.FailedCount);
            yield return Shot(root, "v2_inventory_panel.png", 900, 560);
        }

        // ───────── 画廊（拖拽态、提示框三件、状态变体、九宫格各尺寸） ─────────

        [UnityTest]
        public IEnumerator Gallery_DrawsEveryElement_NineSliceBordersMatchTheTokens_AndShotsTheWholePack()
        {
            var manifest = SkinTestKit.LoadManifest();
            var (slots, qualities) = PackNames();
            var pack = LoadPack(PackName);
            UiSkin.Install(pack.CreateOverride()!);
            var root = NewUiRoot();
            var gallery = UiSkinGallery.Build(root.Content, pack, manifest, slots, qualities);
            Assert.AreEqual(0, gallery.Absent.Count, "参考包没有可选缺失");
            foreach (var entry in gallery.Entries.Where(e => e.File.Element.IsNineSlice))
            {
                var expected = pack.NineSliceBorder(entry.File.Element.NineSliceToken!, entry.File.Element.NineSliceDefault);
                Assert.AreEqual(new Vector4(expected, expected, expected, expected), entry.Image.sprite.border, entry.File.Path);
                Assert.AreEqual(1f, entry.Image.pixelsPerUnit, 1e-4f);
            }

            var drawn = gallery.Entries.Select(e => e.File.Path).Distinct().Count();
            Assert.AreEqual(manifest.Expand(slots, qualities).Count(f => f.Element.IsImage), drawn);
            yield return Shot(root, "01_skin_gallery.png", 1280, 1180);
        }

        // ───────── 衣橱场景 + 拼图 ─────────

        [UnityTest]
        public IEnumerator WardrobeScene_WithTheReferenceSkin_WalksEveryItemAndTheCarouselWithoutExceptions()
        {
            var host = LabHostTestSupport.Host;
            // 进程里别的用例可能已经把占位图标/层图缓存进全局加载器：先卸掉这次要用的资源 id，让场景从参考资产重新加载。
            var template = LabHostTestSupport.Script(EquipWardrobeRunner.TemplateScript);
            var probe = WardrobeStage.Create(host.Runner, template);
            _disposables.Add(probe);
            var global = UnityEngineHost.Ensure().ResourceLoader;
            var probeVisuals = new UiVisuals(LoadPack("default"), probe.Registry, probe.DisplayInfo, global);
            foreach (var entry in probe.Entries)
            {
                var icon = probeVisuals.IconOfTemplate(new Id(entry.ItemId));
                if (icon.HasValue)
                {
                    global.Unload(icon.Value);
                }

                var set = UiVisuals.SetName(entry.ItemId);
                foreach (var direction in PaperdollPreview.Directions)
                {
                    foreach (var layer in new[] { "hand_main", "chest", "hand_off", "head", "legs", "feet", "hands", "neck" })
                    {
                        global.Unload(UiVisuals.LayerId(set, direction, layer));
                    }
                }
            }

            var go = new GameObject("ReferenceWardrobeScene");
            _spawned.Add(go);
            var scene = go.AddComponent<EquipWardrobeScene>();
            typeof(EquipWardrobeScene).GetField("autoAdvance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(scene, false);
            scene.SkinRef = PackRef;
            scene.Begin(host);
            yield return null;

            Assert.AreEqual(PackRef, scene.Visuals!.Pack.SkinRef);
            Assert.IsFalse(scene.Visuals.Pack.IsPlaceholder);
            Assert.IsTrue(UiSkin.IsOverrideInstalled, "非占位皮肤会像生产宿主一样安装皮肤覆盖");
            var stage = scene.Stage!;
            var panel = scene.Panel!;
            AssertPixelsEqualFile(panel.Preview.Background.sprite, PackFile("paperdoll_preview/background.png"), "场景预览区背景", 0);
            for (var i = 0; i < stage.Entries.Count; i++)
            {
                Assert.IsTrue(scene.StepNext());
                scene.AdvanceCarousel();
                for (var frame = 0; frame < 3; frame++)
                {
                    yield return null;       // 全局加载器由宿主每帧 Tick
                }

                var entry = stage.Entries[i];
                var slotIndex = stage.Panel.Slots.ToList().FindIndex(s => s.SlotId.Value == entry.SlotId);
                var cell = panel.Cells[slotIndex];
                var waited = 0;
                while (!cell.Icon.enabled && waited++ < 120)
                {
                    yield return null;
                }

                Assert.IsTrue(cell.Icon.enabled, entry.ItemId + " 的图标没出现");
                var iconId = scene.Visuals.IconOfTemplate(new Id(entry.ItemId));
                AssertPixelsEqualFile(cell.Icon.sprite, IconFile(iconId!.Value), "场景图标 " + entry.ItemId);
            }

            Assert.IsFalse(scene.StepNext(), "穿完一圈后下一步是卸空回绕");
            Assert.AreEqual(0, stage.Panel.OccupiedCount);
            var lap = scene.Carousel!.Lap();
            Assert.AreEqual(scene.Carousel.Count, lap.Count);
            Assert.AreEqual(0, scene.Visuals.FailedCount, "场景里有图标/层图加载失败");
            Assert.AreEqual(0, scene.Visuals.Pack.Fallbacks.Count);
        }

        [UnityTest]
        public IEnumerator Wardrobe_Montage_EveryPaperdollItemInEveryDirection_RendersRealStaticLayers()
        {
            var stage = NewStage();
            var pack = LoadPack(PackName);
            UiSkin.Install(pack.CreateOverride()!);
            var root = NewUiRoot();
            var loader = new UnityResourceLoader();
            var visuals = new UiVisuals(pack, stage.Registry, stage.DisplayInfo, loader);
            _disposables.Add(visuals);

            var items = stage.Entries.Where(e => e.IsPaperdoll).ToList();
            Assert.Greater(items.Count, 0);
            const float cellW = 150f;
            const float cellH = 225f;
            var directions = PaperdollPreview.Directions;
            var previews = new List<(PaperdollPreview Preview, IReadOnlyList<EquipmentPaperdollLayer> Layers, string Item, string Direction)>();
            for (var row = 0; row < items.Count; row++)
            {
                stage.UnequipAll();
                Assert.IsTrue(stage.Equip(items[row].ItemId));
                var layers = stage.Panel.PaperdollLayers.ToList();
                Assert.Greater(layers.Count, 0, items[row].ItemId + " 没有纸娃娃图层");
                for (var col = 0; col < directions.Length; col++)
                {
                    var preview = new PaperdollPreview(root.Content, visuals, "placeholder_hero", 0.58f, directions[col]);
                    var rect = preview.Root;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(8f + col * (cellW + 6f), -(8f + row * (cellH + 6f)));
                    previews.Add((preview, layers, items[row].ItemId, directions[col]));
                }
            }

            foreach (var (preview, layers, _, _) in previews)
            {
                preview.Refresh(layers);
            }

            Pump(loader);
            var checkedLayers = 0;
            foreach (var (preview, layers, item, direction) in previews)
            {
                preview.Refresh(layers);
                Assert.IsTrue(preview.BodyHasSprite, $"{item}/{direction} 身体层没有出现");
                Assert.AreEqual(layers.Count, preview.EquipmentLayersWithSprite, $"{item}/{direction} 有纸娃娃层没拿到精灵");
                foreach (var (layer, set, image) in preview.EquipmentLayers)
                {
                    var file = LayerFile(set, direction, layer);
                    if (File.Exists(file))
                    {
                        AssertPixelsEqualFile(image.sprite, file, $"拼图 {set}/{direction}/{layer}");
                        checkedLayers++;
                    }
                }
            }

            Assert.AreEqual(items.Count * directions.Length, previews.Count);
            Assert.Greater(checkedLayers, 0);
            TestContext.Out.WriteLine($"[reference-montage] items={items.Count} directions={directions.Length} static-layers={checkedLayers}");
            yield return Shot(root, "v2_wardrobe_montage.png", (int)(16 + directions.Length * (cellW + 6f)), (int)(16 + items.Count * (cellH + 6f)));
        }

        // ───────── 逐方向层序与静态层锚点对齐（ADR-0152 第 2 轮） ─────────

        private (PaperdollPreview Preview, UiVisuals Visuals, UnityResourceLoader Loader) NewPreview(WardrobeStage stage, UiRoot root, string direction)
        {
            var pack = LoadPack(PackName);
            var loader = new UnityResourceLoader();
            var visuals = new UiVisuals(pack, stage.Registry, stage.DisplayInfo, loader);
            _disposables.Add(visuals);
            return (new PaperdollPreview(root.Content, visuals, "placeholder_hero", 0.58f, direction), visuals, loader);
        }

        private static void SettlePreview(PaperdollPreview preview, IReadOnlyList<EquipmentPaperdollLayer> layers, UnityResourceLoader loader)
        {
            preview.Refresh(layers);
            Pump(loader);
            preview.Refresh(layers);
        }

        [Test]
        public void Paperdoll_BackDirectionWeapon_IsDrawnBelowTheBody_OnlyInTheDeclaredDirections()
        {
            var stage = NewStage();
            var root = NewUiRoot();
            var (preview, _, loader) = NewPreview(stage, root, "front");
            var declaredItems = 0;
            var undeclaredItems = 0;
            foreach (var entry in stage.Entries.Where(e => e.IsPaperdoll))
            {
                stage.UnequipAll();
                Assert.IsTrue(stage.Equip(entry.ItemId));
                var layers = stage.Panel.PaperdollLayers.ToList();
                var layer = layers.Single();
                var behind = layer.BehindDirections;
                if (behind.Count > 0) declaredItems++; else undeclaredItems++;
                foreach (var direction in PaperdollPreview.Directions)
                {
                    preview.SetDirection(direction);
                    SettlePreview(preview, layers, loader);
                    var expectBehind = behind.Contains(new Id("dir." + direction));
                    var layerImage = preview.EquipmentLayers.Single().Image;
                    var layerIndex = layerImage.transform.GetSiblingIndex();
                    var bodyIndex = preview.BodyImage!.transform.GetSiblingIndex();
                    // 复现的现象：背面列武器盖在身体前面（兄弟序在身体之后）；声明了逐方向层序的方向，武器必须在身体之下。
                    Assert.AreEqual(expectBehind, layerIndex < bodyIndex, $"{entry.ItemId}/{direction}: 武器兄弟序 {layerIndex} 身体 {bodyIndex}，声明 behind={expectBehind}");
                    Assert.AreEqual(expectBehind, preview.IsLayerBehindBody(layer.Layer), $"{entry.ItemId}/{direction}");
                }
            }

            Assert.Greater(declaredItems, 0, "参考数据里至少有一件武器声明了背面层序");
            Assert.Greater(undeclaredItems, 0, "也要有一件没声明的（胸甲），证明缺省方向层序不变");
            // 具体到用户看到的现象：参考包的武器在 back 方向在身体后面，在 front 方向仍在前面。
            stage.UnequipAll();
            var sword = stage.Entries.First(e => e.IsPaperdoll && e.ItemId.Contains("sword_1h")).ItemId;
            Assert.IsTrue(stage.Equip(sword));
            var swordLayers = stage.Panel.PaperdollLayers.ToList();
            preview.SetDirection("back");
            SettlePreview(preview, swordLayers, loader);
            Assert.Less(preview.EquipmentLayers.Single().Image.transform.GetSiblingIndex(), preview.BodyImage!.transform.GetSiblingIndex(), "back 方向武器应在身体之下");
            preview.SetDirection("front");
            SettlePreview(preview, swordLayers, loader);
            Assert.Greater(preview.EquipmentLayers.Single().Image.transform.GetSiblingIndex(), preview.BodyImage.transform.GetSiblingIndex(), "front 方向武器仍在身体之上");
        }

        [Test]
        public void Paperdoll_LayersWithoutDeclaredOrder_KeepTheLegacyBodyFirstOrder_InEveryDirection()
        {
            var stage = NewStage();
            var root = NewUiRoot();
            var (preview, _, loader) = NewPreview(stage, root, "front");
            // 手工构造一个没有任何逐方向层序声明的图层（缺省空列表）：所有方向都是 身体 -> 装备层。
            var layers = new List<EquipmentPaperdollLayer>
            {
                new EquipmentPaperdollLayer("hand_main", new Id("paperdoll.item.std_sword_1h"), new Id("item.std_sword_1h"), new Id("slot.hand_main"), null),
            };
            foreach (var direction in PaperdollPreview.Directions)
            {
                preview.SetDirection(direction);
                SettlePreview(preview, layers, loader);
                Assert.AreEqual(0, preview.BodyImage!.transform.GetSiblingIndex(), direction);
                Assert.AreEqual(1, preview.EquipmentLayers.Single().Image.transform.GetSiblingIndex(), direction);
                Assert.IsFalse(preview.IsLayerBehindBody("hand_main"), direction);
            }
        }

        private static readonly Vector2 NoShift = Vector2.zero;

        /// <summary>在内容根里造一个只有锚点声明的探针精灵集（item_anchor_probe）：画布 100 x 120 的纯色层图，front 方向声明 grip，其余方向不声明。</summary>
        private void WriteAnchorProbe(string anchorsJson)
        {
            foreach (var direction in PaperdollPreview.Directions)
            {
                var dir = Path.Combine(_root, "sprites", "item_anchor_probe", direction);
                Directory.CreateDirectory(dir);
                var texture = new Texture2D(100, 120, TextureFormat.RGBA32, false);
                var pixels = new Color32[100 * 120];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(200, 40, 40, 255);
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(dir, "hand_main.png"), ImageConversion.EncodeToPNG(texture));
                UnityEngine.Object.DestroyImmediate(texture);
            }

            File.WriteAllText(Path.Combine(_root, "sprites", "item_anchor_probe", "anchors.json"), anchorsJson);
        }

        private static List<EquipmentPaperdollLayer> ProbeLayers() => new List<EquipmentPaperdollLayer>
        {
            new EquipmentPaperdollLayer("hand_main", new Id("paperdoll.item.anchor_probe"), new Id("item.anchor_probe"), new Id("slot.hand_main"), null),
        };

        [Test]
        public void Paperdoll_DeclaredGrip_AlignsTheLayerToTheBodyAttachPoint_UndeclaredStaysCentered()
        {
            const float gripX = 10f;
            const float gripY = 20f;
            WriteAnchorProbe("{\"canvas\":{\"width\":100,\"height\":120},\"directions\":{\"front\":{\"grip\":[" + gripX + "," + gripY + "]}}}");
            var stage = NewStage();
            var root = NewUiRoot();
            var (preview, _, loader) = NewPreview(stage, root, "front");
            var bodyAnchors = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(File.ReadAllText(Path.Combine(_root, "sprites", "placeholder_hero", "anchors.json")));
            var directions = (Core.Foundation.Common.Json.JsonObject)bodyAnchors["directions"];

            foreach (var direction in PaperdollPreview.Directions)
            {
                preview.SetDirection(direction);
                SettlePreview(preview, ProbeLayers(), loader);
                var layerRect = (RectTransform)preview.EquipmentLayers.Single().Image.transform;
                var body = preview.BodyImage!.sprite;
                Assert.IsNotNull(body);
                var k = preview.Root.sizeDelta.y * 0.9f / body.rect.height;
                if (direction == "front")
                {
                    // 期望值由规则算出：层的握点（左上原点像素）对到身体 hand_main 锚点上，本地坐标原点取各自画布中心、y 向上。
                    var attach = (Core.Foundation.Common.Json.JsonArray)((Core.Foundation.Common.Json.JsonObject)directions["front"])["hand_main"];
                    var ax = (float)((Core.Foundation.Common.Json.JsonNumber)attach[0]).Value;
                    var ay = (float)((Core.Foundation.Common.Json.JsonNumber)attach[1]).Value;
                    var expected = new Vector2(
                        (ax - body.rect.width * 0.5f) * k - (gripX - 50f) * k,
                        (body.rect.height * 0.5f - ay) * k - (60f - gripY) * k);
                    Assert.AreEqual(expected.x, layerRect.anchoredPosition.x, 0.01f, "front x");
                    Assert.AreEqual(expected.y, layerRect.anchoredPosition.y, 0.01f, "front y");
                    Assert.Greater(layerRect.anchoredPosition.magnitude, 1f, "探针的握点不在画布中心：对齐必须产生可见位移");
                }
                else
                {
                    // 不变量：没有声明 grip 的方向保持画布居中叠放（位移恰好为零）。
                    Assert.AreEqual(NoShift, layerRect.anchoredPosition, direction + " 未声明锚点应居中");
                }
            }
        }

        [Test]
        public void Paperdoll_NoAnchorsFile_OrBodyHasNoMatchingAttach_StaysCentered()
        {
            // 没有 anchors.json：居中。
            WriteAnchorProbe("{}");
            File.Delete(Path.Combine(_root, "sprites", "item_anchor_probe", "anchors.json"));
            var stage = NewStage();
            var root = NewUiRoot();
            var (preview, _, loader) = NewPreview(stage, root, "front");
            SettlePreview(preview, ProbeLayers(), loader);
            Assert.AreEqual(NoShift, ((RectTransform)preview.EquipmentLayers.Single().Image.transform).anchoredPosition, "没有锚点文件");

            // 层声明了 grip，但身体该层名没有挂接点（探针层名 neck_probe 不在身体锚点里）：居中。
            var neckDir = Path.Combine(_root, "sprites", "item_anchor_probe", "front");
            File.Copy(Path.Combine(neckDir, "hand_main.png"), Path.Combine(neckDir, "neck_probe.png"), true);
            File.WriteAllText(Path.Combine(_root, "sprites", "item_anchor_probe", "anchors.json"), "{\"directions\":{\"front\":{\"grip\":[10,20]}}}");
            var layers = new List<EquipmentPaperdollLayer>
            {
                new EquipmentPaperdollLayer("neck_probe", new Id("paperdoll.item.anchor_probe"), new Id("item.anchor_probe"), new Id("slot.neck_probe"), null),
            };
            var (preview2, _, loader2) = NewPreview(stage, root, "front");
            SettlePreview(preview2, layers, loader2);
            Assert.IsTrue(preview2.EquipmentLayers.Single().Image.enabled);
            Assert.AreEqual(NoShift, ((RectTransform)preview2.EquipmentLayers.Single().Image.transform).anchoredPosition, "身体没有同名挂接点");
        }

        [Test]
        public void Paperdoll_ReferenceWeapons_DeclareGripsThatAreConsistentWithTheirPrePositionedCanvases()
        {
            var stage = NewStage();
            var root = NewUiRoot();
            var (preview, visuals, loader) = NewPreview(stage, root, "front");
            var aligned = 0;
            foreach (var entry in stage.Entries.Where(e => e.IsPaperdoll))
            {
                stage.UnequipAll();
                Assert.IsTrue(stage.Equip(entry.ItemId));
                var layers = stage.Panel.PaperdollLayers.ToList();
                foreach (var direction in PaperdollPreview.Directions)
                {
                    preview.SetDirection(direction);
                    SettlePreview(preview, layers, loader);
                    var (layer, set, image) = preview.EquipmentLayers.Single();
                    var hasGrip = visuals.TryGetAnchor(set, direction, "grip", out _);
                    var hasAttach = visuals.TryGetAnchor("placeholder_hero", direction, layer, out _);
                    if (!hasGrip)
                    {
                        // 没有声明握点的物品（胸甲）：居中叠放。
                        Assert.AreEqual(NoShift, ((RectTransform)image.transform).anchoredPosition, entry.ItemId + "/" + direction);
                        continue;
                    }

                    Assert.IsTrue(hasAttach, "身体 " + direction + " 没有 " + layer + " 挂接点");
                    // 参考层图在制作时已把握点画在"身体 hand_main + 画布偏移"处：按锚点对齐的结果与居中叠放一致（位移为零）。
                    Assert.AreEqual(0f, ((RectTransform)image.transform).anchoredPosition.magnitude, 0.01f, entry.ItemId + "/" + direction + " 参考层与身体挂接点不一致");
                    aligned++;
                }
            }

            Assert.Greater(aligned, 0, "至少有一件参考武器走了锚点对齐路径");
        }

        // ───────── 运行期合成读锚点（UnityRenderer2D.SetLayers，ADR-0155）─────────
        // 判断记录：期望值不复用生产公式（PaperdollAnchors），而由"精灵像素点的世界坐标"独立算出——
        // 世界坐标 = 渲染器位置 + ((像素 x - 枢轴 x) / 像素密度（镜像取反）, (画布高 - 像素 y - 枢轴 y) / 像素密度)，锚点值读自内容根里的 anchors.json 文件。

        private sealed class RuntimeRig
        {
            public UnityResourceLoader Loader = null!;
            public UnityRenderer2D Renderer = null!;
            public Core.Foundation.EngineAdapter.SpriteHandle Handle;
            public Transform LayersRoot = null!;

            public SpriteRenderer LayerRenderer(int index) => LayersRoot.GetChild(index).GetComponent<SpriteRenderer>();
        }

        private RuntimeRig NewRuntimeRig()
        {
            var go = new GameObject("RuntimeAnchorRig");
            _spawned.Add(go);
            var loader = new UnityResourceLoader();
            var renderer = new UnityRenderer2D(go.transform, loader);
            var handle = renderer.CreateSpriteInstance(new Id("sprite.hero"));
            return new RuntimeRig { Loader = loader, Renderer = renderer, Handle = handle, LayersRoot = renderer.GetLayersRoot(handle)! };
        }

        private static void LoadAll(RuntimeRig rig, IEnumerable<Id> ids)
        {
            var failed = new List<string>();
            foreach (var id in ids)
            {
                rig.Loader.LoadAsync(id, Core.Foundation.EngineAdapter.ResourceKind.Image, (rid, ok) =>
                {
                    if (!ok)
                    {
                        failed.Add(rid.Value);
                    }
                });
            }

            Pump(rig.Loader);
            CollectionAssert.IsEmpty(failed, "层图加载失败");
        }

        private static Id BodyId(string direction) => new Id("layer.placeholder_hero__" + direction + "__body");

        private static Id LayerOf(string set, string direction, string layer) => new Id("layer." + set + "__" + direction + "__" + layer);

        private static void Place(RuntimeRig rig, IReadOnlyList<Id> ids, bool flipX)
        {
            rig.Renderer.SetLayers(rig.Handle, ids);
            rig.Renderer.SetTransform(rig.Handle, Core.Foundation.Common.Vec2.Zero, 0, 0, 0, 0, 1, flipX);
        }

        /// <summary>读内容根里 sprites/&lt;集&gt;/anchors.json 的 directions.&lt;方向&gt;.&lt;锚点&gt;（独立于加载器的读法）。</summary>
        private Vector2 FileAnchor(string set, string direction, string anchor)
        {
            var doc = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(File.ReadAllText(Path.Combine(_root, "sprites", set, "anchors.json")));
            var one = (Core.Foundation.Common.Json.JsonObject)((Core.Foundation.Common.Json.JsonObject)doc["directions"])[direction];
            var pair = (Core.Foundation.Common.Json.JsonArray)one[anchor];
            return new Vector2((float)((Core.Foundation.Common.Json.JsonNumber)pair[0]).Value, (float)((Core.Foundation.Common.Json.JsonNumber)pair[1]).Value);
        }

        /// <summary>精灵上一个像素点（原点左上）的世界坐标。</summary>
        private static Vector2 WorldOfPixel(SpriteRenderer renderer, Vector2 pixelTopLeft)
        {
            var sprite = renderer.sprite;
            var ppu = sprite.pixelsPerUnit;
            var local = new Vector3(
                (pixelTopLeft.x - sprite.pivot.x) / ppu * (renderer.flipX ? -1f : 1f),
                (sprite.rect.height - pixelTopLeft.y - sprite.pivot.y) / ppu,
                0f);
            return renderer.transform.TransformPoint(local);
        }

        private static Vector2 WorldCenter(SpriteRenderer renderer) =>
            WorldOfPixel(renderer, new Vector2(renderer.sprite.rect.width * 0.5f, renderer.sprite.rect.height * 0.5f));

        [Test]
        public void Runtime_ReferenceWeapon_GripLandsOnTheBodyHandMain_InEveryAuthoredDirectionAndMirrored()
        {
            var rig = NewRuntimeRig();
            var nonZero = 0;
            foreach (var direction in PaperdollPreview.Directions)
            {
                var ids = new List<Id> { BodyId(direction), LayerOf("item_std_sword_1h", direction, "hand_main") };
                LoadAll(rig, ids);
                var attach = FileAnchor("placeholder_hero", direction, "hand_main");
                var grip = FileAnchor("item_std_sword_1h", direction, "grip");
                foreach (var flip in new[] { false, true })
                {
                    Place(rig, ids, flip);
                    var body = rig.LayerRenderer(0);
                    var weapon = rig.LayerRenderer(1);
                    Assert.AreEqual(Vector3.zero, body.transform.localPosition, "身体层始终在原点 " + direction);
                    var handMain = WorldOfPixel(body, attach);
                    var gripWorld = WorldOfPixel(weapon, grip);
                    Assert.AreEqual(handMain.x, gripWorld.x, 1e-4f, $"{direction} flip={flip}: 武器 grip 的世界 x 应落在身体 hand_main 上");
                    Assert.AreEqual(handMain.y, gripWorld.y, 1e-4f, $"{direction} flip={flip}: 武器 grip 的世界 y 应落在身体 hand_main 上");

                    // 对照：不平移时（居中叠放的旧行为）两点是分开的——说明断言不是平凡成立。
                    var applied = weapon.transform.localPosition;
                    weapon.transform.localPosition = Vector3.zero;
                    var unshifted = WorldOfPixel(weapon, grip);
                    weapon.transform.localPosition = applied;
                    if (!flip)
                    {
                        Assert.Greater(Vector2.Distance(unshifted, handMain), 0.01f, direction + " 居中叠放时 grip 与 hand_main 不重合");
                        nonZero++;
                    }

                    TestContext.Out.WriteLine($"[runtime-anchor] {direction} flip={flip} offset=({applied.x:F5},{applied.y:F5}) unshifted-gap={Vector2.Distance(unshifted, handMain):F5}");
                }
            }

            Assert.AreEqual(PaperdollPreview.Directions.Length, nonZero, "每个已制作方向都产生了非零平移");
        }

        [Test]
        public void Runtime_ReferenceItems_ShareTheBodyDensity_SoRelativeSizeAndPlacementMatchThePreview()
        {
            // 参考包每个层精灵集（含不声明握点的胸甲）都声明与身体一致的 pixels_per_unit（ADR-0155）：运行期层与身体按同一个密度换算，
            // 所以"装备相对身体的尺寸"与预览区（两者同一个缩放）一致；位置也一致（预览区对身体居中、层中心 = anchoredPosition）。
            var stage = NewStage();
            var uiRoot = NewUiRoot();
            var (preview, _, previewLoader) = NewPreview(stage, uiRoot, "front");
            var rig = NewRuntimeRig();
            var bodyPpu = (float)((Core.Foundation.Common.Json.JsonNumber)((Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(
                File.ReadAllText(Path.Combine(_root, "sprites", "placeholder_hero", "anchors.json"))))["pixels_per_unit"]).Value;
            var checkedItems = 0;
            foreach (var entry in stage.Entries.Where(e => e.IsPaperdoll))
            {
                stage.UnequipAll();
                Assert.IsTrue(stage.Equip(entry.ItemId));
                var layers = stage.Panel.PaperdollLayers.ToList();
                var (layerName, setName) = (layers.Single().Layer, UiVisuals.SetName(layers.Single().MeshRef.Value));
                foreach (var direction in PaperdollPreview.Directions)
                {
                    preview.SetDirection(direction);
                    SettlePreview(preview, layers, previewLoader);
                    var ids = new List<Id> { BodyId(direction), LayerOf(setName, direction, layerName) };
                    LoadAll(rig, ids);
                    Place(rig, ids, false);
                    var body = rig.LayerRenderer(0);
                    var item = rig.LayerRenderer(1);
                    Assert.AreEqual(bodyPpu, item.sprite.pixelsPerUnit, 1e-4f, entry.ItemId + "/" + direction + " 层集与身体同一个像素密度");

                    var previewBody = preview.BodyImage!;
                    var previewItem = preview.EquipmentLayers.Single().Image;
                    var previewSizeRatio = ((RectTransform)previewItem.transform).sizeDelta.x / ((RectTransform)previewBody.transform).sizeDelta.x;
                    var runtimeSizeRatio = (item.sprite.rect.width / item.sprite.pixelsPerUnit) / (body.sprite.rect.width / body.sprite.pixelsPerUnit);
                    Assert.AreEqual(previewSizeRatio, runtimeSizeRatio, 1e-4f, entry.ItemId + "/" + direction + " 相对宽度");

                    if (!rig.Loader.TryGetSpriteSetAnchor(setName, direction, "grip", out _))
                    {
                        // 没声明握点的层（胸甲）：运行期按各精灵枢轴叠放（既有行为，ADR-0155 不改），相对位置不与预览（画布居中）比较，只比尺寸。
                        continue;
                    }

                    var k = preview.Root.sizeDelta.y * 0.9f / previewBody.sprite.rect.height;
                    var previewRelative = ((RectTransform)previewItem.transform).anchoredPosition / k;
                    var runtimeRelative = (WorldCenter(item) - WorldCenter(body)) * bodyPpu;
                    Assert.AreEqual(previewRelative.x, runtimeRelative.x, 0.01f, entry.ItemId + "/" + direction + " 相对位置 x");
                    Assert.AreEqual(previewRelative.y, runtimeRelative.y, 0.01f, entry.ItemId + "/" + direction + " 相对位置 y");
                    checkedItems++;
                }
            }

            Assert.Greater(checkedItems, 0);
            TestContext.Out.WriteLine($"[runtime-vs-preview] reference items x directions checked = {checkedItems}, body ppu = {bodyPpu}");

            // 非平凡位置：层图没有预摆放（探针，握点不在画布中心，同样声明与身体一致的密度）时运行期相对位置 = 预览区相对位置，且不为零。
            WriteAnchorProbe("{\"canvas\":{\"width\":100,\"height\":120},\"pixels_per_unit\":" + bodyPpu + ",\"directions\":{\"front\":{\"grip\":[10,20]}}}");
            preview.SetDirection("front");
            SettlePreview(preview, ProbeLayers(), previewLoader);
            var probeIds = new List<Id> { BodyId("front"), LayerOf("item_anchor_probe", "front", "hand_main") };
            LoadAll(rig, probeIds);
            Place(rig, probeIds, false);
            var probeBody = rig.LayerRenderer(0);
            var probeLayer = rig.LayerRenderer(1);
            var probeK = preview.Root.sizeDelta.y * 0.9f / preview.BodyImage!.sprite.rect.height;
            var probePreview = ((RectTransform)preview.EquipmentLayers.Single().Image.transform).anchoredPosition / probeK;
            var probeRuntime = (WorldCenter(probeLayer) - WorldCenter(probeBody)) * bodyPpu;
            Assert.Greater(probePreview.magnitude, 1f, "探针的预览相对位置不是零（断言不是平凡成立）");
            Assert.AreEqual(probePreview.x, probeRuntime.x, 0.01f, "探针 x");
            Assert.AreEqual(probePreview.y, probeRuntime.y, 0.01f, "探针 y");
            TestContext.Out.WriteLine($"[runtime-vs-preview] probe preview=({probePreview.x:F4},{probePreview.y:F4}) runtime=({probeRuntime.x:F4},{probeRuntime.y:F4})");
        }

        [Test]
        public void Runtime_UndeclaredAnchors_KeepCenteredPlacement_BitIdenticalToToday()
        {
            // 四种"未声明"：层集没有 anchors.json；层声明了 grip 但身体没有同名挂接点；身体声明了挂接点但层没有 grip（胸甲）；与身体同一个精灵集的层（placeholder_hero 自己的 hand_main/head）。
            WriteAnchorProbe("{}");
            File.Delete(Path.Combine(_root, "sprites", "item_anchor_probe", "anchors.json"));
            var neckDir = Path.Combine(_root, "sprites", "item_anchor_probe", "front");
            File.Copy(Path.Combine(neckDir, "hand_main.png"), Path.Combine(neckDir, "neck_probe.png"), true);
            var gripOnly = Path.Combine(_root, "sprites", "item_grip_only", "front");
            Directory.CreateDirectory(gripOnly);
            File.Copy(Path.Combine(neckDir, "hand_main.png"), Path.Combine(gripOnly, "neck_probe.png"), true);
            File.WriteAllText(Path.Combine(_root, "sprites", "item_grip_only", "anchors.json"), "{\"directions\":{\"front\":{\"grip\":[10,20]}}}");
            var rig = NewRuntimeRig();
            var cases = new Dictionary<string, List<Id>>
            {
                ["没有 anchors.json"] = new List<Id> { BodyId("front"), LayerOf("item_anchor_probe", "front", "hand_main") },
                ["身体没有同名挂接点"] = new List<Id> { BodyId("front"), LayerOf("item_grip_only", "front", "neck_probe") },
                ["层没有 grip（胸甲）"] = new List<Id> { BodyId("front"), LayerOf("item_std_chestplate", "front", "chest") },
                ["与身体同集的层"] = new List<Id> { BodyId("front"), LayerOf("placeholder_hero", "front", "hand_main"), LayerOf("placeholder_hero", "front", "head") },
            };
            foreach (var kv in cases)
            {
                LoadAll(rig, kv.Value);
                foreach (var flip in new[] { false, true })
                {
                    Place(rig, kv.Value, flip);
                    for (var i = 0; i < kv.Value.Count; i++)
                    {
                        var renderer = rig.LayerRenderer(i);
                        Assert.AreEqual(Vector3.zero, renderer.transform.localPosition, $"{kv.Key} flip={flip} 第 {i} 层本地位置应恒为原点");
                        Assert.IsTrue(rig.Loader.TryGetSprite(kv.Value[i], out var loaded));
                        Assert.AreSame(loaded, renderer.sprite, $"{kv.Key} 第 {i} 层精灵应与加载器里的是同一个对象");
                        Assert.AreEqual(flip, renderer.flipX);
                    }
                }
            }

            // 没有 body 层名的层集：整体不对齐。
            var noBody = new List<Id> { LayerOf("item_std_sword_1h", "front", "hand_main") };
            LoadAll(rig, noBody);
            Place(rig, noBody, false);
            Assert.AreEqual(Vector3.zero, rig.LayerRenderer(0).transform.localPosition, "没有 body 层不对齐");
        }

        [Test]
        public void Runtime_AnimatedLayerFrame_DropsTheOffset_AndRestoreBringsItBack_ColdLoadMatchesHotPath()
        {
            var rig = NewRuntimeRig();
            var ids = new List<Id> { BodyId("front"), LayerOf("item_std_sword_1h", "front", "hand_main") };

            // 冷路径：层图还没加载完时 SetLayers 给占位方块，不平移；加载完成后再 SetLayers 才对齐。
            Place(rig, ids, false);
            Assert.AreEqual(Vector3.zero, rig.LayerRenderer(1).transform.localPosition, "冷路径：加载前不平移");
            LoadAll(rig, ids);
            Place(rig, ids, false);
            var hot = rig.LayerRenderer(1).transform.localPosition;
            Assert.Greater(((Vector2)hot).magnitude, 0.01f, "加载后平移生效");
            var handMain = WorldOfPixel(rig.LayerRenderer(0), FileAnchor("placeholder_hero", "front", "hand_main"));
            var gripWorld = WorldOfPixel(rig.LayerRenderer(1), FileAnchor("item_std_sword_1h", "front", "grip"));
            Assert.AreEqual(0f, Vector2.Distance(handMain, gripWorld), 1e-4f, "冷加载与热路径同一规则");

            // 逐层动画帧（另一套美术、自带枢轴摆位）：归零；写回静态层图后恢复同一个平移量。
            rig.Loader.TryGetSprite(ids[0], out var someFrame);
            rig.Renderer.SetLayerSprite(rig.Handle, 1, someFrame);
            Assert.AreEqual(Vector3.zero, rig.LayerRenderer(1).transform.localPosition, "逐层动画帧不平移");
            rig.Renderer.RestoreLayerSprite(rig.Handle, 1);
            Assert.AreEqual(hot, rig.LayerRenderer(1).transform.localPosition, "写回静态层图后恢复平移");

            // 镜像切换不重做 SetLayers：平移量的 x 随之取反，y 不变。
            rig.Renderer.SetTransform(rig.Handle, Core.Foundation.Common.Vec2.Zero, 0, 0, 0, 0, 1, true);
            var mirrored = rig.LayerRenderer(1).transform.localPosition;
            Assert.AreEqual(-hot.x, mirrored.x, 1e-6f);
            Assert.AreEqual(hot.y, mirrored.y, 1e-6f);
        }
    }

    /// <summary>清单元素 → 面板实际用的类型化取用（同 EquipUiSkinPlayModeTests 的映射；任一边漂移用例都会红）。</summary>
    internal static class UiSkinTypedAccess
    {
        public static bool TryGet(UiSkinPack pack, UiSkinFile f, out Sprite? sprite)
        {
            switch (f.Element.Id)
            {
                case "slot_frame": sprite = pack.SlotFrame(f.Name!); return true;
                case "slot_frame_default": sprite = pack.SlotFrameDefault(); return true;
                case "slot_frame_hover": sprite = pack.SlotState("highlight"); return true;
                case "slot_frame_disabled": sprite = pack.SlotState("disabled"); return true;
                case "slot_frame_drag_hover": sprite = pack.SlotState("drag_hover"); return true;
                case "slot_frame_pressed": sprite = pack.SlotSpriteState().pressedSprite; return true;
                case "slot_frame_selected": sprite = pack.SlotSpriteState().selectedSprite; return true;
                case "quality_frame": sprite = pack.QualityFrame(f.Name!); return true;
                case "quality_frame_default": sprite = pack.QualityFrame(string.Empty); return true;
                case "drag_ghost": sprite = pack.DragState("ghost"); return true;
                case "drag_target_ok": sprite = pack.DragState("target_ok"); return true;
                case "drag_target_blocked": sprite = pack.DragState("target_blocked"); return true;
                case "tooltip_background": sprite = pack.TooltipBackground(); return true;
                case "tooltip_divider": sprite = pack.TooltipDivider(); return true;
                case "tooltip_row": sprite = pack.TooltipRow(); return true;
                case "paperdoll_preview_background": sprite = pack.PaperdollBackground(); return true;
                case "panel_background": sprite = pack.PanelBackground(); return true;
                case "button_normal": sprite = pack.Button()?.Normal; return true;
                case "button_hover": sprite = pack.Button()?.Hover; return true;
                case "button_pressed": sprite = pack.Button()?.Pressed; return true;
                case "button_disabled": sprite = pack.Button()?.Disabled; return true;
                case "button_selected": sprite = pack.Button()?.Selected; return true;
            }

            sprite = null;
            return false;
        }
    }
}

#nullable enable
// EquipUiSkinPlayModeTests：装备面板与界面皮肤的 PlayMode 验收（手感设计/08、ADR-0149）。
//
// 覆盖：
//   - 皮肤替换完整性：遍历界面资源契约清单的每一个展开元素，换到另一套（程序生成的）皮肤包后，每个元素解析到的精灵都来自新包（像素颜色逐个核对，没有默认皮肤残留），
//     类型化取用路径（面板实际使用的 SlotFrame/QualityFrame/…）与清单路径逐元素一致（任一边漂移都会红）；
//   - 真实面板：装备面板（槽位框、品质框、状态精灵、预览区背景、面板底图、按钮）换皮肤后引用新资源；
//   - 九宫格：画廊里每个九宫格元素在几种尺寸下，精灵 border 数据等于 theme 令牌、像素密度为 1（四角不变形）；
//   - 状态变体：槽位格与按钮的悬停/按下/选中/禁用随交互切换精灵；
//   - 回落：可选元素缺失沿用框架默认外观，必备元素缺失回落到占位皮肤并留下记录；缺省皮肤不安装任何覆盖（逐位不变）。
// 渲染隔离：每个用例自建 UiRoot，teardown 复位 UiSkin 覆盖、销毁画布与临时皮肤包。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.Ui;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.Common;
using Lab;
using NUnit.Framework;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:ui")]
    [Category("module:lab")]
    public sealed class EquipUiSkinPlayModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<string> _roots = new List<string>();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiSkin.Reset();
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
            foreach (var root in _roots)
            {
                SkinTestKit.Delete(root);
            }

            _roots.Clear();
            yield return null;
        }

        // ───────── 夹具 ─────────

        private sealed class Env
        {
            public string Root = string.Empty;
            public UiSkinManifest Manifest = null!;
            public List<string> Slots = new List<string>();
            public List<string> Qualities = new List<string>();
            public WardrobeStage Stage = null!;
        }

        private Env NewEnv(string label)
        {
            var template = LabHostTestSupport.Script(Adapter.Unity.LabHost.EquipWardrobeRunner.TemplateScript);
            var stage = WardrobeStage.Create(LabHostTestSupport.Host.Runner, template);
            _disposables.Add(stage);
            var env = new Env { Root = SkinTestKit.NewContentRoot(label), Manifest = SkinTestKit.LoadManifest(), Stage = stage };
            _roots.Add(env.Root);
            foreach (var slot in stage.Panel.Slots)
            {
                env.Slots.Add(slot.SlotName);
            }

            foreach (var q in stage.Registry.GetAll("item.quality_definition"))
            {
                env.Qualities.Add(EquipmentViewModel.QualityShortName(q.Id!.Value));
            }

            env.Slots.Sort(StringComparer.Ordinal);
            env.Qualities.Sort(StringComparer.Ordinal);
            return env;
        }

        private UiRoot NewUiRoot()
        {
            var root = UiRoot.Create("SkinTestRoot");
            _spawned.Add(root.gameObject);
            return root;
        }

        private UiSkinPack LoadPack(Env env, string name)
        {
            var pack = UiSkinPack.Load("skin." + name, env.Root);
            _disposables.Add(pack);
            return pack;
        }

        private static void AssertIs(Sprite? sprite, string path, int seed, string what)
        {
            Assert.IsNotNull(sprite, what + " 没有精灵：" + path);
            var got = SkinTestKit.Identify(sprite!);
            var want = SkinTestKit.ColorOf(path, seed);
            Assert.IsTrue(SkinTestKit.Same(got, want), $"{what}: 期望来自 {path}（种子 {seed}）的颜色 {want}，读到 {got}（精灵 {sprite!.name}）");
        }

        /// <summary>清单元素在面板实际使用的类型化取用路径里对应的精灵；没有类型化取用的元素返回 false（用例会让它失败，逼着补上取用或登记）。</summary>
        private static bool TryTyped(UiSkinPack pack, UiSkinFile f, out Sprite? sprite)
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

        // ───────── 清单与类型化取用 ─────────

        [Test]
        public void Manifest_ParsesAndExpandsFromData_CountsAreComputedNotHardcoded()
        {
            var env = NewEnv("manifest");
            var files = env.Manifest.Expand(env.Slots, env.Qualities);
            var images = files.Where(f => f.Element.IsImage).ToList();
            var expected = env.Manifest.Elements.Sum(e => e.Expand == "slot" ? env.Slots.Count : e.Expand == "quality" ? env.Qualities.Count : 1);
            Assert.AreEqual(expected, files.Count);
            Assert.AreEqual(files.Count - 1, images.Count, "除了 theme.json 其余都是图片元素");
            Assert.AreEqual(images.Count, images.Select(f => f.Path).Distinct().Count());
            Assert.Greater(env.Slots.Count, 0);
            Assert.Greater(env.Qualities.Count, 0);
            TestContext.Out.WriteLine($"[skin-manifest] templates={env.Manifest.Elements.Count} files={files.Count} images={images.Count} slots={env.Slots.Count} qualities={env.Qualities.Count}");
        }

        [Test]
        public void SkinSwap_EveryManifestElement_ResolvesToTheNewPack_NoDefaultSkinLeftovers()
        {
            var env = NewEnv("swap");
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed);
            var alt = LoadPack(env, "alt");
            var def = LoadPack(env, "default");

            var walked = 0;
            foreach (var f in env.Manifest.Expand(env.Slots, env.Qualities))
            {
                if (!f.Element.IsImage)
                {
                    continue;
                }

                // 通用路径：清单展开的这个文件在新包里解析到的精灵必须来自新包。
                var generic = alt.ElementSprite(f);
                AssertIs(generic, f.Path, SkinTestKit.AltSeed, "皮肤替换后的清单元素");

                // 类型化路径（面板实际用的取用）与清单路径逐元素一致：同一个精灵对象（共用缓存）。
                Assert.IsTrue(TryTyped(alt, f, out var typed), $"清单元素 {f.Element.Id} 没有对应的类型化取用——给它加取用，或证明它没有运行期消费者");
                Assert.AreSame(generic, typed, $"类型化取用与清单路径不一致：{f.Path}");

                // 对照组：缺省皮肤里同一个必备元素的颜色不同于新包（证明"读回像素"能区分两套皮肤，不是巧合地都读到同一个值）。
                if (f.Element.Requirement != "optional")
                {
                    var before = def.ElementSprite(f);
                    Assert.IsFalse(SkinTestKit.Same(SkinTestKit.Identify(before!), SkinTestKit.Identify(generic!)), $"缺省皮肤与替换皮肤在 {f.Path} 上不可区分");
                }

                walked++;
            }

            Assert.AreEqual(env.Manifest.Expand(env.Slots, env.Qualities).Count - 1, walked);
            Assert.AreEqual(0, alt.Fallbacks.Count, "替换皮肤是全元素的，不应发生任何回落：" + string.Join(", ", alt.Fallbacks.Select(x => x.Item)));
            Assert.AreEqual(0, alt.OptionalAbsent.Count);

            // 主题令牌：颜色逐个读回，数值令牌取非缺省值。
            foreach (var key in env.Manifest.ColorTokens)
            {
                var c = alt.ThemeColor(key);
                Assert.IsTrue(c.HasValue, "缺主题颜色 " + key);
                Assert.IsTrue(SkinTestKit.Same(SkinTestKit.ColorOf("theme/" + key, SkinTestKit.AltSeed), (Color32)c!.Value), "主题颜色读回不一致：" + key);
            }

            Assert.AreEqual(SkinTestKit.PanelBorder, alt.NineSliceBorder("panel_border", 5));
            Assert.AreEqual(SkinTestKit.TooltipBorder, alt.NineSliceBorder("tooltip_border", 2));
            Assert.AreEqual(SkinTestKit.ButtonBorder, alt.NineSliceBorder("button_border", 4));
            TestContext.Out.WriteLine($"[skin-swap] walked={walked} manifest-image-elements fell back 0 times");
        }

        // ───────── 真实面板 ─────────

        private (EquipmentPanel Panel, UiVisuals Visuals) BuildEquipmentPanel(Env env, UiSkinPack pack, UiRoot root)
        {
            var visuals = new UiVisuals(pack, env.Stage.Registry, env.Stage.DisplayInfo);
            _disposables.Add(visuals);
            var go = UiWidgets.CreateRoot("EquipmentPanelHost", root.Content);
            var panel = go.gameObject.AddComponent<EquipmentPanel>();
            var layout = visuals.LayoutOf(UiPanel.Equipment, EquipmentPanel.DefaultLayout);
            layout = new UiPanelLayout(layout.Anchor, layout.Columns, layout.CellSize, layout.PreviewScale, "placeholder_hero", layout.PreviewDirection);
            panel.Construct(go, env.Stage.Panel, visuals, null, layout);
            return (panel, visuals);
        }

        [Test]
        public void SkinSwap_RenderedEquipmentPanel_ReferencesOnlyNewPackAssets()
        {
            var env = NewEnv("panel");
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed);
            var alt = LoadPack(env, "alt");
            UiSkin.Install(alt.CreateOverride()!);
            var root = NewUiRoot();
            var (panel, visuals) = BuildEquipmentPanel(env, alt, root);

            // 全部可装备物品穿上一遍，让每个品质框都有机会出现。
            var qualitiesSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in env.Stage.Entries)
            {
                env.Stage.Equip(entry.ItemId);
                panel.RefreshUi();
                foreach (var s in env.Stage.Panel.Slots)
                {
                    if (s.Occupied)
                    {
                        qualitiesSeen.Add(s.QualityName);
                    }
                }

                for (var i = 0; i < panel.Cells.Count; i++)
                {
                    var slot = env.Stage.Panel.Slots[i];
                    if (slot.Occupied)
                    {
                        AssertIs(panel.Cells[i].Quality.sprite, "quality_frame/" + slot.QualityName + ".png", SkinTestKit.AltSeed, "已装备槽位的品质框 " + slot.SlotName);
                    }
                }
            }

            Assert.Greater(qualitiesSeen.Count, 0);
            for (var i = 0; i < panel.Cells.Count; i++)
            {
                var cell = panel.Cells[i];
                AssertIs(cell.Frame.sprite, "slot_frame/" + cell.SlotName + ".png", SkinTestKit.AltSeed, "槽位框 " + cell.SlotName);
                var state = cell.Button.spriteState;
                Assert.AreEqual(UnityEngine.UI.Selectable.Transition.SpriteSwap, cell.Button.transition);
                AssertIs(state.highlightedSprite, "slot_frame/_highlight.png", SkinTestKit.AltSeed, "槽位框 hover");
                AssertIs(state.pressedSprite, "slot_frame/_pressed.png", SkinTestKit.AltSeed, "槽位框 pressed");
                AssertIs(state.selectedSprite, "slot_frame/_selected.png", SkinTestKit.AltSeed, "槽位框 selected");
                AssertIs(state.disabledSprite, "slot_frame/_disabled.png", SkinTestKit.AltSeed, "槽位框 disabled");
            }

            AssertIs(panel.Preview.Background.sprite, "paperdoll_preview/background.png", SkinTestKit.AltSeed, "纸娃娃预览区背景");

            // 面板底板：九宫格面板底图换成新包的 panel/background.png，边框取 theme 令牌。
            var bg = panel.Background.GetComponent<Image>();
            AssertIs(bg.sprite, "panel/background.png", SkinTestKit.AltSeed, "面板底图");
            Assert.AreEqual(Image.Type.Sliced, bg.type);
            Assert.AreEqual(new Vector4(SkinTestKit.PanelBorder, SkinTestKit.PanelBorder, SkinTestKit.PanelBorder, SkinTestKit.PanelBorder), bg.sprite.border);

            // 面板里的方向按钮：按钮九宫格状态图。
            var prev = panel.transform.GetComponentsInChildren<Button>(true).First(b => b.name == "DirectionPrev");
            var prevImage = prev.GetComponent<Image>();
            AssertIs(prevImage.sprite, "button/normal.png", SkinTestKit.AltSeed, "按钮常态");
            AssertIs(prev.spriteState.highlightedSprite, "button/hover.png", SkinTestKit.AltSeed, "按钮 hover");
            AssertIs(prev.spriteState.pressedSprite, "button/pressed.png", SkinTestKit.AltSeed, "按钮 pressed");
            AssertIs(prev.spriteState.disabledSprite, "button/disabled.png", SkinTestKit.AltSeed, "按钮 disabled");
            AssertIs(prev.spriteState.selectedSprite, "button/selected.png", SkinTestKit.AltSeed, "按钮 selected");

            // 整棵画布下凡是取自皮肤包的精灵都来自新包（颜色必须落在新包元素颜色集合里；缺省皮肤的颜色不在其中），且没有任何回落记录。
            var altColors = new HashSet<(byte, byte, byte)>();
            foreach (var f in env.Manifest.Expand(env.Slots, env.Qualities).Where(x => x.Element.IsImage))
            {
                var c = SkinTestKit.ColorOf(f.Path, SkinTestKit.AltSeed);
                altColors.Add((c.r, c.g, c.b));
            }

            var skinSprites = 0;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                var sprite = image.sprite;
                if (sprite == null || sprite.texture == null || !sprite.texture.name.StartsWith("UiSkinPack:", StringComparison.Ordinal))
                {
                    continue;
                }

                var c = SkinTestKit.Identify(sprite);
                Assert.IsTrue(altColors.Contains((c.r, c.g, c.b)), $"{image.name} 引用了不属于新皮肤包的精灵 {sprite.name}（颜色 {c}）");
                skinSprites++;
            }

            Assert.Greater(skinSprites, panel.Cells.Count, "画布里取自皮肤包的精灵应当包含槽位框、预览底图、面板底图与按钮");
            Assert.AreEqual(0, alt.Fallbacks.Count);
            Assert.AreEqual(0, visuals.Pack.OptionalAbsent.Count);
        }

        // ───────── 九宫格 ─────────

        [Test]
        public void NineSlice_EveryElement_AtSeveralPanelSizes_KeepsCornersUndistorted()
        {
            var env = NewEnv("nine");
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed);
            var alt = LoadPack(env, "alt");
            var root = NewUiRoot();
            var gallery = UiSkinGallery.Build(root.Content, alt, env.Manifest, env.Slots, env.Qualities);

            var nineElements = env.Manifest.Elements.Where(e => e.IsNineSlice).ToList();
            Assert.GreaterOrEqual(nineElements.Count, 3, "至少面板底图/提示框底图/按钮是九宫格元素");
            var sizesSeen = new Dictionary<string, HashSet<Vector2>>();
            foreach (var entry in gallery.Entries.Where(e => e.File.Element.IsNineSlice))
            {
                var token = entry.File.Element.NineSliceToken!;
                var expected = alt.NineSliceBorder(token, entry.File.Element.NineSliceDefault);
                var sprite = entry.Image.sprite;
                Assert.AreEqual(Image.Type.Sliced, entry.Image.type, entry.File.Path);
                Assert.AreEqual(new Vector4(expected, expected, expected, expected), sprite.border, $"{entry.File.Path} 的九宫格 border 应取 theme.{token}");

                // 四角不变形：精灵像素密度与画布参考密度一致（Image.pixelsPerUnit == 1 表示边框按原生像素尺寸绘制），且拉伸区非空。
                Assert.AreEqual(1f, entry.Image.pixelsPerUnit, 1e-4f, $"{entry.File.Path} 四角被缩放了");
                Assert.Greater(entry.Size.x, 2 * expected, entry.File.Path + " 宽度容不下两侧边框");
                Assert.Greater(entry.Size.y, 2 * expected, entry.File.Path + " 高度容不下上下边框");
                var rect = entry.Image.rectTransform.sizeDelta;
                Assert.AreEqual(entry.Size, rect);
                if (!sizesSeen.TryGetValue(entry.File.Path, out var set))
                {
                    sizesSeen[entry.File.Path] = set = new HashSet<Vector2>();
                }

                set.Add(entry.Size);
            }

            foreach (var kv in sizesSeen)
            {
                Assert.AreEqual(UiSkinGallery.DefaultSliceSizes.Length, kv.Value.Count, kv.Key + " 应在每种尺寸下各画一份");
            }

            // 真实面板底板在几种尺寸下同样：border 数据不随尺寸变化。
            UiSkin.Install(alt.CreateOverride()!);
            foreach (var size in new[] { new Vector2(60f, 60f), new Vector2(200f, 120f), new Vector2(600f, 400f) })
            {
                var rect = UiWidgets.CreatePanelBackground("Sized", root.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, Vector2.zero);
                var image = rect.GetComponent<Image>();
                Assert.AreEqual(Image.Type.Sliced, image.type);
                Assert.AreEqual(SkinTestKit.PanelBorder, image.sprite.border.x);
                Assert.AreEqual(1f, image.pixelsPerUnit, 1e-4f);
            }

            TestContext.Out.WriteLine($"[skin-nine-slice] elements={nineElements.Count} entries={gallery.Entries.Count(e => e.File.Element.IsNineSlice)}");
        }

        [Test]
        public void NineSlice_BorderLargerThanHalfTheImage_IsClampedSoCornersNeverOverlap()
        {
            var env = NewEnv("clamp");
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed);
            var dir = Path.Combine(env.Root, "ui", "skin", "alt");
            File.WriteAllText(Path.Combine(dir, "theme.json"),
                File.ReadAllText(Path.Combine(dir, "theme.json")).Replace($"\"panel_border\": {SkinTestKit.PanelBorder}", "\"panel_border\": 99"));
            var alt = LoadPack(env, "alt");
            var sprite = alt.PanelBackground()!;
            Assert.AreEqual(sprite.texture.width / 2, sprite.border.x, "超过半幅的边框被钳到半幅，不会让两侧边框重叠");
        }

        // ───────── 状态变体 ─────────

        private static PointerEventData Pointer() => new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };

        private static void AssertShows(Image image, string path, int seed, string what)
        {
            var shown = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            AssertIs(shown, path, seed, what);
        }

        [Test]
        public void StateVariants_SlotCell_SwitchesSpriteOnPointerAndSelectionAndInteractable()
        {
            var env = NewEnv("states");
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed);
            var alt = LoadPack(env, "alt");
            UiSkin.Install(alt.CreateOverride()!);
            var root = NewUiRoot();
            var (panel, _) = BuildEquipmentPanel(env, alt, root);
            var cell = panel.Cells[0];
            var es = EventSystem.current;
            Assert.IsNotNull(es, "UiRoot 应当保证有 EventSystem");
            var go = cell.Root.gameObject;
            var normalPath = "slot_frame/" + cell.SlotName + ".png";

            AssertShows(cell.Frame, normalPath, SkinTestKit.AltSeed, "常态");

            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerEnterHandler);
            AssertShows(cell.Frame, "slot_frame/_highlight.png", SkinTestKit.AltSeed, "悬停");

            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerDownHandler);
            AssertShows(cell.Frame, "slot_frame/_pressed.png", SkinTestKit.AltSeed, "按下");

            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerExitHandler);
            es.SetSelectedGameObject(null);
            AssertShows(cell.Frame, normalPath, SkinTestKit.AltSeed, "离开并取消选中后回到常态");

            es.SetSelectedGameObject(go);
            AssertShows(cell.Frame, "slot_frame/_selected.png", SkinTestKit.AltSeed, "选中");
            es.SetSelectedGameObject(null);

            cell.Button.interactable = false;
            AssertShows(cell.Frame, "slot_frame/_disabled.png", SkinTestKit.AltSeed, "禁用");
            cell.Button.interactable = true;
            AssertShows(cell.Frame, normalPath, SkinTestKit.AltSeed, "恢复可交互后回到常态");
        }

        [Test]
        public void StateVariants_SkinnedButton_SwitchesSpriteOnInteraction()
        {
            var env = NewEnv("button");
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed);
            var alt = LoadPack(env, "alt");
            UiSkin.Install(alt.CreateOverride()!);
            var root = NewUiRoot();
            var (rect, button, _) = UiWidgets.CreateButton("SkinnedButton", root.Content, "ok", () => { });
            var image = rect.GetComponent<Image>();
            var es = EventSystem.current;
            var go = rect.gameObject;

            Assert.AreEqual(Selectable.Transition.SpriteSwap, button.transition);
            Assert.AreEqual(Image.Type.Sliced, image.type);
            Assert.AreEqual(SkinTestKit.ButtonBorder, image.sprite.border.x);
            AssertShows(image, "button/normal.png", SkinTestKit.AltSeed, "按钮常态");
            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerEnterHandler);
            AssertShows(image, "button/hover.png", SkinTestKit.AltSeed, "按钮悬停");
            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerDownHandler);
            AssertShows(image, "button/pressed.png", SkinTestKit.AltSeed, "按钮按下");
            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, Pointer(), ExecuteEvents.pointerExitHandler);
            es.SetSelectedGameObject(null);
            es.SetSelectedGameObject(go);
            AssertShows(image, "button/selected.png", SkinTestKit.AltSeed, "按钮选中");
            es.SetSelectedGameObject(null);
            button.interactable = false;
            AssertShows(image, "button/disabled.png", SkinTestKit.AltSeed, "按钮禁用");
        }

        // ───────── 回落与缺省 ─────────

        [Test]
        public void Fallback_OptionalElementsAbsent_KeepFrameworkDefaultLook_AndAreRecordedNotReportedAsFallbacks()
        {
            var env = NewEnv("optional");
            bool Skip(string p) => p.StartsWith("button/", StringComparison.Ordinal) || p == "panel/background.png"
                || p == "slot_frame/_pressed.png" || p == "slot_frame/_selected.png";
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed, Skip);
            var alt = LoadPack(env, "alt");
            var skin = alt.CreateOverride()!;
            UiSkin.Install(skin);
            var root = NewUiRoot();

            // 面板底图：没有 panel/background.png → 沿用框架默认九宫格（未覆盖）。
            Assert.IsNull(skin.PanelSprite);
            Assert.AreEqual("UiSkinPanelSprite", UiSkin.PanelSprite.name);
            // 按钮：没有 button/*.png → 沿用框架默认的纯色着色外观。
            Assert.IsNull(skin.ButtonSprites);
            var (rect, button, _) = UiWidgets.CreateButton("DefaultLookButton", root.Content, "x", () => { });
            Assert.AreEqual(Selectable.Transition.ColorTint, button.transition);
            Assert.AreSame(UiSkin.FlatSprite, rect.GetComponent<Image>().sprite);

            // 槽位框 pressed/selected 缺省：取 hover 图。
            var state = alt.SlotSpriteState();
            Assert.AreSame(state.highlightedSprite, state.pressedSprite);
            Assert.AreSame(state.highlightedSprite, state.selectedSprite);
            AssertIs(state.highlightedSprite, "slot_frame/_highlight.png", SkinTestKit.AltSeed, "hover");

            Assert.AreEqual(0, alt.Fallbacks.Count, "可选元素缺失不是回落");
            foreach (var path in new[] { "button/normal.png", "panel/background.png", "slot_frame/_pressed.png", "slot_frame/_selected.png" })
            {
                Assert.Contains(path, alt.OptionalAbsent.ToList());
            }

            // 画廊：缺失的可选元素不画，其余照画。
            var gallery = UiSkinGallery.Build(root.Content, alt, env.Manifest, env.Slots, env.Qualities);
            var absent = gallery.Absent.Select(f => f.Path).ToList();
            Assert.IsTrue(absent.All(Skip), "只有被拿掉的可选元素没画：" + string.Join(", ", absent));
            Assert.AreEqual(env.Manifest.Expand(env.Slots, env.Qualities).Count(f => f.Element.IsImage && Skip(f.Path)), absent.Count);
        }

        [Test]
        public void Fallback_RequiredElementAbsent_FallsBackToPlaceholderSkin_AndLeavesARecord()
        {
            var env = NewEnv("required");
            var missingSlot = env.Slots[0];
            var missing = "slot_frame/" + missingSlot + ".png";
            SkinTestKit.WriteAltPack(env.Root, "alt", env.Manifest, env.Slots, env.Qualities, SkinTestKit.AltSeed, p => p == missing || p == "tooltip/row.png");
            var alt = LoadPack(env, "alt");
            var def = LoadPack(env, "default");

            // 缺的槽位框：落到占位皮肤同名那一张（像素与占位皮肤自己取到的一致），不是新包的颜色。
            var got = alt.SlotFrame(missingSlot);
            Assert.IsTrue(SkinTestKit.Same(SkinTestKit.Identify(def.SlotFrame(missingSlot)), SkinTestKit.Identify(got)));
            Assert.IsFalse(SkinTestKit.Same(SkinTestKit.ColorOf(missing, SkinTestKit.AltSeed), SkinTestKit.Identify(got)));
            var tooltipRow = alt.TooltipRow();
            Assert.IsTrue(SkinTestKit.Same(SkinTestKit.Identify(def.TooltipRow()), SkinTestKit.Identify(tooltipRow)));

            var items = alt.Fallbacks.Select(f => f.Item).ToList();
            CollectionAssert.AreEquivalent(new[] { missing, "tooltip/row.png" }, items);
            Assert.AreEqual("placeholder:" + missing, alt.Fallbacks.First(f => f.Item == missing).Target);

            // 其余槽位框仍来自新包。
            foreach (var slot in env.Slots.Where(s => s != missingSlot))
            {
                AssertIs(alt.SlotFrame(slot), "slot_frame/" + slot + ".png", SkinTestKit.AltSeed, "槽位框 " + slot);
            }
        }

        [Test]
        public void DefaultSkin_InstallsNoOverride_AndLooksExactlyAsBefore()
        {
            var env = NewEnv("default");
            var def = LoadPack(env, "default");
            Assert.IsTrue(def.IsPlaceholder);
            Assert.IsNull(def.CreateOverride(), "skin.default 不安装任何覆盖：面板底图、按钮、字体、配色保持框架原有默认");
            Assert.IsFalse(UiSkin.IsOverrideInstalled);
            var root = NewUiRoot();
            var (rect, button, _) = UiWidgets.CreateButton("B", root.Content, "x", () => { });
            Assert.AreEqual(Selectable.Transition.ColorTint, button.transition);
            Assert.AreSame(UiSkin.FlatSprite, rect.GetComponent<Image>().sprite);
            Assert.AreEqual("UiSkinPanelSprite", UiSkin.PanelSprite.name);
            Assert.AreEqual(0, def.Fallbacks.Count, "占位皮肤自己取自己的必备元素不回落");
            foreach (var f in env.Manifest.Expand(env.Slots, env.Qualities).Where(x => x.Element.IsImage && x.Element.Requirement != "optional"))
            {
                Assert.IsNotNull(def.ElementSprite(f), "占位皮肤缺 " + f.Path);
            }

            Assert.AreEqual(0, def.Fallbacks.Count, "占位皮肤的全部必备元素都在，没有回落：" + string.Join(", ", def.Fallbacks.Select(x => x.Item)));
        }
    }
}

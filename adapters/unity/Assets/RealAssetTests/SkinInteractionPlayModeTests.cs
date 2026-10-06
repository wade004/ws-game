#nullable enable
// SkinInteractionPlayModeTests：背包面板尺寸与物品名、运行期换皮肤缓存失效、悬停提示框与拖放穿脱的 PlayMode 验收（ADR-0152 第 2 轮）。
//
// 每项都是"复现 + 不变量"：
//   - 背包面板尺寸：带/不带皮肤、不同格子边长、很长的物品名下，面板里所有子控件的矩形都落在面板矩形内；内容放得下缺省尺寸时面板仍是 260 x 260；
//   - 物品名：行标签显示 item.template.name_key 的本地化文案（期望值直接读 l10n.text 数据行），没有本地化宿主时退回 id 短名；
//   - 换皮肤：占位 -> 参考 -> 占位往返，面板的槽位框、底图、图标、纸娃娃层的像素每次都等于当前内容根的文件，不残留上一套；切换时还有在途加载也不会留下旧根的图；
//   - 提示框/拖放：悬停出名称、品质、属性行（期望值来自数据行）；背包拖到合法槽位显示 target_ok 并穿上，拖到非法槽位显示 target_blocked 并拒绝，装备拖回背包卸下；
//     没有皮肤声明时影子与叠层取占位包的同名文件。
// 内容根：占位资产（皮肤、图标、静态层）与"占位 + 参考美术"叠成的两个临时根，经 UnityResourceLoader.RootDirOverrideForTests 指过去；每个场景自己建加载器，不与进程里别的用例共享缓存。
using Adapter.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Adapter.Unity.EngineAdapter;
using FeelLab.Unity;
using Adapter.Unity.Ui;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Foundation.Localization;
using Lab;
using NUnit.Framework;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Framework.RealAssetTests
{
    [Category("module:ui")]
    [Category("module:lab")]
    public sealed class SkinInteractionPlayModeTests
    {
        private const string PackName = "reference_fantasy";
        private const string PackRef = "skin." + PackName;
        private const string BowId = "item.std_bow";
        private const string SwordId = "item.std_sword_1h";
        private const string ChestId = "item.std_chestplate";
        private const string MainHandSlot = "item.slot.std_main_hand";
        private const string ChestSlot = "item.slot.std_chest";

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private string _tmp = string.Empty;
        private string _rootRef = string.Empty;      // 占位 + 参考美术
        private string _rootPh = string.Empty;       // 只有占位
        private string _referenceDir = string.Empty;
        private string? _previousOverride;

        // ───────── 夹具 ─────────

        [SetUp]
        public void SetUp()
        {
            _referenceDir = Path.Combine(SkinTestKit.RepoRoot, "assets", "_reference_fantasy");
            var placeholder = Path.Combine(SkinTestKit.RepoRoot, "assets", "_placeholder");
            Assert.IsTrue(Directory.Exists(_referenceDir), "缺参考资产目录 " + _referenceDir);
            _tmp = Path.Combine(Application.temporaryCachePath, "skin_interaction_tests", Guid.NewGuid().ToString("N").Substring(0, 8));
            _rootPh = Path.Combine(_tmp, "placeholder");
            _rootRef = Path.Combine(_tmp, "reference");
            foreach (var root in new[] { _rootPh, _rootRef })
            {
                Copy(Path.Combine(placeholder, "ui", "skin", "default"), Path.Combine(root, "ui", "skin", "default"));
                foreach (var sub in new[] { "icons", "sprites" })
                {
                    Copy(Path.Combine(placeholder, sub), Path.Combine(root, sub));
                }
            }

            Copy(Path.Combine(_referenceDir, "ui", "skin", PackName), Path.Combine(_rootRef, "ui", "skin", PackName));
            foreach (var sub in new[] { "icons", "sprites" })
            {
                Copy(Path.Combine(_referenceDir, sub), Path.Combine(_rootRef, sub));
            }

            _previousOverride = UnityResourceLoader.RootDirOverrideForTests;
            UnityResourceLoader.RootDirOverrideForTests = _rootRef;
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
            SkinTestKit.Delete(_tmp);
            yield return null;
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(from))
            {
                Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

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

        /// <summary>精灵所在纹理的像素与磁盘文件一致（每通道差不超过 2，超差像素不超过 0.5%）。</summary>
        private static void AssertSpriteIsFile(Sprite? sprite, string file, string what)
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
                if (Math.Abs(a.a - e.a) > 2 || (e.a > 0 && (Math.Abs(a.r - e.r) > 2 || Math.Abs(a.g - e.g) > 2 || Math.Abs(a.b - e.b) > 2)))
                {
                    bad++;
                }
            }

            Assert.LessOrEqual(bad, expected.Length / 200, $"{what}: 精灵像素与文件 {file} 不一致（{bad}/{expected.Length} 个像素超差）");
        }

        private static bool SpriteDiffersFromFile(Sprite sprite, string file)
        {
            var expected = FilePixels(file, out var w, out var h);
            if (sprite.texture.width != w || sprite.texture.height != h)
            {
                return true;
            }

            var actual = sprite.texture.GetPixels32();
            var bad = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                if (Math.Abs(actual[i].a - expected[i].a) > 2 || (expected[i].a > 0 && Math.Abs(actual[i].r - expected[i].r) > 2))
                {
                    bad++;
                }
            }

            return bad > expected.Length / 200;
        }

        private static string SkinFile(string contentRoot, string packDir, string relative) =>
            Path.Combine(contentRoot, "ui", "skin", packDir, relative.Replace('/', Path.DirectorySeparatorChar));

        private static string IconFile(string contentRoot, Id iconId)
        {
            var parts = iconId.Value.Split('.');
            return Path.Combine(contentRoot, "icons", parts[1], string.Join(".", parts.Skip(2)) + ".png");
        }

        private static string LayerFile(string contentRoot, string set, string direction, string layer) =>
            Path.Combine(contentRoot, "sprites", set, direction, layer + ".png");

        // ───────── 舞台与面板 ─────────

        private sealed class Rig
        {
            public WardrobeStage Stage = null!;
            public UiRoot Root = null!;
            public UnityResourceLoader Loader = null!;
            public UiVisuals Visuals = null!;
            public EquipmentPanel Equipment = null!;
            public InventoryPanel Inventory = null!;
            public UiInteraction Interaction = null!;
            public ShotRig Shot = null!;
            public string ContentRoot = string.Empty;
        }

        /// <summary>
        /// 搭一套带装备面板 + 背包面板 + 提示框/拖放的界面。背包里放着全部样例物品（逐件穿上再全部卸下）；
        /// <paramref name="skinRef"/> 为空 = 占位皮肤；<paramref name="registryWrap"/> 可包一层数据视图（改布局行用）。
        /// </summary>
        private Rig BuildRig(string? skinRef, string contentRoot, Func<IDataRegistryView, IDataRegistryView>? registryWrap = null, bool l10n = true)
        {
            UnityResourceLoader.RootDirOverrideForTests = contentRoot;
            var template = RealAssetTestHost.Script(EquipWardrobeRunner.TemplateScript);
            var stage = WardrobeStage.Create(RealAssetTestHost.Host.Runner, template);
            _disposables.Add(stage);
            foreach (var entry in stage.Entries)
            {
                Assert.IsTrue(stage.Equip(entry.ItemId), "穿不上 " + entry.ItemId);
            }

            stage.UnequipAll();

            var pack = UiSkinPack.Load(skinRef, contentRoot);
            UiSkin.Reset();
            if (pack.CreateOverride() is { } skinOverride)
            {
                UiSkin.Install(skinOverride);
            }

            var loader = new UnityResourceLoader();
            IDataRegistryView registry = stage.Registry;
            if (registryWrap != null)
            {
                registry = registryWrap(registry);
            }

            var visuals = new UiVisuals(pack, registry, stage.DisplayInfo, loader);
            if (l10n)
            {
                visuals.L10n = stage.L10n;
            }

            _disposables.Add(visuals);

            var uiRoot = UiRoot.Create("SkinInteractionTestRoot");
            _spawned.Add(uiRoot.gameObject);
            var shot = ShotRig.Begin(uiRoot, 900, 560, _spawned);

            var equipmentHost = UiWidgets.CreateRoot("EquipmentPanelHost", uiRoot.Content);
            var equipment = equipmentHost.gameObject.AddComponent<EquipmentPanel>();
            var layout = visuals.LayoutOf(UiPanel.Equipment, EquipmentPanel.DefaultLayout);
            layout = new UiPanelLayout(layout.Anchor, layout.Columns, layout.CellSize, layout.PreviewScale, "placeholder_hero", layout.PreviewDirection);
            equipment.Construct(equipmentHost, stage.Panel, visuals, null, layout);

            var inventoryHost = UiWidgets.CreateRoot("InventoryPanelHost", uiRoot.Content);
            var inventory = inventoryHost.gameObject.AddComponent<InventoryPanel>();
            inventory.Construct(inventoryHost, stage.Bag, null!, visuals);

            var interaction = new UiInteraction(
                uiRoot.Content,
                visuals,
                new UiEquipActions((instance, slot) => stage.EquipInstance(instance, slot), slot => stage.Unequip(slot.Value)));
            _disposables.Add(interaction);
            equipment.AttachInteraction(interaction);       // 装备槽位先登记：重叠时格子优先
            inventory.AttachInteraction(interaction);

            var rig = new Rig
            {
                Stage = stage, Root = uiRoot, Loader = loader, Visuals = visuals, Equipment = equipment, Inventory = inventory,
                Interaction = interaction, Shot = shot, ContentRoot = contentRoot,
            };
            Settle(rig);
            return rig;
        }

        private static void Settle(Rig rig)
        {
            // 图标/层图异步加载：每轮 RefreshUi 发起请求、Pump 等完成；换皮肤后作废的在途请求要多一轮才能重新发起。
            for (var round = 0; round < 3; round++)
            {
                rig.Equipment.RefreshUi();
                rig.Inventory.RefreshUi();
                Pump(rig.Loader);
            }

            rig.Equipment.RefreshUi();
            rig.Inventory.RefreshUi();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rig.Inventory.Background);
            Canvas.ForceUpdateCanvases();
        }

        private static int BagIndex(Rig rig, string template) =>
            rig.Stage.Bag.Slots.ToList().FindIndex(s => s.TemplateId.Value == template);

        private static EquipmentPanel.SlotCell SlotCell(Rig rig, string slotId) =>
            rig.Equipment.Cells.Single(c => c.SlotId.Value == slotId);

        private static bool IsEquipped(Rig rig, string slotId) =>
            rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == slotId).Occupied;

        private static string? EquippedTemplate(Rig rig, string slotId) =>
            rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == slotId).TemplateId?.Value;

        // ───────── 数据读取（期望值取自数据行，不写死文案） ─────────

        private static string L10nText(WardrobeStage stage, string key)
        {
            var row = stage.Registry.GetAll("l10n.text").FirstOrDefault(r => r.TryGetString("key", out var k) && k == key);
            Assert.IsNotNull(row, "l10n.text 没有键 " + key);
            return row!.GetString("text");
        }

        private static string ItemNameOf(WardrobeStage stage, string template) =>
            L10nText(stage, stage.Registry.Get("item.template", template)!.GetString("name_key"));

        // ───────── 指针事件 ─────────

        private static PointerEventData Pointer(Vector2 position) => new PointerEventData(EventSystem.current) { position = position };

        private static void Enter(GameObject go, Vector2 position) => ExecuteEvents.Execute(go, Pointer(position), ExecuteEvents.pointerEnterHandler);

        private static void Exit(GameObject go, Vector2 position) => ExecuteEvents.Execute(go, Pointer(position), ExecuteEvents.pointerExitHandler);

        private static void BeginDrag(GameObject go, Vector2 position) => ExecuteEvents.Execute(go, Pointer(position), ExecuteEvents.beginDragHandler);

        private static void DragTo(GameObject go, Vector2 position) => ExecuteEvents.Execute(go, Pointer(position), ExecuteEvents.dragHandler);

        private static void EndDrag(GameObject go, Vector2 position) => ExecuteEvents.Execute(go, Pointer(position), ExecuteEvents.endDragHandler);

        // ───────── 背包面板尺寸（item 3a） ─────────

        /// <summary>改写 ui_layout_definition 的 inventory 行的数据视图包装：<c>cell</c> 有值 = 该行取这个 cell_size，null = 去掉该行（没有布局行时的缺省）；其它表原样透传。</summary>
        private sealed class LayoutOverrideRegistry : IDataRegistryView
        {
            private readonly IDataRegistryView _inner;
            private readonly float? _cell;

            public LayoutOverrideRegistry(IDataRegistryView inner, float? cell)
            {
                _inner = inner;
                _cell = cell;
            }

            public DataRecord? Get(string table, string key) => _inner.Get(table, key);

            public DataRecord? Get(string table, Id id) => _inner.Get(table, id);

            public IReadOnlyList<DataRecord> GetAll(string table)
            {
                var rows = _inner.GetAll(table);
                if (table != "ui_layout_definition")
                {
                    return rows;
                }

                var result = new List<DataRecord>();
                DataRecord? template = null;
                foreach (var row in rows)
                {
                    if (row.TryGetString("panel", out var panel) && panel == "inventory")
                    {
                        template = row;
                        continue;
                    }

                    result.Add(row);
                }

                if (template != null && _cell.HasValue)
                {
                    var raw = Core.Foundation.Common.Json.JsonReader.Parse(
                        "{\"id\":\"" + template.Key + "\",\"panel\":\"inventory\",\"fields\":{\"anchor\":\"top_right\",\"cell_size\":" + _cell.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},\"skin_ref\":\"skin.default\"}");
                    result.Add(new DataRecord(template.Table, template.Key, template.Id, (Core.Foundation.Common.Json.JsonObject)raw));
                }

                return result;
            }

            public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate) => _inner.Query(table, predicate);

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) => _inner.Query(table, predicateText);

            public IReadOnlyList<string> Tables => _inner.Tables;

            public TableSchema? GetSchema(string table) => _inner.GetSchema(table);
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

        /// <summary>每个物品名都是一长串字的本地化宿主（测面板宽度随内容变）。</summary>
        private sealed class LongNameL10n : IL10nHost
        {
            public string Text(Id key, IReadOnlyDictionary<string, string>? vars = null) => "很长很长很长很长很长很长很长很长的物品名称" + key.Value.Substring(key.Value.LastIndexOf('.') + 1);

            public void SetLocale(Id locale) { }

            public Id GetLocale() => new Id("l10n.locale.zh_cn");

            public bool HasText(Id key) => true;

            public IReadOnlyList<Id> SupportedLocales => new[] { new Id("l10n.locale.zh_cn") };

            public Id DefaultLocale => new Id("l10n.locale.zh_cn");
        }

        private static void AssertAllChildrenInside(RectTransform panel, string what)
        {
            var bounds = new Vector3[4];
            var child = new Vector3[4];
            panel.GetWorldCorners(bounds);
            var bad = new List<string>();
            foreach (var rect in panel.GetComponentsInChildren<RectTransform>(false))
            {
                if (rect == panel)
                {
                    continue;
                }

                rect.GetWorldCorners(child);
                for (var i = 0; i < 4; i++)
                {
                    if (child[i].x < bounds[0].x - 0.5f || child[i].x > bounds[2].x + 0.5f || child[i].y < bounds[0].y - 0.5f || child[i].y > bounds[2].y + 0.5f)
                    {
                        bad.Add($"{rect.name}({child[i].x:0.#},{child[i].y:0.#}) 越出 [{bounds[0].x:0.#},{bounds[0].y:0.#}]-[{bounds[2].x:0.#},{bounds[2].y:0.#}]");
                        break;
                    }
                }
            }

            Assert.AreEqual(0, bad.Count, what + "：有子控件越出面板：" + string.Join("; ", bad.Take(6)));
        }

        [UnityTest]
        public IEnumerator InventoryPanel_SizeFollowsContent_NothingOverflows_WithAndWithoutSkin_AtAnyCellSizeAndNameLength()
        {
            // Data = 用数据集里真实的 inventory 布局行（data/_equip，带 cell_size）；Cell 有值 = 用这个 cell_size 覆盖；
            // 两者都没有 = 没有任何布局行（"没声明"的缺省：32 px 格子、260 x 260 面板）。
            var cases = new (string? Skin, float? Cell, bool LongNames, bool Data)[]
            {
                (null, null, false, false), (PackRef, null, false, false),
                (null, null, false, true), (PackRef, null, false, true),
                (null, 96f, false, false), (PackRef, 96f, false, false),
                (null, null, true, false), (PackRef, 64f, true, false),
            };
            foreach (var (skin, cell, longNames, data) in cases)
            {
                var what = $"skin={skin ?? "placeholder"} cell={cell?.ToString() ?? (data ? "data" : "none")} longNames={longNames}";
                var rig = BuildRig(skin, _rootRef, data ? (Func<IDataRegistryView, IDataRegistryView>?)null : r => new LayoutOverrideRegistry(r, cell));
                if (longNames)
                {
                    rig.Visuals.L10n = new LongNameL10n();
                    Settle(rig);
                }

                var panel = rig.Inventory;
                var rows = rig.Stage.Bag.Slots.Count;
                Assert.Greater(rows, 0);
                var size = panel.Background.rect.size;
                AssertAllChildrenInside(panel.Background, what);
                Assert.GreaterOrEqual(size.x, 260f - 0.01f, what + " 宽度不应小于缺省");
                Assert.GreaterOrEqual(size.y, 260f - 0.01f, what + " 高度不应小于缺省");
                foreach (var c in panel.Cells)
                {
                    Assert.AreEqual(panel.CellSize, c.Frame.rectTransform.rect.width, 0.5f, what + " 物品格宽");
                    Assert.AreEqual(panel.CellSize, c.Frame.rectTransform.rect.height, 0.5f, what + " 物品格高");
                }

                if (data)
                {
                    // 数据声明的格子边长：布局行 cell_size（读数据文件，不写死）；图标矩形 = 格子减两侧 2 px 内边距。
                    Assert.AreEqual(DataInventoryCellSize(), panel.CellSize, 0.01f, what + " 格子边长应取数据 cell_size");
                    foreach (var c in panel.Cells)
                    {
                        Assert.AreEqual(DataInventoryCellSize() - 4f, c.Icon.rectTransform.rect.width, 0.5f, what + " 图标宽");
                        Assert.AreEqual(DataInventoryCellSize() - 4f, c.Icon.rectTransform.rect.height, 0.5f, what + " 图标高");
                    }
                }

                if (!cell.HasValue && !longNames && !data)
                {
                    // 不变量：内容放得下缺省尺寸时面板仍是缺省的 260 x 260（缺省行为不变）。
                    Assert.AreEqual(260f, size.x, 0.01f, what);
                    Assert.AreEqual(260f, size.y, 0.01f, what);
                }

                if (cell.HasValue || data)
                {
                    // 规则算出的期望高度：内边距 2 x 8 + 行高 x 行数 + 行间距 2 x (行数 - 1)。
                    var expectHeight = Mathf.Max(260f, 16f + rows * panel.CellSize + (rows - 1) * 2f);
                    Assert.AreEqual(expectHeight, size.y, 0.5f, what + " 高度应随内容");
                    Assert.Greater(size.y, 260f, what + " 大格子下面板必须比缺省高（复现：缺省 260 高放不下）");
                }

                if (longNames)
                {
                    Assert.Greater(size.x, 260f, what + " 长名字下面板必须变宽");
                    // 名称标签宽度被撑到放得下最长的文本。
                    foreach (var row in panel.Background.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Where(t => t.name == "Label" && t.transform.parent.name.StartsWith("Row", StringComparison.Ordinal)))
                    {
                        Assert.GreaterOrEqual(row.rectTransform.rect.width + 0.5f, row.GetPreferredValues(row.text).x, what + " 标签放不下文本：" + row.text);
                    }
                }

                UnityEngine.Object.Destroy(rig.Root.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator InventoryPanel_WithoutVisuals_KeepsTheLegacyRectAtDefaultContent_AndChildrenStayInside()
        {
            var rig = BuildRig(null, _rootRef);
            var uiRoot = UiRoot.Create("NoVisualsRoot");
            _spawned.Add(uiRoot.gameObject);
            var host = UiWidgets.CreateRoot("InventoryNoVisuals", uiRoot.Content);
            var panel = host.gameObject.AddComponent<InventoryPanel>();
            panel.Construct(host, rig.Stage.Bag, null!);
            panel.RefreshUi();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Background);
            Canvas.ForceUpdateCanvases();

            // 与改动前一致的矩形：锚点右上、260 x 260、中心 (-140,-140)（即右/上缘离屏幕边 10）。
            Assert.AreEqual(new Vector2(1f, 1f), panel.Background.anchorMin);
            Assert.AreEqual(260f, panel.Background.sizeDelta.x, 0.01f);
            Assert.AreEqual(260f, panel.Background.sizeDelta.y, 0.01f);
            Assert.AreEqual(-140f, panel.Background.anchoredPosition.x, 0.01f);
            Assert.AreEqual(-140f, panel.Background.anchoredPosition.y, 0.01f);
            AssertAllChildrenInside(panel.Background, "三参数重载（没有物品格）");
            Assert.AreEqual(0, panel.Cells.Count, "三参数重载没有物品格");
            yield return null;
        }

        // ───────── 物品名（item 3c） ─────────

        [UnityTest]
        public IEnumerator InventoryPanel_ShowsLocalizedItemNames_AndFallsBackToShortIdsWithoutL10n()
        {
            var rig = BuildRig(PackRef, _rootRef);
            var slots = rig.Stage.Bag.Slots;
            var texts = rig.Inventory.LabelTexts;
            Assert.AreEqual(slots.Count, texts.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                var expected = ItemNameOf(rig.Stage, slots[i].TemplateId.Value);
                StringAssert.StartsWith(expected + " x", texts[i], "背包行应显示本地化的物品名");
                StringAssert.DoesNotContain("std_", texts[i], "不应把 id 当名字显示：" + texts[i]);
            }

            UnityEngine.Object.Destroy(rig.Root.gameObject);
            yield return null;

            var bare = BuildRig(PackRef, _rootRef, null, l10n: false);
            var bareSlots = bare.Stage.Bag.Slots;
            var bareTexts = bare.Inventory.LabelTexts;
            for (var i = 0; i < bareSlots.Count; i++)
            {
                var shortId = bareSlots[i].TemplateId.Value.Substring(bareSlots[i].TemplateId.Value.LastIndexOf('.') + 1);
                StringAssert.StartsWith(shortId + " x", bareTexts[i], "没有本地化宿主时退回 id 短名");
            }
        }

        [UnityTest]
        public IEnumerator Naming_BackpackLabel_TooltipTitle_AndSlotLabels_ShareOneDisplayNamePath()
        {
            // 复现（第 2 轮截图）：同一个背包面板，一张图里行标签是 id（std_bow），另一张图里是显示名（占位弓）——
            // 根因是截图夹具没给 UiVisuals 接本地化宿主，而不是两条取名路径。现在所有取名都经 ItemTooltipBuilder.ItemName / SlotText，
            // 这里逐项断言三处（背包行标签、提示框标题、装备槽位标签/提示框槽位行）对同一物品同一槽位给出同一个名字。
            foreach (var (skin, root) in new[] { (PackRef, _rootRef), ((string?)null, _rootPh) })
            {
                var rig = BuildRig(skin, root);
                var labels = rig.Inventory.LabelTexts;
                var slots = rig.Stage.Bag.Slots.ToList();
                for (var i = 0; i < slots.Count; i++)
                {
                    var template = slots[i].TemplateId;
                    var name = ItemNameOf(rig.Stage, template.Value);
                    StringAssert.StartsWith(name + " x", labels[i], "背包行标签 " + template.Value);
                    Assert.AreEqual(name, rig.Visuals.ItemName(template), "UiVisuals.ItemName " + template.Value);
                    var cell = rig.Inventory.Cells[i];
                    Enter(cell.Frame.gameObject, rig.Interaction.ScreenOf(cell.Frame.rectTransform));
                    Assert.AreEqual(name, rig.Interaction.Tooltip.TitleText, "提示框标题 " + template.Value);
                    Exit(cell.Frame.gameObject, rig.Interaction.ScreenOf(cell.Frame.rectTransform));
                }

                // 槽位：空槽标签 = 槽位定义 name_key 的文案，也 = 该槽位里物品的提示框"槽位"行。
                foreach (var slot in rig.Stage.Panel.Slots)
                {
                    Assert.IsFalse(slot.Occupied, "夹具里所有槽位应是空的");
                    var key = rig.Stage.Registry.Get("item.slot_definition", slot.SlotId)!.GetString("name_key");
                    var expected = L10nText(rig.Stage, key);
                    var cell = SlotCell(rig, slot.SlotId.Value);
                    Assert.AreEqual(expected, cell.Label.text, "槽位标签 " + slot.SlotId.Value);
                    StringAssert.DoesNotContain("std_", cell.Label.text, "槽位标签不应是 id：" + cell.Label.text);
                    Assert.AreEqual(expected, rig.Visuals.SlotName(slot.SlotId), "UiVisuals.SlotName " + slot.SlotId.Value);
                }

                foreach (var template in slots.Select(sl => sl.TemplateId).ToList())
                {
                    var content = rig.Visuals.TooltipOf(template)!;
                    var slotId = rig.Stage.Registry.Get("item.template", template)!.GetId("slot");
                    Assert.AreEqual(rig.Visuals.SlotName(slotId), content.SlotText, "提示框槽位行与装备槽位标签应一致 " + template.Value);
                }

                UnityEngine.Object.Destroy(rig.Root.gameObject);
                yield return null;
            }

            // 不变量：缺显示名才回落 id 短名（没有本地化宿主）——物品名、槽位名同时回落，不抛异常。
            var bare = BuildRig(PackRef, _rootRef, null, l10n: false);
            foreach (var slot in bare.Stage.Panel.Slots)
            {
                Assert.AreEqual(slot.SlotName, SlotCell(bare, slot.SlotId.Value).Label.text, "没有本地化宿主时槽位标签退回短名 " + slot.SlotId.Value);
            }

            Assert.AreEqual("std_bow", bare.Visuals.ItemName(new Id(BowId)));
        }

        // ───────── 运行期换皮肤（item 4） ─────────

        private void AssertShowsContentRoot(Rig rig, string contentRoot, string packDir, bool expectOverride, string what)
        {
            var panel = rig.Equipment;
            var bgFile = SkinFile(contentRoot, packDir, "panel/background.png");
            if (File.Exists(bgFile))
            {
                AssertSpriteIsFile(panel.Background.GetComponent<Image>().sprite, bgFile, what + " 装备面板底图");
                AssertSpriteIsFile(rig.Inventory.Background.GetComponent<Image>().sprite, bgFile, what + " 背包面板底图");
            }

            Assert.AreEqual(expectOverride, UiSkin.IsOverrideInstalled, what + " UiSkin 覆盖");
            foreach (var cell in panel.Cells)
            {
                var own = SkinFile(contentRoot, packDir, "slot_frame/" + cell.SlotName + ".png");
                var frame = File.Exists(own) ? own : SkinFile(contentRoot, packDir, "slot_frame/_default.png");
                AssertSpriteIsFile(cell.Frame.sprite, frame, what + " 槽位框 " + cell.SlotName);
            }

            AssertSpriteIsFile(panel.Preview.Background.sprite, SkinFile(contentRoot, packDir, "paperdoll_preview/background.png"), what + " 预览区背景");
            var bag = rig.Inventory.Cells;
            var slots = rig.Stage.Bag.Slots;
            for (var i = 0; i < bag.Count; i++)
            {
                AssertSpriteIsFile(bag[i].Frame.sprite, SkinFile(contentRoot, packDir, "slot_frame/_default.png"), what + " 背包格框");
                var icon = rig.Visuals.IconOfTemplate(slots[i].TemplateId)!.Value;
                Assert.IsTrue(bag[i].Icon.enabled, what + " 图标没出现：" + slots[i].TemplateId.Value);
                AssertSpriteIsFile(bag[i].Icon.sprite, IconFile(contentRoot, icon), what + " 背包图标 " + slots[i].TemplateId.Value);
            }

            foreach (var slot in rig.Stage.Panel.Slots.Where(s => s.Occupied))
            {
                var cell = SlotCell(rig, slot.SlotId.Value);
                AssertSpriteIsFile(cell.Icon.sprite, IconFile(contentRoot, slot.IconId!.Value), what + " 装备图标 " + slot.SlotName);
            }

            foreach (var (layer, set, image) in panel.Preview.EquipmentLayers)
            {
                AssertSpriteIsFile(image.sprite, LayerFile(contentRoot, set, panel.Preview.Direction, layer), what + $" 纸娃娃层 {set}/{layer}");
            }
        }

        [UnityTest]
        public IEnumerator SwitchSkin_PlaceholderToReferenceAndBack_RebuildsPanels_NeverKeepsStaleImages()
        {
            var rig = BuildRig(null, _rootPh);
            Assert.IsTrue(rig.Stage.Equip(SwordId));
            Settle(rig);
            AssertShowsContentRoot(rig, _rootPh, "default", expectOverride: false, what: "占位");
            var placeholderIcon = rig.Inventory.Cells[0].Icon.sprite;
            var placeholderBackground = rig.Equipment.Background;

            // 占位 -> 参考：换内容根（真实游戏换的是整套资产），再切换皮肤。
            UnityResourceLoader.RootDirOverrideForTests = _rootRef;
            rig.Visuals.SwitchSkin(PackRef, _rootRef);
            Assert.AreEqual(1, rig.Visuals.SkinGeneration);
            Assert.AreEqual(PackRef, rig.Visuals.Pack.SkinRef);
            Settle(rig);
            AssertShowsContentRoot(rig, _rootRef, PackName, expectOverride: true, what: "参考");
            Assert.AreNotSame(placeholderBackground, rig.Equipment.Background, "面板应整体重建");
            Assert.AreNotSame(placeholderIcon, rig.Inventory.Cells[0].Icon.sprite, "图标应是新根的精灵");
            Assert.IsTrue(SpriteDiffersFromFile(rig.Inventory.Cells[0].Icon.sprite!, IconFile(_rootPh, rig.Visuals.IconOfTemplate(rig.Stage.Bag.Slots[0].TemplateId)!.Value)), "参考图标不应与占位图标相同");
            Assert.AreEqual(0, rig.Visuals.FailedCount);
            Assert.AreEqual(0, rig.Visuals.Pack.Fallbacks.Count);

            // 参考 -> 占位：回到占位内容根，所有像素回到占位文件，UiSkin 覆盖撤掉。
            UnityResourceLoader.RootDirOverrideForTests = _rootPh;
            rig.Visuals.SwitchSkin(null, _rootPh);
            Assert.AreEqual(2, rig.Visuals.SkinGeneration);
            Assert.IsTrue(rig.Visuals.Pack.IsPlaceholder);
            Settle(rig);
            AssertShowsContentRoot(rig, _rootPh, "default", expectOverride: false, what: "回到占位");
            Assert.AreEqual(0, rig.Visuals.FailedCount);

            // 再来一轮：往返是稳定的。
            UnityResourceLoader.RootDirOverrideForTests = _rootRef;
            rig.Visuals.SwitchSkin(PackRef, _rootRef);
            Settle(rig);
            AssertShowsContentRoot(rig, _rootRef, PackName, expectOverride: true, what: "第二次参考");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwitchSkin_WhileLoadsAreStillInFlight_NeverKeepsTheOldRootsImage()
        {
            var rig = BuildRig(null, _rootPh);
            rig.Visuals.SwitchSkin(null, _rootPh);          // 清掉搭建时已经加载好的，让下面的请求真的在途
            Assert.IsTrue(rig.Stage.Equip(SwordId));
            rig.Equipment.RefreshUi();
            rig.Inventory.RefreshUi();                       // 发起加载，不 Pump
            Assert.Greater(rig.Loader.PendingLoadCount, 0, "前置：此刻应有在途加载");
            UnityResourceLoader.RootDirOverrideForTests = _rootRef;
            rig.Visuals.SwitchSkin(PackRef, _rootRef);
            Settle(rig);
            AssertShowsContentRoot(rig, _rootRef, PackName, expectOverride: true, what: "在途切换后");
            Assert.AreEqual(0, rig.Visuals.FailedCount);
            yield return null;
        }

        [Test]
        public void SwitchSkin_InvalidatesTheLoadersSpriteSetAnchorsCache_SoPixelDensityFollowsTheNewRoot()
        {
            string MakeRoot(string name, int ppu)
            {
                var root = Path.Combine(_tmp, name);
                var dir = Path.Combine(root, "sprites", "probe_set", "front");
                Directory.CreateDirectory(dir);
                var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                texture.SetPixels32(Enumerable.Repeat(new Color32(10, 200, 10, 255), 256).ToArray());
                texture.Apply();
                File.WriteAllBytes(Path.Combine(dir, "body.png"), ImageConversion.EncodeToPNG(texture));
                UnityEngine.Object.DestroyImmediate(texture);
                File.WriteAllText(Path.Combine(root, "sprites", "probe_set", "anchors.json"), "{\"pixels_per_unit\":" + ppu + "}");
                return root;
            }

            var rootA = MakeRoot("probe_a", 32);
            var rootB = MakeRoot("probe_b", 64);
            UnityResourceLoader.RootDirOverrideForTests = rootA;
            var loader = new UnityResourceLoader();
            var visuals = new UiVisuals(UiSkinPack.Load(null, rootA), null, null, loader);
            _disposables.Add(visuals);
            Assert.IsNull(visuals.Layer("probe_set", "front", "body"));
            Pump(loader);
            Assert.AreEqual(32f, visuals.Layer("probe_set", "front", "body")!.pixelsPerUnit, 0.01f, "根 A 的像素密度");
            var reads = loader.SpriteSetAnchorsJsonReadCount;

            UnityResourceLoader.RootDirOverrideForTests = rootB;
            visuals.SwitchSkin(null, rootB);
            Assert.IsNull(visuals.Layer("probe_set", "front", "body"), "切换后旧根的精灵不得残留");
            Pump(loader);
            Assert.AreEqual(64f, visuals.Layer("probe_set", "front", "body")!.pixelsPerUnit, 0.01f, "根 B 的像素密度（锚点缓存必须随切换失效）");
            Assert.Greater(loader.SpriteSetAnchorsJsonReadCount, reads, "切换后应重新读一次 anchors.json");
        }

        [UnityTest]
        public IEnumerator SwitchSkin_KeepsHoverTooltipAndDragWorking_WithTheNewSkinsArt()
        {
            var rig = BuildRig(null, _rootPh);
            UnityResourceLoader.RootDirOverrideForTests = _rootRef;
            rig.Visuals.SwitchSkin(PackRef, _rootRef);
            Settle(rig);

            var bow = rig.Inventory.Cells[BagIndex(rig, BowId)];
            Enter(bow.Frame.gameObject, rig.Interaction.ScreenOf(bow.Frame.rectTransform));
            Assert.IsTrue(rig.Interaction.Tooltip.IsVisible);
            AssertSpriteIsFile(rig.Interaction.Tooltip.Background.sprite, SkinFile(_rootRef, PackName, "tooltip/background.png"), "换皮肤后的提示框底板");
            Exit(bow.Frame.gameObject, Vector2.zero);

            var main = SlotCell(rig, MainHandSlot);
            BeginDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(bow.Frame.rectTransform));
            AssertSpriteIsFile(rig.Interaction.Drag.GhostFrame.sprite, SkinFile(_rootRef, PackName, "drag/ghost.png"), "换皮肤后的拖拽影子");
            DragTo(bow.Frame.gameObject, rig.Interaction.ScreenOf(main.Root));
            Assert.AreEqual(UiDropState.Ok, rig.Interaction.Drag.State);
            AssertSpriteIsFile(rig.Interaction.Drag.Hover!.Mark!.sprite, SkinFile(_rootRef, PackName, "drag/target_ok.png"), "换皮肤后的 target_ok");
            EndDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(main.Root));
            Assert.AreEqual(BowId, EquippedTemplate(rig, MainHandSlot));

            // 换回占位：同一套交互读回占位美术。
            UnityResourceLoader.RootDirOverrideForTests = _rootPh;
            rig.Visuals.SwitchSkin(null, _rootPh);
            Settle(rig);
            var sword = rig.Inventory.Cells[BagIndex(rig, SwordId)];
            Enter(sword.Frame.gameObject, rig.Interaction.ScreenOf(sword.Frame.rectTransform));
            AssertSpriteIsFile(rig.Interaction.Tooltip.Background.sprite, SkinFile(_rootPh, "default", "tooltip/background.png"), "换回占位后的提示框底板");
            yield return null;
        }

        // ───────── 提示框（item 5） ─────────

        private static void AssertTooltipShowsTemplate(Rig rig, string template, string what)
        {
            var tip = rig.Interaction.Tooltip;
            var row = rig.Stage.Registry.Get("item.template", template)!;
            Assert.IsTrue(tip.IsVisible, what + " 提示框应可见");
            Assert.AreEqual(ItemNameOf(rig.Stage, template), tip.TitleText, what + " 名称");
            var qualityKey = rig.Stage.Registry.Get("item.quality_definition", row.GetId("quality"))!.GetString("name_key");
            Assert.AreEqual(L10nText(rig.Stage, qualityKey), tip.QualityText, what + " 品质");
            var slotKey = rig.Stage.Registry.Get("item.slot_definition", row.GetId("slot"))!.GetString("name_key");

            // 数据里这件物品没有固定属性/等级需求（若以后有了，这里要同步补期望行，不能静默漏掉）。
            Assert.IsFalse(row.Has("stats"), template + " 数据有 stats：补期望行");
            Assert.IsFalse(row.Has("requirements"), template + " 数据有 requirements：补期望行");
            var expected = new List<(string, string)>
            {
                (ItemTooltipBuilder.LabelSlot, L10nText(rig.Stage, slotKey)),
                (ItemTooltipBuilder.LabelItemLevel, row.GetInt("item_level").ToString(System.Globalization.CultureInfo.InvariantCulture)),
            };
            if (row.TryGetObject("weapon_profile", out var profile))
            {
                double Num(string key) => ((Core.Foundation.Common.Json.JsonNumber)profile[key]).Value;
                expected.Add((ItemTooltipBuilder.LabelDamage, Num("damage_min").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " - " + Num("damage_max").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                expected.Add((ItemTooltipBuilder.LabelSpeed, Num("speed").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
            }

            CollectionAssert.AreEqual(expected, tip.VisibleRows.ToList(), what + " 属性行");

            // 提示框完整落在画布内（夹在可见范围里）。
            var bounds = new Vector3[4];
            var corners = new Vector3[4];
            rig.Interaction.Content.GetWorldCorners(bounds);
            tip.Root.GetWorldCorners(corners);
            for (var i = 0; i < 4; i++)
            {
                Assert.GreaterOrEqual(corners[i].x, bounds[0].x - 0.5f, what + " 提示框越出左边");
                Assert.LessOrEqual(corners[i].x, bounds[2].x + 0.5f, what + " 提示框越出右边");
                Assert.GreaterOrEqual(corners[i].y, bounds[0].y - 0.5f, what + " 提示框越出下边");
                Assert.LessOrEqual(corners[i].y, bounds[2].y + 0.5f, what + " 提示框越出上边");
            }
        }

        [UnityTest]
        public IEnumerator Tooltip_Hover_ShowsNameQualityAndAttributeRowsFromData_WithTheSkinsTooltipArt()
        {
            foreach (var (skin, root, packDir) in new[] { (PackRef, _rootRef, PackName), ((string?)null, _rootPh, "default") })
            {
                var rig = BuildRig(skin, root);
                var tip = rig.Interaction.Tooltip;
                Assert.IsFalse(tip.IsVisible, "没悬停时不显示");

                // 背包格子悬停：每件样例物品逐一核对。
                foreach (var slot in rig.Stage.Bag.Slots.ToList())
                {
                    var cell = rig.Inventory.Cells[BagIndex(rig, slot.TemplateId.Value)];
                    var position = rig.Interaction.ScreenOf(cell.Frame.rectTransform);
                    Enter(cell.Frame.gameObject, position);
                    AssertTooltipShowsTemplate(rig, slot.TemplateId.Value, $"背包 {slot.TemplateId.Value}（{packDir}）");
                    AssertSpriteIsFile(tip.Background.sprite, SkinFile(root, packDir, "tooltip/background.png"), "提示框底板");
                    AssertSpriteIsFile(tip.Divider.sprite, SkinFile(root, packDir, "tooltip/divider.png"), "提示框分隔线");
                    Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, tip.Background.type);
                    Exit(cell.Frame.gameObject, position);
                    Assert.IsFalse(tip.IsVisible, "移出后隐藏");
                }

                // 装备槽位悬停：空槽不出提示框，已装备的出。
                Assert.IsTrue(rig.Stage.Equip(SwordId));
                Settle(rig);
                var main = SlotCell(rig, MainHandSlot);
                var chest = SlotCell(rig, ChestSlot);
                Enter(chest.Root.gameObject, rig.Interaction.ScreenOf(chest.Root));
                Assert.IsFalse(tip.IsVisible, "空槽没有提示框");
                Exit(chest.Root.gameObject, Vector2.zero);
                Enter(main.Root.gameObject, rig.Interaction.ScreenOf(main.Root));
                AssertTooltipShowsTemplate(rig, SwordId, "装备槽位 main_hand");
                Exit(main.Root.gameObject, Vector2.zero);
                Assert.IsFalse(tip.IsVisible);

                if (skin != null)
                {
                    var sword = rig.Inventory.Cells[BagIndex(rig, SwordId)];
                    Enter(sword.Frame.gameObject, rig.Interaction.ScreenOf(sword.Frame.rectTransform));
                    yield return null;
                    rig.Shot.Capture("v2_tooltip_hover.png");
                    Exit(sword.Frame.gameObject, Vector2.zero);
                }

                rig.Shot.End();
                UnityEngine.Object.Destroy(rig.Root.gameObject);
                yield return null;
            }
        }

        // ───────── 拖放穿脱（item 5） ─────────

        [UnityTest]
        public IEnumerator Drag_BagToCompatibleSlot_ShowsGhostAndTargetOk_AndDropEquips()
        {
            foreach (var (skin, root, packDir) in new[] { (PackRef, _rootRef, PackName), ((string?)null, _rootPh, "default") })
            {
                var rig = BuildRig(skin, root);
                var drag = rig.Interaction.Drag;
                var bagBefore = rig.Stage.Bag.Slots.Count;
                var bowIndex = BagIndex(rig, BowId);
                var bowInstance = rig.Stage.Bag.Slots[bowIndex].InstanceId;
                var bow = rig.Inventory.Cells[bowIndex];
                var main = SlotCell(rig, MainHandSlot);
                Assert.IsFalse(IsEquipped(rig, MainHandSlot), "前置：主手为空");

                BeginDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(bow.Frame.rectTransform));
                Assert.IsTrue(drag.IsDragging);
                Assert.IsTrue(drag.GhostVisible, "拖拽中影子可见");
                AssertSpriteIsFile(drag.GhostFrame.sprite, SkinFile(root, packDir, "drag/ghost.png"), "影子底图");
                AssertSpriteIsFile(drag.GhostIcon.sprite, IconFile(root, rig.Visuals.IconOfTemplate(new Id(BowId))!.Value), "影子图标");

                var target = rig.Interaction.ScreenOf(main.Root);
                DragTo(bow.Frame.gameObject, target);
                Assert.AreEqual(UiDropState.Ok, drag.State, "主手槽位接受弓");
                Assert.AreEqual("slot:" + main.SlotName, drag.Hover!.Name);
                Assert.IsTrue(drag.Hover.Mark!.enabled);
                AssertSpriteIsFile(drag.Hover.Mark.sprite, SkinFile(root, packDir, "drag/target_ok.png"), "target_ok 叠层");
                var ghostPosition = rig.Interaction.ScreenToLocal(target);
                Assert.AreEqual(ghostPosition.x, drag.Ghost.anchoredPosition.x, 0.5f, "影子跟随指针 x");
                Assert.AreEqual(ghostPosition.y, drag.Ghost.anchoredPosition.y, 0.5f, "影子跟随指针 y");
                foreach (var other in drag.Targets.Where(t => t != drag.Hover))
                {
                    Assert.IsFalse(other.Mark!.enabled, "只有指针下的落点显示叠层：" + other.Name);
                }

                if (skin != null)
                {
                    yield return null;
                    rig.Shot.Capture("v2_drag_ok.png");
                }

                EndDrag(bow.Frame.gameObject, target);
                Assert.IsFalse(drag.IsDragging);
                Assert.IsFalse(drag.GhostVisible, "松手后影子收起");
                Assert.IsTrue(drag.LastDrop!.Value.Accepted, "落下应当穿上：" + drag.LastDrop.Value.Reason);
                Assert.AreEqual("equip", drag.LastDrop.Value.Action);
                Assert.AreEqual(BowId, EquippedTemplate(rig, MainHandSlot), "主手装上了弓");
                Assert.AreEqual(bowInstance, rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == MainHandSlot).InstanceId, "穿上的就是被拖的那一件实例");
                Assert.AreEqual(bagBefore - 1, rig.Stage.Bag.Slots.Count, "弓离开背包");
                Assert.IsFalse(rig.Stage.Bag.Slots.Any(s => s.InstanceId == bowInstance));
                Assert.IsTrue(drag.Targets.All(t => !t.Mark!.enabled), "叠层收起");

                rig.Shot.End();
                UnityEngine.Object.Destroy(rig.Root.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Drag_BagToIncompatibleSlot_ShowsTargetBlocked_DropRejectsAndLeavesEverythingUnchanged()
        {
            foreach (var (skin, root, packDir) in new[] { (PackRef, _rootRef, PackName), ((string?)null, _rootPh, "default") })
            {
                var rig = BuildRig(skin, root);
                var drag = rig.Interaction.Drag;
                // 胸部装着胸甲，让面板不是空的；弓拖到胸部槽位 = 槽位不匹配。
                Assert.IsTrue(rig.Stage.Equip(ChestId));
                Settle(rig);
                var chestInstance = rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == ChestSlot).InstanceId;
                var bagBefore = rig.Stage.Bag.Slots.Select(s => s.InstanceId.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var bowIndex = BagIndex(rig, BowId);
                var bow = rig.Inventory.Cells[bowIndex];
                var chest = SlotCell(rig, ChestSlot);

                BeginDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(bow.Frame.rectTransform));
                var target = rig.Interaction.ScreenOf(chest.Root);
                DragTo(bow.Frame.gameObject, target);
                Assert.AreEqual(UiDropState.Blocked, drag.State, "胸部槽位不接受弓");
                Assert.IsTrue(drag.Hover!.Mark!.enabled);
                AssertSpriteIsFile(drag.Hover.Mark.sprite, SkinFile(root, packDir, "drag/target_blocked.png"), "target_blocked 叠层");
                if (skin != null)
                {
                    yield return null;
                    rig.Shot.Capture("v2_drag_blocked.png");
                }

                EndDrag(bow.Frame.gameObject, target);
                var drop = drag.LastDrop!.Value;
                Assert.IsFalse(drop.Accepted, "不合法的落下必须被拒绝");
                Assert.AreEqual(Core.Carriers.Common.EquipFailureReason.SlotMismatch.ToString(), drop.Reason);
                Assert.AreEqual(ChestId, EquippedTemplate(rig, ChestSlot), "胸部装备不变");
                Assert.AreEqual(chestInstance, rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == ChestSlot).InstanceId);
                Assert.IsFalse(IsEquipped(rig, MainHandSlot), "主手仍为空");
                CollectionAssert.AreEqual(bagBefore, rig.Stage.Bag.Slots.Select(s => s.InstanceId.Value).OrderBy(x => x, StringComparer.Ordinal).ToList(), "背包不变");

                // 反过来：胸甲拖到主手槽位同样被拒。
                var chestItem = rig.Inventory.Cells[BagIndex(rig, ChestId)];
                var main = SlotCell(rig, MainHandSlot);
                BeginDrag(chestItem.Frame.gameObject, rig.Interaction.ScreenOf(chestItem.Frame.rectTransform));
                DragTo(chestItem.Frame.gameObject, rig.Interaction.ScreenOf(main.Root));
                Assert.AreEqual(UiDropState.Blocked, drag.State);
                EndDrag(chestItem.Frame.gameObject, rig.Interaction.ScreenOf(main.Root));
                Assert.IsFalse(drag.LastDrop!.Value.Accepted);
                Assert.IsFalse(IsEquipped(rig, MainHandSlot));

                // 落在没有任何落点的地方：什么也不发生。
                BeginDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(bow.Frame.rectTransform));
                DragTo(bow.Frame.gameObject, new Vector2(2f, 2f));
                Assert.AreEqual(UiDropState.None, drag.State);
                Assert.IsNull(drag.Hover);
                EndDrag(bow.Frame.gameObject, new Vector2(2f, 2f));
                Assert.IsFalse(drag.LastDrop!.Value.Accepted);
                Assert.AreEqual("NoTarget", drag.LastDrop.Value.Reason);
                Assert.IsFalse(IsEquipped(rig, MainHandSlot));

                rig.Shot.End();
                UnityEngine.Object.Destroy(rig.Root.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Drag_EquippedSlotBackToBag_Unequips_AndOtherSlotsAreBlocked()
        {
            var rig = BuildRig(PackRef, _rootRef);
            var drag = rig.Interaction.Drag;
            Assert.IsTrue(rig.Stage.Equip(SwordId));
            Settle(rig);
            var swordInstance = rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == MainHandSlot).InstanceId!.Value;
            var bagBefore = rig.Stage.Bag.Slots.Count;
            var main = SlotCell(rig, MainHandSlot);
            var chest = SlotCell(rig, ChestSlot);
            var bagRect = rig.Inventory.Background;

            // 拖到另一个装备槽位：不合法。
            BeginDrag(main.Root.gameObject, rig.Interaction.ScreenOf(main.Root));
            Assert.IsTrue(drag.IsDragging, "已装备的槽位可以拖出");
            AssertSpriteIsFile(drag.GhostIcon.sprite, IconFile(_rootRef, rig.Visuals.IconOfTemplate(new Id(SwordId))!.Value), "影子图标 = 被拖装备的图标");
            DragTo(main.Root.gameObject, rig.Interaction.ScreenOf(chest.Root));
            Assert.AreEqual(UiDropState.Blocked, drag.State, "装备不能直接换到别的槽位");
            // 拖回自己的槽位：没有落点（无事发生）。
            DragTo(main.Root.gameObject, rig.Interaction.ScreenOf(main.Root));
            Assert.AreEqual(UiDropState.None, drag.State);
            // 拖到背包面板：可放置。
            var bagPoint = rig.Interaction.ScreenOf(bagRect);
            DragTo(main.Root.gameObject, bagPoint);
            Assert.AreEqual(UiDropState.Ok, drag.State, "背包接受从装备槽位拖来的物品");
            Assert.AreEqual("bag", drag.Hover!.Name);
            AssertSpriteIsFile(drag.Hover.Mark!.sprite, SkinFile(_rootRef, PackName, "drag/target_ok.png"), "背包 target_ok");
            EndDrag(main.Root.gameObject, bagPoint);

            Assert.IsTrue(drag.LastDrop!.Value.Accepted, "落下应当卸下：" + drag.LastDrop.Value.Reason);
            Assert.AreEqual("unequip", drag.LastDrop.Value.Action);
            Assert.IsFalse(IsEquipped(rig, MainHandSlot), "主手已卸下");
            Assert.AreEqual(bagBefore + 1, rig.Stage.Bag.Slots.Count, "卸下的物品回到背包");
            Assert.IsTrue(rig.Stage.Bag.Slots.Any(s => s.InstanceId == swordInstance), "回到背包的就是原来那一件实例");
            Settle(rig);
            Assert.AreEqual(rig.Stage.Bag.Slots.Count, rig.Inventory.Cells.Count, "背包面板随之多一行");

            // 空槽拖不起来；从背包拖到背包面板自己：没有落点。
            BeginDrag(main.Root.gameObject, rig.Interaction.ScreenOf(main.Root));
            Assert.IsFalse(drag.IsDragging, "空槽没有可拖的东西");
            var anyBag = rig.Inventory.Cells[0];
            BeginDrag(anyBag.Frame.gameObject, rig.Interaction.ScreenOf(anyBag.Frame.rectTransform));
            DragTo(anyBag.Frame.gameObject, rig.Interaction.ScreenOf(bagRect));
            Assert.AreEqual(UiDropState.None, drag.State, "背包物品拖回背包：不是落点");
            EndDrag(anyBag.Frame.gameObject, rig.Interaction.ScreenOf(bagRect));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Drag_DropOntoAnOccupiedSlot_FollowsTheEquipmentCarriersResult()
        {
            var rig = BuildRig(PackRef, _rootRef);
            var drag = rig.Interaction.Drag;
            Assert.IsTrue(rig.Stage.Equip(SwordId));
            Settle(rig);
            var oldInstance = rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == MainHandSlot).InstanceId;
            var bowIndex = BagIndex(rig, BowId);
            var bowInstance = rig.Stage.Bag.Slots[bowIndex].InstanceId;
            var bow = rig.Inventory.Cells[bowIndex];
            var main = SlotCell(rig, MainHandSlot);

            BeginDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(bow.Frame.rectTransform));
            DragTo(bow.Frame.gameObject, rig.Interaction.ScreenOf(main.Root));
            Assert.AreEqual(UiDropState.Ok, drag.State, "槽位匹配就显示可放置（占位/置换规则由装备载体裁决）");
            EndDrag(bow.Frame.gameObject, rig.Interaction.ScreenOf(main.Root));

            var drop = drag.LastDrop!.Value;
            var nowEquipped = rig.Stage.Panel.Slots.Single(s => s.SlotId.Value == MainHandSlot).InstanceId;
            if (drop.Accepted)
            {
                Assert.AreEqual(bowInstance, nowEquipped, "成功 = 新的那件装上了");
                Assert.IsTrue(rig.Stage.Bag.Slots.Any(s => s.InstanceId == oldInstance), "被置换下来的回到背包");
            }
            else
            {
                Assert.AreEqual(oldInstance, nowEquipped, "失败 = 装备不变：" + drop.Reason);
                Assert.IsTrue(rig.Stage.Bag.Slots.Any(s => s.InstanceId == bowInstance), "失败 = 物品仍在背包");
                Assert.AreNotEqual(string.Empty, drop.Reason, "失败必须带出原因");
            }

            yield return null;
        }
    }

    /// <summary>截图装置：把 UiRoot 的画布切到固定尺寸的相机渲染（宽高固定，布局确定），需要时把画面存成 PNG（环境变量 GF_SKIN_SHOTS_DIR 指向目录时输出，不进 git）。</summary>
    internal sealed class ShotRig
    {
        private readonly UiRoot _root;
        private readonly Camera _camera;
        private readonly RenderTexture _texture;
        private readonly int _width;
        private readonly int _height;

        private ShotRig(UiRoot root, Camera camera, RenderTexture texture, int width, int height)
        {
            _root = root;
            _camera = camera;
            _texture = texture;
            _width = width;
            _height = height;
        }

        public static string? ShotDir => Environment.GetEnvironmentVariable("GF_SKIN_SHOTS_DIR") is { Length: > 0 } dir ? dir : null;

        public static ShotRig Begin(UiRoot root, int width, int height, List<GameObject> spawned)
        {
            var camGo = new GameObject("SkinShotCamera");
            spawned.Add(camGo);
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
            return new ShotRig(root, camera, texture, width, height);
        }

        public void Capture(string fileName)
        {
            var dir = ShotDir;
            if (dir == null)
            {
                return;
            }

            Directory.CreateDirectory(dir);
            Canvas.ForceUpdateCanvases();
            _camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = _texture;
            var picture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            picture.ReadPixels(new UnityEngine.Rect(0, 0, _width, _height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(dir, fileName), ImageConversion.EncodeToPNG(picture));
            UnityEngine.Object.DestroyImmediate(picture);
            TestContext.Out.WriteLine("[skin-shot] " + Path.Combine(dir, fileName));
        }

        public void End()
        {
            _camera.targetTexture = null;
            _texture.Release();
            UnityEngine.Object.Destroy(_texture);
        }
    }
}

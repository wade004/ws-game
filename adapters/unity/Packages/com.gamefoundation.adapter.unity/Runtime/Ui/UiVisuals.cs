#nullable enable
// UiVisuals：界面面板取美术资源的统一入口（手感设计/08 第 3/4/6 节、ADR-0149）。
//
// 面板（背包、装备）自己不写死任何图片路径、尺寸或位置：
//   皮肤包  ：UiSkinPack（槽位框、品质框、预览区背景、面板底图、主题）；
//   图标    ：物品模板 → display.map.icon_id → 适配器资源加载器的 icon 类别路径（icons/<类别>/<名>.png）；
//   纸娃娃层：装备外观行 mesh_ref（paperdoll.<集>）→ 层资源 layer.<集>__<方向>__<层>（sprites/<集>/<方向>/<层>.png）；
//   布局    ：ui_layout_definition 行（panel + fields：anchor/columns/cell_size/preview_*）。
// 游戏换美术只换这些资源文件与数据行，不写界面代码；资源缺失时面板用皮肤包回落链或空白占位，永远建得出来。
//
// 判断记录（图标与纸娃娃层异步取用）：它们走适配器资源加载器（后台读文件、主线程按帧预算解码，由 UnityEngineHost 每帧 Tick），
// 面板每帧刷新时 <see cref="Icon"/>/<see cref="Layer"/> 首次请求发起加载并返回 null，加载完成后的下一次刷新自然拿到精灵——
// 不阻塞主线程，也不要求面板知道加载何时完成。加载失败记入 <see cref="FailedCount"/> 且不重试（缺失的资源由导入校验与衣橱报告暴露）。
//
// 判断记录（运行期切换皮肤，ADR-0152）：<see cref="SwitchSkin"/> 是换皮肤的唯一入口，一次做完四件事——
//   1) 本入口发起过的图标/层图加载全部作废：已加载的按 id <c>Unload</c>，在途的记为"待作废"（完成后立刻卸掉，期间同 id 不再发起新请求，
//      免得并入旧在途请求拿到旧根的图）；加载器按精灵集目录缓存的 anchors.json 解析结果一并清空（新根下同一精灵集的锚点/像素密度可能不同）；
//   2) 本入口自己的锚点文件缓存清空；
//   3) 重新装载皮肤包并重装 UiSkin 覆盖（占位皮肤则撤掉覆盖，回到框架原有默认外观）；
//   4) 发出 <see cref="SkinChanged"/>，面板（背包、装备、提示框、拖拽）订阅后整体重建——它们持有旧皮肤包的精灵，不重建就是旧外观。
// 旧皮肤包不立即销毁（仍订阅不到事件的面板，如 HUD，持有的是旧包的 UiSkin 精灵，销毁后会变成空白）：挂在退役清单里，随本对象 <see cref="Dispose"/> 一起释放。
using System;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Localization;
using Presentation.Ui;
using UnityEngine;

namespace Adapter.Unity.Ui
{
    /// <summary>一个面板的布局参数（<c>ui_layout_definition.fields</c>）；缺省值由调用方给出，取不到数据行时全部取缺省。</summary>
    public readonly struct UiPanelLayout
    {
        /// <summary>锚点名：<c>top_left/top_center/top_right/left_center/center/right_center/bottom_left/bottom_center/bottom_right</c>。</summary>
        public string Anchor { get; }

        /// <summary>网格列数（<c>columns</c>）。</summary>
        public int Columns { get; }

        /// <summary>格子边长（像素，<c>cell_size</c>）；0 表示"取皮肤包槽位框的原生尺寸"。</summary>
        public float CellSize { get; }

        /// <summary>预览区缩放（<c>preview_scale</c>，相对皮肤包预览区背景的原生尺寸）。</summary>
        public float PreviewScale { get; }

        /// <summary>预览区身体层精灵集（<c>preview_body_set</c>，如占位集的 <c>placeholder_hero</c>）；空 = 只画装备层。</summary>
        public string PreviewBodySet { get; }

        /// <summary>预览区初始方向（<c>preview_direction</c>，缺省 <c>front</c>）。</summary>
        public string PreviewDirection { get; }

        public UiPanelLayout(string anchor, int columns, float cellSize, float previewScale, string previewBodySet, string previewDirection)
        {
            Anchor = anchor;
            Columns = columns;
            CellSize = cellSize;
            PreviewScale = previewScale;
            PreviewBodySet = previewBodySet;
            PreviewDirection = previewDirection;
        }

        /// <summary>
        /// 格子边长：布局的 <c>cell_size</c> 大于 0 取它，为 0（或负）按契约取皮肤包槽位框的原生宽度（<see cref="CellSize"/> 注释）。
        /// 装备面板与背包面板共用这一处，避免两个面板对"0"各解各的（背包曾把 0 当成缺省 32 px）。
        /// </summary>
        public static float ResolveCellSize(float configured, Sprite defaultFrame) =>
            configured > 0f ? configured : defaultFrame.rect.width;

        /// <summary>读某面板的布局行（没有该面板的行取缺省）；<paramref name="defaults"/> 提供缺省值。</summary>
        public static UiPanelLayout Read(IDataRegistryView? registry, UiPanel panel, UiPanelLayout defaults)
        {
            if (registry == null)
            {
                return defaults;
            }

            foreach (var record in registry.GetAll(UiSchemas.UiLayoutDefinition.Name))
            {
                var def = UiLayoutDefinition.FromRecord(record);
                if (def.Panel != panel)
                {
                    continue;
                }

                var f = def.Fields;
                return new UiPanelLayout(
                    Text(f, "anchor", defaults.Anchor),
                    (int)Math.Max(1, Number(f, "columns", defaults.Columns)),
                    (float)Math.Max(0, Number(f, "cell_size", defaults.CellSize)),
                    (float)Math.Max(0.1, Number(f, "preview_scale", defaults.PreviewScale)),
                    Text(f, "preview_body_set", defaults.PreviewBodySet),
                    Text(f, "preview_direction", defaults.PreviewDirection));
            }

            return defaults;
        }

        private static string Text(Core.Foundation.Common.Json.JsonObject f, string key, string fallback) =>
            f.TryGetValue(key, out var v) && v is Core.Foundation.Common.Json.JsonString s ? s.Value : fallback;

        private static double Number(Core.Foundation.Common.Json.JsonObject f, string key, double fallback) =>
            f.TryGetValue(key, out var v) && v is Core.Foundation.Common.Json.JsonNumber n ? n.Value : fallback;

        /// <summary>按锚点名摆放矩形：锚点与轴心取同一角/边/中心，离边 <paramref name="margin"/> 像素；锚点名不认识时按 <c>center</c>。</summary>
        public void ApplyAnchor(RectTransform rect, Vector2 size, float margin = 10f)
        {
            var (ax, ay) = Anchor switch
            {
                "top_left" => (0f, 1f),
                "top_center" => (0.5f, 1f),
                "top_right" => (1f, 1f),
                "left_center" => (0f, 0.5f),
                "right_center" => (1f, 0.5f),
                "bottom_left" => (0f, 0f),
                "bottom_center" => (0.5f, 0f),
                "bottom_right" => (1f, 0f),
                _ => (0.5f, 0.5f),
            };

            var anchor = new Vector2(ax, ay);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            // 贴边的轴向内缩 margin；居中的轴不偏移。
            rect.anchoredPosition = new Vector2(
                ax <= 0f ? margin : ax >= 1f ? -margin : 0f,
                ay <= 0f ? margin : ay >= 1f ? -margin : 0f);
        }
    }

    public sealed class UiVisuals : IDisposable
    {
        private readonly HashSet<Id> _requested = new HashSet<Id>();
        private readonly HashSet<Id> _failed = new HashSet<Id>();
        private readonly HashSet<Id> _stalePending = new HashSet<Id>();
        private readonly List<UiSkinPack> _retiredPacks = new List<UiSkinPack>();
        private UnityResourceLoader? _loader;
        private int _generation;
        private bool _overrideInstalledBySwitch;

        /// <summary>当前皮肤包；<see cref="SwitchSkin"/> 会换成新的（订阅 <see cref="SkinChanged"/> 的面板据此重建）。</summary>
        public UiSkinPack Pack { get; private set; }

        /// <summary>换过几次皮肤（<see cref="SwitchSkin"/> 的累计次数）；面板用它判断自己持有的是不是旧皮肤。</summary>
        public int SkinGeneration => _generation;

        /// <summary><see cref="SwitchSkin"/> 完成后发出（皮肤包、UiSkin 覆盖、资源缓存都已换好）。</summary>
        public event Action? SkinChanged;

        /// <summary>本地化宿主（物品名等文案）；为空时物品名退回模板 id 短名。宿主装配时设置。</summary>
        public IL10nHost? L10n { get; set; }

        public IDataRegistryView? Registry { get; }

        public IDisplayInfoRegistry? DisplayInfo { get; }

        /// <summary>适配器资源加载器；构造时没给就在首次取用时取全局宿主的那一个。</summary>
        public UnityResourceLoader Loader => _loader ??= UnityEngineHost.Ensure().ResourceLoader;

        /// <summary>加载失败（资源缺失或格式不对）的资源个数。</summary>
        public int FailedCount => _failed.Count;

        /// <summary>已发起过加载请求的资源个数（含已完成与失败）。</summary>
        public int RequestedCount => _requested.Count;

        public UiVisuals(UiSkinPack pack, IDataRegistryView? registry, IDisplayInfoRegistry? displayInfo, UnityResourceLoader? loader = null)
        {
            Pack = pack ?? throw new ArgumentNullException(nameof(pack));
            Registry = registry;
            DisplayInfo = displayInfo;
            _loader = loader;
        }

        /// <summary>
        /// 按数据装配：皮肤包取 <c>ui_layout_definition</c> 里第一个声明了 <c>skin_ref</c> 的行（装备、背包、角色面板的行优先），都没有声明取占位皮肤。
        /// </summary>
        public static UiVisuals Create(IDataRegistryView registry, IDisplayInfoRegistry? displayInfo, UnityResourceLoader? loader = null, string? contentRoot = null)
        {
            string? chosen = null;
            var priority = int.MaxValue;
            foreach (var record in registry.GetAll(UiSchemas.UiLayoutDefinition.Name))
            {
                var def = UiLayoutDefinition.FromRecord(record);
                if (!def.SkinRef.HasValue)
                {
                    continue;
                }

                var rank = def.Panel == UiPanel.Equipment ? 0 : def.Panel == UiPanel.Inventory ? 1 : def.Panel == UiPanel.CharacterStats ? 2 : 3;
                if (rank < priority)
                {
                    priority = rank;
                    chosen = def.SkinRef.Value.Value;
                }
            }

            return new UiVisuals(UiSkinPack.Load(chosen, contentRoot), registry, displayInfo, loader);
        }

        public UiPanelLayout LayoutOf(UiPanel panel, UiPanelLayout defaults) => UiPanelLayout.Read(Registry, panel, defaults);

        /// <summary>物品模板的图标资源引用（<c>display.map.icon_id</c>）；取不到为 null。</summary>
        public Id? IconOfTemplate(Id template)
        {
            var info = DisplayInfo?.Lookup(template);
            return info != null && !string.IsNullOrEmpty(info.IconId) && Id.TryParse(info.IconId, out var id) ? id : (Id?)null;
        }

        /// <summary>物品显示名：<c>item.template.name_key</c> 经 <see cref="L10n"/> 取文案；没有键、没有 L10n 或取不到时退回模板 id 的短名（如 <c>std_bow</c>）。</summary>
        public string ItemName(Id template)
        {
            var row = Registry?.Get("item.template", template);
            if (L10n != null && row != null && row.TryGetId("name_key", out var key))
            {
                var text = L10n.Text(key);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            var value = template.Value;
            var dot = value.LastIndexOf('.');
            return dot >= 0 ? value.Substring(dot + 1) : value;
        }

        /// <summary>物品提示框内容（名称、品质、属性行）；数据里没有该模板返回 null。</summary>
        public ItemTooltipContent? TooltipOf(Id template) =>
            Registry == null ? null : ItemTooltipBuilder.Build(Registry, template, L10n == null ? (Func<Id, string>?)null : key => L10n.Text(key));

        /// <summary>物品模板的品质短名（<c>item.template.quality</c> 去前缀）；没有为空串。</summary>
        public string QualityNameOf(Id template)
        {
            var row = Registry?.Get("item.template", template);
            return row != null && row.TryGetId("quality", out var q) ? EquipmentViewModel.QualityShortName(q) : string.Empty;
        }

        /// <summary>图标精灵：已加载返回精灵；未加载则发起加载并返回 null；加载失败返回 null。</summary>
        public Sprite? Icon(Id? iconId) => iconId.HasValue ? Image(iconId.Value) : null;

        /// <summary>纸娃娃静态层精灵（<c>layer.&lt;集&gt;__&lt;方向&gt;__&lt;层&gt;</c>，文件 <c>sprites/&lt;集&gt;/&lt;方向&gt;/&lt;层&gt;.png</c>）。</summary>
        public Sprite? Layer(string spriteSet, string direction, string layer) =>
            Image(LayerId(spriteSet, direction, layer));

        /// <summary>层资源 id；<paramref name="spriteSet"/> 是 <c>paperdoll.item.x</c> 形式的资源集引用时取去前缀、点号换下划线后的集名。</summary>
        public static Id LayerId(string spriteSet, string direction, string layer) =>
            new Id("layer." + SetName(spriteSet) + "__" + direction + "__" + layer);

        /// <summary>资源集引用 → 精灵集目录名（<c>paperdoll.item.std_sword_1h</c> → <c>item_std_sword_1h</c>；已是集名的原样返回）。</summary>
        public static string SetName(string meshRefOrSet)
        {
            const string prefix = "paperdoll.";
            var name = meshRefOrSet.StartsWith(prefix, StringComparison.Ordinal) ? meshRefOrSet.Substring(prefix.Length) : meshRefOrSet;
            return name.Replace('.', '_');
        }

        private readonly Dictionary<string, Core.Foundation.Common.Json.JsonObject?> _anchorFiles = new Dictionary<string, Core.Foundation.Common.Json.JsonObject?>(StringComparer.Ordinal);

        /// <summary>
        /// 精灵集 <c>sprites/&lt;集&gt;/anchors.json</c> 里 <c>directions.&lt;方向&gt;.&lt;锚点名&gt;</c> 声明的像素锚点（<c>[x, y]</c>，原点左上，单位是该集图片自己的像素）。
        /// 文件缺失、没有该方向/锚点或格式不对一律返回 false（调用方回落到画布居中，行为与未声明时逐位一致）；按集缓存，换皮肤时随缓存一起清掉。
        /// </summary>
        public bool TryGetAnchor(string spriteSet, string direction, string anchor, out Vector2 pixel)
        {
            pixel = default;
            var set = SetName(spriteSet);
            if (!_anchorFiles.TryGetValue(set, out var file))
            {
                file = null;
                var path = System.IO.Path.Combine(UnityResourceLoader.ContentRoot, "sprites", set, "anchors.json");
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        file = Core.Foundation.Common.Json.JsonReader.Parse(System.IO.File.ReadAllText(path)) as Core.Foundation.Common.Json.JsonObject;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[UiVisuals] {path} 解析失败：{ex.Message}");
                    }
                }

                _anchorFiles[set] = file;
            }

            if (file != null
                && file.TryGetValue("directions", out var directions) && directions is Core.Foundation.Common.Json.JsonObject byDirection
                && byDirection.TryGetValue(direction, out var one) && one is Core.Foundation.Common.Json.JsonObject anchors
                && anchors.TryGetValue(anchor, out var value) && value is Core.Foundation.Common.Json.JsonArray pair && pair.Count == 2
                && pair[0] is Core.Foundation.Common.Json.JsonNumber x && pair[1] is Core.Foundation.Common.Json.JsonNumber y)
            {
                pixel = new Vector2((float)x.Value, (float)y.Value);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 运行期切换皮肤（见类型注释"运行期切换皮肤"）。<paramref name="skinRef"/> 为空取占位皮肤；<paramref name="contentRoot"/> 缺省取适配器资源根。
        /// 本方法接管 UiSkin 覆盖：先撤掉当前的，再按新皮肤包装（占位皮肤不装）。
        /// </summary>
        public void SwitchSkin(string? skinRef, string? contentRoot = null)
        {
            var loader = Loader;
            foreach (var id in _requested)
            {
                if (loader.GetLoadProgress(id) < 1.0 && loader.GetLoadProgress(id) > 0.0)
                {
                    _stalePending.Add(id);      // 在途：完成回调里卸掉，期间不再发起同 id 的新请求
                }
                else
                {
                    loader.Unload(id);
                }
            }

            _requested.Clear();
            _failed.Clear();
            _anchorFiles.Clear();
            loader.InvalidateSpriteSetAnchorsCache();
            _generation++;

            UiSkin.Reset();
            _retiredPacks.Add(Pack);
            Pack = UiSkinPack.Load(skinRef, contentRoot);
            var skinOverride = Pack.CreateOverride();
            if (skinOverride != null)
            {
                UiSkin.Install(skinOverride);
                _overrideInstalledBySwitch = true;
            }
            else
            {
                _overrideInstalledBySwitch = false;
            }

            SkinChanged?.Invoke();
        }

        private Sprite? Image(Id id)
        {
            if (_stalePending.Contains(id))
            {
                return null;
            }

            var loader = Loader;
            if (loader.TryGetSprite(id, out var sprite) && sprite != null)
            {
                return sprite;
            }

            if (_requested.Add(id))
            {
                var generation = _generation;
                try
                {
                    loader.LoadAsync(id, ResourceKind.Image, (rid, ok) =>
                    {
                        if (generation != _generation)
                        {
                            // 切换皮肤之前发起的请求现在才完成：它读的是旧根，立刻卸掉，下次取用重新请求。
                            loader.Unload(rid);
                            _stalePending.Remove(rid);
                            return;
                        }

                        if (!ok)
                        {
                            _failed.Add(rid);
                        }
                    });
                }
                catch (FormatException)
                {
                    // 形状不合约定的 id（例如 icon 类别但不是 icon.<类别>.<名>）：记为失败，不再请求。
                    _failed.Add(id);
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_overrideInstalledBySwitch)
            {
                UiSkin.Reset();
                _overrideInstalledBySwitch = false;
            }

            foreach (var retired in _retiredPacks)
            {
                retired.Dispose();
            }

            _retiredPacks.Clear();
            Pack.Dispose();
        }
    }
}

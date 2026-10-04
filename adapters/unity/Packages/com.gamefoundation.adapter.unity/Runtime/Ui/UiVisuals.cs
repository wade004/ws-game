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
using System;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
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
        private UnityResourceLoader? _loader;

        public UiSkinPack Pack { get; }

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

        private Sprite? Image(Id id)
        {
            var loader = Loader;
            if (loader.TryGetSprite(id, out var sprite) && sprite != null)
            {
                return sprite;
            }

            if (_requested.Add(id))
            {
                try
                {
                    loader.LoadAsync(id, ResourceKind.Image, (rid, ok) =>
                    {
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

        public void Dispose() => Pack.Dispose();
    }
}

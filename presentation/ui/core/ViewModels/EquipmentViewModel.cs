using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;

namespace Presentation.Ui
{
    /// <summary>装备面板一个槽位的快照（手感设计/08 第 3 节"装备面板"：槽位网格 + 纸娃娃预览）。</summary>
    public readonly struct EquipmentSlotSnapshot
    {
        /// <summary>槽位 id（<c>item.slot_definition</c> 行 id）。</summary>
        public Id SlotId { get; }

        /// <summary>槽位短名（槽位 id 去掉 <c>item.slot.</c> 前缀；皮肤包 <c>slot_frame/&lt;短名&gt;.png</c> 按它命名）。</summary>
        public string SlotName { get; }

        /// <summary>槽位本地化名键（<c>item.slot_definition.name_key</c>，没有为 null）。</summary>
        public Id? NameKey { get; }

        public int SortWeight { get; }

        public bool IsWeapon { get; }

        /// <summary>装着的物品实例 id；空槽为 null。</summary>
        public Id? InstanceId { get; }

        /// <summary>装着的物品模板 id；空槽为 null。</summary>
        public Id? TemplateId { get; }

        /// <summary>物品品质短名（<c>item.template.quality</c> 去掉 <c>item.quality.</c> 前缀；皮肤包 <c>quality_frame/&lt;短名&gt;.png</c>
        /// 按它命名）；空槽或模板没有品质为空串。</summary>
        public string QualityName { get; }

        /// <summary>物品名本地化键（<c>item.template.name_key</c>）。</summary>
        public Id? ItemNameKey { get; }

        /// <summary>物品图标资源引用（<c>display.map.icon_id</c>，经 <c>item.template.display_ref</c> 对应的外形行）；缺失为 null。</summary>
        public Id? IconId { get; }

        public bool Occupied => TemplateId.HasValue;

        public EquipmentSlotSnapshot(
            Id slotId, string slotName, Id? nameKey, int sortWeight, bool isWeapon,
            Id? instanceId, Id? templateId, string qualityName, Id? itemNameKey, Id? iconId)
        {
            SlotId = slotId;
            SlotName = slotName;
            NameKey = nameKey;
            SortWeight = sortWeight;
            IsWeapon = isWeapon;
            InstanceId = instanceId;
            TemplateId = templateId;
            QualityName = qualityName;
            ItemNameKey = itemNameKey;
            IconId = iconId;
        }
    }

    /// <summary>纸娃娃预览里一件装备贡献的一个图层（<c>display.equip_visual</c> 的 <c>slot_mesh</c> 型 sprite 行）。</summary>
    public readonly struct EquipmentPaperdollLayer
    {
        /// <summary>图层名（<c>equip_visual.slot_id</c> 去掉 <c>slot.</c> 前缀，如 <c>hand_main</c>）。</summary>
        public string Layer { get; }

        /// <summary>层资源集引用（<c>equip_visual.mesh_ref</c>，<c>paperdoll.</c> 前缀，ADR-0071）。</summary>
        public Id MeshRef { get; }

        public Id ItemId { get; }

        /// <summary>该装备占用的装备槽位。</summary>
        public Id SlotId { get; }

        /// <summary>装备外观行声明的预览方向档（<c>preview_direction</c>，如 <c>dir.front_side_r</c>）；缺省为 null。</summary>
        public Id? PreviewDirection { get; }

        /// <summary>该层画在身体后面的方向档（<c>behind_directions</c>，方向槽位 id，如 <c>dir.back</c>）；缺省空 = 所有方向现行顺序。</summary>
        public IReadOnlyList<Id> BehindDirections { get; }

        public EquipmentPaperdollLayer(string layer, Id meshRef, Id itemId, Id slotId, Id? previewDirection)
            : this(layer, meshRef, itemId, slotId, previewDirection, null)
        {
        }

        public EquipmentPaperdollLayer(string layer, Id meshRef, Id itemId, Id slotId, Id? previewDirection, IReadOnlyList<Id>? behindDirections)
        {
            BehindDirections = behindDirections ?? Array.Empty<Id>();
            Layer = layer;
            MeshRef = meshRef;
            ItemId = itemId;
            SlotId = slotId;
            PreviewDirection = previewDirection;
        }
    }

    /// <summary>
    /// 装备面板视图模型（手感设计/08 第 3 节、ADR-0149）：槽位网格（数据里全部 <c>is_equipment</c> 槽位，按 <c>sort_weight</c>
    /// 再按 id 排序）与纸娃娃预览的装备层清单。槽位清单与每件装备的外观完全来自数据（<c>item.slot_definition</c>、<c>item.template</c>、
    /// <c>display.equip_visual</c>、<c>display.map</c>），不需要游戏配置槽位 id 清单；装备实例经 <c>player.equipment.&lt;槽位&gt;.*</c>
    /// 只读查询，穿脱事件与读档后整体重建，惯例同 <see cref="InventoryViewModel"/>。
    /// <para>
    /// 判断记录（预览图层数 = 有 sprite 外观的已装备槽位数）：装备面板里"纸娃娃预览"的装备层只来自 <c>display.equip_visual</c>
    /// 里 <c>slot_mesh</c> 模式、<c>mesh_ref</c> 带 <c>paperdoll.</c> 前缀（sprite 型，ADR-0038/0071）的行；<c>model</c> 型
    /// （<c>socket_attach</c> 或非 <c>paperdoll.</c> 前缀的网格引用）不是 2D 图层，不进 <see cref="PaperdollLayers"/>，计入
    /// <see cref="NonSpriteVisualCount"/>（模型预览是 3D 宿主的事）。因此 <c>PaperdollLayers.Count + NonSpriteVisualCount +
    /// 无外观数 = 已装备槽位数</c>，面板与纸娃娃的数量由数据算出、互相可核对。
    /// </para>
    /// <para>
    /// 判断记录（装备槽位清单取自数据而不取自 <c>PresentationOptions.EquipmentSlotIds</c>）：该选项缺省为空，缺省装配下
    /// <see cref="InventoryViewModel.EquippedSlots"/> 恒空；装备面板要"数据一填就能用"，所以槽位清单直接读
    /// <c>item.slot_definition</c>，不改既有选项的缺省语义（缺省逐位不变）。
    /// </para>
    /// </summary>
    public sealed class EquipmentViewModel : IDisposable
    {
        private const string SlotIdPrefix = "item.slot.";
        private const string QualityIdPrefix = "item.quality.";
        private const string LayerSlotPrefix = "slot.";
        private const string PaperdollMeshPrefix = "paperdoll.";

        private readonly IUiDataSource _dataSource;
        private readonly IDataRegistryView _registry;
        private readonly IDisplayInfoRegistry? _displayInfo;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<EquipmentSlotSnapshot> _slots = new List<EquipmentSlotSnapshot>();
        private readonly List<EquipmentPaperdollLayer> _layers = new List<EquipmentPaperdollLayer>();

        /// <summary>全部装备槽位（含空槽），按 <c>sort_weight</c> 再按 id 升序。</summary>
        public IReadOnlyList<EquipmentSlotSnapshot> Slots => _slots;

        /// <summary>已装备物品贡献的纸娃娃图层，顺序同 <see cref="Slots"/>。</summary>
        public IReadOnlyList<EquipmentPaperdollLayer> PaperdollLayers => _layers;

        /// <summary>已装备的槽位数。</summary>
        public int OccupiedCount { get; private set; }

        /// <summary>已装备但外观不是 2D 图层（<c>model</c> 型）的槽位数。</summary>
        public int NonSpriteVisualCount { get; private set; }

        /// <summary>已装备但没有 <c>display.equip_visual</c> 行的槽位数（无外观槽位，如戒指）。</summary>
        public int NoVisualCount { get; private set; }

        public EquipmentViewModel(IUiDataSource dataSource, IDataRegistryView registry, IDisplayInfoRegistry? displayInfo)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _displayInfo = displayInfo;

            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemEquipped, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemUnequipped, OnRelevantEvent));
            // 同 InventoryViewModel（UI-111-01）：读档抑制作用域会压住穿脱事件本身，save.loaded 在作用域外正常派发。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        /// <summary>槽位 id 去掉 <c>item.slot.</c> 前缀（没有该前缀取最后一个点分段），同导入工具 <c>skin_pack.py</c> 的命名规则。</summary>
        public static string SlotShortName(Id slotId) => NameAfter(slotId.Value, SlotIdPrefix);

        /// <summary>品质 id 去掉 <c>item.quality.</c> 前缀（没有该前缀取最后一个点分段）。</summary>
        public static string QualityShortName(Id qualityId) => NameAfter(qualityId.Value, QualityIdPrefix);

        private static string NameAfter(string value, string prefix)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value.Substring(prefix.Length);
            }

            var dot = value.LastIndexOf('.');
            return dot >= 0 ? value.Substring(dot + 1) : value;
        }

        public void Refresh()
        {
            _slots.Clear();
            _layers.Clear();
            OccupiedCount = 0;
            NonSpriteVisualCount = 0;
            NoVisualCount = 0;

            var slotRows = new List<DataRecord>();
            foreach (var row in _registry.GetAll("item.slot_definition"))
            {
                if (row.TryGetBool("is_equipment", out var isEquipment) && isEquipment)
                {
                    slotRows.Add(row);
                }
            }

            var ordered = slotRows
                .Select(r => (Row: r, Weight: r.TryGetInt("sort_weight", out var w) ? (int)w : 0))
                .OrderBy(x => x.Weight)
                .ThenBy(x => x.Row.Key, StringComparer.Ordinal);

            var visuals = new Dictionary<string, DataRecord>(StringComparer.Ordinal);
            foreach (var row in _registry.GetAll("display.equip_visual"))
            {
                if (row.TryGetString("item_id", out var itemId) && !visuals.ContainsKey(itemId))
                {
                    visuals[itemId] = row;
                }
            }

            foreach (var (row, weight) in ordered)
            {
                var slotId = new Id(row.Key);
                var isWeapon = row.TryGetBool("is_weapon", out var w) && w;
                var nameKey = row.TryGetId("name_key", out var nk) ? (Id?)nk : null;

                Id? instance = null;
                Id? template = null;
                var equipped = _dataSource.Query($"player.equipment.{slotId}");
                if (equipped.HasValue)
                {
                    instance = equipped.Value.AsId;
                    var templateValue = _dataSource.Query($"player.equipment.{slotId}.template");
                    if (templateValue.HasValue)
                    {
                        template = templateValue.Value.AsId;
                    }
                }

                var quality = string.Empty;
                Id? itemNameKey = null;
                Id? icon = null;
                if (template.HasValue)
                {
                    var item = _registry.Get("item.template", template.Value);
                    if (item != null)
                    {
                        if (item.TryGetId("quality", out var q))
                        {
                            quality = QualityShortName(q);
                        }

                        if (item.TryGetId("name_key", out var itemName))
                        {
                            itemNameKey = itemName;
                        }
                    }

                    var info = _displayInfo?.Lookup(template.Value);
                    if (info != null && !string.IsNullOrEmpty(info.IconId) && Id.TryParse(info.IconId, out var iconId))
                    {
                        icon = iconId;
                    }

                    OccupiedCount++;
                    ClassifyVisual(visuals, template.Value, slotId);
                }

                _slots.Add(new EquipmentSlotSnapshot(slotId, SlotShortName(slotId), nameKey, weight, isWeapon, instance, template, quality, itemNameKey, icon));
            }
        }

        private void ClassifyVisual(Dictionary<string, DataRecord> visuals, Id template, Id slotId)
        {
            if (!visuals.TryGetValue(template.Value, out var visual))
            {
                NoVisualCount++;
                return;
            }

            var isSlotMesh = visual.TryGetString("mode", out var mode) && string.Equals(mode, "slot_mesh", StringComparison.Ordinal);
            if (isSlotMesh
                && visual.TryGetId("mesh_ref", out var mesh)
                && mesh.Value.StartsWith(PaperdollMeshPrefix, StringComparison.Ordinal)
                && visual.TryGetString("slot_id", out var layerSlot))
            {
                var layer = layerSlot.StartsWith(LayerSlotPrefix, StringComparison.Ordinal) ? layerSlot.Substring(LayerSlotPrefix.Length) : layerSlot;
                var preview = visual.TryGetId("preview_direction", out var dir) ? (Id?)dir : null;
                var behind = visual.TryGetIdList("behind_directions", out var behindList) ? behindList : null;
                _layers.Add(new EquipmentPaperdollLayer(layer, mesh, template, slotId, preview, behind));
                return;
            }

            NonSpriteVisualCount++;
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}

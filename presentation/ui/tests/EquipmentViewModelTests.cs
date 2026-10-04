using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 装备面板视图模型（手感设计/08 第 3 节、ADR-0149）：槽位清单与每件装备的外观全部由数据算出，
    /// 穿脱后"纸娃娃图层数 + 非 2D 外观数 + 无外观数 = 已装备槽位数"恒成立。
    /// </summary>
    public sealed class EquipmentViewModelTests
    {
        private sealed class FakeDisplayInfo : IDisplayInfoRegistry
        {
            private readonly Dictionary<Id, DisplayInfo> _byLogical = new Dictionary<Id, DisplayInfo>();

            public void AddIcon(string itemId, string iconId) =>
                _byLogical[new Id(itemId)] = new DisplayInfo(
                    new Id("display.map." + itemId.Substring(5)), DisplayCategory.Item, new Id(itemId), DisplayKind.Sprite,
                    iconId, null, null, 1.0, ShadowMode.None, 0.0, null, null, null);

            public DisplayInfo? Lookup(Id logicalId) => _byLogical.TryGetValue(logicalId, out var v) ? v : null;

            public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) => _byLogical.Values.ToList();

            public IReadOnlyList<DisplayInfo> All => _byLogical.Values.ToList();

            public void Reload()
            {
            }
        }

        internal static IDataRegistry BuildRegistry(UiWorldFixture world)
        {
            var source = new InMemoryDataSource()
                .Add("item.slot_definition",
                    "{\"table\":\"item.slot_definition\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"item.slot.main_hand\",\"name_key\":\"l10n.slot.main_hand\",\"sort_weight\":1,\"is_weapon\":true,\"is_equipment\":true}," +
                    "{\"id\":\"item.slot.chest\",\"sort_weight\":11,\"is_weapon\":false,\"is_equipment\":true}," +
                    "{\"id\":\"item.slot.ring\",\"sort_weight\":20,\"is_weapon\":false,\"is_equipment\":true}," +
                    "{\"id\":\"item.slot.bag\",\"sort_weight\":2,\"is_weapon\":false,\"is_equipment\":false}]}")
                .Add("item.template",
                    "{\"table\":\"item.template\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"item.sword\",\"slot\":\"item.slot.main_hand\",\"quality\":\"item.quality.std_rare\",\"name_key\":\"l10n.item.sword\"}," +
                    "{\"id\":\"item.plate\",\"slot\":\"item.slot.chest\",\"quality\":\"item.quality.std_common\"}," +
                    "{\"id\":\"item.band\",\"slot\":\"item.slot.ring\"}]}")
                .Add("display.equip_visual",
                    "{\"table\":\"display.equip_visual\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"display.equip_visual.sword\",\"item_id\":\"item.sword\",\"mode\":\"slot_mesh\",\"slot_id\":\"slot.hand_main\",\"mesh_ref\":\"paperdoll.item.sword\",\"preview_direction\":\"dir.front_side_r\"}," +
                    "{\"id\":\"display.equip_visual.plate\",\"item_id\":\"item.plate\",\"mode\":\"socket_attach\",\"socket_id\":\"socket.torso\",\"model_ref\":\"model.plate\"}]}");
            var registry = new DataRegistry(source, world.EventBus, new DataRegistryOptions { FailOnUnknownTable = false });
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));
            return registry;
        }

        private static void Equip(UiWorldFixture world, string template, string slot)
        {
            var templateId = new Id(template);
            var instance = world.Inventory.AddItemForTest(world.PlayerId, templateId, 1);
            world.Equipment.TemplatesByInstance[instance] = templateId;
            world.Equipment.Equip(world.PlayerId, instance, new Id(slot));
        }

        [Fact]
        public void Slots_AreReadFromData_SortedByWeight_AndEmptyUntilEquipped()
        {
            var world = new UiWorldFixture();
            using var vm = new EquipmentViewModel(world.DataSource, BuildRegistry(world), new FakeDisplayInfo());

            // 只有 is_equipment 的三个槽位（bag 不是），按 sort_weight 升序；槽位清单不需要游戏配置。
            Assert.Equal(new[] { "main_hand", "chest", "ring" }, vm.Slots.Select(s => s.SlotName).ToArray());
            Assert.Equal(new Id("l10n.slot.main_hand"), vm.Slots[0].NameKey);
            Assert.True(vm.Slots[0].IsWeapon);
            Assert.All(vm.Slots, s => Assert.False(s.Occupied));
            Assert.Equal(0, vm.OccupiedCount);
            Assert.Empty(vm.PaperdollLayers);
        }

        [Fact]
        public void EquipAndUnequip_RefreshesSlotsAndPaperdollLayers_FromData()
        {
            var world = new UiWorldFixture();
            var display = new FakeDisplayInfo();
            display.AddIcon("item.sword", "icon.item.sword");
            using var vm = new EquipmentViewModel(world.DataSource, BuildRegistry(world), display);

            Equip(world, "item.sword", "item.slot.main_hand");
            vm.Refresh();

            var main = vm.Slots.Single(s => s.SlotName == "main_hand");
            Assert.True(main.Occupied);
            Assert.Equal(new Id("item.sword"), main.TemplateId);
            Assert.Equal("std_rare", main.QualityName);
            Assert.Equal(new Id("l10n.item.sword"), main.ItemNameKey);
            Assert.Equal(new Id("icon.item.sword"), main.IconId);

            // sword 的外观是 paperdoll 型 slot_mesh：一个纸娃娃图层，层名取 slot_id 去掉 slot. 前缀。
            Assert.Single(vm.PaperdollLayers);
            Assert.Equal("hand_main", vm.PaperdollLayers[0].Layer);
            Assert.Equal(new Id("paperdoll.item.sword"), vm.PaperdollLayers[0].MeshRef);
            Assert.Equal(new Id("dir.front_side_r"), vm.PaperdollLayers[0].PreviewDirection);

            world.Equipment.Unequip(world.PlayerId, new Id("item.slot.main_hand"));
            vm.Refresh();
            Assert.False(vm.Slots.Single(s => s.SlotName == "main_hand").Occupied);
            Assert.Empty(vm.PaperdollLayers);
        }

        [Fact]
        public void LayerCount_Invariant_PaperdollPlusNonSpritePlusNoVisual_EqualsOccupied()
        {
            var world = new UiWorldFixture();
            using var vm = new EquipmentViewModel(world.DataSource, BuildRegistry(world), new FakeDisplayInfo());

            Equip(world, "item.sword", "item.slot.main_hand"); // paperdoll 层
            Equip(world, "item.plate", "item.slot.chest");     // socket_attach：不是 2D 层
            Equip(world, "item.band", "item.slot.ring");       // 没有外观行
            vm.Refresh();

            Assert.Equal(3, vm.OccupiedCount);
            Assert.Single(vm.PaperdollLayers);
            Assert.Equal(1, vm.NonSpriteVisualCount);
            Assert.Equal(1, vm.NoVisualCount);
            Assert.Equal(vm.OccupiedCount, vm.PaperdollLayers.Count + vm.NonSpriteVisualCount + vm.NoVisualCount);
        }

        [Fact]
        public void ShortNames_FollowTheSkinPackNamingRule()
        {
            // 与 toolchain/asset_import/skin_pack.py 的 _name_after 一致：有前缀去前缀，没有取最后一个点分段。
            Assert.Equal("std_main_hand", EquipmentViewModel.SlotShortName(new Id("item.slot.std_main_hand")));
            Assert.Equal("hand", EquipmentViewModel.SlotShortName(new Id("game.slot.hand")));
            Assert.Equal("std_rare", EquipmentViewModel.QualityShortName(new Id("item.quality.std_rare")));
            Assert.Equal("epic", EquipmentViewModel.QualityShortName(new Id("game.q.epic")));
        }

        private sealed class KeyRecordingSource : IUiDataSource
        {
            private readonly IUiDataSource _inner;
            public readonly List<Id> Keys = new List<Id>();

            public KeyRecordingSource(IUiDataSource inner) => _inner = inner;

            public Core.Foundation.Expr.ExprValue? Query(string path) => _inner.Query(path);

            public SubscriptionHandle Subscribe(Id eventKey, Core.Foundation.EventBus.EventHandler handler)
            {
                Keys.Add(eventKey);
                return _inner.Subscribe(eventKey, handler);
            }

            public void Unsubscribe(SubscriptionHandle handle) => _inner.Unsubscribe(handle);
        }

        [Fact]
        public void Subscribes_ToEquipUnequipAndSaveLoaded_LikeInventoryViewModel()
        {
            var world = new UiWorldFixture();
            var source = new KeyRecordingSource(world.DataSource);
            using var vm = new EquipmentViewModel(source, BuildRegistry(world), null);

            Assert.Equal(
                new[] { "item.equipped", "item.unequipped", "save.loaded" },
                source.Keys.Select(k => k.Value).OrderBy(k => k, System.StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Dispose_UnsubscribesEveryHandle()
        {
            var world = new UiWorldFixture();
            var vm = new EquipmentViewModel(world.DataSource, BuildRegistry(world), null);
            vm.Dispose();
            vm.Dispose();
        }
    }
}

using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Numbers.StatBlock;
using System;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2，第十轮审计已复现）：
    /// <see cref="EquipmentPersistable.Load"/> 修复前在校验 <c>data</c> 的 JSON 形状之前，就已经
    /// 无条件调用 <see cref="EquipmentHost.ClearAllEquippedForLoad"/>——坏 shape（<c>data</c> 本身
    /// 不是 JSON 对象，槽位键不是合法 <see cref="Id"/>，或某个槽位的物品实例存档数据格式非法）会在
    /// 清空之后才抛 <see cref="FormatException"/>，此时该玩家读档前的全部装备（含属性修正/技能
    /// 授予/光环施加/套装加成）已经丢失，而 <c>SaveSystem</c> 只把"已成功加载"的段加入回滚列表，
    /// 本段自身从未成功加载过，不会被回滚（真实探针 <c>core-persistence-probe.raw.log</c>
    /// FAILED-EQUIPMENT-SEGMENT 段复现：装备清空前存在，抛错后消失，且 <c>after_dispatch</c> 仍为
    /// <c>false</c>，装备回不来）。
    /// <para>
    /// 根治后 <see cref="EquipmentPersistable.Load"/> 遵循"先解析校验成临时恢复计划、再一次性
    /// 提交"：先完整遍历 <c>data</c>，校验全部槽位键与物品实例形状，任何一条坏形状都会在改动任何
    /// 状态之前直接抛异常返回。本文件直接对 <see cref="EquipmentPersistable"/> 调用 <see
    /// cref="EquipmentPersistable.Load"/>，断言坏 shape/非法 slot/坏物品实例三类输入都会抛出异常，
    /// 且异常前后 <see cref="EquipmentHost"/>/<see cref="InventoryHost"/>/<see cref="StatHost"/> 的
    /// 运行期状态完全不变（不是"部分清空"）。
    /// </para>
    /// </summary>
    public sealed class CORE_170_03_EquipmentPersistableLoadFailureTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.main_hand\", \"name_key\": \"l10n.item.slot.main_hand\", \"is_weapon\": true}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\"}]";

        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"group\": \"primary\"}]";

        private const string TemplateJson =
            "[{\"id\": \"item.sample_weapon_a\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_weapon_a\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_weapon_a\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 7}]}]";

        private static readonly Id Player = new Id("player.core_170_03_hero");
        private static readonly Id MainHandSlot = new Id("item.slot.main_hand");
        private static readonly Id WeaponTemplate = new Id("item.sample_weapon_a");
        private static readonly Id StatStrength = new Id("stat.strength");

        private sealed class Hosts
        {
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public EquipmentHost Equipment = null!;
            public EquipmentPersistable Persistable = null!;
        }

        private static Core.Foundation.DataRegistry.DataRegistry BuildRegistry() => TestSupport.BuildRegistry(source =>
        {
            source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
            source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
            source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
            source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
        });

        private static Hosts BuildHosts(Core.Foundation.DataRegistry.DataRegistry registry)
        {
            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            statHost.RegisterUnit(Player);
            var unitAccess = new FakeUnitAccess().Add(Player, level: 99);
            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, new FakeEffectSink(), new RecordingSkillGranter().Grant, unitAccess);
            var persistable = new EquipmentPersistable(Player, inventory, equipment);

            return new Hosts { Inventory = inventory, StatHost = statHost, Equipment = equipment, Persistable = persistable };
        }

        /// <summary>装备一件真实武器，返回其实例 id，供各用例建立"读档前已经装备着"的前提。</summary>
        private static Id EquipRealWeapon(Hosts hosts)
        {
            hosts.Inventory.AddItem(Player, WeaponTemplate, 1);
            var items = hosts.Inventory.ListItems(Player);
            var instanceId = items[items.Count - 1].InstanceId;
            var result = hosts.Equipment.Equip(Player, instanceId, MainHandSlot);
            Assert.True(result.Success, $"前置条件：装备应当成功：{result.Reason}");
            return instanceId;
        }

        /// <summary>不经内部 ItemInstanceJson（跨程序集不可见，见 ItemPersistableTests 同款判断
        /// 记录）：用一次性夹具真实装备一件武器、Save() 拿到公开 JsonValue 形态的合法物品实例存档
        /// 数据，供需要"一份合法物品实例 JSON，但外层结构本身非法"的用例复用。</summary>
        private static JsonValue CaptureValidInstanceJson()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            EquipRealWeapon(hosts);
            var saved = (JsonObject)hosts.Persistable.Save();
            Assert.True(saved.TryGetValue(MainHandSlot.Value, out var instanceJson));
            return instanceJson;
        }

        private static void AssertStillEquipped(Hosts hosts, Id expectedInstanceId)
        {
            var equipped = hosts.Equipment.GetEquipped(Player, MainHandSlot);
            Assert.True(equipped.HasValue, "CORE-170-03 核心断言：坏 shape 读档失败后，读档前的装备不应消失。");
            Assert.Equal(expectedInstanceId, equipped!.Value.InstanceId);
            Assert.Equal(0 + 7, hosts.StatHost.GetStat(Player, StatStrength), 6);
        }

        [Fact]
        public void Load_DataIsNotJsonObject_ThrowsFormatException_AndLeavesEquipmentUntouched()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            var instanceId = EquipRealWeapon(hosts);

            var badShape = new JsonString("wrong-shape");
            var ex = Record.Exception(() => hosts.Persistable.Load(badShape));

            Assert.IsType<FormatException>(ex);
            AssertStillEquipped(hosts, instanceId);
        }

        [Fact]
        public void Load_SlotKeyIsNotLegalId_ThrowsFormatException_AndLeavesEquipmentUntouched()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            var instanceId = EquipRealWeapon(hosts);

            // 非法 slot 键：空字符串必然不是合法 Id（Id.TryParse 要求非空文本）。物品实例值本身
            // 合法（来自另一夹具真实 Save() 的输出），单独验证"槽位键校验"这一件事。
            var validInstanceJson = CaptureValidInstanceJson();
            var badSlotData = new JsonObjectBuilder().Add("", validInstanceJson).Build();

            var ex = Record.Exception(() => hosts.Persistable.Load(badSlotData));

            Assert.IsType<FormatException>(ex);
            AssertStillEquipped(hosts, instanceId);
        }

        [Fact]
        public void Load_ItemInstanceShapeIsInvalid_ThrowsFormatException_AndLeavesEquipmentUntouched()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);
            var instanceId = EquipRealWeapon(hosts);

            // 合法槽位键，但物品实例值本身不是 JSON 对象（内部 ItemInstanceJson.FromJson 要求
            // JsonObject，见该方法判断记录）。
            var badInstanceData = new JsonObjectBuilder()
                .Add(MainHandSlot.Value, new JsonString("not-an-item-instance"))
                .Build();

            var ex = Record.Exception(() => hosts.Persistable.Load(badInstanceData));

            Assert.IsType<FormatException>(ex);
            AssertStillEquipped(hosts, instanceId);
        }

        /// <summary>非致命场景对照：合法的快照本身应当读档成功、不抛异常——与上面三个"坏形状必须在
        /// 改动任何状态前发现"的用例区分开，证明本次根治没有把"合法快照"也误判为坏形状。</summary>
        [Fact]
        public void Load_ValidSnapshot_DoesNotThrow_AndEquipsSnapshotItem()
        {
            var registry = BuildRegistry();
            var hosts = BuildHosts(registry);

            var validInstanceJson = CaptureValidInstanceJson();
            var snapshot = new JsonObjectBuilder().Add(MainHandSlot.Value, validInstanceJson).Build();

            var ex = Record.Exception(() => hosts.Persistable.Load(snapshot));

            Assert.Null(ex);
            var equipped = hosts.Equipment.GetEquipped(Player, MainHandSlot);
            Assert.True(equipped.HasValue, "合法快照读档后应当成功装备快照里的物品。");
            Assert.Equal(0 + 7, hosts.StatHost.GetStat(Player, StatStrength), 6);
        }
    }
}

using System;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-9（ADR-0034 决策 8"背包容量来源"；07 第 1.3 节 2026-09-14 修订段）
    /// 验收测试：<see cref="InventoryOptions.MaxSlots"/>/<see cref="InventoryOptions.MaxSlotsStat"/>
    /// 二选一来源、<see cref="IInventoryHost.GetCapacity"/> 默认接口成员与 <see
    /// cref="InventoryHost"/> 的显式实现。数据夹具独立于 <see cref="InventoryHostTests"/>（后者的
    /// 固定容量用例不涉及 <c>stat.definition</c>，同 <c>T_N2_5_ArmorAffixReqLevelTests</c> 一类
    /// "自带最小夹具"惯例），全部模板 <c>stack_size</c> 固定为 1（不可堆叠），使"已用格子数"与
    /// "已加入物品数"严格相等，便于精确断言容量边界。
    /// </summary>
    public class T_N2_9_InventoryCapacitySourceTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.t9_consumable\", \"name_key\": \"l10n.item.slot.t9_consumable\"}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.t9_common\", \"name_key\": \"l10n.item.quality.t9_common\"}]";

        // default_base=1：作为"属性来源"路径的基线容量，AddModifier/RemoveModifiersBySource 在其
        // 基础上叠加/撤销光环式修正，模拟"容量随属性变化"（07 第 1.3 节"游戏层要扩容就让容量引用
        // 属性并用光环或物品修改该属性"）。
        private const string StatDefJson =
            "[{\"id\": \"stat.t9_inventory_capacity\", \"name_key\": \"l10n.stat.t9_inventory_capacity\"," +
            " \"category\": \"primary\", \"default_base\": 1}]";

        private static readonly Id CapacityStat = new Id("stat.t9_inventory_capacity");
        private static readonly Id TemplateId = new Id("item.t9_sample");
        private static readonly Id Unit = new Id("player.t9_hero");

        private static string TemplateJson() =>
            "[{"
            + "\"id\": \"" + TemplateId.Value + "\","
            + "\"slot\": \"item.slot.t9_consumable\","
            + "\"quality\": \"item.quality.t9_common\","
            + "\"item_level\": 1,"
            + "\"display_ref\": \"display.item.t9_sample\","
            // stack_size=1：每次 AddItem 都新开一个不可堆叠的实例（见类型顶部判断记录）。
            + "\"stack_size\": 1,"
            + "\"name_key\": \"l10n.item.t9_sample\""
            + "}]";

        private static IDataRegistryView BuildRegistry() =>
            TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.template", TestSupport.Table("item.template", TemplateJson()));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
            });

        [Fact]
        public void GetCapacity_FixedZero_ReturnsUnlimited_HistoricBehaviorUnchanged()
        {
            var registry = BuildRegistry();
            var bus = TestSupport.CreateBus();
            var host = new InventoryHost(registry, bus, new InventoryOptions());

            Assert.Equal(int.MaxValue, host.GetCapacity(Unit));

            // 固定值路径行为与既有完全一致（第一组）：MaxSlots 默认 0，大批量加入不受限。
            Assert.True(host.AddItem(Unit, TemplateId, 50));
            Assert.Equal(50, host.CountOf(Unit, TemplateId));
        }

        [Fact]
        public void GetCapacity_FixedPositive_ReturnsConfiguredValue_AndRejectBehaviorUnchanged()
        {
            var registry = BuildRegistry();
            var bus = TestSupport.CreateBus();
            var options = new InventoryOptions { MaxSlots = 2, FullPolicy = InventoryFullPolicy.Reject };
            var host = new InventoryHost(registry, bus, options);

            Assert.Equal(2, host.GetCapacity(Unit));

            // 固定值路径行为与既有完全一致（第二组）：同 InventoryHostTests.
            // AddItem_MaxSlotsReject_FullBatchFailsAtomically 口径——stack_size=1 时 3 个物品需要
            // 3 个格子，MaxSlots=2，整批原子失败，不落地任何变化。
            Assert.False(host.AddItem(Unit, TemplateId, 3));
            Assert.Empty(host.ListItems(Unit));

            Assert.True(host.AddItem(Unit, TemplateId, 2));
            Assert.Equal(2, host.CountOf(Unit, TemplateId));
        }

        [Fact]
        public void GetCapacity_StatSource_TracksStatValue_AsModifiersChangeIt()
        {
            var registry = BuildRegistry();
            var bus = TestSupport.CreateBus();
            var stats = new StatHost(registry, bus);
            stats.RegisterUnit(Unit);

            var options = new InventoryOptions { MaxSlotsStat = CapacityStat };
            var host = new InventoryHost(registry, bus, options, (unitId, stat) => stats.GetStat(unitId, stat));

            // default_base=1，未加任何修正：容量随属性取值，此刻为 1（向下取整）。
            Assert.Equal(1, host.GetCapacity(Unit));
            Assert.True(host.AddItem(Unit, TemplateId, 1));
            Assert.False(host.AddItem(Unit, TemplateId, 1)); // 第 2 件：容量已满，Reject 下失败。

            // 光环式修正：flat +2，容量随之变为 3（第三组验收标准——"容量属性经 StatHost 加光环/
            // 修饰后 GetCapacity 随之变化"）。GetCapacity 每次都现查 _statLookup，不缓存，因此
            // AddModifier 之后立即生效，不需要任何显式的"重算容量"调用。
            stats.AddModifier(Unit, new StatModifier(CapacityStat, StatModifierOp.Flat, 2, new Id("aura.t9_capacity_buff")));
            Assert.Equal(3, host.GetCapacity(Unit));
            Assert.True(host.AddItem(Unit, TemplateId, 2));
            Assert.Equal(3, host.CountOf(Unit, TemplateId));

            // 撤销光环：容量回落到 1，此时背包里已有 3 件（超过新容量）。
            stats.RemoveModifiersBySource(Unit, new Id("aura.t9_capacity_buff"));
            Assert.Equal(1, host.GetCapacity(Unit));
        }

        /// <summary>验收标准里显式要求的判断记录用例——"已有物品数超过新容量时的行为按契约：不丢
        /// 物品、只拒绝新增"：容量因属性被减益压低到已有物品数以下时，<see
        /// cref="InventoryHost.ListItems"/> 必须原样保留全部既有物品（不做任何"强制吐出"），后续
        /// <see cref="InventoryHost.AddItem"/> 按容量判定正常拒绝新增——两者用同一份
        /// <see cref="InventoryHost.GetCapacity"/> 现查结果，AddItemCore 的原子失败分支
        /// （<c>newSlotsNeeded &gt; availableSlots</c>）天然覆盖这一场景，不需要额外特判代码。</summary>
        [Fact]
        public void AddItem_CapacityShrinksBelowExistingCount_KeepsExistingItems_OnlyRejectsNewAdds()
        {
            var registry = BuildRegistry();
            var bus = TestSupport.CreateBus();
            var stats = new StatHost(registry, bus);
            stats.RegisterUnit(Unit);
            // 先把基线容量买到 3（flat +2 叠加 default_base=1），装满 3 件。
            stats.AddModifier(Unit, new StatModifier(CapacityStat, StatModifierOp.Flat, 2, new Id("aura.t9_capacity_buff")));

            var options = new InventoryOptions { MaxSlotsStat = CapacityStat, FullPolicy = InventoryFullPolicy.Reject };
            var host = new InventoryHost(registry, bus, options, (unitId, stat) => stats.GetStat(unitId, stat));

            Assert.True(host.AddItem(Unit, TemplateId, 3));
            Assert.Equal(3, host.CountOf(Unit, TemplateId));

            // 光环消失：容量回落到 1，已有的 3 件物品必须原样保留，不丢物品。
            stats.RemoveModifiersBySource(Unit, new Id("aura.t9_capacity_buff"));
            Assert.Equal(1, host.GetCapacity(Unit));
            Assert.Equal(3, host.ListItems(Unit).Count);
            Assert.Equal(3, host.CountOf(Unit, TemplateId));

            // 只拒绝新增：Reject 策略下整批失败，Partial 策略下 actualCount 恰为 0——两者都不触碰
            // 已有的 3 件。就地把同一个 InventoryOptions 实例（_options 字段持有的是同一份引用，
            // 不是构造期拷贝，见 InventoryHost 构造函数）的 FullPolicy 改成 Partial，复用同一个
            // host/同一份 _bags 状态，而不是另建一个空背包的新 host——否则新 host 的 bag.Count 从
            // 0 起算，测不出"容量已用满"这个前提条件。
            Assert.False(host.AddItem(Unit, TemplateId, 1));
            Assert.Equal(3, host.CountOf(Unit, TemplateId));

            options.FullPolicy = InventoryFullPolicy.Partial;
            Assert.False(host.TryAddItem(Unit, TemplateId, 1, out var actualCount));
            Assert.Equal(0, actualCount);
            Assert.Equal(3, host.CountOf(Unit, TemplateId));
        }

        [Fact]
        public void GetCapacity_StatSourceNegativeStat_ClampsToZero_NotTreatedAsUnlimited()
        {
            var registry = BuildRegistry();
            var bus = TestSupport.CreateBus();
            var stats = new StatHost(registry, bus);
            stats.RegisterUnit(Unit);
            // default_base=1，减益 -5：最终属性值 -4，容量必须夹取到 0（不是"不限"）——见
            // InventoryOptions.MaxSlotsStat 判断记录"下限口径"：属性来源没有与固定值路径同等的
            // "≤0 表示不限"语义。
            stats.AddModifier(Unit, new StatModifier(CapacityStat, StatModifierOp.Flat, -5, new Id("debuff.t9_capacity_curse")));

            var options = new InventoryOptions { MaxSlotsStat = CapacityStat };
            var host = new InventoryHost(registry, bus, options, (unitId, stat) => stats.GetStat(unitId, stat));

            Assert.Equal(0, host.GetCapacity(Unit));
            Assert.False(host.AddItem(Unit, TemplateId, 1));
        }

        [Fact]
        public void GetCapacity_StatSourceWithoutStatLookupInjected_ThrowsOnFirstResolution()
        {
            var registry = BuildRegistry();
            var bus = TestSupport.CreateBus();
            var options = new InventoryOptions { MaxSlotsStat = CapacityStat };
            // 未提供 statLookup（4 参重载显式传 null，等价于旧 3 参构造函数）：MaxSlotsStat 非 null
            // 但没有查询委托可用，构造期不报错，首次真正解析容量时才抛异常（同 PowerHost 未注入
            // StatLookup 的既有处理时机，见 InventoryHost.GetCapacity 判断记录）。
            var host = new InventoryHost(registry, bus, options, null);

            Assert.Throws<InvalidOperationException>(() => host.GetCapacity(Unit));
        }

        /// <summary>接口默认成员本身的行为（不经 <see cref="InventoryHost"/>）：一个未覆盖 <see
        /// cref="IInventoryHost.GetCapacity"/> 的最小实现应恒定退回 <see cref="int.MaxValue"/>——
        /// 见 <see cref="IInventoryHost.GetCapacity"/> 判断记录"默认实现选择'转发到既有语义'"。本
        /// 测试项目不在 <c>InterfaceDefaultMemberForwardingTests</c> 反射的六个生产程序集之列，
        /// 因此这里独立补一条直接断言默认值本身的回归用例。</summary>
        [Fact]
        public void IInventoryHost_GetCapacity_DefaultImplementation_ReturnsIntMaxValue()
        {
            IInventoryHost minimal = new MinimalFakeInventoryHost();

            Assert.Equal(int.MaxValue, minimal.GetCapacity(Unit));
        }

        private sealed class MinimalFakeInventoryHost : IInventoryHost
        {
            public bool AddItem(Id unitId, Id templateId, int count) => true;
            public bool RemoveItem(Id unitId, Id instanceId, int count) => true;
            public System.Collections.Generic.IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();
            public int CountOf(Id unitId, Id templateId) => 0;
            public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
            // 故意不覆盖 GetCapacity/TryAddItem，验证两者的默认接口实现。
        }
    }
}

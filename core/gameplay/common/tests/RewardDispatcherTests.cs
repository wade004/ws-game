using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.WorldState;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Gameplay.Common
{
    /// <summary>最小 <see cref="IInventoryHost"/> 假实现：只记录 <see cref="AddItem"/> 调用，
    /// 其余成员本测试用不到，抛异常以便误用时能立刻发现（惯例同仓库内其它模块的 Fake）。</summary>
    internal sealed class FakeInventoryHost : IInventoryHost
    {
        public readonly List<(Id UnitId, Id TemplateId, int Count)> AddCalls = new List<(Id, Id, int)>();
        public readonly List<(Id UnitId, Id InstanceId, int Count)> RemoveCalls = new List<(Id, Id, int)>();

        // N02 测试用：一份模板 id 一旦落在这个集合里，AddItem 立即返回 false（不产生任何状态变化），
        // 模拟 InventoryFullPolicy.Reject 下背包放不下该物品——比真实构造一个容量受限的
        // InventoryHost 更直接，只关心 RewardDispatcher 在"某一项加不进去"时是否原子回滚。
        public readonly HashSet<Id> RejectTemplates = new HashSet<Id>();

        // 用 templateId 本身当 instanceId，一个单位一种模板只有一个堆叠，足够测试用。
        private readonly Dictionary<(Id UnitId, Id TemplateId), int> _counts = new Dictionary<(Id, Id), int>();

        public bool AddItem(Id unitId, Id templateId, int count)
        {
            AddCalls.Add((unitId, templateId, count));
            if (RejectTemplates.Contains(templateId))
            {
                return false;
            }

            var key = (unitId, templateId);
            _counts[key] = (_counts.TryGetValue(key, out var c) ? c : 0) + count;
            return true;
        }

        public bool RemoveItem(Id unitId, Id instanceId, int count)
        {
            RemoveCalls.Add((unitId, instanceId, count));
            var key = (unitId, instanceId); // instanceId == templateId，见上方注释
            if (!_counts.TryGetValue(key, out var current) || current < count)
            {
                return false;
            }

            _counts[key] = current - count;
            return true;
        }

        public IReadOnlyList<ItemInstance> ListItems(Id unitId)
        {
            var result = new List<ItemInstance>();
            foreach (var kv in _counts)
            {
                if (kv.Key.UnitId.Equals(unitId) && kv.Value > 0)
                {
                    result.Add(new ItemInstance(kv.Key.TemplateId, kv.Key.TemplateId, kv.Value));
                }
            }
            return result;
        }

        public int CountOf(Id unitId, Id templateId) =>
            _counts.TryGetValue((unitId, templateId), out var c) ? c : 0;

        public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
    }

    /// <summary>最小 <see cref="IProgressionHost"/> 假实现：只记录 <see cref="AddXp"/> 调用。</summary>
    internal sealed class FakeProgressionHost : IProgressionHost
    {
        public readonly List<(Id UnitId, Id SourceId, long Amount)> AddXpCalls = new List<(Id, Id, long)>();

        public void RegisterUnit(Id unitId, Id curveId, int startLevel = 1) => throw new NotSupportedException();
        public int GetLevel(Id unitId) => 1;
        public long GetXp(Id unitId) => 0;
        public long GetXpToNext(Id unitId) => 0;

        public void AddXp(Id unitId, Id sourceId, long amount) => AddXpCalls.Add((unitId, sourceId, amount));

        public void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1) => throw new NotSupportedException();
    }

    /// <summary>最小 <see cref="IWorldState"/> 假实现：只记录 <see cref="Set"/> 调用。</summary>
    internal sealed class FakeWorldStateForRewards : IWorldState
    {
        public readonly List<(Id FlagKey, ExprValue Value, Id WriterId)> SetCalls = new List<(Id, ExprValue, Id)>();

        public ExprValue Get(Id flagKey) => ExprValue.OfBool(false);

        public void Set(Id flagKey, ExprValue value, Id writerId) => SetCalls.Add((flagKey, value, writerId));

        public bool Has(Id flagKey) => false;
        public bool Remove(Id flagKey, Id writerId) => false;
        public SubscriptionHandle OnChanged(Id flagKey, FlagChangedCallback callback) => throw new NotSupportedException();
        public IReadOnlyList<Id> Keys => Array.Empty<Id>();
        public IReadOnlyList<Id> KeysUnder(Id prefix) => Array.Empty<Id>();
        public int Count => 0;
    }

    public class RewardDispatcherTests
    {
        private static readonly Id Unit = new Id("unit.hero");
        private static readonly Id Source = new Id("quest.sample_kill_wolves");

        [Fact]
        public void Grant_WithItems_CallsInventoryAddItem()
        {
            var inventory = new FakeInventoryHost();
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(inventory: inventory);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[] { new ItemStack(new Id("item.iron_sword"), 2) },
                xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            dispatcher.Grant(Unit, bundle, Source);

            var call = Assert.Single(inventory.AddCalls);
            Assert.Equal(Unit, call.UnitId);
            Assert.Equal(new Id("item.iron_sword"), call.TemplateId);
            Assert.Equal(2, call.Count);
        }

        /// <summary>N02 复现与根治：背包已满（<c>InventoryFullPolicy.Reject</c>）时物品奖励
        /// <c>AddItem</c> 返回 false——旧实现忽略这个返回值，物品奖励静默丢失但 <c>Grant</c> 仍视为
        /// 成功继续发放其它类别。修复后：<c>Grant</c> 返回 false，且不再发放 xp/货币等其它类别。</summary>
        [Fact]
        public void Grant_ItemAddFails_ReturnsFalse_AndSkipsOtherRewardCategories()
        {
            var inventory = new FakeInventoryHost();
            inventory.RejectTemplates.Add(new Id("item.iron_sword"));
            var progression = new FakeProgressionHost();
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(inventory: inventory, progression: progression);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[] { new ItemStack(new Id("item.iron_sword"), 1) },
                xp: 100, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            var granted = dispatcher.Grant(Unit, bundle, Source);

            Assert.False(granted);
            Assert.Empty(progression.AddXpCalls); // 物品失败后不再继续发放 xp
        }

        /// <summary>N02 原子性：一份奖励里有两件物品，第一件能加入、第二件因背包已满加不进去——已经
        /// 加入的第一件应被回滚（从背包移除），不能出现"部分奖励生效、部分丢失"的中间状态。</summary>
        [Fact]
        public void Grant_SecondItemFails_RollsBackFirstItemAlreadyGranted()
        {
            var inventory = new FakeInventoryHost();
            inventory.RejectTemplates.Add(new Id("item.iron_sword"));
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(inventory: inventory);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[]
                {
                    new ItemStack(new Id("item.healing_potion"), 3),
                    new ItemStack(new Id("item.iron_sword"), 1),
                },
                xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            var granted = dispatcher.Grant(Unit, bundle, Source);

            Assert.False(granted);
            // 第一件药水已经被回滚，背包里不应再有它。
            Assert.Equal(0, inventory.CountOf(Unit, new Id("item.healing_potion")));
        }

        // -------------------------------------------------------------
        // C05 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
        // Partial 策略下少量加入仍返回成功，此前 GrantItems 按"请求量"而不是"实际落地量"回滚，
        // 会把这一批发放之前就已存在的同模板堆叠也一并删掉。用真实 Core.Carriers.Item.InventoryHost
        // （FakeInventoryHost 不支持 Partial 部分吞没语义，覆盖不到这个问题），复现 CORE-B 的确切
        // 输入：MaxSlots=1、FullPolicy=Partial、已有 A5、堆叠上限 10，奖励 [A10,B1]。
        // -------------------------------------------------------------

        private static IEventBus CreateRealBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(CarriersEventKeys.ItemAdded, "item", new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(CarriersEventKeys.ItemRemoved, "item", new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static DataRegistry BuildRealItemRegistry(IEventBus bus)
        {
            string Table(string name, string rows) => "{\"table\":\"" + name + "\",\"schema_version\":1,\"rows\":" + rows + "}";

            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Table("item.slot_definition",
                    "[{\"id\":\"item.slot.consumable\",\"name_key\":\"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Table("item.quality_definition",
                    "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.quality.common\"}]"))
                .Add("item.template", Table("item.template", "["
                    + "{\"id\":\"item.a\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.a\",\"stack_size\":10,\"name_key\":\"l10n.item.a\"},"
                    + "{\"id\":\"item.b\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.b\",\"stack_size\":10,\"name_key\":\"l10n.item.b\"}]"));

            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join(";", report.Issues.Select(i => i.ToString())));
            return registry;
        }

        /// <summary>C05 复现：Partial 策略下第一项（A）只能续填 5（10 全部落地会超出容量），第二项
        /// （B）完全放不下、失败——整批因此回滚。修复前：回滚按请求量 10 移除 A，越过实际只加入的 5，
        /// 把发放前就已存在的 A5 也一并删空（变成 A0）。修复后：回滚只按实际落地量 5 移除，A 精确
        /// 回到发放前的 A5。</summary>
        [Fact]
        public void Grant_PartialPolicy_SecondItemFails_RollsBackOnlyActuallyAddedAmount_NotPreExistingStock()
        {
            var bus = CreateRealBus();
            var registry = BuildRealItemRegistry(bus);
            var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial });
            inventory.AddItem(Unit, new Id("item.a"), 5);
            Assert.Equal(5, inventory.CountOf(Unit, new Id("item.a")));

            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(inventory: inventory);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[] { new ItemStack(new Id("item.a"), 10), new ItemStack(new Id("item.b"), 1) },
                xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            var granted = dispatcher.Grant(Unit, bundle, Source);

            Assert.False(granted);
            Assert.Equal(5, inventory.CountOf(Unit, new Id("item.a"))); // 精确回到发放前，不是 0。
            Assert.Equal(0, inventory.CountOf(Unit, new Id("item.b")));
        }

        /// <summary>回归：Reject 策略下（本就是"要么整批全加、要么整批不加"的语义）用真实
        /// InventoryHost 验证整批原子失败仍然成立——确保 C05 的改动没有破坏 Reject 既有行为。</summary>
        [Fact]
        public void Grant_RejectPolicy_SecondItemFails_WholeBatchAtomicallyFails_WithRealInventoryHost()
        {
            var bus = CreateRealBus();
            var registry = BuildRealItemRegistry(bus);
            var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject });

            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(inventory: inventory);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[] { new ItemStack(new Id("item.a"), 5), new ItemStack(new Id("item.b"), 1) },
                xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            var granted = dispatcher.Grant(Unit, bundle, Source);

            Assert.False(granted);
            Assert.Equal(0, inventory.CountOf(Unit, new Id("item.a")));
            Assert.Equal(0, inventory.CountOf(Unit, new Id("item.b")));
        }

        /// <summary>R01 复现与根治（architecture/落地计划/audit-5e779c6-20260907）：背包已有 A×2
        /// （MaxSlots=1，同模板续填不占新格）；奖励 [A×1, B×1]，A 能续填进已有堆叠（成功、入队一条
        /// item.added），B 因没有空格子失败（Reject）——整批回滚。用真实 <see cref="EventBus"/>，在
        /// <c>Grant</c> 调用期间不调用 <see cref="IEventBus.DispatchPending"/>（模拟"发放与事件派发
        /// 之间还有别的调用方"这一真实时序，见 QuestHost.HandleItemAdded 的 consumeOnProgress 场景）：
        /// 修复前，回滚只精确移除了数量（<c>CountOf</c> 立即验证也是对的），但总线待处理队列里仍残留
        /// 一条 item.added + 一条抵消用的 item.removed，随后 DispatchPending 时会把这两条当作两个独立
        /// 真实事件分别派发给订阅者——下游据此误判"发放成功过"。修复后：Grant 内部走事务，事件在失败
        /// 时被整批丢弃，从未真正入队，DispatchPending 派发数为 0，任何订阅者都不会被通知。</summary>
        [Fact]
        public void Grant_RejectPolicy_SecondItemFails_RollsBackQueuedEventsToo_R01()
        {
            var bus = CreateRealBus();
            var registry = BuildRealItemRegistry(bus);
            var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject });
            inventory.AddItem(Unit, new Id("item.a"), 2);
            bus.DispatchPending(); // 清空初始铺底的 item.added，只观察 Grant 调用期间产生的事件。

            var itemAddedCount = 0;
            var itemRemovedCount = 0;
            bus.Subscribe(CarriersEventKeys.ItemAdded, _ => itemAddedCount++);
            bus.Subscribe(CarriersEventKeys.ItemRemoved, _ => itemRemovedCount++);

            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(inventory: inventory);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[] { new ItemStack(new Id("item.a"), 1), new ItemStack(new Id("item.b"), 1) },
                xp: 0, currency: Array.Empty<(Id, long)>(), skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            var granted = dispatcher.Grant(Unit, bundle, Source);
            Assert.False(granted);
            Assert.Equal(2, inventory.CountOf(Unit, new Id("item.a"))); // 数量已经精确回滚（C05 既有保证）。

            var dispatchedCount = bus.DispatchPending();

            Assert.Equal(0, dispatchedCount); // 事件从未真正入队，不是"入队又被抵消"。
            Assert.Equal(0, itemAddedCount);
            Assert.Equal(0, itemRemovedCount);
            Assert.Equal(2, inventory.CountOf(Unit, new Id("item.a"))); // 派发后数量仍然不变。
        }

        [Fact]
        public void Grant_WithXp_CallsProgressionAddXp()
        {
            var progression = new FakeProgressionHost();
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(progression: progression);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: Array.Empty<ItemStack>(), xp: 150, currency: Array.Empty<(Id, long)>(),
                skills: Array.Empty<Id>(), worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 0);

            dispatcher.Grant(Unit, bundle, Source);

            var call = Assert.Single(progression.AddXpCalls);
            Assert.Equal(Unit, call.UnitId);
            Assert.Equal(Source, call.SourceId);
            Assert.Equal(150, call.Amount);
        }

        [Fact]
        public void Grant_WithWorldFlags_CallsWorldStateSet()
        {
            var worldState = new FakeWorldStateForRewards();
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(worldState: worldState);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: Array.Empty<ItemStack>(), xp: 0, currency: Array.Empty<(Id, long)>(),
                skills: Array.Empty<Id>(),
                worldFlags: new[] { (new Id("world.bridge.repaired"), ExprValue.OfBool(true)) },
                talentPoints: 0);

            dispatcher.Grant(Unit, bundle, Source);

            var call = Assert.Single(worldState.SetCalls);
            Assert.Equal(new Id("world.bridge.repaired"), call.FlagKey);
            Assert.True(call.Value.AsBool);
            Assert.Equal(Source, call.WriterId);
        }

        [Fact]
        public void Grant_WithSkillsAndTalentPoints_CallsDelegates()
        {
            // GP-04/RC-05 接线跟进：SkillGranter 委托签名从 (unitId, skillId, learn) 三参改为
            // (unitId, skillId, sourceId, learn) 四参（见 core/carriers/item/contracts/
            // SkillGranter.cs，供 SkillHost 按来源引用计数），RewardDispatcher.GrantSkills 现在把
            // Grant 收到的 sourceId 透传进去——本用例一并断言这个透传。
            var skillCalls = new List<(Id UnitId, Id SkillId, Id SourceId, bool Learn)>();
            var talentCalls = new List<(Id UnitId, int Amount, Id SourceId)>();
            var currencyCalls = new List<(Id UnitId, Id CurrencyId, long Amount, Id SourceId)>();

            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(
                skillGranter: (unitId, skillId, sourceId, learn) => skillCalls.Add((unitId, skillId, sourceId, learn)),
                talentPointGranter: (unitId, amount, sourceId) => talentCalls.Add((unitId, amount, sourceId)),
                currencyGranter: (unitId, currencyId, amount, sourceId) => currencyCalls.Add((unitId, currencyId, amount, sourceId)));

            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: Array.Empty<ItemStack>(), xp: 0,
                currency: new[] { (new Id("econ.currency.gold"), 50L) },
                skills: new[] { new Id("skill.fireball") },
                worldFlags: Array.Empty<(Id, ExprValue)>(), talentPoints: 1);

            dispatcher.Grant(Unit, bundle, Source);

            Assert.Single(skillCalls);
            Assert.True(skillCalls[0].Learn);
            Assert.Equal(Source, skillCalls[0].SourceId);
            Assert.Single(talentCalls);
            Assert.Equal(1, talentCalls[0].Amount);
            Assert.Single(currencyCalls);
            Assert.Equal(50, currencyCalls[0].Amount);
        }

        [Fact]
        public void Grant_MissingDependency_RecordsWarningAndSkipsWithoutThrowing()
        {
            var diagnostics = new Core.Gameplay.Common.InMemoryRewardDiagnostics();
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(diagnostics: diagnostics);
            var bundle = new Core.Gameplay.Common.RewardBundle(
                items: new[] { new ItemStack(new Id("item.iron_sword"), 1) },
                xp: 10, currency: new[] { (new Id("econ.currency.gold"), 5L) },
                skills: new[] { new Id("skill.fireball") },
                worldFlags: new[] { (new Id("world.bridge.repaired"), ExprValue.OfBool(true)) },
                talentPoints: 1);

            // 全部依赖均未注入：不应抛异常，应记 6 条警告（每类各一条）。
            var exception = Record.Exception(() => dispatcher.Grant(Unit, bundle, Source));

            Assert.Null(exception);
            Assert.Equal(6, diagnostics.Warnings.Count);
        }

        [Fact]
        public void Grant_EmptyBundleWithNoDependencies_ProducesNoWarnings()
        {
            var diagnostics = new Core.Gameplay.Common.InMemoryRewardDiagnostics();
            var dispatcher = new Core.Gameplay.Common.RewardDispatcher(diagnostics: diagnostics);

            dispatcher.Grant(Unit, Core.Gameplay.Common.RewardBundle.Empty, Source);

            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void RewardBundle_FromRecord_Null_ReturnsEmpty()
        {
            Assert.Same(Core.Gameplay.Common.RewardBundle.Empty, Core.Gameplay.Common.RewardBundle.FromRecord(null));
        }

        [Fact]
        public void RewardBundle_FromRecord_ParsesAllFields()
        {
            var json = "{"
                + "\"items\": [{\"itemId\": \"item.iron_sword\", \"count\": 2}],"
                + "\"xp\": 100,"
                + "\"currency\": [{\"currencyId\": \"econ.currency.gold\", \"amount\": 30}],"
                + "\"skills\": [\"skill.fireball\"],"
                + "\"world_flags\": [{\"flagKey\": \"world.bridge.repaired\", \"value\": true}],"
                + "\"talent_points\": 1"
                + "}";
            var obj = (JsonObject)JsonReader.Parse(json);

            var bundle = Core.Gameplay.Common.RewardBundle.FromRecord(obj);

            Assert.Single(bundle.Items);
            Assert.Equal(new Id("item.iron_sword"), bundle.Items[0].TemplateId);
            Assert.Equal(2, bundle.Items[0].Count);
            Assert.Equal(100, bundle.Xp);
            Assert.Single(bundle.Currency);
            Assert.Equal(30, bundle.Currency[0].Amount);
            Assert.Single(bundle.Skills);
            Assert.Single(bundle.WorldFlags);
            Assert.True(bundle.WorldFlags[0].Value.AsBool);
            Assert.Equal(1, bundle.TalentPoints);
            Assert.False(bundle.IsEmpty);
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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

using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Achievement;
using Core.Gameplay.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Achievement
{
    public class AchievementHostTests
    {
        private static readonly Id Player = new Id("unit.sample_player");
        private static readonly Id MonsterTemplate = new Id("creature.sample_monster");
        private static readonly Id HerbTemplate = new Id("item.sample_herb");
        private static readonly Id IntroQuest = new Id("quest.sample_intro");
        private static readonly Id SummitTrigger = new Id("area.sample_summit");
        private static readonly Id FireballSkill = new Id("skill.sample_fireball");
        private static readonly Id CustomEventKey = new Id("evt.sample_custom");

        private const string DefRows = "[" +
            "{\"id\": \"achv.sample_kill\", \"name_key\": \"l10n.achv.sample_kill.name\", " +
            "\"criteria\": [{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", " +
            "\"target_ref\": \"creature.sample_monster\", \"count\": 3}]}," +
            "{\"id\": \"achv.sample_collect\", \"name_key\": \"l10n.achv.sample_collect.name\", " +
            "\"criteria\": [{\"type\": \"collect_count\", \"observe_event\": \"item.added\", " +
            "\"target_ref\": \"item.sample_herb\", \"count\": 5}]}," +
            "{\"id\": \"achv.sample_quest\", \"name_key\": \"l10n.achv.sample_quest.name\", " +
            "\"criteria\": [{\"type\": \"quest_complete\", \"observe_event\": \"quest.turned_in\", " +
            "\"target_ref\": \"quest.sample_intro\", \"count\": 1}]}," +
            "{\"id\": \"achv.sample_area\", \"name_key\": \"l10n.achv.sample_area.name\", " +
            "\"criteria\": [{\"type\": \"reach_area\", \"observe_event\": \"area.trigger_entered\", " +
            "\"target_ref\": \"area.sample_summit\", \"count\": 1}]}," +
            "{\"id\": \"achv.sample_cast\", \"name_key\": \"l10n.achv.sample_cast.name\", " +
            "\"criteria\": [{\"type\": \"cast_count\", \"observe_event\": \"skill.cast_success\", " +
            "\"target_ref\": \"skill.sample_fireball\", \"count\": 2}]}," +
            "{\"id\": \"achv.sample_custom\", \"name_key\": \"l10n.achv.sample_custom.name\", " +
            "\"criteria\": [{\"type\": \"custom_event\", \"observe_event\": \"evt.sample_custom\", " +
            "\"count\": 1, \"filter\": \"event.amount >= 10\"}]}," +
            "{\"id\": \"achv.sample_once\", \"name_key\": \"l10n.achv.sample_once.name\", " +
            "\"criteria\": [{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", " +
            "\"target_ref\": \"creature.sample_monster\", \"count\": 1}], " +
            "\"rewards\": {\"xp\": 10}}," +
            "{\"id\": \"achv.sample_multi\", \"name_key\": \"l10n.achv.sample_multi.name\", " +
            "\"criteria\": [" +
            "{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", \"target_ref\": \"creature.sample_monster\", \"count\": 1}," +
            "{\"type\": \"collect_count\", \"observe_event\": \"item.added\", \"target_ref\": \"item.sample_herb\", \"count\": 1}" +
            "], \"rewards\": {\"xp\": 50}}" +
            "]";

        private static AchievementHost MakeHost(
            out FakeUnitAccess units,
            out FakeRewardDispatcher rewards,
            out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus, DefRows);
            units = new FakeUnitAccess();
            rewards = new FakeRewardDispatcher();

            var customEventSchema = new ExprSchema().Register(ExprGroups.Event, "amount", ExprValueKind.Int);

            return new AchievementHost(
                registry, bus, units, new FakeExprHostFactory(), rewards,
                new AchievementOptions(() => Player),
                exprSchema: Core.Rules.ExprHost.RulesExprSchema.Compose(customEventSchema));
        }

        // -------------------------------------------------------------
        // 六种 criterion 类型
        // -------------------------------------------------------------

        [Fact]
        public void KillCount_MatchingKills_AccumulatesAndUnlocksAtCount()
        {
            var host = MakeHost(out var units, out var rewards, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);

            host.Evaluate(new UnitDiedEvent(monster, Player));
            host.Evaluate(new UnitDiedEvent(monster, Player));
            Assert.False(host.IsUnlocked(Player, new Id("achv.sample_kill")));

            host.Evaluate(new UnitDiedEvent(monster, Player));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_kill")));
        }

        [Fact]
        public void KillCount_WrongTemplate_NotCounted()
        {
            var host = MakeHost(out var units, out _, out _);
            var other = new Id("creature.inst_2");
            units.SetTemplate(other, new Id("creature.sample_other"));

            host.Evaluate(new UnitDiedEvent(other, Player));

            var progress = host.GetProgress(Player, new Id("achv.sample_kill"));
            Assert.Equal(0, progress[0].Current);
        }

        [Fact]
        public void KillCount_KillerNotPlayer_NotCounted()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            var otherKiller = new Id("creature.sample_ally_unit");

            host.Evaluate(new UnitDiedEvent(monster, otherKiller));

            var progress = host.GetProgress(Player, new Id("achv.sample_kill"));
            Assert.Equal(0, progress[0].Current);
        }

        [Fact]
        public void KillCount_NoKillerId_NotCounted()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);

            host.Evaluate(new UnitDiedEvent(monster, null));

            var progress = host.GetProgress(Player, new Id("achv.sample_kill"));
            Assert.Equal(0, progress[0].Current);
        }

        [Fact]
        public void CollectCount_AccumulatesByEventCount_NotJustOne()
        {
            var host = MakeHost(out _, out _, out _);

            host.Evaluate(new ItemAddedEvent(Player, new Id("item.inst_1"), HerbTemplate, 3));

            var progress = host.GetProgress(Player, new Id("achv.sample_collect"));
            Assert.Equal(3, progress[0].Current);

            host.Evaluate(new ItemAddedEvent(Player, new Id("item.inst_2"), HerbTemplate, 2));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_collect")));
        }

        [Fact]
        public void QuestComplete_MatchingQuestId_Unlocks()
        {
            var host = MakeHost(out _, out _, out _);

            host.Evaluate(new FakeQuestTurnedInEvent(Player, IntroQuest));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_quest")));
        }

        [Fact]
        public void ReachArea_MatchingTriggerId_Unlocks()
        {
            var host = MakeHost(out _, out _, out _);

            host.Evaluate(new FakeAreaTriggerEnteredEvent(SummitTrigger, Player));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_area")));
        }

        [Fact]
        public void CastCount_MatchingSkillId_AccumulatesToCount()
        {
            var host = MakeHost(out _, out _, out _);

            host.Evaluate(new SkillCastSuccessEvent(Player, FireballSkill, new[] { new Id("creature.inst_1") }));
            Assert.False(host.IsUnlocked(Player, new Id("achv.sample_cast")));

            host.Evaluate(new SkillCastSuccessEvent(Player, FireballSkill, new[] { new Id("creature.inst_1") }));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_cast")));
        }

        [Fact]
        public void CustomEvent_FilterTrue_UnlocksImmediately()
        {
            var host = MakeHost(out _, out _, out _);

            host.Evaluate(new FakeCustomAmountEvent(CustomEventKey, 15));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_custom")));
        }

        [Fact]
        public void CustomEvent_FilterFalse_DoesNotCount()
        {
            var host = MakeHost(out _, out _, out _);

            host.Evaluate(new FakeCustomAmountEvent(CustomEventKey, 1));

            Assert.False(host.IsUnlocked(Player, new Id("achv.sample_custom")));
        }

        // -------------------------------------------------------------
        // 多 criterion / 解锁 / 奖励幂等
        // -------------------------------------------------------------

        [Fact]
        public void MultiCriterion_PartialProgress_DoesNotUnlock()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);

            host.Evaluate(new UnitDiedEvent(monster, Player));

            Assert.False(host.IsUnlocked(Player, new Id("achv.sample_multi")));
        }

        [Fact]
        public void MultiCriterion_AllAchieved_Unlocks()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);

            host.Evaluate(new UnitDiedEvent(monster, Player));
            host.Evaluate(new ItemAddedEvent(Player, new Id("item.inst_1"), HerbTemplate, 1));

            Assert.True(host.IsUnlocked(Player, new Id("achv.sample_multi")));
        }

        [Fact]
        public void Unlock_DispatchesRewards_AndPublishesUnlockedEvent()
        {
            var host = MakeHost(out var units, out var rewards, out var bus);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            AchievementUnlockedEvent? captured = null;
            bus.Subscribe<AchievementUnlockedEvent>(AchievementEventKeys.Unlocked, evt => captured = evt);

            host.Evaluate(new UnitDiedEvent(monster, Player));

            Assert.Single(rewards.Grants);
            Assert.Equal(Player, rewards.Grants[0].UnitId);
            Assert.Equal(10, rewards.Grants[0].Bundle.Xp);
            Assert.NotNull(captured);
            Assert.Equal(new Id("achv.sample_once"), captured!.AchievementId);
            Assert.Equal(Player, captured.UnitId);
        }

        [Fact]
        public void Unlock_IsIdempotent_NoRepeatRewardOrEvent()
        {
            // achv.sample_once（count=1）与 achv.sample_kill（count=3）都订阅同一个 unit.died +
            // creature.sample_monster 组合，3 次击杀期间两者各自恰好解锁一次——本测试只关心
            // achv.sample_once 那一个的幂等性（第 1 次评估后就已达标，第 2、3 次不应再重复），
            // 用 achievementId 过滤订阅，避免与 achv.sample_kill 自己的解锁事件混在一起统计。
            var host = MakeHost(out var units, out var rewards, out var bus);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            var onceUnlockedCount = 0;
            bus.Subscribe<AchievementUnlockedEvent>(AchievementEventKeys.Unlocked, evt =>
            {
                if (evt.AchievementId.Equals(new Id("achv.sample_once")))
                {
                    onceUnlockedCount++;
                }
            });

            host.Evaluate(new UnitDiedEvent(monster, Player));
            host.Evaluate(new UnitDiedEvent(monster, Player));
            host.Evaluate(new UnitDiedEvent(monster, Player));

            var onceGrants = rewards.Grants.FindAll(g => g.SourceId.Equals(new Id("achv.sample_once")));
            Assert.Single(onceGrants);
            Assert.Equal(1, onceUnlockedCount);
        }

        /// <summary>
        /// C04 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：达成条件
        /// 满足但物品奖励因背包已满（<c>InventoryFullPolicy.Reject</c>）而 <c>Grant</c> 返回 false
        /// ——旧实现仍先把 key 写进 <c>_unlocked</c> 再调用 Grant，忽略返回值，奖励永久丢失、
        /// <c>IsUnlocked</c> 却已经报告"已解锁"。根治后：Grant 失败时不写 <c>_unlocked</c>、不发
        /// <see cref="AchievementUnlockedEvent"/>，记入待领奖集合；<see
        /// cref="IAchievementHost.RetryPendingRewards"/> 是本类型（不像 <c>EncounterHost.Evaluate</c>
        /// 那样有天然的周期性重新求值入口）的显式重试通道——玩家清出背包空间后调用一次，恰好补发
        /// 一次奖励、正式转为解锁状态；此后再调用不会重复发放（幂等）。用真实 <see
        /// cref="Core.Carriers.Item.InventoryHost"/>（<c>MaxSlots=1</c>、<c>FullPolicy=Reject</c>，
        /// 先塞满一个不同模板的物品占掉唯一格子）+ 真实 <see cref="RewardDispatcher"/>，不用
        /// <see cref="FakeRewardDispatcher"/>（后者恒成功，测不出这个问题）。
        /// </summary>
        [Fact]
        public void Unlock_RewardGrantFails_StaysLocked_RetryPendingRewardsGrantsExactlyOnceAfterRoomFreed()
        {
            const string RewardAchievementId = "achv.sample_item_reward";
            var defRows = "[{\"id\": \"" + RewardAchievementId + "\", \"name_key\": \"l10n.achv.sample_item_reward.name\", " +
                "\"criteria\": [{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", " +
                "\"target_ref\": \"creature.sample_monster\", \"count\": 1}], " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.sample_reward\", \"count\": 1}]}}]";

            var bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus, defRows);
            var units = new FakeUnitAccess();

            var itemBus = new EventBus(EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(CarriersEventKeys.ItemAdded, "item", new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(CarriersEventKeys.ItemRemoved, "item", new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            }));
            string Table(string name, string rows) => "{\"table\":\"" + name + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var itemSource = new InMemoryDataSource()
                .Add("item.slot_definition", Table("item.slot_definition", "[{\"id\":\"item.slot.consumable\",\"name_key\":\"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Table("item.quality_definition", "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.quality.common\"}]"))
                .Add("item.template", Table("item.template", "["
                    + "{\"id\":\"item.filler\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.filler\",\"stack_size\":1,\"name_key\":\"l10n.item.filler\"},"
                    + "{\"id\":\"item.sample_reward\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.sample_reward\",\"stack_size\":1,\"name_key\":\"l10n.item.sample_reward\"}]"));
            var itemRegistry = new DataRegistry(itemSource, itemBus);
            itemRegistry.RegisterSchema(ItemSchemas.Template);
            itemRegistry.RegisterSchema(ItemSchemas.SlotDefinition);
            itemRegistry.RegisterSchema(ItemSchemas.QualityDefinition);
            var itemReport = itemRegistry.LoadAll();
            Assert.False(itemReport.IsBlocking, string.Join(";", itemReport.Issues.Select(i => i.ToString())));

            var inventory = new InventoryHost(itemRegistry, itemBus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject });
            inventory.AddItem(Player, new Id("item.filler"), 1); // 唯一格子被占满，奖励物品完全放不下。
            var rewardDispatcher = new RewardDispatcher(inventory: inventory);

            var host = new AchievementHost(
                registry, bus, units, new FakeExprHostFactory(), rewardDispatcher, new AchievementOptions(() => Player));

            var unlockedCount = 0;
            bus.Subscribe<AchievementUnlockedEvent>(AchievementEventKeys.Unlocked, _ => unlockedCount++);

            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);

            // 达成条件成立，但 Grant 因背包已满失败——不应判定为已解锁，不应发 Unlocked。
            host.Evaluate(new UnitDiedEvent(monster, Player));
            Assert.False(host.IsUnlocked(Player, new Id(RewardAchievementId)));
            Assert.Equal(0, unlockedCount);
            Assert.Equal(0, inventory.CountOf(Player, new Id("item.sample_reward")));

            // 背包状态未变时重试：仍然失败，仍然保持未解锁（不产生任何变化，可安全反复调用）。
            var retried1 = host.RetryPendingRewards(Player);
            Assert.Empty(retried1);
            Assert.False(host.IsUnlocked(Player, new Id(RewardAchievementId)));

            // 玩家清出空间：移除占位物品。
            var fillerInstanceId = inventory.ListItems(Player).Single(i => i.TemplateId.Equals(new Id("item.filler"))).InstanceId;
            inventory.RemoveItem(Player, fillerInstanceId, 1);

            // 重试：Grant 成功，恰好转为解锁一次、恰好发一次 Unlocked、恰好拿到一次奖励物品。
            var retried2 = host.RetryPendingRewards(Player);
            Assert.Equal(new[] { new Id(RewardAchievementId) }, retried2);
            Assert.True(host.IsUnlocked(Player, new Id(RewardAchievementId)));
            Assert.Equal(1, unlockedCount);
            Assert.Equal(1, inventory.CountOf(Player, new Id("item.sample_reward")));

            // 再次重试：已经解锁，幂等，不重复发放。
            var retried3 = host.RetryPendingRewards(Player);
            Assert.Empty(retried3);
            Assert.Equal(1, unlockedCount);
            Assert.Equal(1, inventory.CountOf(Player, new Id("item.sample_reward")));
        }

        [Fact]
        public void Progressed_EventFires_WithCurrentAndTarget()
        {
            // achv.sample_kill/achv.sample_multi/achv.sample_once 都订阅同一个 unit.died +
            // creature.sample_monster 组合，同一次 Evaluate 会给三者各发一次 progressed——按
            // achievementId 过滤只捕获 achv.sample_kill 自己的那一条。
            var host = MakeHost(out var units, out _, out var bus);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            AchievementProgressedEvent? captured = null;
            bus.Subscribe<AchievementProgressedEvent>(AchievementEventKeys.Progressed, evt =>
            {
                if (evt.AchievementId.Equals(new Id("achv.sample_kill")))
                {
                    captured = evt;
                }
            });

            host.Evaluate(new UnitDiedEvent(monster, Player));

            Assert.NotNull(captured);
            Assert.Equal(new Id("achv.sample_kill"), captured!.AchievementId);
            Assert.Equal(1, captured.Current);
            Assert.Equal(3, captured.Target);
        }

        // -------------------------------------------------------------
        // GetProgress / IsUnlocked
        // -------------------------------------------------------------

        [Fact]
        public void GetProgress_DefaultsToZero_BeforeAnyEvent()
        {
            var host = MakeHost(out _, out _, out _);

            var progress = host.GetProgress(Player, new Id("achv.sample_kill"));

            Assert.Equal(0, progress[0].Current);
            Assert.Equal(3, progress[0].Target);
        }

        [Fact]
        public void IsUnlocked_UnknownAchievement_Throws()
        {
            var host = MakeHost(out _, out _, out _);

            Assert.Throws<ArgumentException>(() => host.IsUnlocked(Player, new Id("achv.sample_nonexistent")));
        }

        // -------------------------------------------------------------
        // IPersistable
        // -------------------------------------------------------------

        [Fact]
        public void SectionKey_IsPlayerAchievementState()
        {
            var host = MakeHost(out _, out _, out _);

            Assert.Equal(Core.Foundation.SaveSystem.SaveSections.PlayerAchievementState, host.SectionKey);
        }

        [Fact]
        public void Save_Load_RoundTrips_ProgressAndUnlockedState()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            host.Evaluate(new UnitDiedEvent(monster, Player)); // 1/3，未解锁
            host.Evaluate(new ItemAddedEvent(Player, new Id("item.inst_1"), HerbTemplate, 2)); // 2/5，未解锁

            var saved = host.Save();

            var restored = MakeHost(out _, out _, out _);
            restored.Load(saved);

            var killProgress = restored.GetProgress(Player, new Id("achv.sample_kill"));
            Assert.Equal(1, killProgress[0].Current);
            var collectProgress = restored.GetProgress(Player, new Id("achv.sample_collect"));
            Assert.Equal(2, collectProgress[0].Current);
            Assert.False(restored.IsUnlocked(Player, new Id("achv.sample_kill")));
        }

        [Fact]
        public void Save_Load_RoundTrips_UnlockedFlag()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            host.Evaluate(new UnitDiedEvent(monster, Player)); // 解锁 achv.sample_once（count=1）

            var saved = host.Save();

            var restored = MakeHost(out _, out _, out _);
            restored.Load(saved);

            Assert.True(restored.IsUnlocked(Player, new Id("achv.sample_once")));
        }

        [Fact]
        public void Load_DoesNotReDispatchRewards()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            host.Evaluate(new UnitDiedEvent(monster, Player));
            var saved = host.Save();

            var restored = MakeHost(out _, out var restoredRewards, out _);
            restored.Load(saved);

            Assert.Empty(restoredRewards.Grants);
        }

        [Fact]
        public void Load_NullData_LeavesNoProgress()
        {
            var host = MakeHost(out var units, out _, out _);
            var monster = new Id("creature.inst_1");
            units.SetTemplate(monster, MonsterTemplate);
            host.Evaluate(new UnitDiedEvent(monster, Player));

            host.Load(JsonNull.Instance);

            var progress = host.GetProgress(Player, new Id("achv.sample_kill"));
            Assert.Equal(0, progress[0].Current);
        }
    }
}

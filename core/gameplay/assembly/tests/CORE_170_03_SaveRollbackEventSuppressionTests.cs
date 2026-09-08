using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using System.Collections.Generic;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// CORE-170-03 补充根治（architecture/落地计划/audit-8160178-20260908，P2，第十轮审计已复现）：
    /// <c>EquipmentPersistable.Load</c> 为复用真实装备逻辑而调用真正的 <c>EquipmentHost.Equip</c>/
    /// <c>Unequip</c>，两者本身会正常派发 <c>ItemEquipped</c>/<c>ItemUnequipped</c>/<c>StatChanged</c>
    /// 等真实领域事件；<c>SaveSystem.Load</c> 逆序回滚失败读档时重放这些调用，会让
    /// <c>AchievementHost</c> 一类按 <c>custom_event</c> 观察条件计数的消费者把"读档/回滚期间的重放"
    /// 误当成真实的一次玩家操作再计一次数（真实探针 <c>core-persistence-probe.raw.log</c>
    /// SAVE-ROLLBACK-DERIVED-STATE 段复现：<c>achievement_before_event_dispatch=1</c> →
    /// <c>after_event_dispatch=2</c>，进度被回滚重放的事件错误推高并触发解锁）。
    /// <para>
    /// 根治后 <c>SaveSystem.Load</c> 把整段"逐段 Load + 失败回滚"逻辑纳入 <see
    /// cref="IEventBus.SuppressDispatch"/> 抑制作用域（见该方法判断记录"读档不是业务事件"）——
    /// 作用域内经 <c>Equip</c>/<c>Unequip</c> 产生的事件被直接丢弃，不会派发给任何订阅者，
    /// <c>AchievementHost</c> 的计数不会被读档/回滚重放污染。本文件用真实 <c>GameplayAssembly</c> +
    /// 真实 <c>EquipmentHost</c>/<c>AchievementHost</c> + 真实 <c>SaveSystem</c> 复现审计验收清单：
    /// 坏 shape、跨段失败（成功段逆序回滚 + 事件观察者/成就计数不被污染）。
    /// </para>
    /// </summary>
    public sealed class CORE_170_03_SaveRollbackEventSuppressionTests
    {
        private static readonly Id MapId = new Id("world.core_170_03_map");
        private static readonly Id PlayerId = new Id("unit.core_170_03_player");
        private static readonly Id PlayerFactionId = new Id("fac.core_170_03_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.core_170_03_sample");
        private static readonly Id MaxHealthStat = new Id("stat.max_health");
        private static readonly Id Health = WellKnownPowers.Health;

        private static readonly Id MaxHealthSlot = new Id("item.slot.core_170_03_max_health");
        private static readonly Id MaxHealthItem = new Id("item.sample_core_170_03_max_health");
        private static readonly Id AchievementId = new Id("achv.core_170_03_rollback");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.core_170_03_sample" + "\", \"name_key\": \"l10n.arch.class.core_170_03_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var itemSlotDefinitionRows = "[{\"id\": \"" + MaxHealthSlot.Value + "\", \"name_key\": \"l10n.item.slot.core_170_03_max_health\"}]";
            var itemQualityDefinitionRows =
                "[{\"id\": \"item.quality.core_170_03_common\", \"name_key\": \"l10n.item.quality.core_170_03_common\"}]";
            var itemTemplateRows = "[{\"id\": \"" + MaxHealthItem.Value + "\", \"slot\": \"" + MaxHealthSlot.Value + "\", " +
                "\"quality\": \"item.quality.core_170_03_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.core_170_03_max_health\", \"stack_size\": 1, \"name_key\": \"l10n.item.core_170_03_max_health\", " +
                "\"stats\": [{\"stat\": \"" + MaxHealthStat.Value + "\", \"op\": \"flat\", \"value\": 100}]}]";

            // 真实探针同款配置：criteria 为 custom_event 观察 item.equipped，count=2——第一次真实
            // 装备只推进到 1（未解锁），只有真的再装备一次才应该推进到 2 并解锁；读档/回滚重放绝不
            // 应该充当这"第二次"。
            var achvRows = "[{\"id\": \"" + AchievementId.Value + "\", \"name_key\": \"l10n.achv.core_170_03_rollback\", " +
                "\"criteria\": [{\"type\": \"custom_event\", \"observe_event\": \"item.equipped\", \"count\": 2}]}]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("achv.def", Envelope("achv.def", achvRows))
                .Add("item.slot_definition", Envelope("item.slot_definition", itemSlotDefinitionRows))
                .Add("item.quality_definition", Envelope("item.quality_definition", itemQualityDefinitionRows))
                .Add("item.template", Envelope("item.template", itemTemplateRows))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 200}]}]"));
        }

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public List<string> Events = null!;
        }

        /// <summary>真实探针同款 <c>ThrowingPersistable</c>：一个恒定抛异常的自定义段，排在
        /// Equipment/Vitals/Achievement 三个真实段之后注册，模拟"前面几段都成功、最后一段失败"。</summary>
        private sealed class ThrowingPersistable : IPersistable
        {
            public string SectionKey => "zz.core_170_03_probe_failure";
            public JsonValue Save() => JsonNull.Instance;
            public void Load(JsonValue data) => throw new System.InvalidOperationException("probe failure after real sections");
        }

        private static Fixture Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.core_170_03_unused")), bus),
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            var events = new List<string>();
            bus.Subscribe<Core.Carriers.Common.ItemEquippedEvent>(Core.Carriers.Common.CarriersEventKeys.ItemEquipped, _ => events.Add("ItemEquipped"));
            bus.Subscribe<Core.Carriers.Common.ItemUnequippedEvent>(Core.Carriers.Common.CarriersEventKeys.ItemUnequipped, _ => events.Add("ItemUnequipped"));
            bus.Subscribe<Core.Numbers.PowerSet.PowerChangedEvent>(Core.Numbers.PowerSet.PowerEventKeys.Changed, _ => events.Add("PowerChanged"));
            bus.Subscribe<Core.Numbers.StatBlock.StatChangedEvent>(Core.Numbers.StatBlock.StatBlockEventKeys.StatChanged, _ => events.Add("StatChanged"));
            bus.DispatchPending();
            events.Clear();

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player, Events = events };
        }

        private static JsonObject Meta(string slotId) => new JsonObjectBuilder()
            .Add("save_version", new JsonNumber(1))
            .Add("slot_id", new JsonString(slotId))
            .Add("created_at", new JsonString("probe"))
            .Add("updated_at", new JsonString("probe"))
            .Add("game_id", new JsonString("game.core_170_03_probe"))
            .Build();

        /// <summary>
        /// 核心复现与根治，对应真实探针 SAVE-ROLLBACK-DERIVED-STATE 段：Equipment、PlayerVitals、
        /// Achievement 三个真实段成功加载后，后续自定义段抛异常，SaveSystem 逆序回滚。断言：
        /// (a) 装备/生命值数值最终恢复到读档前；(b) 事件观察者列表里不出现任何被回滚重放污染的
        /// ItemEquipped（成就计数因此不会被推高）；(c) 成就进度/解锁状态回到读档前。
        /// </summary>
        [Fact]
        public void Rollback_AfterCrossSectionFailure_RestoresDerivedState_AndDoesNotPolluteAchievementCounter()
        {
            var fx = Build();
            fx.Gameplay.Carriers.Inventory.AddItem(PlayerId, MaxHealthItem, 1);
            var instance = fx.Gameplay.Carriers.Inventory.ListItems(PlayerId)[0].InstanceId;
            fx.Gameplay.Carriers.Equipment.Equip(PlayerId, instance, MaxHealthSlot);
            fx.Bus.DispatchPending();
            fx.Events.Clear();

            var maxBefore = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(PlayerId, Health);
            var achievementBefore = fx.Gameplay.Achievement.GetProgress(PlayerId, AchievementId)[0].Current;
            Assert.Equal(1, achievementBefore); // 真实第一次装备已经推进到 1（未解锁，count=2）。
            Assert.False(fx.Gameplay.Achievement.IsUnlocked(PlayerId, AchievementId));

            // 把生命值填满（等价探针 fx.Gameplay.Carriers.Rules.Powers.ModifyPower 那一步），供后面
            // 断言"回滚后生命值恢复到读档前"有意义的非零基线。
            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(
                PlayerId, Health, maxBefore - fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, Health), new Id("probe.fill"));
            fx.Bus.DispatchPending();
            fx.Events.Clear();

            var fs = new StubFileSystem();
            var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_170_03_probe")), fx.Bus);
            var equipmentPersistable = new Core.Carriers.Item.EquipmentPersistable(PlayerId, fx.Gameplay.Carriers.Inventory, fx.Gameplay.Carriers.Equipment);
            var vitalsPersistable = new PlayerVitalsPersistable(fx.Player, fx.Gameplay.Carriers.Rules.Powers);
            save.RegisterPersistable(equipmentPersistable);
            save.RegisterPersistable(vitalsPersistable);
            save.RegisterPersistable(fx.Gameplay.Achievement);
            save.RegisterPersistable(new ThrowingPersistable());

            var malformedAfterRealSections = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add("meta", Meta("slot.core_170_03"))
                    .Add("player.equipment", new JsonObjectBuilder().Build()) // 合法但清空
                    .Add("player.vitals", new JsonObjectBuilder()
                        .Add("alive", JsonBool.True)
                        .Add("health", new JsonNumber(100))
                        .Build())
                    .Add(SaveSections.PlayerAchievementState, JsonNull.Instance)
                    .Add("zz.core_170_03_probe_failure", new JsonString("boom"))
                    .Build())
                .Build();
            fs.WriteTextAtomic("user://saves/slot.core_170_03.json", JsonWriter.Write(malformedAfterRealSections));

            var beforeEquip = fx.Gameplay.Carriers.Equipment.GetEquipped(PlayerId, MaxHealthSlot).HasValue;
            var beforeHealth = fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, Health);

            var result = save.Load(new Id("slot.core_170_03"));

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);

            // 回滚在 Load() 调用内部同步完成（不需要额外 DispatchPending 才能观察到状态恢复——
            // 状态本身不经事件队列，是 Equip/Unequip/ModifyPower 直接写入的，见各自实现）。
            var afterEquip = fx.Gameplay.Carriers.Equipment.GetEquipped(PlayerId, MaxHealthSlot).HasValue;
            var afterHealth = fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, Health);
            var afterMax = fx.Gameplay.Carriers.Rules.Powers.GetPowerMax(PlayerId, Health);
            var afterAchievement = fx.Gameplay.Achievement.GetProgress(PlayerId, AchievementId)[0].Current;
            var afterUnlocked = fx.Gameplay.Achievement.IsUnlocked(PlayerId, AchievementId);

            Assert.Equal(beforeEquip, afterEquip);
            Assert.True(afterEquip, "CORE-170-03 核心断言：回滚后装备应当恢复到读档前（仍然装备着）。");
            Assert.Equal(beforeHealth, afterHealth);
            Assert.Equal(maxBefore, afterMax);

            // CORE-170-03 补充核心断言：成就进度/解锁状态必须回到读档前——不能因为回滚期间重放了
            // Equip/Unequip 而被误计数推高到 2 并解锁。
            Assert.Equal(achievementBefore, afterAchievement);
            Assert.False(afterUnlocked, "回滚重放的事件不应该被 AchievementHost 计成一次新的装备操作。");

            // 排空事件队列后同样不应该出现任何 ItemEquipped/ItemUnequipped——抑制作用域内产生的事件
            // 被直接丢弃，根本没有进入待处理队列，DispatchPending 无事可派发。
            fx.Bus.DispatchPending();
            Assert.DoesNotContain("ItemEquipped", fx.Events);
            Assert.DoesNotContain("ItemUnequipped", fx.Events);

            var finalAchievement = fx.Gameplay.Achievement.GetProgress(PlayerId, AchievementId)[0].Current;
            var finalUnlocked = fx.Gameplay.Achievement.IsUnlocked(PlayerId, AchievementId);
            Assert.Equal(achievementBefore, finalAchievement);
            Assert.False(finalUnlocked);
        }

        /// <summary>坏 shape：<c>player.equipment</c> 段本身不是 JSON 对象（真实探针
        /// FAILED-EQUIPMENT-SEGMENT 段复现场景）。断言装备在 Load() 抛异常前后完全不变，且排空事件
        /// 队列后不出现任何 StatChanged/ItemUnequipped——修复前这两个事件会在清空装备时真实产生并
        /// 进入队列（真实探针 <c>events=StatChanged,ItemUnequipped</c>）。</summary>
        [Fact]
        public void Load_BadShapeEquipmentSection_RestoresEquipment_AndDoesNotLeakEventsToObservers()
        {
            var fx = Build();
            fx.Gameplay.Carriers.Inventory.AddItem(PlayerId, MaxHealthItem, 1);
            var instance = fx.Gameplay.Carriers.Inventory.ListItems(PlayerId)[0].InstanceId;
            var equip = fx.Gameplay.Carriers.Equipment.Equip(PlayerId, instance, MaxHealthSlot);
            Assert.True(equip.Success);
            fx.Bus.DispatchPending();
            fx.Events.Clear();

            var fs = new StubFileSystem();
            var save = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_170_03_probe")), fx.Bus);
            save.RegisterPersistable(new Core.Carriers.Item.EquipmentPersistable(PlayerId, fx.Gameplay.Carriers.Inventory, fx.Gameplay.Carriers.Equipment));

            var malformed = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add("meta", Meta("slot.core_170_03_bad_shape"))
                    .Add("player.equipment", new JsonString("wrong-shape"))
                    .Build())
                .Build();
            fs.WriteTextAtomic("user://saves/slot.core_170_03_bad_shape.json", JsonWriter.Write(malformed));

            var beforeEquipped = fx.Gameplay.Carriers.Equipment.GetEquipped(PlayerId, MaxHealthSlot).HasValue;
            var result = save.Load(new Id("slot.core_170_03_bad_shape"));
            var afterLoadEquipped = fx.Gameplay.Carriers.Equipment.GetEquipped(PlayerId, MaxHealthSlot).HasValue;

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);
            Assert.True(beforeEquipped);
            Assert.True(afterLoadEquipped, "CORE-170-03 核心断言：坏 shape 读档失败后，装备不应消失。");

            fx.Bus.DispatchPending();
            Assert.Empty(fx.Events);
        }
    }
}

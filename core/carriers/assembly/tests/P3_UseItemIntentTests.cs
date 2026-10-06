using System;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Item;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// 消费方反馈（2026-10-07 样板游戏 A P3 缺口 G1）：背包里的消耗品没有"使用"这条规则层通路。
    /// <para>
    /// 症状：表现层 <c>UiIntents.UseItem</c> 早就把"使用物品"提交成 <c>use_item</c> 意图
    /// （<c>{instance_id}</c>），但没有任何 tick 处理器消费它；<c>item.template</c> 也没有登记"使用时做什么"
    /// 的字段——药水只能躺在背包里，游戏要么另写一套平行的消耗逻辑，要么没法做药水。
    /// </para>
    /// <para>
    /// 本文件全部用真实 <see cref="CarriersAssembly"/>（生产装配入口）+ 真实意图队列
    /// （<c>WorldSim.SubmitIntent</c> → <c>Tick</c>）+ 真实施法链路，断言运行期可观测结果：生命值由多少变成多少、
    /// 背包里药水数量由多少变成多少；期望值一律由写进数据里的声明常量算出，不写裸数。
    /// </para>
    /// <para>
    /// 修前预期为红：<c>item.template.on_use</c> 字段未登记（数据校验阻断，夹具构造即抛）；即便绕开校验，
    /// 也没有处理器消费 <c>use_item</c> 意图，生命值与药水数量全程不变。
    /// </para>
    /// </summary>
    public sealed class P3_UseItemIntentTests
    {
        private static readonly Id MapId = new Id("map.p3use");
        private static readonly Id PlayerId = new Id("unit.p3use_player");
        private static readonly Id OtherId = new Id("unit.p3use_other");
        private static readonly Id FactionId = new Id("fac.p3use");
        private static readonly Id ClassId = new Id("arch.class.p3use");
        private static readonly Id Health = WellKnownPowers.Health;

        private static readonly Id SlotConsumable = new Id("item.slot.p3use_consumable");
        private static readonly Id QualityId = new Id("item.quality.p3use_common");
        private static readonly Id PotionHeal = new Id("item.p3use_potion_heal");
        private static readonly Id PotionKeep = new Id("item.p3use_potion_keep");
        private static readonly Id Junk = new Id("item.p3use_junk");

        private static readonly Id ChainSelf = new Id("target.chain.p3use_self");
        private static readonly Id SkillHeal = new Id("skill.p3use_heal");

        /// <summary>生命上限（写进资源类型定义）。</summary>
        private const double MaxHealth = 100;

        /// <summary>治疗技能声明的治疗量；命中表全禁用后结算无波动，实际治疗量恒等于它。</summary>
        private const double HealAmount = 35;

        /// <summary>药水技能声明的冷却（秒）。</summary>
        private const double PotionCooldown = 10;

        /// <summary>测试起手时先把生命扣掉的量。</summary>
        private const double DamageTaken = 60;

        private const int PotionsHeld = 3;

        private sealed class Fixture : IDisposable
        {
            public EventBus Bus = null!;
            public WorldSim World = null!;
            public CarriersAssembly Assembly = null!;

            public void Dispose() => World.Dispose();

            public double Hp(Id unit) => Assembly.Rules.Powers.GetPower(unit, Health);

            public Id InstanceOf(Id unit, Id template) =>
                Assembly.Inventory.ListItems(unit).First(i => i.TemplateId.Equals(template)).InstanceId;

            public void Use(Id actor, Id instanceId)
            {
                var args = new JsonObjectBuilder().Add("instance_id", new JsonString(instanceId.Value)).Build();
                World.SubmitIntent(new Intent(actor, "use_item", args));
                World.Tick(SimStep.Continuous(0.1));
                Bus.DispatchPending();
            }
        }

        private static string Table(string name, string rows) =>
            "{\"table\": \"" + name + "\", \"schema_version\": 1, \"rows\": [" + rows + "]}";

        private static string Num(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            source.Add("item.budget_curve", Table("item.budget_curve",
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}"));
            source.Add("stat.definition", Table("stat.definition",
                "{\"id\": \"stat.p3use_none\", \"name_key\": \"l10n.stat.p3use_none\", \"group\": \"primary\", \"default_base\": 0}"));
            source.Add("arch.power_type", Table("arch.power_type",
                "{\"id\": \"" + Health + "\", \"name_key\": \"l10n.power.p3use_health\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": " + Num(MaxHealth) + "}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}"));
            source.Add("arch.class", Table("arch.class",
                "{\"id\": \"" + ClassId + "\", \"name_key\": \"l10n.arch.class.p3use\", \"primary_stat\": \"stat.p3use_none\", " +
                "\"base_stats\": {}, \"power_types\": [\"" + Health + "\"]}"));
            source.Add("fac.faction", Table("fac.faction",
                "{\"id\": \"" + FactionId + "\", \"name_key\": \"l10n.fac.p3use\", \"default_reaction\": \"friendly\"}"));
            source.Add("fac.reaction_matrix", Table("fac.reaction_matrix", ""));
            source.Add("combat.hit_table_config", Table("combat.hit_table_config",
                "{\"id\": \"combat.hit_table.default\", " +
                "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, " +
                "\"crit_multiplier_base\": 2.0}"));
            source.Add("combat.resist_curve", Table("combat.resist_curve", ""));
            source.Add("target.chain_def", Table("target.chain_def",
                "{\"id\": \"" + ChainSelf + "\", \"source\": \"self\", \"max_targets\": 1}"));
            source.Add("skill.def", Table("skill.def",
                "{\"id\": \"" + SkillHeal + "\", \"school\": \"school.p3use\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 0, \"cooldown_duration\": " + Num(PotionCooldown) + ", \"respects_gcd\": false, " +
                "\"target_shape_ref\": \"" + ChainSelf + "\", " +
                "\"effects\": [{\"kind\": \"heal\", \"params\": {\"base_value\": " + Num(HealAmount) + ", \"coefficient\": 0}}]}"));

            source.Add("item.slot_definition", Table("item.slot_definition",
                "{\"id\": \"" + SlotConsumable + "\", \"name_key\": \"l10n.item.slot.p3use\", \"is_equipment\": false}"));
            source.Add("item.quality_definition", Table("item.quality_definition",
                "{\"id\": \"" + QualityId + "\", \"name_key\": \"l10n.item.quality.p3use\"}"));

            string Item(Id id, string extra) =>
                "{\"id\": \"" + id + "\", \"slot\": \"" + SlotConsumable + "\", \"quality\": \"" + QualityId + "\", " +
                "\"item_level\": 1, \"display_ref\": \"display.p3use\", \"stack_size\": 20, " +
                "\"name_key\": \"l10n.item.p3use\"" + extra + "}";

            source.Add("item.template", Table("item.template",
                Item(PotionHeal, ", \"on_use\": {\"skill_ref\": \"" + SkillHeal + "\"}") + "," +
                Item(PotionKeep, ", \"on_use\": {\"skill_ref\": \"" + SkillHeal + "\", \"consume\": false}") + "," +
                Item(Junk, "")));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "P3_UseItemIntentTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var world = new WorldSim(bus);
            var assembly = new CarriersAssembly(bus, registry, new RngHost(7), world, new StubSpatialQuery());
            world.AddEntity(new PlayerUnit(PlayerId, MapId, FactionId, ClassId) { Position = Vec2.Zero });
            world.AddEntity(new PlayerUnit(OtherId, MapId, FactionId, ClassId) { Position = new Vec2(5, 0) });
            foreach (var unit in new[] { PlayerId, OtherId })
            {
                assembly.Rules.RegisterUnit(unit, ClassId, null, 1);
                assembly.Inventory.RegisterUnit(unit);
            }

            bus.DispatchPending();

            var fx = new Fixture { Bus = bus, World = world, Assembly = assembly };
            assembly.Rules.Powers.ModifyPower(PlayerId, Health, -DamageTaken, new Id("system.test"));
            assembly.Inventory.AddItem(PlayerId, PotionHeal, PotionsHeld);
            assembly.Inventory.AddItem(PlayerId, PotionKeep, 1);
            assembly.Inventory.AddItem(PlayerId, Junk, 1);
            return fx;
        }

        // 复现：use_item 意图提交后，生命值应由 MaxHealth-DamageTaken 变为 +HealAmount，药水少 1。
        [Fact]
        public void UseItem_Intent_RunsOnUseSkill_AndConsumesExactlyOne()
        {
            using var fx = Build();
            var hpBefore = MaxHealth - DamageTaken;
            Assert.Equal(hpBefore, fx.Hp(PlayerId));
            Assert.Equal(PotionsHeld, fx.Assembly.Inventory.CountOf(PlayerId, PotionHeal));

            fx.Use(PlayerId, fx.InstanceOf(PlayerId, PotionHeal));

            Assert.Equal(hpBefore + HealAmount, fx.Hp(PlayerId));
            Assert.Equal(PotionsHeld - 1, fx.Assembly.Inventory.CountOf(PlayerId, PotionHeal));
        }

        // 不变量：技能被拒（冷却中）时不消耗物品；冷却结束后可再次使用。
        [Fact]
        public void UseItem_WhileSkillOnCooldown_DoesNotConsume_AndWorksAfterCooldown()
        {
            using var fx = Build();
            var id = fx.InstanceOf(PlayerId, PotionHeal);
            fx.Use(PlayerId, id);
            var hpAfterFirst = fx.Hp(PlayerId);
            var left = fx.Assembly.Inventory.CountOf(PlayerId, PotionHeal);

            fx.Use(PlayerId, id);

            Assert.Equal(hpAfterFirst, fx.Hp(PlayerId));
            Assert.Equal(left, fx.Assembly.Inventory.CountOf(PlayerId, PotionHeal));

            // 推进到冷却结束（冷却 PotionCooldown 秒）。
            for (var t = 0.0; t < PotionCooldown + 1; t += 1.0)
            {
                fx.World.Tick(SimStep.Continuous(1.0));
            }

            fx.Use(PlayerId, id);
            Assert.Equal(Math.Min(MaxHealth, hpAfterFirst + HealAmount), fx.Hp(PlayerId));
            Assert.Equal(left - 1, fx.Assembly.Inventory.CountOf(PlayerId, PotionHeal));
        }

        // 不变量：没有 on_use 的物品、不属于该单位背包的实例、不存在的实例，使用意图一律无副作用。
        [Fact]
        public void UseItem_WithoutOnUse_OrForeignInstance_HasNoEffect()
        {
            using var fx = Build();
            var hp = fx.Hp(PlayerId);
            var junkId = fx.InstanceOf(PlayerId, Junk);

            fx.Use(PlayerId, junkId);
            Assert.Equal(1, fx.Assembly.Inventory.CountOf(PlayerId, Junk));
            Assert.Equal(hp, fx.Hp(PlayerId));

            // 别人用我的药水实例：实例不在他的背包里，不得生效也不得消耗我的药水。
            fx.Use(OtherId, fx.InstanceOf(PlayerId, PotionHeal));
            Assert.Equal(PotionsHeld, fx.Assembly.Inventory.CountOf(PlayerId, PotionHeal));
            Assert.Equal(hp, fx.Hp(PlayerId));

            fx.Use(PlayerId, new Id("item.inst_does_not_exist"));
            Assert.Equal(hp, fx.Hp(PlayerId));
        }

        // consume=false：使用成功但不消耗（可重复使用的物品，如传送符/护符）。
        [Fact]
        public void UseItem_ConsumeFalse_RunsSkillButKeepsItem()
        {
            using var fx = Build();
            var hpBefore = fx.Hp(PlayerId);

            fx.Use(PlayerId, fx.InstanceOf(PlayerId, PotionKeep));

            Assert.Equal(hpBefore + HealAmount, fx.Hp(PlayerId));
            Assert.Equal(1, fx.Assembly.Inventory.CountOf(PlayerId, PotionKeep));
        }
    }
}

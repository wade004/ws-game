using System.Collections.Generic;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.ProgressionBridge;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;
using static Tests.Gameplay.ProgressionBridge.ProgressionBridgeTestSupport;

namespace Tests.Gameplay.ProgressionBridge
{
    /// <summary>
    /// T-N6-3b（ADR-0035 决策 3"生物模板按指定等级出生"）：验证经 6 参
    /// <c>Core.Carriers.Common.ICreatureFactory.Spawn(Id, Id, Vec2, double, Id?, int)</c> 按覆盖
    /// 等级出生的生物被击杀时，击杀经验按新等级（而不是 <c>creature.template.level</c>）计算——
    /// <see cref="CreatureDeathXpListener"/> 经 <see cref="IUnitAccess.GetLevel"/> 读到的是
    /// <see cref="CreatureFactory"/> 写入 <c>CreatureUnit.Level</c> 的出生等级覆盖值（见
    /// <c>CreatureFactory.SpawnCore</c> 判断记录）。本模块其余测试用
    /// <see cref="FakeCreatureTemplateQuery"/>/手工 <c>AddCreature</c> 隔离验证监听器自身逻辑；本用例
    /// 改用真实 <see cref="CreatureFactory"/> 串联验证"等级覆盖"确实能自然传导到"击杀经验"这一消费方
    /// （任务书原句"经验发放等消费单位等级的地方应自然读到新等级"）。
    /// </summary>
    public sealed class T_N6_3b_CreatureLevelOverrideKillXpTests
    {
        private const string KillSourceRows = @"[
            { ""id"": ""prog.xp_source.kill"", ""kind"": ""kill"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.pb"", ""level_diff_ref"": ""combat.level_diff.pb"" }
        ]";

        private const string TierRows =
            "[{\"id\": \"creature.tier.pb_override_normal\", " +
            "\"name_key\": \"l10n.creature.tier.pb_override_normal.name\", \"stat_multiplier\": 1}]";

        // 模板登记等级 1；本用例出生时按 6 参 Spawn 显式覆盖为 8 级——击杀经验应按 8 级计算，不是
        // 模板登记的 1 级（两者算出的经验数值差异很大，见下方用例注释里的手算，能明确区分"用对了
        // 覆盖等级"还是"仍在用模板等级"）。
        private const string TemplateRows =
            "[{\"id\": \"creature.pb_override_wolf\", \"name_key\": \"l10n.creature.pb_override_wolf.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.pb_override_normal\", \"base_stats\": {}, " +
            "\"faction_id\": \"fac.pb_monster\", \"display_ref\": \"display.pb_override_wolf\"}]";

        // PowerHost.RegisterUnit 不接受空的 powerTypes 集合（见该方法判断记录）——CreatureFactory
        // 未显式配置 CreatureOptions.DefaultPowerTypes 时回落到"数据集全部已登记 arch.power_type"，
        // 本用例因此至少登记一条（固定值资源池，不依赖 stat.definition 具体属性）。
        private const string PowerTypeRows = "[{\"id\": \"arch.power.pb_health\", " +
            "\"name_key\": \"l10n.power.pb_health.name\", \"max_source\": {\"kind\": \"fixed\", \"value\": 100}, " +
            "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
            "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}]";

        private static DataRegistry MakeCreatureRegistry(IEventBus bus)
        {
            // stat.definition 表本身为空（本用例模板 base_stats 为空，不需要任何具体属性定义）——
            // StatHost 构造期强制要求该表已加载（见 StatHost.LoadDefinitions 判断记录），因此仍需注册
            // schema + 加载一份空表，惯例同 core/carriers/creature/tests/CreatureTestSupport。
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", "[]"))
                .Add(PowerSchemas.PowerType.Name, Envelope(PowerSchemas.PowerType.Name, PowerTypeRows))
                .Add(CreatureSchemas.TierDefinition.Name, Envelope(CreatureSchemas.TierDefinition.Name, TierRows))
                .Add(CreatureSchemas.Template.Name, Envelope(CreatureSchemas.Template.Name, TemplateRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(PowerSchemas.PowerType);
            registry.RegisterSchema(CreatureSchemas.TierDefinition);
            registry.RegisterSchema(CreatureSchemas.Template);
            registry.RegisterValidationRule(new CreatureContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private static PowerHost MakePowerHost(DataRegistry registry, IEventBus bus, IStatHost stats)
        {
            var powerTypes = new List<PowerTypeDefinition>();
            foreach (var record in registry.GetAll(PowerSchemas.PowerType.Name))
            {
                powerTypes.Add(new PowerTypeDefinition(record));
            }
            return new PowerHost(powerTypes, bus, stats.GetStat);
        }

        [Fact]
        public void OnUnitDied_CreatureSpawnedWithLevelOverride_GrantsXpUsingOverriddenLevel()
        {
            var bus = NewEventBus();
            var progressionRegistry = MakeProgressionRegistry(bus, KillSourceRows);
            var progression = MakeProgressionHost(progressionRegistry, bus);

            // 玩家起始 3 级：与出生覆盖等级 8 的 Δ 恰好 = 5，命中 combat.level_diff.pb 登记的断点
            // （Δ=5→xp_factor=1.5，见 ProgressionBridgeTestSupport.MakeProgressionRegistry 判断
            // 记录），避免依赖曲线外推/夹取细节，手算结果确定。
            var playerId = new Id("unit.pb_override_player");
            progression.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 3);

            var world = NewWorld(bus);
            var units = new WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            var creatureRegistry = MakeCreatureRegistry(bus);
            var stats = new StatHost(creatureRegistry, bus);
            var powers = MakePowerHost(creatureRegistry, bus, stats);
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) => { };
            var factory = new CreatureFactory(creatureRegistry, world, bus, stats, powers, progression, units, registrar);

            // CreatureDeathXpListener 的 ICreatureTemplateQuery 参数直接传真实 CreatureFactory（它
            // 同时实现 ICreatureTemplateQuery），不需要额外的 Fake。
            _ = new CreatureDeathXpListener(bus, progression, units, factory);

            var creatureId = factory.Spawn(
                new Id("creature.pb_override_wolf"), new Id("map.pb"), new Vec2(1, 0), 0,
                ownerId: null, level: 8);

            Assert.Equal(8, units.GetLevel(creatureId)); // 出生态核对：确实按覆盖等级出生。

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: playerId));
            bus.DispatchPending();

            // prog.xp_base_curve.pb：Evaluate(x)=100x（两点线性 (1,100)-(11,1100)）→ 8 级
            // baseAmount=800。combat.level_diff.pb：Δ=8-3=5 → xp_factor=1.5。granted=800×1×1.5=1200。
            // 若监听器错误地仍按模板登记等级 1 计算，baseAmount=Evaluate(1)=100、Δ=1-3=-2（曲线内插，
            // 不是整 1200 这样的"整手算"结果），与本断言的 1200 明显不同，足以证伪。
            Assert.Equal(1200, progression.GetXp(playerId));
        }
    }
}

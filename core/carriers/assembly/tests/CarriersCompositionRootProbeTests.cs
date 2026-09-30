using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Gobj;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// 测试覆盖梳理 T-H8（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）："真实组合根"探针。
    /// <para>
    /// <c>create_item</c>/<c>open_lock</c>/<c>summon</c> 三个效果扩展各自的单测都用桩宿主，
    /// <see cref="CarriersAssembly"/> 构造期把它们经 <see cref="CompositeEffectExtension"/> 组合后
    /// 换入 <c>Rules.EffectExtension</c> 这一步的接线（顺序、换绑、宿主实例是否同一份）没有任何测试
    /// 覆盖——即 11 章 §6 勘误点名的"单测通过不能代表组合根正确"。本文件全部用真实
    /// <see cref="CarriersAssembly"/>（生产装配入口，不用桩宿主）+ 真实施法链路
    /// （<c>Rules.Skill.CastSkill</c> → <c>EffectDispatcher</c> → 组合扩展）施放三类效果，断言运行期
    /// 可观测结果：物品入包、锁状态被写为已解锁、召唤物实体出现且归属施法者。
    /// </para>
    /// <para>
    /// 数据全部内联最小构造（<c>probe_*</c> 命名），期望值一律由同一份常量（也就是写进数据里的那个
    /// 声明值）算出，不写裸数。
    /// </para>
    /// </summary>
    public sealed class CarriersCompositionRootProbeTests
    {
        private static readonly Id MapId = new Id("map.probe_root");
        private static readonly Id CasterId = new Id("unit.probe_caster");
        private static readonly Id FactionId = new Id("fac.probe_caster");
        private static readonly Id ClassId = new Id("arch.class.probe_sample");

        private static readonly Id ItemPotion = new Id("item.probe_potion");
        private static readonly Id ItemKey = new Id("item.probe_key");
        private static readonly Id SlotId = new Id("item.slot.probe_bag");
        private static readonly Id QualityId = new Id("item.quality.probe_common");

        private static readonly Id GobjDoorTemplate = new Id("gobj.probe_door");
        private static readonly Id LockId = new Id("gobj.lock.probe_iron");

        private static readonly Id CreatureTemplateId = new Id("creature.probe_minion");
        private static readonly Id TierId = new Id("creature.tier.probe_normal");

        private static readonly Id ChainSelf = new Id("target.chain.probe_self");
        private static readonly Id SkillCreateItem = new Id("skill.probe_create_item");
        private static readonly Id SkillOpenLock = new Id("skill.probe_open_lock");
        private static readonly Id SkillSummon = new Id("skill.probe_summon");
        private static readonly Id SkillSetWorldFlag = new Id("skill.probe_set_world_flag");

        /// <summary>写进 <c>create_item</c> 效果 <c>params.count</c> 的声明值；断言的期望物品数量由它算出。</summary>
        private const int DeclaredItemCount = 3;

        /// <summary>写进 <c>summon</c> 效果 <c>params.position</c> 的声明坐标；断言的期望落点由它算出。</summary>
        private static readonly Vec2 DeclaredSummonPosition = new Vec2(4, 2);

        /// <summary>写进 <c>summon</c> 效果 <c>params.duration</c> 的声明时长（秒）。</summary>
        private const double DeclaredSummonDuration = 30;

        /// <summary>写进 <c>skill.probe_set_world_flag</c> 的声明标志键；断言的期望诊断由它算出。</summary>
        private static readonly Id DeclaredFlagKey = new Id("world.probe_flag");

        /// <summary>内存版 <see cref="IWorldFlags"/>（L4 回调的测试实现）：<see cref="CarriersAssembly"/>
        /// 未注入时退化为 <see cref="NullWorldFlags"/>（读恒 null），无法观测开锁写入的
        /// <c>unlocked</c> 状态，故注入这份实现。</summary>
        private sealed class ProbeWorldFlags : IWorldFlags
        {
            public readonly Dictionary<Id, ExprValue> Values = new Dictionary<Id, ExprValue>();

            public ExprValue? Get(Id flagKey) => Values.TryGetValue(flagKey, out var v) ? v : (ExprValue?)null;

            public void Set(Id flagKey, ExprValue value, Id writerId) => Values[flagKey] = value;

            public bool Has(Id flagKey) => Values.ContainsKey(flagKey);
        }

        private sealed class Fixture : IDisposable
        {
            public IEventBus Bus = null!;
            public DataRegistry Registry = null!;
            public WorldSim World = null!;
            public CarriersAssembly Assembly = null!;
            public ProbeWorldFlags Flags = null!;
            public PlayerUnit Caster = null!;

            public void Dispose() => World.Dispose();

            public IReadOnlyList<string> SkillWarnings =>
                ((InMemorySkillDiagnostics)Assembly.Rules.Skill.Diagnostics).Warnings;

            public CastResult Cast(Id skillId, params Id[] explicitTargets)
            {
                var result = Assembly.Rules.Skill.CastSkill(CasterId, skillId, explicitTargets);
                Bus.DispatchPending();
                return result;
            }
        }

        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();

            // 三张"必须已加载（哪怕零行）"的前置表，惯例同 CarriersAssemblyTests.AddMinimalRequiredTables。
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            // summon 需要 creature.template：生物出生走 StatHost/PowerHost 注册，须有血量属性与能量类型。
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.probe_max_health\", \"name_key\": \"l10n.stat.probe_max_health.name\", " +
                "\"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"arch.power.probe_health\", \"name_key\": \"l10n.power.probe_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.probe_max_health\"}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]}");
            source.Add("fac.faction",
                "{\"table\": \"fac.faction\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + FactionId + "\", \"name_key\": \"l10n.fac.probe_caster.name\", \"default_reaction\": \"friendly\"}" +
                "]}");
            source.Add("fac.reaction_matrix", "{\"table\": \"fac.reaction_matrix\", \"schema_version\": 1, \"rows\": []}");
            source.Add(CreatureSchemas.TierDefinition.Name,
                "{\"table\": \"" + CreatureSchemas.TierDefinition.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + TierId + "\", \"name_key\": \"l10n.creature.tier.probe_normal.name\", \"stat_multiplier\": 1}" +
                "]}");
            source.Add(CreatureSchemas.Template.Name,
                "{\"table\": \"" + CreatureSchemas.Template.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + CreatureTemplateId + "\", \"name_key\": \"l10n.creature.probe_minion.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.probe_max_health\": 100}, " +
                "\"faction_id\": \"" + FactionId + "\", \"display_ref\": \"display.probe_minion\"}" +
                "]}");

            // create_item / open_lock 需要的物品：药水（可堆叠，装得下 DeclaredItemCount）与钥匙。
            source.Add("item.slot_definition",
                "{\"table\": \"item.slot_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + SlotId + "\", \"name_key\": \"l10n.item.slot.probe_bag\", \"is_equipment\": false}" +
                "]}");
            source.Add("item.quality_definition",
                "{\"table\": \"item.quality_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + QualityId + "\", \"name_key\": \"l10n.item.quality.probe_common\"}" +
                "]}");
            source.Add("item.template",
                "{\"table\": \"item.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + ItemPotion + "\", \"slot\": \"" + SlotId + "\", \"quality\": \"" + QualityId + "\", " +
                "\"item_level\": 1, \"display_ref\": \"display.probe_potion\", \"stack_size\": 20, " +
                "\"name_key\": \"l10n.item.probe_potion\"}," +
                "{\"id\": \"" + ItemKey + "\", \"slot\": \"" + SlotId + "\", \"quality\": \"" + QualityId + "\", " +
                "\"item_level\": 1, \"display_ref\": \"display.probe_key\", \"stack_size\": 1, " +
                "\"name_key\": \"l10n.item.probe_key\"}" +
                "]}");

            // open_lock：一扇需要 ItemKey 且开锁后消耗钥匙的门。
            source.Add("gobj.lock",
                "{\"table\": \"gobj.lock\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + LockId + "\", \"requirement\": {\"kind\": \"item_key\", \"item_id\": \"" + ItemKey + "\"}, " +
                "\"consume_key\": true}" +
                "]}");
            source.Add("gobj.template",
                "{\"table\": \"gobj.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + GobjDoorTemplate + "\", \"name_key\": \"l10n.gobj.probe_door.name\", \"kind\": \"door\", " +
                "\"type_data\": {}, \"lock_id\": \"" + LockId + "\", \"display_ref\": \"display.probe_door\"}" +
                "]}");

            source.Add("target.chain_def",
                "{\"table\": \"target.chain_def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + ChainSelf + "\", \"source\": \"self\", \"max_targets\": 1}" +
                "]}");

            string Skill(Id id, string effectJson) =>
                "{\"id\": \"" + id + "\", \"school\": \"school.probe\", \"kind\": \"active\", " +
                "\"range\": 0, \"cast_time\": 0, \"respects_gcd\": false, \"target_shape_ref\": \"" + ChainSelf + "\", " +
                "\"effects\": [" + effectJson + "]}";

            var vec = DeclaredSummonPosition;
            source.Add("skill.def",
                "{\"table\": \"skill.def\", \"schema_version\": 1, \"rows\": [" +
                Skill(SkillCreateItem,
                    "{\"kind\": \"create_item\", \"params\": {\"item_template\": \"" + ItemPotion + "\", " +
                    "\"count\": " + DeclaredItemCount + "}}") + "," +
                Skill(SkillOpenLock, "{\"kind\": \"open_lock\"}") + "," +
                Skill(SkillSummon,
                    "{\"kind\": \"summon\", \"params\": {\"creature_template\": \"" + CreatureTemplateId + "\", " +
                    "\"duration\": " + DeclaredSummonDuration.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " +
                    "\"position\": {\"x\": " + vec.X.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ", \"y\": " + vec.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}}") + "," +
                // set_world_flag：生产装配的三个扩展（item/gobj/summon）都不认这一类，
                // L4 的 WorldState 扩展没有接入——恰是"没有任何扩展处理"的真实例子。
                Skill(SkillSetWorldFlag,
                    "{\"kind\": \"set_world_flag\", \"params\": {\"flag_key\": \"" + DeclaredFlagKey + "\"}}") +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "CarriersCompositionRootProbeTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var world = new WorldSim(bus);
            var flags = new ProbeWorldFlags();
            var assembly = new CarriersAssembly(
                bus, registry, new RngHost(1), world, new StubSpatialQuery(), navigation: null,
                spatialSyncKinds: null, worldFlags: flags);

            var caster = new PlayerUnit(CasterId, MapId, FactionId, ClassId) { Position = Vec2.Zero };
            world.AddEntity(caster);
            bus.DispatchPending();

            return new Fixture
            {
                Bus = bus, Registry = registry, World = world, Assembly = assembly, Flags = flags, Caster = caster,
            };
        }

        // -----------------------------------------------------------------
        // 真实组合根 × 真实施法链路：三类效果各一条端到端用例
        // -----------------------------------------------------------------

        [Fact]
        public void CreateItem_ViaRealAssemblyCast_PutsDeclaredCountIntoCasterInventory()
        {
            using var fx = Build();
            Assert.Equal(0, fx.Assembly.Inventory.CountOf(CasterId, ItemPotion));

            var result = fx.Cast(SkillCreateItem);

            Assert.True(result.Success, $"施法应成功：{result.Reason}");
            // 期望数量 = 效果声明的 count；且只落在施法者（chain=self 的目标）背包里。
            Assert.Equal(DeclaredItemCount, fx.Assembly.Inventory.CountOf(CasterId, ItemPotion));
            var stacks = fx.Assembly.Inventory.ListItems(CasterId).Where(i => i.TemplateId.Equals(ItemPotion)).ToList();
            Assert.Equal(DeclaredItemCount, stacks.Sum(i => i.Count));
            // 没走到"未被扩展处理"的降级分支。
            Assert.DoesNotContain(fx.SkillWarnings, w => w.Contains("未被 IEffectExtension 处理"));
        }

        [Fact]
        public void OpenLock_ViaRealAssemblyCast_WithKey_UnlocksDoor_AndConsumesKey()
        {
            using var fx = Build();
            var doorId = fx.Assembly.GameObjects.SpawnFromTemplate(fx.Registry, GobjDoorTemplate, MapId, new Vec2(1, 0), facing: 0);
            Assert.Equal(LockId, ((GameObjectEntity)fx.World.GetEntity(doorId)!).LockId);
            var unlockedKey = GobjStateKeys.For(doorId, "unlocked");
            Assert.False(fx.Flags.Has(unlockedKey));

            const int keysHeld = 1;
            Assert.True(fx.Assembly.Inventory.AddItem(CasterId, ItemKey, keysHeld));

            var result = fx.Cast(SkillOpenLock, doorId);

            Assert.True(result.Success, $"施法应成功：{result.Reason}");
            // 锁状态被真实 GameObjectHost 写为已解锁（经注入的 IWorldFlags 可观测）。
            Assert.True(fx.Flags.Has(unlockedKey));
            Assert.True(fx.Flags.Get(unlockedKey)!.Value.AsBool);
            // 锁声明 consume_key=true：钥匙数量 = 持有数 - 消耗 1 把。
            Assert.Equal(keysHeld - 1, fx.Assembly.Inventory.CountOf(CasterId, ItemKey));
            // 再次开锁不再需要钥匙（已解锁短路），证明状态确实落在同一个宿主实例上。
            Assert.True(fx.Assembly.GameObjectInteractions.TryUnlock(CasterId, doorId));
            Assert.DoesNotContain(fx.SkillWarnings, w => w.Contains("未被 IEffectExtension 处理"));
        }

        [Fact]
        public void OpenLock_ViaRealAssemblyCast_WithoutKey_LeavesDoorLocked()
        {
            // 反向对照：防止上一条用例的"已解锁"断言在组合根接线错误（例如 open_lock 被别的扩展吞掉后
            // 状态本来就恒为真）时空过。
            using var fx = Build();
            var doorId = fx.Assembly.GameObjects.SpawnFromTemplate(fx.Registry, GobjDoorTemplate, MapId, new Vec2(1, 0), facing: 0);
            var unlockedKey = GobjStateKeys.For(doorId, "unlocked");

            fx.Cast(SkillOpenLock, doorId);

            Assert.False(fx.Flags.Has(unlockedKey));
            Assert.False(fx.Assembly.GameObjectInteractions.TryUnlock(CasterId, doorId));
        }

        [Fact]
        public void Summon_ViaRealAssemblyCast_SpawnsCreatureOwnedByCasterAtDeclaredPosition()
        {
            using var fx = Build();
            var before = fx.World.QueryEntities(new EntityFilter(kind: EntityKinds.Creature)).Count;
            Assert.Empty(fx.Assembly.Summons.GetSummons(CasterId));

            var result = fx.Cast(SkillSummon);

            Assert.True(result.Success, $"施法应成功：{result.Reason}");
            // 召唤物出现在世界：生物实体数量比施法前恰好多 1。
            var creatures = fx.World.QueryEntities(new EntityFilter(kind: EntityKinds.Creature));
            Assert.Equal(before + 1, creatures.Count);
            // 归属施法者：SummonHost 登记与生物实体自身的 OwnerId 一致。
            var summonId = Assert.Single(fx.Assembly.Summons.GetSummons(CasterId));
            Assert.Equal(CasterId, fx.Assembly.Summons.GetOwner(summonId));
            var entity = Assert.IsType<CreatureUnit>(fx.World.GetEntity(summonId));
            Assert.Equal(CasterId, entity.OwnerId);
            Assert.Equal(CreatureTemplateId, entity.TemplateId);
            // 落点 = 效果声明的 position（不是默认的施法者朝向偏移）。
            Assert.Equal(DeclaredSummonPosition, entity.Position);
            Assert.Equal(DeclaredSummonPosition, fx.Assembly.Units.GetPosition(summonId));
            Assert.DoesNotContain(fx.SkillWarnings, w => w.Contains("未被 IEffectExtension 处理"));
        }

        // -----------------------------------------------------------------
        // 没有任何扩展处理的效果类型（真实组合根）
        // -----------------------------------------------------------------

        [Fact]
        public void SetWorldFlag_ViaRealAssemblyCast_HandledByNoExtension_WarnsAndCastSucceedsWithoutSideEffects()
        {
            // 语义（EffectDispatcher.ApplyExtension）：组合扩展返回 false → 记一条 Warn 诊断、按 NoOp
            // 返回；不抛异常，施法整体仍成功。三类已接线扩展的副作用都不应被误触发。
            using var fx = Build();

            var result = fx.Cast(SkillSetWorldFlag);

            Assert.True(result.Success, $"施法应成功：{result.Reason}");
            var warning = Assert.Single(fx.SkillWarnings, w => w.Contains("未被 IEffectExtension 处理"));
            Assert.Contains($"EffectKind.{EffectKind.SetWorldFlag}", warning);
            Assert.Empty(fx.Assembly.Summons.GetSummons(CasterId));
            Assert.Equal(0, fx.Assembly.Inventory.CountOf(CasterId, ItemPotion));
            Assert.Empty(fx.Flags.Values);
        }

        // -----------------------------------------------------------------
        // CompositeEffectExtension 顺序语义（纯组合逻辑，探针扩展记录调用顺序）
        // -----------------------------------------------------------------

        private sealed class SpyExtension : IEffectExtension
        {
            private readonly string _name;
            private readonly Func<EffectContext, bool> _accepts;
            private readonly List<string> _log;

            public SpyExtension(string name, Func<EffectContext, bool> accepts, List<string> log)
            {
                _name = name;
                _accepts = accepts;
                _log = log;
            }

            public ResolveResult? LastResult { get; private set; }

            public bool TryHandle(EffectContext context, out ResolveResult result)
            {
                _log.Add(_name);
                if (_accepts(context))
                {
                    // 用 requestedAmount 标记"是谁处理的"，便于断言落到了哪一个扩展。
                    result = new ResolveResult(
                        HitResult.Hit, requestedAmount: _name.Length, finalAmount: 0, absorbed: 0,
                        immune: false, isHeal: false);
                    LastResult = result;
                    return true;
                }

                result = null!;
                return false;
            }
        }

        private static EffectContext ContextOf(EffectKind kind) =>
            new EffectContext(CasterId, CasterId, SkillCreateItem, kind, new Id("school.probe"), 0, 0);

        [Fact]
        public void Composite_FirstDeclines_FallsThroughToNext_AndStopsAtFirstAcceptor()
        {
            var log = new List<string>();
            var first = new SpyExtension("A", c => c.Kind == EffectKind.CreateItem, log);
            var second = new SpyExtension("BB", c => c.Kind == EffectKind.OpenLock, log);
            var third = new SpyExtension("CCC", c => c.Kind == EffectKind.OpenLock || c.Kind == EffectKind.Summon, log);
            var composite = new CompositeEffectExtension(first, second, third);

            // OpenLock：A 不认 -> 落到 B 处理 -> C 不应再被询问。
            var handled = composite.TryHandle(ContextOf(EffectKind.OpenLock), out var result);

            Assert.True(handled);
            Assert.Equal(new[] { "A", "BB" }, log);
            Assert.Equal("BB".Length, result.RequestedAmount);

            // Summon：A、B 都不认 -> 落到第三个。
            log.Clear();
            handled = composite.TryHandle(ContextOf(EffectKind.Summon), out result);

            Assert.True(handled);
            Assert.Equal(new[] { "A", "BB", "CCC" }, log);
            Assert.Equal("CCC".Length, result.RequestedAmount);
        }

        [Fact]
        public void Composite_WhenSeveralCouldHandle_EarlierExtensionWins_LaterNotConsulted()
        {
            // 顺序即优先级：两个扩展都认同一类型时，先传入者生效，后者不被调用。
            var log = new List<string>();
            var first = new SpyExtension("A", c => c.Kind == EffectKind.Summon, log);
            var second = new SpyExtension("BB", c => c.Kind == EffectKind.Summon, log);
            var composite = new CompositeEffectExtension(first, second);

            var handled = composite.TryHandle(ContextOf(EffectKind.Summon), out var result);

            Assert.True(handled);
            Assert.Equal(new[] { "A" }, log);
            Assert.Equal("A".Length, result.RequestedAmount);
            Assert.Null(second.LastResult);
        }

        [Fact]
        public void Composite_WhenNoExtensionHandlesKind_ReturnsFalse_ConsultsAllInOrder_DoesNotThrow()
        {
            var log = new List<string>();
            var composite = new CompositeEffectExtension(
                new SpyExtension("A", _ => false, log),
                new SpyExtension("BB", _ => false, log),
                new SpyExtension("CCC", _ => false, log));

            var handled = composite.TryHandle(ContextOf(EffectKind.SetWorldFlag), out var result);

            Assert.False(handled);
            Assert.Null(result);
            Assert.Equal(new[] { "A", "BB", "CCC" }, log);
        }

        [Fact]
        public void Composite_WithoutAnyExtension_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new CompositeEffectExtension());
            Assert.Throws<ArgumentException>(() => new CompositeEffectExtension(Array.Empty<IEffectExtension>()));
        }
    }
}

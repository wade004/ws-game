using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <see cref="ICreatureFactory"/> 的默认实现（见 07 第 2 节 Creature 补充信息表）：按
    /// <c>creature.template</c> 生成/移除 <see cref="CreatureUnit"/> 实例，串联 L1（StatBlock/
    /// PowerSet/Progression）与 L2（AI，经 <see cref="AiRegistrar"/> 委托）完成一整套"读模板 →
    /// 装配单位"流程（见 <c>core/carriers/common/contracts/ICreatureFactory.cs</c> 顶部注释
    /// "core/gameplay/spawn（L4）与 ISummonHost 的实现均可复用本接口生成生物实体"）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性解析 <c>creature.template</c>/<c>creature.tier_definition</c>/
    /// <c>prog.level_curve</c>（成长曲线口径同 <c>Core.Numbers.Progression.ProgressionHost</c>，
    /// 见 <see cref="ApplyStats"/> 判断记录）三张表，之后只读，惯例同 <c>AiHost</c>/<c>StatHost</c>。
    /// </summary>
    public sealed class CreatureFactory : ICreatureFactory, ICreatureTemplateQuery
    {
        private sealed class TierInfo
        {
            public double StatMultiplier;
            public bool ControlImmune;
        }

        private sealed class GrowthCurveInfo
        {
            public int MaxLevel;
            public List<IReadOnlyDictionary<string, double>> LevelGrowth = new List<IReadOnlyDictionary<string, double>>();
        }

        private readonly IDataRegistryView _registry;
        private readonly IWorldSim _world;
        private readonly IEventBus _bus;
        private readonly IStatHost _stats;
        private readonly IPowerHost _powers;
        private readonly IProgressionHost _progression;
        private readonly IUnitAccess _units;
        private readonly AiRegistrar _aiRegistrar;
        private readonly CreatureOptions _options;

        private readonly Dictionary<string, CreatureTemplate> _templates = new Dictionary<string, CreatureTemplate>(StringComparer.Ordinal);
        private readonly Dictionary<string, TierInfo> _tiers = new Dictionary<string, TierInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, GrowthCurveInfo> _growthCurves = new Dictionary<string, GrowthCurveInfo>(StringComparer.Ordinal);

        public CreatureFactory(
            IDataRegistryView registry,
            IWorldSim world,
            IEventBus bus,
            IStatHost stats,
            IPowerHost powers,
            IProgressionHost progression,
            IUnitAccess units,
            AiRegistrar aiRegistrar,
            CreatureOptions? options = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _aiRegistrar = aiRegistrar ?? throw new ArgumentNullException(nameof(aiRegistrar));
            _options = options ?? new CreatureOptions();

            LoadTiers(_registry);
            LoadTemplates(_registry);
            LoadGrowthCurves(_registry);
        }

        // -----------------------------------------------------------------
        // ICreatureFactory
        // -----------------------------------------------------------------

        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null)
        {
            var template = RequireTemplate(templateId);
            var tier = RequireTier(template.TierId);

            var entityId = _world.AllocateEntityId("creature");
            var unit = new CreatureUnit(entityId, mapId, template.FactionId, templateId)
            {
                Position = position,
                Facing = facing,
                Level = template.Level,
                LootTableId = template.LootTableRef,
                OwnerId = ownerId,
            };

            foreach (var flag in template.NpcFlags)
            {
                unit.NpcFlags.Add(NpcFlagIds.ToId(flag));
                unit.Tags.Add(NpcFlagIds.ToTag(flag));
            }

            for (var i = 0; i < template.Immunities.Count; i++)
            {
                unit.Immunities.Add(template.Immunities[i]);
            }

            if (tier.ControlImmune && _options.ImmunityTagPrefix.HasValue)
            {
                var controlImmuneTag = _options.ImmunityTagPrefix.Value;
                if (!unit.Immunities.Contains(controlImmuneTag))
                {
                    unit.Immunities.Add(controlImmuneTag);
                }
            }

            unit.Tags.Add(template.TierId);

            _world.AddEntity(unit);
            // 冗余但幂等地再写一次位置：经注入的 IUnitAccess（如 WorldUnitAccess）写入，触发其可能
            // 附带的空间索引同步（见 core/carriers/unit README 判断记录 4"WorldUnitAccess.SetPosition
            // 写入新位置后同步登记"）——直接写 unit.Position 不会触碰任何空间索引。
            _units.SetPosition(entityId, position);

            _stats.RegisterUnit(entityId);
            ApplyStats(entityId, template, tier);

            if (template.StatGrowthRef.HasValue)
            {
                _progression.RegisterUnit(entityId, template.StatGrowthRef.Value, template.Level);
            }

            _powers.RegisterUnit(entityId, _options.DefaultPowerTypes);

            if (template.AiBehaviorRef.HasValue)
            {
                _aiRegistrar(entityId, template.AiBehaviorRef.Value, position, template.AiRotationRef);
            }

            _bus.Enqueue(new CreatureSpawnedEvent(entityId, templateId));

            return entityId;
        }

        public void Despawn(Id entityId, string reason)
        {
            if (reason == null) throw new ArgumentNullException(nameof(reason));

            _world.MarkForDestruction(entityId);

            _stats.UnregisterUnit(entityId);
            _powers.UnregisterUnit(entityId);

            _bus.Enqueue(new CreatureDespawnedEvent(entityId, reason));
        }

        // -----------------------------------------------------------------
        // ICreatureTemplateQuery
        // -----------------------------------------------------------------

        public CreatureTemplate Get(Id templateId) => RequireTemplate(templateId);

        public bool HasFlag(Id templateId, NpcFlag flag)
        {
            var flags = RequireTemplate(templateId).NpcFlags;
            for (var i = 0; i < flags.Count; i++)
            {
                if (flags[i] == flag)
                {
                    return true;
                }
            }
            return false;
        }

        // -----------------------------------------------------------------
        // 属性装配
        // -----------------------------------------------------------------

        /// <summary>
        /// 按 <c>base_stats × tier.stat_multiplier</c> 算出各属性基础值，<paramref name="template"/>
        /// 挂了 <c>stat_growth_ref</c> 时再叠加"从 2 级累加到 <c>template.Level</c>"的曲线成长量
        /// （与 <c>Core.Numbers.Progression.ProgressionHost.ApplyGrowth</c> 同一口径：升 1 级只在
        /// 曲线 <c>entries[level-1].growth</c> 生效，1 级本身没有成长增量），最终按属性 id 调用一次
        /// <see cref="IStatHost.SetBase"/>（<see cref="IStatHost.SetBase"/> 整体替换基础值而非叠加，
        /// 因此这里先在内存里把 tier 倍率与成长量汇总成同一份"最终基础值"再一次性写入，不是先
        /// SetBase 一次基础值、再另外调一次成长）。
        /// </summary>
        private void ApplyStats(Id unitId, CreatureTemplate template, TierInfo tier)
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var kv in template.BaseStats)
            {
                var key = kv.Key.Value;
                totals[key] = kv.Value * tier.StatMultiplier;
                order.Add(key);
            }

            if (template.StatGrowthRef.HasValue)
            {
                var curve = RequireGrowthCurve(template.StatGrowthRef.Value);
                var maxLevel = template.Level < curve.MaxLevel ? template.Level : curve.MaxLevel;

                for (var level = 2; level <= maxLevel; level++)
                {
                    var growth = curve.LevelGrowth[level - 1];
                    foreach (var kv in growth)
                    {
                        if (!totals.ContainsKey(kv.Key))
                        {
                            totals[kv.Key] = 0;
                            order.Add(kv.Key);
                        }
                        totals[kv.Key] += kv.Value;
                    }
                }
            }

            for (var i = 0; i < order.Count; i++)
            {
                var statKey = order[i];
                _stats.SetBase(unitId, new Id(statKey), totals[statKey]);
            }
        }

        // -----------------------------------------------------------------
        // 数据加载
        // -----------------------------------------------------------------

        private void LoadTemplates(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(CreatureSchemas.Template.Name))
            {
                var template = CreatureTemplate.FromRecord(record);
                _templates[template.Id.Value] = template;
            }
        }

        private void LoadTiers(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(CreatureSchemas.TierDefinition.Name))
            {
                var id = record.GetId("id");
                var statMultiplier = record.TryGetNumber("stat_multiplier", out var multiplier) ? multiplier : 1.0;
                var controlImmune = record.TryGetBool("control_immune", out var immune) && immune;

                _tiers[id.Value] = new TierInfo { StatMultiplier = statMultiplier, ControlImmune = controlImmune };
            }
        }

        /// <summary>独立解析 <c>prog.level_curve</c> 用于成长累加（见 <see cref="ApplyStats"/>）。
        /// 判断记录（与 <c>Core.Numbers.Progression.ProgressionHost</c> 重复解析同一张表）：
        /// <c>IProgressionHost</c> 契约本身不暴露"给定曲线与等级，返回累计成长量"这一查询（只有
        /// <see cref="IProgressionHost.RegisterUnit"/> 这样的写操作，且刻意不在 RegisterUnit 内隐式
        /// 写成长，见该接口注释判断记录），本模块需要在装配基础属性这一步就拿到最终数值（用于
        /// <see cref="IPowerHost.RegisterUnit"/> 之前，供 <c>max_source: stat</c> 的资源类型正确
        /// 计算上限），因此独立按 <c>prog.level_curve</c> 的既定结构（04 第 1.1 节 + 04 未给出的
        /// entries 结构，见 <c>ProgSchemas.LevelCurve</c> 描述）解析一份只读索引，不依赖
        /// <c>ProgressionHost</c> 内部实现。</summary>
        private void LoadGrowthCurves(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll("prog.level_curve"))
            {
                var id = record.GetId("id");
                var maxLevel = (int)record.GetInt("max_level");
                var entries = record.GetArray("entries");

                var levelGrowth = new List<IReadOnlyDictionary<string, double>>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                {
                    var growth = new Dictionary<string, double>(StringComparer.Ordinal);
                    if (entries[i] is JsonObject entryObj &&
                        entryObj.TryGetValue("growth", out var growthValue) &&
                        growthValue is JsonObject growthObj)
                    {
                        foreach (var kv in growthObj)
                        {
                            if (kv.Value is JsonNumber num)
                            {
                                growth[kv.Key] = num.Value;
                            }
                        }
                    }
                    levelGrowth.Add(growth);
                }

                _growthCurves[id.Value] = new GrowthCurveInfo { MaxLevel = maxLevel, LevelGrowth = levelGrowth };
            }
        }

        // -----------------------------------------------------------------
        // 内部辅助
        // -----------------------------------------------------------------

        private CreatureTemplate RequireTemplate(Id templateId)
        {
            if (_templates.TryGetValue(templateId.Value, out var template))
            {
                return template;
            }
            throw new ArgumentException($"未知的 creature.template \"{templateId}\"", nameof(templateId));
        }

        private TierInfo RequireTier(Id tierId)
        {
            if (_tiers.TryGetValue(tierId.Value, out var tier))
            {
                return tier;
            }
            throw new ArgumentException($"未知的 creature.tier_definition \"{tierId}\"", nameof(tierId));
        }

        private GrowthCurveInfo RequireGrowthCurve(Id curveId)
        {
            if (_growthCurves.TryGetValue(curveId.Value, out var curve))
            {
                return curve;
            }
            throw new ArgumentException($"未知的 prog.level_curve \"{curveId}\"（creature.template.stat_growth_ref 引用）", nameof(curveId));
        }
    }
}

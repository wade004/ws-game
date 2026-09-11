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
    /// <para>
    /// 判断记录（消费方反馈第 33 条，资源类型注册顺序）：<see cref="Spawn"/> 此前恒按
    /// <see cref="CreatureOptions.DefaultPowerTypes"/> 的旧默认值 <c>[WellKnownPowers.Health]</c>
    /// 向 <see cref="IPowerHost.RegisterUnit"/> 注册资源类型，与模板/数据集实际定义的资源类型无关
    /// ——示例技能消耗 <c>arch.power.mana</c> 时，生成的生物从未注册过 <c>mana</c>，
    /// <see cref="IPowerHost.GetPower"/> 必然抛异常。<see cref="ResolvePowerTypes"/> 改为按
    /// <see cref="CreatureOptions.DefaultPowerTypes"/> 的新语义（见该属性判断记录）解析：显式覆盖
    /// 时优先生效；否则尝试"模板/职业实际声明或引用的资源类型"（<c>creature.template</c> 当前未
    /// 登记此类字段，本步骤恒空——为未来该表补充职业/资源类型引用字段预留扩展点，届时只需要在
    /// <see cref="ResolvePowerTypes"/> 内补一段读取逻辑，不需要改动本方法的调用点或
    /// <see cref="IPowerHost"/> 契约本身）；再否则回落到构造期解析好的
    /// <see cref="_allPowerTypeIds"/>（当前数据集 <c>arch.power_type</c> 全部已登记 id，按升序保证
    /// 确定性——<see cref="IPowerHost.RegisterUnit"/> 按传入顺序确定性初始化，见该方法契约注释）。
    /// </para>
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

        /// <summary>消费方反馈第 33 条：当前数据集 <c>arch.power_type</c> 全部已登记 id，按升序
        /// 排列，见 <see cref="ResolvePowerTypes"/>/<see cref="LoadPowerTypeIds"/> 判断记录。独立
        /// 解析（不依赖 <c>PowerHost</c> 内部实现），惯例同 <see cref="LoadGrowthCurves"/> 判断记录
        /// "本模块需要……因此独立按既定结构解析一份只读索引，不依赖……内部实现"。</summary>
        private readonly List<Id> _allPowerTypeIds = new List<Id>();

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
            LoadPowerTypeIds(_registry);
        }

        // -----------------------------------------------------------------
        // ICreatureFactory
        // -----------------------------------------------------------------

        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null)
        {
            var template = RequireTemplate(templateId);
            var tier = RequireTier(template.TierId);

            var entityId = _world.AllocateEntityId(EntityKinds.Creature);
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

            _powers.RegisterUnit(entityId, ResolvePowerTypes(template));

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
        // 资源类型注册顺序（消费方反馈第 33 条）
        // -----------------------------------------------------------------

        /// <summary>按 <see cref="CreatureOptions.DefaultPowerTypes"/> 的语义解析
        /// <paramref name="template"/> 生成的单位实际应注册的资源类型集合，见类型顶部判断记录。</summary>
        private IReadOnlyList<Id> ResolvePowerTypes(CreatureTemplate template)
        {
            if (_options.DefaultPowerTypes != null)
            {
                return _options.DefaultPowerTypes;
            }

            // 步骤①：模板/职业实际声明或引用的资源类型。creature.template 当前未登记此类字段
            // （07 第 2.1 节字段表无对应项），本步骤恒空——为未来该表补充职业/资源类型引用字段
            // 预留扩展点，届时只需要在此处补一段读取逻辑（如 template.PowerTypes 非空时直接
            // 返回），不需要改动调用点或 IPowerHost 契约本身。忽略未使用参数警告：template 已经
            // 是本步骤将来读取的对象，提前接收它可以避免届时改签名。
            _ = template;

            // 步骤②：回落到当前数据集里全部已登记的 arch.power_type 定义（见
            // _allPowerTypeIds/LoadPowerTypeIds 判断记录）。
            return _allPowerTypeIds;
        }

        /// <summary>见 <see cref="_allPowerTypeIds"/> 判断记录：独立解析 <c>arch.power_type</c>
        /// 全部已登记 id，不依赖 <c>PowerHost</c> 内部实现——本方法与
        /// <c>Core.Rules.Assembly.RulesAssembly</c> 装配 <c>PowerHost</c> 时遍历同一张表的顺序无
        /// 强制一致性要求（<see cref="IPowerHost.RegisterUnit"/> 只要求传入的 id 集合是
        /// <c>PowerHost</c> 构造期已登记 id 的子集，不要求顺序与之相同），因此本方法按 id
        /// 升序排列，保证同一次加载内多次调用 <see cref="ResolvePowerTypes"/> 结果确定。表未注册
        /// （<c>GetAll</c> 对未知表名返回空列表，不抛异常，见 <c>DataRegistry.GetAllUnchecked</c>
        /// 判断记录）或数据集本身没有登记任何资源类型时，<see cref="_allPowerTypeIds"/> 为空列表——
        /// <see cref="ResolvePowerTypes"/> 据此向 <see cref="IPowerHost.RegisterUnit"/> 传入空集合，
        /// 等价于该单位不持有任何资源池，不抛异常（与 06 第 2.1 节"资源类型可配置数量……含 0 种"的
        /// 既有语义一致）。</summary>
        private void LoadPowerTypeIds(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll("arch.power_type"))
            {
                _allPowerTypeIds.Add(record.GetId("id"));
            }
            _allPowerTypeIds.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
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

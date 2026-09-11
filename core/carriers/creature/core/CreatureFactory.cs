using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
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
    /// <see cref="IDataRegistryView"/> 一次性解析 <c>creature.template</c>/<c>creature.tier_definition</c>
    /// 两张表，之后只读，惯例同 <c>AiHost</c>/<c>StatHost</c>。
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
    /// <para>
    /// 判断记录（消费方反馈第 36 条根治，成长不再由本类型承载）：此前 <see cref="ApplyStats"/> 自行
    /// 重复解析 <c>prog.level_curve</c>，把"2 级到出生等级"的累计成长直接叠进
    /// <see cref="IStatHost.SetBase"/> 写的基础值——该单位一旦经
    /// <see cref="Core.Numbers.Progression.IProgressionHost.AddXp"/> 真实升级，
    /// <c>Core.Numbers.Progression.ProgressionHost.ApplyGrowth</c> 又会把"2 级到新等级"整段成长
    /// 重算并整体覆盖写入修正，与已经叠进基础值的那一段重复计入（真实探针：出生等级 2、出生
    /// strength 7，升到 3 级实测 11，应为 9；细节见
    /// <c>Core.Numbers.Progression.ProgressionHost</c> 判断记录"成长唯一来源"）。根治为"成长统一
    /// 只由修正承载"：本类型不再解析 <c>prog.level_curve</c>，<see cref="ApplyStats"/> 只写
    /// <c>base_stats × tier.stat_multiplier</c>；<see cref="Spawn"/> 改为在
    /// <see cref="Core.Numbers.Progression.IProgressionHost.RegisterUnit"/> 之后紧接着调用
    /// <see cref="Core.Numbers.Progression.IProgressionHost.ApplyGrowthToCurrentLevel"/>——与
    /// 升级、读档共用同一份聚合实现，出生等级 1（无成长曲线的既有示例数据）时这一步是空操作
    /// （曲线 2..1 区间不存在），行为不变。
    /// </para>
    /// </summary>
    public sealed class CreatureFactory : ICreatureFactory, ICreatureTemplateQuery
    {
        private sealed class TierInfo
        {
            public double StatMultiplier;
            public bool ControlImmune;
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

        /// <summary>消费方反馈第 33 条：当前数据集 <c>arch.power_type</c> 全部已登记 id，按升序
        /// 排列，见 <see cref="ResolvePowerTypes"/>/<see cref="LoadPowerTypeIds"/> 判断记录。独立
        /// 解析（不依赖 <c>PowerHost</c> 内部实现），惯例同 <c>LoadTiers</c>/<c>LoadTemplates</c>——
        /// 本模块按既定结构解析一份只读索引，不依赖其它模块内部实现。</summary>
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
                // 消费方反馈第 36 条根治：出生等级 > 1 时，"2 级到出生等级"的曲线成长只经这一步
                // 写成修正（与升级、读档共用同一份聚合实现，见类型判断记录），不再叠进 ApplyStats
                // 写的基础值——放在 Powers.RegisterUnit 之前，保证 max_source: stat 的资源类型
                // 用到的是已经计入成长的最终属性值（与改动前的既有顺序要求一致）。
                _progression.ApplyGrowthToCurrentLevel(entityId);
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
        /// 按 <c>base_stats × tier.stat_multiplier</c> 算出各属性基础值，按属性 id 调用一次
        /// <see cref="IStatHost.SetBase"/>。消费方反馈第 36 条根治：本方法不再叠加曲线成长量——
        /// 成长统一由 <see cref="Core.Numbers.Progression.IProgressionHost.ApplyGrowthToCurrentLevel"/>
        /// 以修正形式写入（见 <see cref="Spawn"/> 调用点、类型判断记录"成长不再由本类型承载"），
        /// 基础值只承载"分档倍率"这一项，避免与修正重复计入同一段成长。
        /// </summary>
        private void ApplyStats(Id unitId, CreatureTemplate template, TierInfo tier)
        {
            foreach (var kv in template.BaseStats)
            {
                _stats.SetBase(unitId, kv.Key, kv.Value * tier.StatMultiplier);
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
    }
}

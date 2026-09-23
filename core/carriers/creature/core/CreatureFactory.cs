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
    /// <see cref="IDataRegistryView"/> 一次性解析 <c>creature.template</c>/<c>creature.tier_definition</c>
    /// 两张表，之后只读，惯例同 <c>AiHost</c>/<c>StatHost</c>。
    /// <para>
    /// 判断记录（消费方反馈第 33 条，资源类型注册顺序）：<see cref="SpawnCore"/> 此前恒按
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
    /// <c>base_stats × tier.stat_multiplier</c>；<see cref="SpawnCore"/> 改为在
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

            /// <summary>T-N3-6 新增（ADR-0031 决策 8）：与 <see cref="ControlImmune"/>（全部类别）
            /// 并存的按类别声明——缺省空列表（<see cref="LoadTiers"/> 未命中
            /// <c>control_immune_categories</c> 字段时的缺省值，同字段 schema 缺省），不改变
            /// <see cref="ControlImmune"/> 既有语义。</summary>
            public IReadOnlyList<string> ControlImmuneCategories = Array.Empty<string>();

            /// <summary>T-N4-4 新增（ADR-0033 决策 4）：该分档的经验倍率，缺省 1——见
            /// <see cref="TryGetXpMultiplier"/> 判断记录。</summary>
            public double XpMultiplier = 1.0;

            /// <summary>T-N6-3b 新增（N4 遗留第 7 项；ADR-0034 决策 3 延伸；08 第 7.4 节"怪物掉钱 =
            /// 当量 × econ.gold_base_curve(怪物等级) × 分档倍率 × diff.tier.loot_multiplier"）：该
            /// 分档的金币倍率，缺省 1——见 <see cref="TryGetGoldMultiplier"/> 判断记录。</summary>
            public double GoldMultiplier = 1.0;
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

        /// <summary>T-N6-3b 新增（ADR-0035 决策 3）：可选的等级缩放器，供按指定等级出生（见 6 参
        /// <see cref="Spawn(Id, Id, Vec2, double, Id?, int)"/>）时换算基础属性——未注入（<c>null</c>，
        /// 旧构造重载走的路径）时按 <see cref="ICreatureLevelScaler"/> 类型判断记录"等级改、属性不变"
        /// 退化，见 <see cref="ResolveBaseStats"/>。
        /// <para>
        /// T-N6-4 判断记录（改为构造后可写的公开属性，而不是新增第 11 个构造参数）：
        /// <c>Core.Sim.HeadlessWorldBuilder.Build</c> 需要在装配出 <c>HeadlessWorld.AnchorTable</c>
        /// （数据装载、校验全部完成之后）才能判断"数据源是否真的含 sim.anchor 行"、进而决定要不要
        /// 装配 <c>Core.Sim.AnchorCreatureLevelScaler</c>——但 <see cref="CreatureFactory"/>（经
        /// <c>Core.Carriers.Assembly.CarriersAssembly</c>）在 <c>registry.LoadAll</c> 之前就已经
        /// 构造完成，装配根拿不到"构造期就知道要不要传 levelScaler"这个时机（与
        /// <c>AnchorTableSkillBudgetAnchorProvider</c> 走"预扫描数据源文件原始 JSON、构造期就能
        /// 确定"的路径不同——本属性没有等价的预扫描手段，等级缩放器本身就需要读已装载的
        /// <see cref="AnchorTable"/>）。与其给 <see cref="CarriersAssembly"/>/
        /// <see cref="Core.Gameplay.Assembly.GameplayAssembly"/> 各追加一个新的构造函数重载（两处
        /// 都要新增一份完整参数列表的物理签名，改动面更大），选择把既有的构造期专属只读字段改成
        /// 一个构造完成后仍可写的公开属性——两个既有构造函数（5 参本身/9 参本身经 10 参重载）行为
        /// 不变（默认 <c>null</c>），<c>HeadlessWorldBuilder.Build</c> 在 <c>GameplayAssembly</c>
        /// 构造完成、<see cref="AnchorTable"/> 确定非空之后，直接对
        /// <c>gameplay.Carriers.Creatures.LevelScaler</c> 赋值即可，属于本任务硬性规则"只新增重载/
        /// 可选属性"允许的落地方式之一。
        /// </para>
        /// </summary>
        public ICreatureLevelScaler? LevelScaler { get; set; }

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

            // ADR-0079：见 OnEntityDestroyed/Despawn 判断记录——只对本类型自己 Despawn 过的实体注销
            // Stats/Powers，惯例同 AiHost/EntitySpatialSyncHost 订阅同一事件，但多一层"是不是我
            // 标记过"的过滤。
            _bus.Subscribe<Core.Foundation.SimLoop.EntityDestroyedEvent>(
                Core.Foundation.SimLoop.SimEventKeys.EntityDestroyed, OnEntityDestroyed);
        }

        /// <summary>
        /// T-N6-3b 新增重载（ABI 硬性规则"只允许新增"，不改既有 9 参构造函数签名——同
        /// <c>Core.Gameplay.Loot.LootHost</c> 追加 <c>economyHost</c> 的既有先例）：额外接受
        /// <paramref name="levelScaler"/>，供按指定等级出生（见 <see cref="ResolveBaseStats"/>）使用。
        /// 未提供（<c>null</c>——旧 9 参构造函数走的路径，或本重载显式传 <c>null</c>）时按指定等级
        /// 出生只改变 <c>CreatureUnit.Level</c>，基础属性不变，见 <see cref="ICreatureLevelScaler"/>
        /// 类型判断记录。
        /// </summary>
        public CreatureFactory(
            IDataRegistryView registry,
            IWorldSim world,
            IEventBus bus,
            IStatHost stats,
            IPowerHost powers,
            IProgressionHost progression,
            IUnitAccess units,
            AiRegistrar aiRegistrar,
            CreatureOptions? options,
            ICreatureLevelScaler? levelScaler)
            : this(registry, world, bus, stats, powers, progression, units, aiRegistrar, options)
        {
            LevelScaler = levelScaler;
        }

        // -----------------------------------------------------------------
        // ICreatureFactory
        // -----------------------------------------------------------------

        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null) =>
            SpawnCore(templateId, mapId, position, facing, ownerId, levelOverride: null);

        /// <summary>
        /// T-N6-3b 新增重载（ADR-0035 决策 3"生物模板按指定等级出生"；<see cref="ICreatureFactory"/>
        /// 对应默认接口成员的显式实现，见该接口成员判断记录）：按 <paramref name="level"/> 覆盖出生
        /// 等级（不再取 <c>creature.template.level</c>）。<c>CreatureUnit.Level</c>（及以其为准的
        /// <see cref="IUnitAccess.GetLevel"/>、经验发放、等级差计算等消费方）自然读到
        /// <paramref name="level"/>；基础属性经 <see cref="ResolveBaseStats"/> 换算（未注入
        /// <see cref="ICreatureLevelScaler"/> 时不变），分档 <c>stat_multiplier</c> 仍在换算之后按
        /// 既有顺序相乘。
        /// </summary>
        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId, int level) =>
            SpawnCore(templateId, mapId, position, facing, ownerId, levelOverride: level);

        private Id SpawnCore(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId, int? levelOverride)
        {
            var template = RequireTemplate(templateId);
            var tier = RequireTier(template.TierId);
            var spawnLevel = levelOverride ?? template.Level;

            var entityId = _world.AllocateEntityId(EntityKinds.Creature);
            var unit = new CreatureUnit(entityId, mapId, template.FactionId, templateId)
            {
                Position = position,
                Facing = facing,
                Level = spawnLevel,
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

            // T-N3-6 新增：control_immune_categories 按类别声明，与上面的整体标记并存、各自独立写入
            // （不受 _options.ImmunityTagPrefix 是否配置影响——该选项只是"整体控制免疫"这个便捷标记
            // 的开关，本字段是内容直接声明的具体类别子集，两者是正交维度，见 CreatureSchemas/
            // CreatureImmunityProvider.ControlCategoryPrefix 判断记录）。
            for (var i = 0; i < tier.ControlImmuneCategories.Count; i++)
            {
                var categoryTag = new Id(CreatureImmunityProvider.ControlCategoryPrefix + tier.ControlImmuneCategories[i]);
                if (!unit.Immunities.Contains(categoryTag))
                {
                    unit.Immunities.Add(categoryTag);
                }
            }

            unit.Tags.Add(template.TierId);

            _world.AddEntity(unit);
            // 冗余但幂等地再写一次位置：经注入的 IUnitAccess（如 WorldUnitAccess）写入，触发其可能
            // 附带的空间索引同步（见 core/carriers/unit README 判断记录 4"WorldUnitAccess.SetPosition
            // 写入新位置后同步登记"）——直接写 unit.Position 不会触碰任何空间索引。
            _units.SetPosition(entityId, position);

            _stats.RegisterUnit(entityId);
            ApplyStats(entityId, template, tier, spawnLevel);

            if (template.StatGrowthRef.HasValue)
            {
                // T-N6-3b 判断记录（RegisterUnit 改传 spawnLevel，而不是固定 template.Level）：
                // spawnLevel 未被 6 参 Spawn 覆盖时恒等于 template.Level，本行为对既有调用方
                // （5 参 Spawn/既有测试）零变化。被覆盖时，Progression 内部登记的"当前等级"自然
                // 同步为 spawnLevel（否则后续 AddXp/GetXpToNext 等仍按模板等级计算，与
                // CreatureUnit.Level 已经写成 spawnLevel 相互矛盾）——不是本任务新引入的"双重计分"：
                // 曲线"2..当前登记等级"复利本就是 ApplyGrowthToCurrentLevel 的既有既定语义（见类型
                // 判断记录"成长唯一来源"），本任务只是把"当前登记等级"的来源从硬编码的 template.Level
                // 改成 spawnLevel，曲线本身的计算方式不变。若某模板既配置了 stat_growth_ref、又通过
                // 6 参 Spawn 指定了与模板不同的等级、且未注入 ICreatureLevelScaler，基础属性仍会经
                // 本步骤按曲线"2..spawnLevel"计算出与"未覆盖时不同"的修正值——<see
                // cref="ICreatureLevelScaler"/> 类型判断记录"未注入时属性不变"特指本类型新增的缩放器
                // 这一步（<see cref="ResolveBaseStats"/>）不生效，不改写曲线这一既有独立机制的既定
                // 行为；两者是否要在同一模板上组合使用，留给设计层/装配层的口味决策，本任务不禁止也不
                // 特殊处理这一组合。
                _progression.RegisterUnit(entityId, template.StatGrowthRef.Value, spawnLevel);
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

        /// <summary>
        /// ADR-0079 补齐（本类自己的"待注销"记账，见 <see cref="Despawn"/> 判断记录）：只登记经
        /// <see cref="Despawn"/> 标记过待销毁、Stats/Powers 注销延后到真正移除那一刻的实体 id——
        /// 不是"当前全部存活生物"的镜像，未调用过 <see cref="Despawn"/> 的实体不会出现在这里。
        /// </summary>
        private readonly HashSet<Id> _pendingDespawnUnregistration = new HashSet<Id>();

        /// <summary>
        /// ADR-0079 根治（消费方反馈第二十二批，框架内部时序缺陷，一次异常永久锁死主循环）：本方法
        /// 此前在这里同步调用 <c>_stats.UnregisterUnit</c>/<c>_powers.UnregisterUnit</c>——两者立即
        /// 生效，而 <see cref="_world"/> 对该实体的真正移除（连同 <c>AiHost</c> 的行为外壳登记、
        /// <c>EntitySpatialSyncHost</c> 的空间索引，两者都靠订阅 <c>EntityDestroyedEvent</c> 自清理）
        /// 要等到某次 <see cref="IWorldSim.Tick"/> 阶段 8（生命周期清理）。"实体何时不再存在"因此
        /// 出现两个不同时刻：两次调用之间若该实体仍有生效的 AI 移动意图，下一拍
        /// <c>MovementTickHandler.ResolveSpeed</c> 会向已经注销的 <c>StatHost.GetStat</c> 要属性，
        /// 抛 <c>InvalidOperationException</c> 中止整个 Tick——阶段 8 这次跑不到，实体没被真正移除，
        /// 下一拍同样的路径再抛一次，永久卡死。根治：本方法不再同步注销 Stats/Powers，只标记待销毁
        /// + 发事件（<see cref="CreatureDespawnedEvent"/> 的发出时机与内容不变）+ 把 <paramref
        /// name="entityId"/> 记进 <see cref="_pendingDespawnUnregistration"/>；本类型构造函数订阅的
        /// <c>EntityDestroyedEvent</c> 处理器只对命中这个集合的 id 才调用 Stats/Powers 的
        /// UnregisterUnit，与世界真正移除该实体在同一批 <c>DispatchPending</c> 里一起生效——"实体
        /// 何时不再存在"从此只有一个时刻。
        /// <para>
        /// 判断记录（不直接让 <c>StatHost</c>/<c>PowerHost</c> 订阅 <c>EntityDestroyedEvent</c>、
        /// 而是把订阅与"是不是我 Despawn 过的"过滤都放在本类：<c>EntityDestroyedEvent</c> 不只在
        /// <see cref="IWorldSim.Tick"/> 阶段 8 为待销毁实体触发，<see cref="IWorldSim.ClearAll"/>
        /// （地图切换）也会为**当时仍存活、从未调用过 <see cref="Despawn"/>** 的全部实体无差别触发
        /// 同一事件——包括玩家单位。玩家的 <c>Stats</c>/<c>Powers</c> 注册代表跨地图持久的角色状态，
        /// <c>GameplayAssembly.EnterMap</c> 重新登记的只是世界实体本身，不会重新调用
        /// <c>RulesAssembly.RegisterUnit</c>。本判断记录改动前的第一版实现让 <c>StatHost</c>/
        /// <c>PowerHost</c> 直接订阅 <c>EntityDestroyedEvent</c>、无差别注销，被
        /// <c>RacePassiveAuraCrossMapTests</c> 等既有跨地图用例的真实回归当场拦下（玩家 ClearAll
        /// 后属性宿主未注册，光环重新施加时抛 <c>InvalidOperationException</c>）——StatHost/PowerHost
        /// 的 <c>RegisterUnit</c> 有多个调用方（本类型与 <c>RulesAssembly.RegisterUnit</c>），各自的
        /// 生命周期语义不同，注销责任应该留在各自的登记方，不能集中收归两个 L1 宿主自己判断。
        /// </para>
        /// </summary>
        public void Despawn(Id entityId, string reason)
        {
            if (reason == null) throw new ArgumentNullException(nameof(reason));

            _world.MarkForDestruction(entityId);
            _pendingDespawnUnregistration.Add(entityId);

            _bus.Enqueue(new CreatureDespawnedEvent(entityId, reason));
        }

        /// <summary>见 <see cref="Despawn"/> 判断记录：本类型构造函数订阅的 <c>EntityDestroyedEvent</c>
        /// 处理器，只对经本类型 <see cref="Despawn"/> 标记过的 id 注销 Stats/Powers——命中
        /// <see cref="_pendingDespawnUnregistration"/> 才处理并移除，未命中（玩家、或从未调用过
        /// <see cref="Despawn"/> 就被 <see cref="IWorldSim.ClearAll"/> 整体清空的实体）原样跳过，不
        /// 触碰其 Stats/Powers 注册状态。</summary>
        private void OnEntityDestroyed(Core.Foundation.SimLoop.EntityDestroyedEvent evt)
        {
            if (!_pendingDespawnUnregistration.Remove(evt.EntityId))
            {
                return;
            }

            _stats.UnregisterUnit(evt.EntityId);
            _powers.UnregisterUnit(evt.EntityId);
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
        /// 按 <c>ResolveBaseStats(template, spawnLevel) × tier.stat_multiplier</c> 算出各属性基础值，
        /// 按属性 id 调用一次 <see cref="IStatHost.SetBase"/>。消费方反馈第 36 条根治：本方法不叠加
        /// 曲线成长量——成长统一由
        /// <see cref="Core.Numbers.Progression.IProgressionHost.ApplyGrowthToCurrentLevel"/> 以修正
        /// 形式写入（见 <see cref="SpawnCore"/> 调用点、类型判断记录"成长不再由本类型承载"），基础值
        /// 只承载"等级缩放 × 分档倍率"两项，避免与修正重复计入同一段成长。T-N6-3b：等级缩放这一项见
        /// <see cref="ResolveBaseStats"/>。
        /// </summary>
        private void ApplyStats(Id unitId, CreatureTemplate template, TierInfo tier, int spawnLevel)
        {
            foreach (var kv in ResolveBaseStats(template, spawnLevel))
            {
                _stats.SetBase(unitId, kv.Key, kv.Value * tier.StatMultiplier);
            }
        }

        /// <summary>
        /// T-N6-3b 新增（ADR-0035 决策 3）：<paramref name="spawnLevel"/> 等于
        /// <paramref name="template"/>.Level（未覆盖出生等级，或 6 参 <c>Spawn</c> 显式传入与模板相同
        /// 的等级）、或未注入 <see cref="_levelScaler"/> 时，原样返回 <paramref name="template"/>.
        /// BaseStats（未经分档倍率处理的原始模板值，同改动前 <see cref="ApplyStats"/> 的输入）——见
        /// <see cref="ICreatureLevelScaler"/> 类型判断记录"未注入时等级改、属性不变"。否则委托给
        /// <see cref="_levelScaler"/>.ScaleBaseStats 换算。
        /// </summary>
        private IReadOnlyDictionary<Id, double> ResolveBaseStats(CreatureTemplate template, int spawnLevel)
        {
            if (LevelScaler == null || spawnLevel == template.Level)
            {
                return template.BaseStats;
            }
            return LevelScaler.ScaleBaseStats(template, template.Level, spawnLevel, template.BaseStats);
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
                var xpMultiplier = record.TryGetNumber("xp_multiplier", out var xpMult) ? xpMult : 1.0;
                // T-N6-3b 新增：gold_multiplier（惯例同 xp_multiplier 一贯"缺省 1"解析写法）。
                var goldMultiplier = record.TryGetNumber("gold_multiplier", out var goldMult) ? goldMult : 1.0;

                // T-N3-6 新增：control_immune_categories（并存字段，见 CreatureSchemas 判断记录），
                // 惯例同 SkillDefCache 解析 interrupt_flags 数组字段——直接遍历 JsonArray 取字符串，
                // 不认识的元素类型（理论上不会出现，schema 已限定 Enum 元素）静默跳过。
                var controlImmuneCategories = Array.Empty<string>() as IReadOnlyList<string>;
                if (record.TryGetArray("control_immune_categories", out var categoriesArray))
                {
                    var categories = new List<string>(categoriesArray.Count);
                    for (var i = 0; i < categoriesArray.Count; i++)
                    {
                        if (categoriesArray[i] is JsonString s)
                        {
                            categories.Add(s.Value);
                        }
                    }
                    controlImmuneCategories = categories;
                }

                _tiers[id.Value] = new TierInfo
                {
                    StatMultiplier = statMultiplier,
                    ControlImmune = controlImmune,
                    ControlImmuneCategories = controlImmuneCategories,
                    XpMultiplier = xpMultiplier,
                    GoldMultiplier = goldMultiplier,
                };
            }
        }

        // -----------------------------------------------------------------
        // T-N4-4：分档经验倍率查询（ADR-0033 决策 4）
        // -----------------------------------------------------------------

        /// <summary>查询 <paramref name="tierId"/>（<c>creature.tier_definition</c>）对应的
        /// <c>xp_multiplier</c>（缺省 1）。供
        /// <see cref="Core.Numbers.Progression.ProgressionOptions.ExtraXpMultiplierProvider"/>
        /// 钩子读取（<c>core/gameplay/assembly.GameplayAssembly</c> 接线，见该属性判断记录）。
        /// <para>
        /// 判断记录（未登记 <paramref name="tierId"/> 时返回 <c>false</c> 而不抛异常）：与本类型
        /// 其余查询方法（<see cref="RequireTier"/> 惯例，未知 id 抛 <see cref="ArgumentException"/>）
        /// 刻意不同——本方法唯一的调用方是"击杀经验倍率折算"这一非阻断性场景（同
        /// <see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener.ResolveTierId"/>
        /// 判断记录"未登记模板/分档不阻断经验发放本身"一贯口径），未登记/拼写错误的
        /// <paramref name="tierId"/>（或 <c>null</c>，由调用方在解包 <see
        /// cref="Core.Numbers.Progression.XpContext.TierId"/> 时判断）不应该让整条经验发放链路
        /// 抛异常，只是退化为"无分档加成"（<paramref name="multiplier"/> 恒为 <c>1.0</c>）。
        /// </para>
        /// </summary>
        public bool TryGetXpMultiplier(Id tierId, out double multiplier)
        {
            if (_tiers.TryGetValue(tierId.Value, out var tier))
            {
                multiplier = tier.XpMultiplier;
                return true;
            }
            multiplier = 1.0;
            return false;
        }

        // -----------------------------------------------------------------
        // T-N6-3b：分档金币倍率查询（N4 遗留第 7 项；ADR-0034 决策 3 延伸；08 第 7.4 节）
        // -----------------------------------------------------------------

        /// <summary>查询 <paramref name="tierId"/>（<c>creature.tier_definition</c>）对应的
        /// <c>gold_multiplier</c>（缺省 1）。供
        /// <see cref="Core.Gameplay.Loot.LootGoldMultiplierProvider"/> 钩子读取（<c>core/gameplay/
        /// assembly.GameplayAssembly</c> 接线，取法与 <see cref="TryGetXpMultiplier"/> 一致——见该
        /// 方法判断记录，本方法未登记 <paramref name="tierId"/> 时同样返回 <c>false</c> 而不抛异常，
        /// 供"怪物掉钱换算"这一非阻断性场景退化为"无分档加成"）。</summary>
        public bool TryGetGoldMultiplier(Id tierId, out double multiplier)
        {
            if (_tiers.TryGetValue(tierId.Value, out var tier))
            {
                multiplier = tier.GoldMultiplier;
                return true;
            }
            multiplier = 1.0;
            return false;
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

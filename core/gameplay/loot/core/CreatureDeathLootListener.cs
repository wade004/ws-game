using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 订阅 <c>unit.died</c>（见 <c>core/rules/common.RulesEventKeys.UnitDied</c>）：查询死亡单位的
    /// <c>creature.template.loot_table_ref</c>（经 <see cref="ICreatureTemplateQuery"/>），有则抽取
    /// 掉落并在死亡位置生成地面掉落物（08 第 1 节、任务书"CreatureDeathLootListener"）。
    /// <para>
    /// 判断记录：<see cref="RollContext.Multiplier"/> 经构造注入的 <c>Func&lt;double&gt;
    /// lootMultiplierProvider</c> 取得（任务书原句"难度模块提供；默认 1"）——本任务不依赖 08 第 9 节
    /// Difficulty 行"L4 Loot（倍率）"具体如何计算倍率，只声明这一处扩展点；难度模块（不在本任务
    /// 范围）若要影响掉落倍率，只需把自己的查询函数注入到这个委托即可，不需要 Loot 模块反向依赖它。
    /// </para>
    /// <para>
    /// T-N4-7：可选注入 <see cref="Core.Gameplay.Economy.IEconomyHost"/>——<see cref="OnUnitDied"/>
    /// 在 <see cref="Core.Gameplay.Economy.CurrencyDepositPolicy.OnKill"/>（默认）策略下据此把
    /// 货币掉落条目在击杀这一刻直接入账给击杀者，见该方法判断记录。
    /// </para>
    /// </summary>
    public sealed class CreatureDeathLootListener
    {
        private readonly LootHost _lootHost;
        private readonly ICreatureTemplateQuery _templates;
        private readonly IUnitAccess _units;
        private readonly IWorldSim _world;
        private readonly Func<double> _lootMultiplierProvider;
        private readonly Core.Gameplay.Difficulty.IDifficultyHost? _difficultyHost;
        private readonly Core.Gameplay.Economy.IEconomyHost? _economyHost;
        private readonly ISummonHost? _summons;

        /// <summary>消费方反馈同构问题第三处根治（判断记录 19）：默认给一份 <see
        /// cref="InMemoryLootDiagnostics"/>（field 初始化器，不是在某一个构造函数体内赋值）——本类
        /// 既有四个构造重载彼此按"窄→宽"单向链式 <c>: this(...)</c>（6→7→8→9 参），新增的带
        /// <c>diagnostics</c> 参数的最宽重载（见下方）只会被显式传入 <paramref name="diagnostics"/>
        /// 的调用点触达，其余四个既有重载完全不经过它——若诊断字段只在最宽重载里赋值（同
        /// <c>IProgressionBridgeDiagnostics</c> 判断记录"惯例"，该模块此前只有一个构造函数，不存在
        /// 这条链），窄重载构造出来的实例 <c>_diagnostics</c> 会是 <c>null</c>，<see cref="OnUnitDied"/>
        /// 里的 <c>_diagnostics.Warn(...)</c> 调用会抛 <see cref="NullReferenceException"/>。字段初始
        /// 化器先于全部构造函数体执行（C# 规范：字段初始化器在到达"不带 <c>this(...)</c> 的构造函数"
        /// 时，先于该构造函数体运行），故窄重载天然拿到这份默认实例；新增的宽重载在自己的构造函数体
        /// 内用 <c>diagnostics ?? new InMemoryLootDiagnostics()</c> 覆盖它（合法：readonly 字段允许在
        /// "本类型的构造函数体"内重新赋值，不限定只能赋值一次）。</summary>
        private readonly ILootDiagnostics _diagnostics = new InMemoryLootDiagnostics();

        /// <summary>诊断出口只读暴露（ABI 只新增只读属性，见
        /// architecture/adr/0042-诊断契约统一转发到宿主控制台.md）：供 adapters/unity 侧统一诊断
        /// 转发机制轮询本实例累积的 Warnings，不改变本类型任何既有公开签名。</summary>
        public ILootDiagnostics Diagnostics => _diagnostics;

        public CreatureDeathLootListener(
            IEventBus bus,
            LootHost lootHost,
            ICreatureTemplateQuery templates,
            IUnitAccess units,
            IWorldSim world,
            Func<double>? lootMultiplierProvider = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _lootHost = lootHost ?? throw new ArgumentNullException(nameof(lootHost));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _lootMultiplierProvider = lootMultiplierProvider ?? (() => 1.0);

            bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, OnUnitDied);
        }

        /// <summary>
        /// T-N2-8b 新增重载（ABI 硬性规则"只允许新增"，不改既有 6 参构造函数签名——同
        /// <c>Core.Carriers.Item.InventoryHost</c> 构造重载一贯做法：给既有 <c>.ctor</c> 追加参数
        /// 即便带默认值，在物理 IL 签名层面仍是破坏性变更）：额外接受 <paramref
        /// name="difficultyHost"/>，供 <see cref="OnUnitDied"/> 构造 <see cref="RollContext"/> 时取
        /// <see cref="Core.Gameplay.Difficulty.IDifficultyHost.ItemLevelOffset"/>（README"来源等级
        /// 接线"判断记录）。未提供（null——既有 6 参构造函数走的路径，或本重载显式传 null）时偏移恒为
        /// 0，与 <see cref="Core.Gameplay.Difficulty.IDifficultyHost.ItemLevelOffset"/> 默认接口成员
        /// "无难度修正"的中性默认值同一语义——本类不因为拿不到难度宿主就抛异常或跳过掉落生成。
        /// </summary>
        public CreatureDeathLootListener(
            IEventBus bus,
            LootHost lootHost,
            ICreatureTemplateQuery templates,
            IUnitAccess units,
            IWorldSim world,
            Func<double>? lootMultiplierProvider,
            Core.Gameplay.Difficulty.IDifficultyHost? difficultyHost)
            : this(bus, lootHost, templates, units, world, lootMultiplierProvider)
        {
            _difficultyHost = difficultyHost;
        }

        /// <summary>
        /// T-N4-7 新增重载（ABI 硬性规则"只允许新增"，不改既有 7 参构造函数签名——同上一条 T-N2-8b
        /// 先例）：额外接受 <paramref name="economyHost"/>，供 <see cref="OnUnitDied"/> 在
        /// <see cref="Core.Gameplay.Economy.CurrencyDepositPolicy.OnKill"/> 策略下把货币掉落条目在
        /// 击杀这一刻直接入账给击杀者（见该方法判断记录）。未提供（<c>null</c>）时按该方法判断记录
        /// 整条退回"随其它掉落物一并落地"的既有路径，不抛异常、不跳过掉落生成。
        /// </summary>
        public CreatureDeathLootListener(
            IEventBus bus,
            LootHost lootHost,
            ICreatureTemplateQuery templates,
            IUnitAccess units,
            IWorldSim world,
            Func<double>? lootMultiplierProvider,
            Core.Gameplay.Difficulty.IDifficultyHost? difficultyHost,
            Core.Gameplay.Economy.IEconomyHost? economyHost)
            : this(bus, lootHost, templates, units, world, lootMultiplierProvider, difficultyHost)
        {
            _economyHost = economyHost;
        }

        /// <summary>
        /// 2026-09-16 深度复审 D-M1 新增重载（ABI 硬性规则"只允许新增"，不改既有 8 参构造函数签名）：
        /// 额外接受 <paramref name="summons"/>，供 <see cref="OnUnitDied"/> 在货币入账分支前把
        /// <c>evt.KillerId</c> 解析为记账单位（召唤物击杀者归主人，逻辑同 <see
        /// cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener.ResolveCreditUnit"/>，两者
        /// 共同调用 <see cref="Core.Gameplay.Common.SummonCreditResolver.ResolveCreditUnit"/>）。未
        /// 提供（<c>null</c>——既有构造重载走的路径，或本重载显式传 null）时，记账单位恒等于
        /// <c>evt.KillerId</c> 本身（同 <see cref="Core.Gameplay.Common.SummonCreditResolver
        /// .ResolveCreditUnit"/> "无召唤宿主可用"退化语义），不影响未注入本参数的既有调用点行为。
        /// </summary>
        public CreatureDeathLootListener(
            IEventBus bus,
            LootHost lootHost,
            ICreatureTemplateQuery templates,
            IUnitAccess units,
            IWorldSim world,
            Func<double>? lootMultiplierProvider,
            Core.Gameplay.Difficulty.IDifficultyHost? difficultyHost,
            Core.Gameplay.Economy.IEconomyHost? economyHost,
            ISummonHost? summons)
            : this(bus, lootHost, templates, units, world, lootMultiplierProvider, difficultyHost, economyHost)
        {
            _summons = summons;
        }

        /// <summary>
        /// 消费方反馈同构问题第三处根治新增构造重载（判断记录 19；ABI 门禁"公开 API 只能新增"——既有
        /// 九参构造函数已发布，本重载十个参数全部不带默认值，与既有构造函数在参数个数上不重叠
        /// （9 对 10，惯例同 <see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener"/>
        /// 对应重载判断记录），互不冲突，也不产生调用点重载二义性）：额外接受 <paramref
        /// name="diagnostics"/>，供 <see cref="OnUnitDied"/> 未能取得死亡单位强类型模板时（模板未
        /// 登记、或字段非法）记一条警告（判断记录 19"运行时路径不静默降级"）。未提供时缺省 <see
        /// cref="InMemoryLootDiagnostics"/>（惯例同本仓库其余全部 Host 的 diagnostics 可选参数；本类
        /// 因既有构造重载链的方向问题改用字段初始化器提供默认值，见 <see cref="_diagnostics"/> 字段
        /// 判断记录）。本重载直接链到既有九参构造函数完成全部既有初始化，构造函数体内只追加
        /// <c>_diagnostics</c> 赋值，不重复既有任何字段赋值逻辑。
        /// </summary>
        public CreatureDeathLootListener(
            IEventBus bus,
            LootHost lootHost,
            ICreatureTemplateQuery templates,
            IUnitAccess units,
            IWorldSim world,
            Func<double>? lootMultiplierProvider,
            Core.Gameplay.Difficulty.IDifficultyHost? difficultyHost,
            Core.Gameplay.Economy.IEconomyHost? economyHost,
            ISummonHost? summons,
            ILootDiagnostics? diagnostics)
            : this(bus, lootHost, templates, units, world, lootMultiplierProvider, difficultyHost, economyHost, summons)
        {
            _diagnostics = diagnostics ?? new InMemoryLootDiagnostics();
        }

        private void OnUnitDied(UnitDiedEvent evt)
        {
            // 消费方反馈第十二批根治（判断记录 21）：本监听器只负责生物死亡掉落，玩家单位死亡是每局
            // 必然发生的正常事件，不属于其职责范围。用已注入的 IUnitAccess.GetSourceKind（T-N1-6，
            // 见该成员判断记录）判别——不新增依赖：本类全部五个构造重载都已持有 _units 字段；生产
            // 装配（GameplayAssembly 第 6 步）实际接的是 Core.Carriers.Unit.WorldUnitAccess，其
            // GetSourceKind 按 Entity.Kind（PlayerUnit.Kind = EntityKinds.Player、CreatureUnit.Kind
            // = EntityKinds.Creature）返回真实值，不是默认接口方法的 Unknown 占位。不用"模板 id 前缀"
            // 一类字符串判断——玩家单位的 Entity.TemplateId 可能被具体游戏设置成职业原型 id（不在
            // creature.template 表里，无法反向判断"看起来像生物模板"），只有权威的载体类型判别才
            // 可靠。非生物（Player/Unknown）直接跳过，不写任何诊断——"不是生物"本身不是内容缺口，
            // 与下面 catch 分支"确实是生物、但查不到模板/掉落表"这一真正缺口的语义不同，不能共用
            // 同一条告警文案。
            if (_units.GetSourceKind(evt.UnitId) != SourceKind.Creature)
            {
                return;
            }

            var templateId = _units.GetTemplateId(evt.UnitId);
            if (!templateId.HasValue)
            {
                return;
            }

            Id? lootTableRef;
            Id tierId;
            try
            {
                var creatureTemplate = _templates.Get(templateId.Value);
                lootTableRef = creatureTemplate.LootTableRef;
                // T-N6-3b（08 第 7.4 节"怪物掉钱……× 分档倍率"）：与 lootTableRef 同一次
                // ICreatureTemplateQuery.Get 调用一并取出死亡单位的分档 id，供下面 RollContext.TierId
                // 供 LootHost.ResolveCurrencyOutcome 查询 creature.tier_definition.gold_multiplier
                // 使用——取法同 Core.Gameplay.ProgressionBridge.CreatureDeathXpListener.ResolveTierId
                // 判断记录，只是本类已经在这里持有 creatureTemplate，不需要单独再查一次。
                tierId = creatureTemplate.TierId;
            }
            catch (ArgumentException ex)
            {
                // 判断记录 19（消费方反馈同构问题第三处根治，运行时路径不静默降级，见 AGENTS.md §3）：
                // 此前"模板查询失败 → 跳过"完全没有任何可观察信号，与 progression_bridge 的
                // CreatureDeathXpListener/AreaTriggerDiscoveryXpListener 同一病灶（用户侧表现"杀怪
                // 不掉东西且无任何线索"）。行为不变（仍然跳过、仍然不产出掉落、不阻断死亡结算），
                // 只是显式标记——消息给到具体表名 + 具体的模板 id，供内容作者直接去 creature.template
                // 表核对。
                //
                // 判断记录 19（catch (ArgumentException) 覆盖面核实）：ICreatureTemplateQuery.Get
                // 的契约文档只承诺一种 ArgumentException 成因（"未登记的模板 id"），生产装配实际接的
                // CreatureFactory.Get（RequireTemplate）也确实只在这一种情况下抛出；但同接口另一个
                // 实现 RegistryCreatureTemplateQuery.Get（该类型判断记录"异常收敛，不重复报告字段级
                // 问题"）还会把字段级 DataFieldException 包成 ArgumentException 抛出——若本类某天
                // 改接这个实现（目前没有任何生产/测试调用点这么做），单纯按类型 catch 会把"表里根本
                // 没这条记录"和"记录存在但字段非法、已由字段级校验单独报出"两种完全不同的成因混进
                // 同一条含糊消息，误导内容作者去核对错误的问题。收窄方式：借用
                // RegistryCreatureTemplateQuery.Get 自己的既有约定（"inner 保留原始异常供排查"），用
                // ex.InnerException is DataFieldException 区分两种成因，给出准确的诊断消息；不收窄
                // 异常类型本身、不改变"跳过、不外抛"这一控制流——本方法处在 IEventBus 派发链路上，
                // 同一次 unit.died 还有 progression_bridge/economy 等其它订阅者要处理，改成向外抛出
                // 会连带阻断它们，这个代价超出本次"只读诊断，不改变阻断语义"的范围，故只收窄"诊断
                // 消息的精确性"，不收窄"是否吞掉"本身。
                var message = ex.InnerException is DataFieldException
                    ? $"CreatureDeathLootListener: creature.template 模板 \"{templateId.Value}\" 字段非法，" +
                      "无法解析为强类型模板（该问题已由字段级校验单独报出），跳过本次死亡掉落生成" +
                      $"（unitId={evt.UnitId}）"
                    : $"CreatureDeathLootListener: creature.template 未登记模板 \"{templateId.Value}\"，" +
                      $"跳过本次死亡掉落生成（unitId={evt.UnitId}）";
                _diagnostics.Warn(message);
                return;
            }

            if (!lootTableRef.HasValue)
            {
                return;
            }

            // T-N2-8b（README"来源等级接线"判断记录）：sourceLevel 取死亡单位当前等级（IUnitAccess.
            // GetLevel 是既有必须实现的抽象成员，本类已持有 _units 引用，不需要新依赖）；
            // itemLevelOffset 取注入的 IDifficultyHost.ItemLevelOffset（未注入难度宿主时恒 0，同该
            // 成员默认接口方法语义）。两者均是确定性折算，不消耗任何随机数（见 RollContext.
            // SourceLevel/ItemLevelOffset 判断记录）。T-N6-3b：tierId 见上，一并传入
            // RollContext.TierId（7 参新重载）。
            var sourceLevel = _units.GetLevel(evt.UnitId);
            var itemLevelOffset = _difficultyHost?.ItemLevelOffset ?? 0;
            var context = new RollContext(
                evt.UnitId, evt.KillerId, _lootMultiplierProvider(), evt.UnitId, sourceLevel, itemLevelOffset, tierId);

            // T-N2-8b：改用带身份的 RollDetailed/Drop(outcomes) 路径，保证品质骰/词缀骰结果（若
            // loot.table 配置了 quality_weights）真正落到地面掉落物身份上，供 PickUp 转发进背包——旧
            // Roll(ItemStack)/Drop(ItemStack) 路径会让 DroppedLootEntity.Outcomes 退化成
            // ResolveDefaultOutcome 的模板缺省值，丢失这次抽取真正掷出的品质/词缀（见 core/gameplay/
            // loot/README.md 判断记录 15 已知缺口"拾取入包的品质/词缀传递留给后续任务"，本任务即该
            // 后续任务）。两条路径共用同一份 IRngHost 消耗，改用 RollDetailed 不增加/减少随机数调用
            // 次数（见 ILootHost.RollDetailed 判断记录"两条路径共用同一份 RngHost 消耗，不重复抽取"）。
            var outcomes = _lootHost.RollDetailed(lootTableRef.Value, context);
            if (outcomes.Count == 0)
            {
                return;
            }

            var entity = _world.GetEntity(evt.UnitId);
            if (entity == null)
            {
                // 契约缺口兜底：unit.died 派发时死亡单位理应仍存在于 IWorldSim 集合中（生命周期清理
                // 在阶段 8，晚于事件派发的阶段 7），但 IUnitAccess 本身不暴露 MapId（见
                // core/rules/common.IUnitAccess.GetMapId 默认实现返回 null 的判断记录），本监听器
                // 因此改经 IWorldSim.GetEntity 取 MapId；若调用方接入的 IWorldSim 实现里该单位确实
                // 已经不存在，没有安全的兜底地图 id 可用，只能放弃本次掉落生成（不抛异常中断死亡
                // 结算）。
                return;
            }

            // T-N4-7（ADR-0034 决策 4；EconomyOptions.DepositPolicy 判断记录）：OnKill（默认）策略下，
            // 货币产出在击杀这一刻直接入账给击杀者，不落地成地面掉落物——不占背包格、不需要拾取。
            // sourceId 取死亡单位自身（evt.UnitId，"谁给的钱"，惯例同 EconomyHost.Sell 的
            // sourceId: vendorId）。
            //
            // 判断记录（设计层裁定（2026-09-16）：采纳）：ADR/08 原文只给出"击杀即入账（默认）"这一句，未说明找不到
            // 明确击杀者（evt.KillerId 为 null，如环境死亡/自然死亡）时该怎么处理——本任务选择退回
            // GroundPickup 语义（货币随其它掉落物一并落地，不静默丢弃），不是"归属死亡单位自己"或
            // "直接丢弃"，理由：没有击杀者就没有 OnKill 策略要求的"击杀者"这一入账对象，落地待拾取
            // 是唯一不丢钱的选择。
            //
            // 判断记录（2026-09-16 深度复审 D-M1 根治：击杀者身份核对 + 召唤物归属解析）：此前本分支
            // 只要 evt.KillerId 有值就无条件 Add 进 evt.KillerId 自己的钱包——生物互殺（击杀者是怪物）
            // 时金币进了一个通常几秒后就销毁的运行期单位钱包，永久遗失；玩家召唤物击杀目标时金币进了
            // 召唤物自己的钱包，玩家收不到。现在先经 SummonCreditResolver.ResolveCreditUnit（同
            // CreatureDeathXpListener.ResolveCreditUnit 同款逻辑，两者共用同一份静态辅助）把
            // evt.KillerId 解析为记账单位（召唤物→其主人），再核对 IUnitAccess.GetSourceKind 是否为
            // SourceKind.Player——不是玩家（且不归属任何玩家召唤物）时，整条产出（不只是货币条目）
            // 原样退回 GroundPickup 语义（下方 groundOutcomes 保持 = outcomes 不变，随其它掉落物一并
            // 落地），与上面"击杀者为空"的既有处理口径一致，不静默丢弃。
            var groundOutcomes = outcomes;
            Id? creditUnitId = evt.KillerId.HasValue
                ? Core.Gameplay.Common.SummonCreditResolver.ResolveCreditUnit(_summons, evt.KillerId.Value)
                : null;
            if (_economyHost != null
                && _economyHost.DepositPolicy == Core.Gameplay.Economy.CurrencyDepositPolicy.OnKill
                && creditUnitId.HasValue
                && _units.GetSourceKind(creditUnitId.Value) == SourceKind.Player)
            {
                var remaining = new List<LootRollOutcome>(outcomes.Count);
                foreach (var outcome in outcomes)
                {
                    if (outcome.TemplateId.Domain == "econ")
                    {
                        _economyHost.Add(creditUnitId.Value, outcome.TemplateId, outcome.Count, sourceId: evt.UnitId);
                    }
                    else
                    {
                        remaining.Add(outcome);
                    }
                }

                groundOutcomes = remaining;
                if (groundOutcomes.Count == 0)
                {
                    // 全部产出都是货币且已当场入账，没有需要落地的物品——不生成空的地面掉落物实体
                    // （Drop 的既有契约总是返回一个真实创建的实体 id，本类不打算为"什么都不落地"的
                    // 情形改变这一契约，直接不调用）。
                    return;
                }
            }

            _lootHost.Drop(entity.MapId, _units.GetPosition(evt.UnitId), groundOutcomes, ownerHint: evt.KillerId);
        }
    }
}

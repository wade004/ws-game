using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Foundation.Common;
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

        private void OnUnitDied(UnitDiedEvent evt)
        {
            var templateId = _units.GetTemplateId(evt.UnitId);
            if (!templateId.HasValue)
            {
                return;
            }

            Id? lootTableRef;
            try
            {
                lootTableRef = _templates.Get(templateId.Value).LootTableRef;
            }
            catch (ArgumentException)
            {
                // 未登记的模板 id（见 ICreatureTemplateQuery.Get 注释"未登记的模板 id 抛
                // ArgumentException"）：不是本监听器的职责范围，静默跳过，不阻断死亡结算流程。
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
            // SourceLevel/ItemLevelOffset 判断记录）。
            var sourceLevel = _units.GetLevel(evt.UnitId);
            var itemLevelOffset = _difficultyHost?.ItemLevelOffset ?? 0;
            var context = new RollContext(
                evt.UnitId, evt.KillerId, _lootMultiplierProvider(), evt.UnitId, sourceLevel, itemLevelOffset);

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
            var groundOutcomes = outcomes;
            if (_economyHost != null
                && _economyHost.DepositPolicy == Core.Gameplay.Economy.CurrencyDepositPolicy.OnKill
                && evt.KillerId.HasValue)
            {
                var remaining = new List<LootRollOutcome>(outcomes.Count);
                foreach (var outcome in outcomes)
                {
                    if (outcome.TemplateId.Domain == "econ")
                    {
                        _economyHost.Add(evt.KillerId.Value, outcome.TemplateId, outcome.Count, sourceId: evt.UnitId);
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

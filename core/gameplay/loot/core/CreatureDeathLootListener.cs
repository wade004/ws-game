using System;
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
    /// </summary>
    public sealed class CreatureDeathLootListener
    {
        private readonly LootHost _lootHost;
        private readonly ICreatureTemplateQuery _templates;
        private readonly IUnitAccess _units;
        private readonly IWorldSim _world;
        private readonly Func<double> _lootMultiplierProvider;

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

            var context = new RollContext(evt.UnitId, evt.KillerId, _lootMultiplierProvider(), evt.UnitId);
            var items = _lootHost.Roll(lootTableRef.Value, context);
            if (items.Count == 0)
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

            _lootHost.Drop(entity.MapId, _units.GetPosition(evt.UnitId), items, ownerHint: evt.KillerId);
        }
    }
}

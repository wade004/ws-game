using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Quest;
using Core.Gameplay.Spawn;

namespace Core.Gameplay.Assembly
{
    /// <summary>
    /// <see cref="IQuestGuideLocator"/> 的默认实现（ADR-0173）：直接读数据注册表——<c>area.trigger_def</c> 行取形状中心与地图切换通道，
    /// <c>spawn.table</c> 行取刷新点位置。放在装配层而不是任务模块：它要同时认识区域触发与刷新两个姊妹模块的表，任务模块不依赖它们。
    /// <para>
    /// 判断记录（缓存）：地图切换通道列表首次使用时一次性解析并缓存（含条件表达式的解析）；开发期热重载数据表后调
    /// <see cref="Invalidate"/> 重建。单行查询（区域、刷新点）每次现读，不缓存。表缺失（该游戏没有区域触发或刷新表）按"没有"处理，不抛。
    /// </para>
    /// </summary>
    public sealed class RegistryQuestGuideLocator : IQuestGuideLocator
    {
        private readonly IDataRegistryView _registry;
        private readonly IExprSchema _exprSchema;
        private IReadOnlyList<QuestGuideTransition>? _transitions;

        public RegistryQuestGuideLocator(IDataRegistryView registry, IExprSchema exprSchema)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _exprSchema = exprSchema ?? throw new ArgumentNullException(nameof(exprSchema));
        }

        /// <summary>丢弃缓存的通道列表（数据表热重载之后调用）。</summary>
        public void Invalidate() => _transitions = null;

        public bool TryResolveArea(Id areaId, out QuestGuideAnchor anchor)
        {
            anchor = default;
            var record = _registry.Get("area.trigger_def", areaId);
            if (record == null)
            {
                return false;
            }

            var def = AreaTriggerDef.FromRecord(record);
            anchor = new QuestGuideAnchor(def.MapId, def.Shape.Origin, areaId);
            return true;
        }

        public bool TryResolveSpawn(Id spawnId, out QuestGuideAnchor anchor)
        {
            anchor = default;
            var record = _registry.Get(SpawnSchemas.Table.Name, spawnId);
            if (record == null || !record.TryGetId("map_id", out var mapId) || !record.TryGetObject("position", out var pos) ||
                !pos.TryGetValue("x", out var x) || !(x is Core.Foundation.Common.Json.JsonNumber xn) ||
                !pos.TryGetValue("y", out var y) || !(y is Core.Foundation.Common.Json.JsonNumber yn))
            {
                return false;
            }

            anchor = new QuestGuideAnchor(mapId, new Vec2(xn.Value, yn.Value), spawnId);
            return true;
        }

        public IReadOnlyList<QuestGuideTransition> Transitions => _transitions ??= BuildTransitions();

        private IReadOnlyList<QuestGuideTransition> BuildTransitions()
        {
            var result = new List<QuestGuideTransition>();
            if (!_registry.TryGetAll("area.trigger_def", out var records))
            {
                return result;
            }

            foreach (var record in records)
            {
                var def = AreaTriggerDef.FromRecord(record);
                if (def.TriggerType != AreaTriggerType.MapTransition || def.MapTransition == null)
                {
                    continue;
                }

                var condition = string.IsNullOrEmpty(def.ConditionText) ? null : ExprParser.Parse(def.ConditionText!, _exprSchema);
                result.Add(new QuestGuideTransition(def.Id, def.MapId, def.MapTransition.Value.TargetMap, def.Shape.Origin, condition));
            }

            return result;
        }
    }
}

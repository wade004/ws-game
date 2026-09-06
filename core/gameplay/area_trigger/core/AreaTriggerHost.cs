using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Foundation.SimLoop;
using Core.Gameplay.WorldState;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// <see cref="IAreaTriggerHost"/> 的默认实现（见 05_对象模型与世界.md 第 7、7.1 节）。构造注入
    /// <see cref="IWorldSim"/>（区域触发体实体的创建/销毁，见类型顶部加固判断记录）、
    /// <see cref="Core.Gameplay.WorldState.IWorldState"/>（<c>one_shot</c> 标志）、
    /// <see cref="IEventBus"/>、<see cref="IExprHostFactory"/>（<c>condition</c> 求值）；
    /// <see cref="IHookRegistry"/> 可选——未注入时 <c>script</c> 类型触发只记一条诊断（见
    /// <see cref="AreaTriggerOptions"/> 顶部同款处理方式）。
    /// <para>
    /// 判断记录：<c>condition</c> 文本解析用 <see cref="ConditionSchema"/>——
    /// <c>Core.Rules.ExprHost.RulesExprSchema.Compose</c>（覆盖 <c>self</c>/<c>target</c>/
    /// <c>combat</c>/<c>enemies</c>/<c>time</c> 五个内置分组，严格模式：未登记的 <c>group.key</c>
    /// 一律按 Id 字面量解析，见该类型判断记录 ADR-0015）叠加
    /// <c>Core.Gameplay.WorldState.WorldExprSchemaEntries.BuildStandalone()</c>（补 <c>world.get</c>/
    /// <c>world.has</c>/<c>world.get_int</c> 三条精确签名）合并而成。本模块不新增任何自有
    /// <c>group.key</c> 组合（见 <see cref="AreaTriggerExprSchemaEntries"/>），只消费这两处既有登记表。
    /// </para>
    /// <para>
    /// 判断记录（实体化：字典键仍用 <c>TriggerId</c>，<c>RuntimeEntry</c> 维护到 <c>EntityId</c> 的
    /// 映射，二选一见任务书）：<see cref="IAreaTriggerHost.Register"/>/<see cref="Unregister"/> 的
    /// 契约签名（入参/返回值都是 <c>triggerId</c>，见 05 第 7.1 节）不能变——<c>RegisterTrap</c> 的
    /// 调用方（<c>core/carriers/gobj</c>）与既有测试都按 <c>triggerId</c> 引用触发体。本类型因此继续
    /// 用 <see cref="_entries"/>（<c>SortedDictionary&lt;Id, RuntimeEntry&gt;</c>，键仍是
    /// <c>TriggerId</c>）维护登记表，<c>RuntimeEntry</c> 新增一个 <c>EntityId</c> 字段记录本次
    /// 登记在 <see cref="IWorldSim"/> 里对应创建的 <see cref="AreaTriggerEntity"/> 的运行期实例 id
    /// （由 <see cref="IWorldSim.AllocateEntityId"/> 独立分配，不等于 <c>TriggerId</c>——后者是内容
    /// 模板 id 或陷阱内部生成 id，前者是"运行期唯一实例 id"，二者语义不同，见 05 第 1.1 节
    /// <c>entityId</c>/<c>templateId</c> 字段表）。
    /// </para>
    /// </summary>
    public sealed class AreaTriggerHost : IAreaTriggerHost
    {
        /// <summary>见类型顶部判断记录：<c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/
        /// <c>time</c>（<c>RulesExprSchema.Base</c>）+ <c>world.get</c>/<c>world.has</c>/
        /// <c>world.get_int</c>（<see cref="Core.Gameplay.WorldState.WorldExprSchemaEntries"/>）。</summary>
        private static readonly IExprSchema ConditionSchema =
            Core.Rules.ExprHost.RulesExprSchema.Compose(Core.Gameplay.WorldState.WorldExprSchemaEntries.BuildStandalone());

        private sealed class RuntimeEntry
        {
            public Id TriggerId;

            /// <summary>本次登记在 <see cref="IWorldSim"/> 里对应创建的 <see cref="AreaTriggerEntity"/>
            /// 运行期实例 id（见类型顶部判断记录：与 <see cref="TriggerId"/> 语义不同、独立分配）。</summary>
            public Id EntityId;
            public Id MapId;
            public Shape Shape;
            public AreaTriggerKind Kind;
            public string? ConditionText;
            public ExprNode? ConditionNode;
            public bool OneShot;
            public MapTransitionParams? MapTransition;
            public EncounterStartParams? EncounterStart;
            public ScriptParams? Script;
            public Id? TrapGobjId;
        }

        private readonly IWorldSim _world;
        private readonly IWorldState _worldState;
        private readonly IEventBus _bus;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IHookRegistry? _hooks;
        private readonly AreaTriggerOptions _options;
        private readonly IAreaTriggerDiagnostics _diagnostics;

        // SortedDictionary 保证 Evaluate 按 TriggerId 序数遍历，跨调用/跨单位确定性一致
        // （惯例同 IUnitAccess.AllUnits"按 Id 序数排序"）。
        private readonly SortedDictionary<Id, RuntimeEntry> _entries = new SortedDictionary<Id, RuntimeEntry>();

        // (triggerId, unitId) 当前是否处于"已成功进入"状态（见 Evaluate 判断记录：只有真正触发过
        // AreaTriggerEnteredEvent 的组合才会被记入，条件不满足/one_shot 已触发时不记入，避免离开时
        // 产生没有对应"进入"的孤立 AreaTriggerLeftEvent）。
        private readonly HashSet<(Id TriggerId, Id UnitId)> _inside = new HashSet<(Id, Id)>();

        private long _nextTrapSeq = 1;

        public AreaTriggerHost(
            IWorldSim world,
            IWorldState worldState,
            IEventBus bus,
            IExprHostFactory exprHostFactory,
            IHookRegistry? hooks = null,
            AreaTriggerOptions? options = null,
            IAreaTriggerDiagnostics? diagnostics = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _hooks = hooks;
            _options = options ?? new AreaTriggerOptions();
            _diagnostics = diagnostics ?? new InMemoryAreaTriggerDiagnostics();
        }

        // -----------------------------------------------------------------
        // IAreaTriggerHost
        // -----------------------------------------------------------------

        public Id Register(AreaTriggerDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            // 判断记录（同一 triggerId 重复 Register 的防御性处理）：理论上不应发生（内容管线保证
            // area.trigger_def.id 唯一），但此前的纯字典实现允许静默覆盖——实体化后若不先销毁旧
            // 实体就覆盖 _entries，会在 IWorldSim 里残留一个再也没有登记表条目指向它的"孤儿"
            // AreaTriggerEntity（Unregister 再也找不到它的 EntityId）。这里补一步：命中重复 id 时
            // 先销毁旧实体，保持"一个 triggerId 对应至多一个存活实体"的不变量。
            if (_entries.TryGetValue(def.Id, out var existing))
            {
                _world.MarkForDestruction(existing.EntityId);
            }

            var entityId = _world.AllocateEntityId(EntityKinds.AreaTrigger);
            var entity = new AreaTriggerEntity(entityId, def.MapId, def.Shape, def.TriggerType, def.ConditionText, def.OneShot)
            {
                Position = def.Shape.Origin,
                TemplateId = def.Id,
            };
            _world.AddEntity(entity);

            var entry = new RuntimeEntry
            {
                TriggerId = def.Id,
                EntityId = entityId,
                MapId = def.MapId,
                Shape = def.Shape,
                Kind = AreaTriggerKindConvert.FromType(def.TriggerType),
                ConditionText = def.ConditionText,
                ConditionNode = ParseCondition(def.ConditionText),
                OneShot = def.OneShot,
                MapTransition = def.MapTransition,
                EncounterStart = def.EncounterStart,
                Script = def.Script,
            };

            _entries[def.Id] = entry;
            return def.Id;
        }

        public void Unregister(Id triggerId)
        {
            if (_entries.TryGetValue(triggerId, out var entry))
            {
                _world.MarkForDestruction(entry.EntityId);
            }

            _entries.Remove(triggerId);
            _inside.RemoveWhere(pair => pair.TriggerId.Equals(triggerId));
        }

        public void LoadForMap(Id mapId, IDataRegistryView data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            foreach (var record in data.GetAll(AreaTriggerSchemas.TriggerDef.Name))
            {
                var def = AreaTriggerDef.FromRecord(record);
                if (def.MapId.Equals(mapId))
                {
                    Register(def);
                }
            }
        }

        public void UnloadMap(Id mapId)
        {
            var toRemove = new List<Id>();
            foreach (var entry in _entries.Values)
            {
                if (entry.MapId.Equals(mapId))
                {
                    toRemove.Add(entry.TriggerId);
                }
            }

            foreach (var triggerId in toRemove)
            {
                Unregister(triggerId);
            }
        }

        public Id RegisterTrap(Id gobjInstanceId, Shape shape, Id mapId)
        {
            var triggerId = new Id("area.trap_" + _nextTrapSeq++);

            // 陷阱同样生成一个 Kind=area_trigger 的实体（见 AreaTriggerEntity 类型注释判断记录）：
            // TriggerType 为 null（trap 不在 05 第 1.5 节 triggerType 枚举里）；陷阱不是内容驱动，
            // 没有对应的 area.trigger_def 模板 id，TemplateId 留空（惯例同 Entity.TemplateId"手工
            // 放置对象可为空"）。
            var entityId = _world.AllocateEntityId(EntityKinds.AreaTrigger);
            var entity = new AreaTriggerEntity(entityId, mapId, shape, triggerType: null, conditionText: null, oneShot: false)
            {
                Position = shape.Origin,
            };
            _world.AddEntity(entity);

            var entry = new RuntimeEntry
            {
                TriggerId = triggerId,
                EntityId = entityId,
                MapId = mapId,
                Shape = shape,
                Kind = AreaTriggerKind.Trap,
                OneShot = false,
                TrapGobjId = gobjInstanceId,
            };

            _entries[triggerId] = entry;
            return triggerId;
        }

        public void Evaluate(Id unitId, Vec2 position)
        {
            foreach (var entry in _entries.Values)
            {
                var key = (entry.TriggerId, unitId);
                var wasInside = _inside.Contains(key);
                var isInside = AreaTriggerShapeGeometry.Contains(entry.Shape, position);

                if (isInside && !wasInside)
                {
                    HandleEnter(entry, unitId, key);
                }
                else if (!isInside && wasInside)
                {
                    HandleLeave(entry, unitId, key);
                }
            }
        }

        // -----------------------------------------------------------------
        // 内部实现
        // -----------------------------------------------------------------

        /// <summary>见 05 第 7.1 节顺序："进入且 condition 为真 → 若 one_shot 且已触发则跳过；发
        /// area.trigger_entered；按类型分发；one_shot 置标志"——本方法按此字面顺序实现：先判
        /// condition，再判 one_shot 是否已触发；两者均通过才记入 <see cref="_inside"/> 并发事件/
        /// 分发，避免出现"从未成功进入却收到离开事件"的孤立 <see cref="AreaTriggerLeftEvent"/>（见
        /// <see cref="_inside"/> 字段注释）。</summary>
        private void HandleEnter(RuntimeEntry entry, Id unitId, (Id TriggerId, Id UnitId) key)
        {
            if (!ConditionPasses(entry, unitId))
            {
                return;
            }

            if (entry.OneShot && HasFired(entry.TriggerId))
            {
                return;
            }

            _inside.Add(key);
            _bus.Enqueue(new AreaTriggerEnteredEvent(entry.TriggerId, unitId));
            Dispatch(entry, unitId);

            if (entry.OneShot)
            {
                MarkFired(entry.TriggerId);
            }
        }

        private void HandleLeave(RuntimeEntry entry, Id unitId, (Id TriggerId, Id UnitId) key)
        {
            _inside.Remove(key);
            _bus.Enqueue(new AreaTriggerLeftEvent(entry.TriggerId, unitId));

            if (entry.Kind == AreaTriggerKind.Script)
            {
                InvokeScriptHook(entry, unitId, "leave");
            }
        }

        private void Dispatch(RuntimeEntry entry, Id unitId)
        {
            switch (entry.Kind)
            {
                case AreaTriggerKind.MapTransition:
                    DispatchMapTransition(entry, unitId);
                    break;

                case AreaTriggerKind.QuestExplore:
                    // 05 第 7 节：不做额外分发，任务模块订阅 area.trigger_entered 事件自行推进。
                    break;

                case AreaTriggerKind.EncounterStart:
                    var es = entry.EncounterStart!.Value;
                    if (_options.EncounterStartRequested != null)
                    {
                        _options.EncounterStartRequested(unitId, es.EncounterRef);
                    }
                    else
                    {
                        _diagnostics.Warn($"触发体 \"{entry.TriggerId}\" 是 encounter_start，但未注入 AreaTriggerOptions.EncounterStartRequested，跳过分发");
                    }
                    break;

                case AreaTriggerKind.Script:
                    InvokeScriptHook(entry, unitId, "enter");
                    break;

                case AreaTriggerKind.Trap:
                    if (_options.TrapTrigger != null)
                    {
                        _options.TrapTrigger(entry.TrapGobjId!.Value, unitId);
                    }
                    else
                    {
                        _diagnostics.Warn($"陷阱触发体 \"{entry.TriggerId}\" 未注入 AreaTriggerOptions.TrapTrigger，跳过分发");
                    }
                    break;
            }
        }

        private void DispatchMapTransition(RuntimeEntry entry, Id unitId)
        {
            var mt = entry.MapTransition!.Value;

            if (_options.MapTransitionRequested != null)
            {
                _options.MapTransitionRequested(unitId, mt.TargetMap, mt.SpawnPoint);
            }
            else
            {
                _diagnostics.Warn($"触发体 \"{entry.TriggerId}\" 是 map_transition，但未注入 AreaTriggerOptions.MapTransitionRequested，跳过委托分发");
            }

            if (_options.SceneRouter != null)
            {
                try
                {
                    _options.SceneRouter.LoadScene(mt.TargetMap);
                }
                catch (Exception ex)
                {
                    // 见 AreaTriggerOptions.SceneRouter 判断记录：加载失败不中断本次 Evaluate 循环。
                    _diagnostics.Error($"触发体 \"{entry.TriggerId}\" 请求场景路由加载 \"{mt.TargetMap}\" 失败：{ex.Message}");
                }
            }
        }

        private void InvokeScriptHook(RuntimeEntry entry, Id unitId, string phase)
        {
            if (_hooks == null)
            {
                _diagnostics.Warn($"触发体 \"{entry.TriggerId}\" 是 script 类型，但未注入 IHookRegistry，跳过钩子调用（phase={phase}）");
                return;
            }

            var hookId = entry.Script!.Value.HookId;
            var args = new HookArgs(new Dictionary<string, object?>
            {
                ["triggerId"] = entry.TriggerId,
                ["unitId"] = unitId,
                ["phase"] = phase,
            });

            _hooks.Invoke(hookId, args);
        }

        private bool ConditionPasses(RuntimeEntry entry, Id unitId)
        {
            if (entry.ConditionNode == null)
            {
                return true;
            }

            var host = _exprHostFactory.CreateFor(unitId, null, null);
            var diagnostics = new ExprDiagnosticsRecorder();
            return ExprEvaluator.EvaluateBool(entry.ConditionNode, host, diagnostics);
        }

        private static ExprNode? ParseCondition(string? conditionText) =>
            string.IsNullOrEmpty(conditionText) ? null : ExprParser.Parse(conditionText, ConditionSchema);

        private bool HasFired(Id triggerId) => _worldState.Has(FiredFlagKey(triggerId));

        private void MarkFired(Id triggerId) => _worldState.Set(FiredFlagKey(triggerId), ExprValue.OfBool(true), _options.WriterId);

        /// <summary>见任务书拍板 <c>world.area.&lt;name&gt;.fired</c>：<c>&lt;name&gt;</c> 取
        /// <paramref name="triggerId"/> 去掉 <c>"area."</c> domain 前缀后的剩余部分（陷阱内部生成的
        /// <c>area.trap_&lt;n&gt;</c> id 同样落在这一命名空间下，但陷阱 <see cref="OneShot"/> 恒为
        /// false，不会用到本方法）。</summary>
        private static Id FiredFlagKey(Id triggerId)
        {
            var value = triggerId.Value;
            var dot = value.IndexOf('.');
            var name = dot < 0 ? value : value.Substring(dot + 1);
            return new Id("world.area." + name + ".fired");
        }
    }
}

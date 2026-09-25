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

        /// <summary>诊断出口只读暴露（ABI 只新增只读属性，见 architecture/adr/0042-诊断契约统一转发到宿主控制台.md）：
        /// 供 adapters/unity 侧统一诊断转发机制轮询本实例累积的 Warnings/Errors，不改变本类型任何既有公开签名。</summary>
        public IAreaTriggerDiagnostics Diagnostics => _diagnostics;

        // SortedDictionary 保证 Evaluate 按 TriggerId 序数遍历，跨调用/跨单位确定性一致
        // （惯例同 IUnitAccess.AllUnits"按 Id 序数排序"）。
        private readonly SortedDictionary<Id, RuntimeEntry> _entries = new SortedDictionary<Id, RuntimeEntry>();

        // (triggerId, unitId) 当前是否处于"已成功进入"状态 -> 进入序号（见 Evaluate 判断记录：只有
        // 真正触发过 AreaTriggerEnteredEvent 的组合才会被记入，条件不满足/one_shot 已触发时不记入，
        // 避免离开时产生没有对应"进入"的孤立 AreaTriggerLeftEvent）。
        // 判断记录（ADR-0066，进入序号用确定性单调计数而不是墙钟时间）：GetActiveTriggerIds 契约要求
        // "按进入先后排序"，取墙钟时间戳会破坏本仓库"运行时路径不依赖系统时间"的确定性铁律（见
        // AGENTS.md §3、本仓库其它模块同类判断记录），改用 _nextEnterSeq 单调递增计数——同一进程内
        // 全部单位、全部触发体共用一个计数器，任意两次成功进入的相对先后关系严格保序，足以满足"某单位
        // 自己名下多个触发体的进入先后"这一查询需求（不要求跨单位可比）。
        private readonly Dictionary<(Id TriggerId, Id UnitId), long> _inside = new Dictionary<(Id, Id), long>();

        private long _nextEnterSeq = 1;

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
            RemoveInsideForTrigger(triggerId);
        }

        /// <summary>见 <see cref="_inside"/> 判断记录：<see cref="Unregister"/>/<see cref="UnloadMap"/>
        /// （后者逐个转调 <see cref="Unregister"/>，见该方法）都要清掉对应触发体在 <see cref="_inside"/>
        /// 里的记录，否则会残留一个再也不会被 <see cref="HandleLeave"/> 清除的"孤儿"进入记录，
        /// <see cref="GetActiveTriggerIds"/> 会持续报告一个已经被移除登记的触发体。
        /// <para>
        /// 判断记录（ADR-0090，消费方第三十六批相邻缺口）：清理前，对每个命中的 <c>(triggerId,
        /// unitId)</c> 组合各补发一条 <c>reason=Unloaded</c> 的 <see cref="AreaTriggerLeftEvent"/>——
        /// 触发体本身即将不存在（<see cref="Unregister"/> 总是先 <see cref="IWorldSim.MarkForDestruction"/>
        /// 对应实体），单位自然也就"不在其中了"，事件必须与 <see cref="GetActiveTriggerIds"/> 的状态
        /// 保持一致，否则由"离开"规则负责收尾的下游逻辑（如 ADR-0089 循环音效的 <c>stop_sfx</c>）永远
        /// 等不到这条事件。只对仍在 <see cref="_inside"/> 里的组合发（已经正常走出的单位不会残留在
        /// 这里，天然不会重复发送）；<see cref="UnloadMap"/> 逐个调用 <see cref="Unregister"/>，无需
        /// 额外处理即继承同一份补发逻辑。
        /// </para>
        /// </summary>
        private void RemoveInsideForTrigger(Id triggerId)
        {
            List<(Id TriggerId, Id UnitId)>? toRemove = null;
            foreach (var key in _inside.Keys)
            {
                if (key.TriggerId.Equals(triggerId))
                {
                    (toRemove ??= new List<(Id, Id)>()).Add(key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            foreach (var key in toRemove)
            {
                _inside.Remove(key);
                _bus.Enqueue(new AreaTriggerLeftEvent(key.TriggerId, key.UnitId, AreaTriggerLeaveReason.Unloaded));
            }
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
                var wasInside = _inside.ContainsKey(key);
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
        // ADR-0066：IAreaTriggerHost 显式接口实现（判断记录同 Core.Rules.Common.ISkillHost 同类新增
        // 成员——不让 GetActiveTriggerIds 隐式满足接口成员，避免其物理 IL 属性被编译器改写成
        // virtual sealed，被 toolchain/abi_surface 逐字节比对误判为破坏；公开方法本身供本类型内部/
        // 测试直接调用，物理签名是普通实例方法）。
        // -----------------------------------------------------------------

        /// <summary>见 <see cref="IAreaTriggerHost.GetActiveTriggerIds"/> 判断记录：<paramref
        /// name="unitId"/> 当前所在的全部触发区域 id，按 <see cref="_inside"/> 记录的进入序号升序
        /// 排列（最早进入的在最前，最近进入的在最后）。</summary>
        public IReadOnlyList<Id> GetActiveTriggerIds(Id unitId)
        {
            List<(Id TriggerId, long Seq)>? matches = null;
            foreach (var pair in _inside)
            {
                if (pair.Key.UnitId.Equals(unitId))
                {
                    (matches ??= new List<(Id, long)>()).Add((pair.Key.TriggerId, pair.Value));
                }
            }

            if (matches == null)
            {
                return Array.Empty<Id>();
            }

            // 进入序号（_nextEnterSeq 单调递增分配，见 _inside 字段判断记录）两两不同，Sort 不存在
            // 需要稳定性的并列项，结果确定性不依赖 List<T>.Sort 是否稳定。
            matches.Sort((a, b) => a.Seq.CompareTo(b.Seq));

            var result = new Id[matches.Count];
            for (var i = 0; i < matches.Count; i++)
            {
                result[i] = matches[i].TriggerId;
            }

            return result;
        }

        IReadOnlyList<Id> IAreaTriggerHost.GetActiveTriggerIds(Id unitId) => GetActiveTriggerIds(unitId);

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

            _inside.Add(key, _nextEnterSeq++);
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

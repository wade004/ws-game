using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Gameplay.Common;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <see cref="IEncounterHost"/> 的默认（唯一）实现（见 08 第 4、9 节）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性解析 <c>encounter.def</c>，把 <c>victory_condition</c>/
    /// <c>defeat_condition</c>/<c>waves[].trigger_condition</c>/<c>phases[].enter_condition</c> 编译
    /// 成 <see cref="ExprNode"/>（<c>exprSchema</c> 可选注入，默认 <see cref="RulesExprSchema.Base"/>，
    /// 惯例同 <c>AchievementHost</c>）；之后只读。
    /// </summary>
    public sealed class EncounterHost : IEncounterHost
    {
        private sealed class CompiledWave
        {
            public ExprNode TriggerCondition = null!;
            public List<Id> SpawnRefs = new List<Id>();
        }

        private sealed class CompiledPhase
        {
            public ExprNode EnterCondition = null!;
            public Dictionary<Id, Id> AiRotationOverride = new Dictionary<Id, Id>();
            public Id? OnEnterHook;
        }

        private sealed class CompiledDef
        {
            public Id Id;
            public List<EncounterUnitSpec> Units = new List<EncounterUnitSpec>();
            public List<CompiledWave> Waves = new List<CompiledWave>();
            public List<CompiledPhase> Phases = new List<CompiledPhase>();
            public Shape? ArenaBoundsShape;
            public bool ArenaResetIfLeave;
            public ExprNode VictoryCondition = null!;
            public ExprNode DefeatCondition = null!;
            public RewardBundle Rewards = RewardBundle.Empty;

            /// <summary>收边任务补齐（08 第 4.1 节字段表 <c>combat_mode_override</c>/
            /// <c>initiative_override</c>）：此前只在 <see cref="EncounterDefinition"/> 落地读取，
            /// 从未透传到 <see cref="CompiledDef"/>/<see cref="Instance"/>，<c>TimeModelSwitch.
            /// SetPendingOverride</c> 因此从无调用点——本次任务补上：<see cref="Start"/> 时原样
            /// 带入实例，供 <see cref="TryGetModeOverride"/> 供 <c>GameplayAssembly</c> 读取。</summary>
            public string? CombatModeOverride;
            public JsonObject? InitiativeOverride;
        }

        private sealed class Instance
        {
            public Id InstanceId;
            public CompiledDef Def = null!;
            public Id MapId;
            public Id PlayerUnitId;
            public bool[] WaveTriggered = Array.Empty<bool>();
            public int CurrentPhaseIndex = -1;
            public List<Id> ParticipantUnitIds = new List<Id>();
            public bool IsActive = true;
        }

        private readonly SortedDictionary<string, CompiledDef> _defs = new SortedDictionary<string, CompiledDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, Instance> _instances = new Dictionary<string, Instance>(StringComparer.Ordinal);
        private readonly List<Id> _instanceOrder = new List<Id>();

        private readonly IEventBus _bus;
        private readonly ICreatureFactory _creatureFactory;
        private readonly IAiHost _aiHost;
        private readonly IHookRegistry _hooks;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IRewardDispatcher _rewardDispatcher;
        private readonly IUnitAccess _units;
        private readonly SpawnRequester _spawnRequester;
        private readonly IExprDiagnostics _diagnostics;

        private int _nextInstanceNumber = 1;

        public EncounterHost(
            IDataRegistryView registry,
            IEventBus bus,
            ICreatureFactory creatureFactory,
            IAiHost aiHost,
            IHookRegistry hooks,
            IExprHostFactory exprHostFactory,
            IRewardDispatcher rewardDispatcher,
            IUnitAccess units,
            SpawnRequester spawnRequester,
            IExprSchema? exprSchema = null,
            IExprDiagnostics? diagnostics = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _creatureFactory = creatureFactory ?? throw new ArgumentNullException(nameof(creatureFactory));
            _aiHost = aiHost ?? throw new ArgumentNullException(nameof(aiHost));
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _rewardDispatcher = rewardDispatcher ?? throw new ArgumentNullException(nameof(rewardDispatcher));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _spawnRequester = spawnRequester ?? throw new ArgumentNullException(nameof(spawnRequester));
            var schema = exprSchema ?? RulesExprSchema.Base;
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();

            foreach (var record in registry.GetAll(EncounterSchemas.Def.Name))
            {
                var def = EncounterDefinition.FromRecord(record);
                var compiled = new CompiledDef
                {
                    Id = def.Id,
                    Units = new List<EncounterUnitSpec>(def.Units),
                    ArenaBoundsShape = def.ArenaRules?.BoundsShape,
                    ArenaResetIfLeave = def.ArenaRules?.ResetIfLeave ?? false,
                    VictoryCondition = ExprParser.Parse(def.VictoryConditionText, schema),
                    DefeatCondition = ExprParser.Parse(def.DefeatConditionText, schema),
                    Rewards = def.Rewards,
                    CombatModeOverride = def.CombatModeOverride,
                    InitiativeOverride = def.InitiativeOverride,
                };

                foreach (var wave in def.Waves)
                {
                    compiled.Waves.Add(new CompiledWave
                    {
                        TriggerCondition = ExprParser.Parse(wave.TriggerConditionText, schema),
                        SpawnRefs = new List<Id>(wave.SpawnRefs),
                    });
                }

                foreach (var phase in def.Phases)
                {
                    compiled.Phases.Add(new CompiledPhase
                    {
                        EnterCondition = ExprParser.Parse(phase.EnterConditionText, schema),
                        AiRotationOverride = new Dictionary<Id, Id>(phase.AiRotationOverride),
                        OnEnterHook = phase.OnEnterHook,
                    });
                }

                _defs[def.Id.Value] = compiled;
            }
        }

        // -----------------------------------------------------------------
        // IEncounterHost
        // -----------------------------------------------------------------

        public Id Start(Id encounterId, Id mapId, Id playerUnitId)
        {
            var def = RequireDef(encounterId);

            var instanceId = new Id($"encounter.inst_{_nextInstanceNumber}");
            _nextInstanceNumber++;

            var instance = new Instance
            {
                InstanceId = instanceId,
                Def = def,
                MapId = mapId,
                PlayerUnitId = playerUnitId,
                WaveTriggered = new bool[def.Waves.Count],
                CurrentPhaseIndex = -1,
                IsActive = true,
            };

            SpawnInitialUnits(instance);

            _instances[instanceId.Value] = instance;
            _instanceOrder.Add(instanceId);

            _bus.PublishImmediate(new EncounterStartedEvent(instanceId));

            return instanceId;
        }

        public void Evaluate(Id instanceId)
        {
            if (!_instances.TryGetValue(instanceId.Value, out var instance) || !instance.IsActive)
            {
                return;
            }

            var def = instance.Def;
            var host = _exprHostFactory.CreateFor(instance.PlayerUnitId, null, null);

            // 1) 波次：逐条检查尚未触发的 trigger_condition。
            for (var i = 0; i < def.Waves.Count; i++)
            {
                if (instance.WaveTriggered[i])
                {
                    continue;
                }

                if (!ExprEvaluator.EvaluateBool(def.Waves[i].TriggerCondition, host, _diagnostics))
                {
                    continue;
                }

                instance.WaveTriggered[i] = true;
                var spawnedIds = new List<Id>();
                foreach (var spawnRef in def.Waves[i].SpawnRefs)
                {
                    var ids = _spawnRequester(spawnRef, instance.MapId);
                    if (ids != null)
                    {
                        spawnedIds.AddRange(ids);
                        instance.ParticipantUnitIds.AddRange(ids);
                    }
                }
                _bus.PublishImmediate(new EncounterWaveSpawnedEvent(instanceId, i, spawnedIds));
            }

            // 2) 阶段：按声明顺序单调推进，一次 Evaluate 至多切换一个阶段（08 第 4.3 节"阶段单调
            // 递进"，判断记录：不允许因为一次求值里连续多个 enter_condition 同时为真就一口气跳过
            // 中间阶段——只要下一候选阶段条件不满足就停止本轮阶段检查，不再看更靠后的阶段）。
            var nextPhaseIndex = instance.CurrentPhaseIndex + 1;
            if (nextPhaseIndex < def.Phases.Count && ExprEvaluator.EvaluateBool(def.Phases[nextPhaseIndex].EnterCondition, host, _diagnostics))
            {
                var oldPhase = instance.CurrentPhaseIndex;
                instance.CurrentPhaseIndex = nextPhaseIndex;
                ApplyPhase(instance, def.Phases[nextPhaseIndex]);
                _bus.PublishImmediate(new EncounterPhaseChangedEvent(instanceId, oldPhase, nextPhaseIndex));
            }

            // 3) 场地规则：玩家离开边界则重置（重置后本次 Evaluate 结束，不再继续判定胜负——
            // 参战单位已被清空重生，胜负条件此刻求值没有意义）。
            if (def.ArenaBoundsShape.HasValue && def.ArenaResetIfLeave)
            {
                var playerPos = _units.GetPosition(instance.PlayerUnitId);
                if (!EncounterShapeMath.Contains(def.ArenaBoundsShape.Value, playerPos))
                {
                    ResetInstance(instance);
                    return;
                }
            }

            // 4) 胜负判定。
            if (ExprEvaluator.EvaluateBool(def.VictoryCondition, host, _diagnostics))
            {
                instance.IsActive = false;
                if (!def.Rewards.IsEmpty)
                {
                    _rewardDispatcher.Grant(instance.PlayerUnitId, def.Rewards, def.Id);
                }
                _bus.PublishImmediate(new EncounterWonEvent(instanceId));
                return;
            }

            if (ExprEvaluator.EvaluateBool(def.DefeatCondition, host, _diagnostics))
            {
                instance.IsActive = false;
                _bus.PublishImmediate(new EncounterLostEvent(instanceId));
            }
        }

        public void Abort(Id instanceId)
        {
            var instance = RequireInstance(instanceId);
            instance.IsActive = false;
        }

        public IReadOnlyList<Id> AbortForMap(Id mapId)
        {
            var aborted = new List<Id>();
            foreach (var id in _instanceOrder)
            {
                if (_instances.TryGetValue(id.Value, out var instance) && instance.IsActive && instance.MapId.Equals(mapId))
                {
                    instance.IsActive = false;
                    aborted.Add(id);
                }
            }
            return aborted;
        }

        public EncounterState GetState(Id instanceId)
        {
            var instance = RequireInstance(instanceId);
            return new EncounterState(instance.CurrentPhaseIndex, (bool[])instance.WaveTriggered.Clone(), instance.IsActive);
        }

        /// <summary>收边任务补齐：不在 <see cref="IEncounterHost"/> 契约里的具体类型便利成员
        /// （惯例同 <c>TurnScheduler.AddParticipant</c>/<c>RemoveParticipant</c> 判断记录），供
        /// <c>GameplayAssembly</c> 在收到 <see cref="EncounterStartedEvent"/> 时读取该次运行对应
        /// <c>encounter.def</c> 的 <c>combat_mode_override</c>/<c>initiative_override</c>，据此调用
        /// <c>TimeModelSwitch.SetPendingOverride</c>（见 08 第 4.1 节字段表）。<paramref name="instanceId"/>
        /// 不存在时返回 <c>false</c>（防御性：调用方是事件订阅回调，不应因为查不到实例就抛异常）。
        /// 实例结束（<see cref="EncounterState.IsActive"/> 为 false）后仍可查询——<see cref="_instances"/>
        /// 不移除已结束的实例（见 <see cref="Abort"/> 判断记录），<c>encounter.won</c>/<c>encounter.lost</c>
        /// 收尾时调用方仍需要能查到（尽管收尾只是清空 pending override，不再依赖具体取值）。</summary>
        public bool TryGetModeOverride(Id instanceId, out string? combatModeOverride, out JsonObject? initiativeOverride)
        {
            if (_instances.TryGetValue(instanceId.Value, out var instance))
            {
                combatModeOverride = instance.Def.CombatModeOverride;
                initiativeOverride = instance.Def.InitiativeOverride;
                return true;
            }

            combatModeOverride = null;
            initiativeOverride = null;
            return false;
        }

        public IReadOnlyList<Id> ActiveInstanceIds
        {
            get
            {
                var result = new List<Id>();
                foreach (var id in _instanceOrder)
                {
                    if (_instances.TryGetValue(id.Value, out var instance) && instance.IsActive)
                    {
                        result.Add(id);
                    }
                }
                return result;
            }
        }

        // -----------------------------------------------------------------
        // 内部辅助
        // -----------------------------------------------------------------

        private void SpawnInitialUnits(Instance instance)
        {
            foreach (var unit in instance.Def.Units)
            {
                if (unit.TemplateRef.HasValue)
                {
                    // 08 第 4.1 节 position 标注可选：内联模板缺省时退化为 Vec2.Zero（判断记录，
                    // 见 EncounterUnitSpec 注释）。
                    var position = unit.Position ?? Vec2.Zero;
                    var entityId = _creatureFactory.Spawn(unit.TemplateRef.Value, instance.MapId, position, facing: 0);
                    instance.ParticipantUnitIds.Add(entityId);
                }
                else
                {
                    var ids = _spawnRequester(unit.SpawnRef!.Value, instance.MapId);
                    if (ids != null)
                    {
                        instance.ParticipantUnitIds.AddRange(ids);
                    }
                }
            }
        }

        /// <summary>08 第 4.3 节"reset_if_leave...Reset（销毁参战单位并重新 Start，README 说明）"：
        /// 判断记录——本实现原地复位同一个 <see cref="Instance"/>（同一 instanceId），不分配新
        /// instanceId，因为 <see cref="Evaluate"/> 签名是 <c>void</c>，没有把"重开产生的新实例 id"
        /// 传回调用方的通道；"重新 Start"按语义理解为"把这次遭遇的运行状态恢复到刚开始的样子"，
        /// 而不是必须换一个新的实例标识。</summary>
        private void ResetInstance(Instance instance)
        {
            foreach (var unitId in instance.ParticipantUnitIds)
            {
                if (_units.Exists(unitId))
                {
                    _creatureFactory.Despawn(unitId, "encounter_reset");
                }
            }

            instance.ParticipantUnitIds.Clear();
            Array.Clear(instance.WaveTriggered, 0, instance.WaveTriggered.Length);
            instance.CurrentPhaseIndex = -1;

            SpawnInitialUnits(instance);
        }

        /// <summary>把 <c>phases[].ai_rotation_override</c> 应用到参战单位（06 第 6.4 节"Boss 阶段 =
        /// 换 Rotation"）：键既可能是具体参战单位 id，也可能是模板 id（任务书原句"键为模板 id 时对
        /// 该模板的全部参战单位"）——判断记录：用"键是否等于某个当前参战单位 id"区分，是则按单位 id
        /// 精确处理；否则退化为按模板 id 匹配 <see cref="IUnitAccess.GetTemplateId"/> 命中的全部
        /// 参战单位（都不命中则该键无效果，不报错——阶段切换不因为一个无效键而中断整体处理）。</summary>
        private void ApplyPhase(Instance instance, CompiledPhase phase)
        {
            foreach (var kv in phase.AiRotationOverride)
            {
                var key = kv.Key;
                var rotationId = kv.Value;

                if (ContainsUnit(instance, key))
                {
                    _aiHost.SetRotation(key, rotationId);
                    continue;
                }

                foreach (var unitId in instance.ParticipantUnitIds)
                {
                    if (_units.Exists(unitId) && _units.GetTemplateId(unitId) is Id templateId && templateId.Equals(key))
                    {
                        _aiHost.SetRotation(unitId, rotationId);
                    }
                }
            }

            if (phase.OnEnterHook.HasValue)
            {
                _hooks.Invoke(phase.OnEnterHook.Value, HookArgs.Empty);
            }
        }

        private static bool ContainsUnit(Instance instance, Id unitId)
        {
            for (var i = 0; i < instance.ParticipantUnitIds.Count; i++)
            {
                if (instance.ParticipantUnitIds[i].Equals(unitId))
                {
                    return true;
                }
            }
            return false;
        }

        private CompiledDef RequireDef(Id encounterId)
        {
            if (_defs.TryGetValue(encounterId.Value, out var def))
            {
                return def;
            }
            throw new ArgumentException($"未知的 encounter.def \"{encounterId}\"", nameof(encounterId));
        }

        private Instance RequireInstance(Id instanceId)
        {
            if (_instances.TryGetValue(instanceId.Value, out var instance))
            {
                return instance;
            }
            throw new ArgumentException($"未知的遭遇实例 \"{instanceId}\"", nameof(instanceId));
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Projectile
{
    /// <summary>
    /// <see cref="IProjectileSpawner"/> 的唯一实现（见该接口判断记录"依赖倒置"）：按
    /// <c>projectile</c> 效果的参数生成一个 05 第 1.4 节 <c>Projectile</c> 实体，随后由
    /// <see cref="Advance"/>（<see cref="ProjectileTickHandler"/> 每 tick 调用）按飞行方式推进
    /// 位置、经 <see cref="ISpatialQuery"/>/<see cref="INavigation2D"/> 判定命中，命中后把
    /// <c>on_hit_effects</c> 逐项交回调用方在 <see cref="IProjectileSpawner.Spawn"/> 传入的
    /// <see cref="IEffectSink"/>。
    /// <para>
    /// 判断记录（运行期簿记不放进 <see cref="ProjectileEntity"/>）：瞄准目标、已飞行距离、已命中
    /// 单位集合、命中后效果列表、效果回灌出口等字段只在本类内部的 <see cref="_states"/> 字典维护，
    /// 不是 05 第 1.4 节定义的对象模型字段，见 <see cref="ProjectileEntity"/> 类型注释。
    /// </para>
    /// <para>
    /// 判断记录（不注册进 <see cref="ISpatialQuery"/> 空间索引）：<c>Core.Carriers.Assembly.
    /// CarriersAssembly.DefaultSpatialSyncKinds</c> 默认只登记 <c>creature</c>/<c>player</c> 两类
    /// Unit（"避免'最近敌人'捞到物件"同一顾虑延伸到投射物——若投射物也登记进同一空间索引，任何
    /// 不显式按标签过滤的目标解析/AI 感知查询都可能把飞行中的箭矢当成候选目标），本类因此从不
    /// 调用 <see cref="ISpatialQuery.Register"/>/<see cref="ISpatialQuery.UpdatePosition"/> 登记
    /// 投射物自身；本类只把 <see cref="ISpatialQuery"/> 当"查询别的单位"的只读工具使用。
    /// </para>
    /// <para>
    /// 判断记录（不新增专属事件）：06 第 8 节事件词汇表未给投射物登记 <c>projectile.spawned</c>/
    /// <c>projectile.expired</c> 一类专属事件（07/08 GameObject/Loot 一类实体各自的"专属事件"都是
    /// 已有登记表行，投射物没有对应行）；本类复用 <see cref="IWorldSim.AddEntity"/>/
    /// <see cref="IWorldSim.MarkForDestruction"/> 自身触发的 <c>entity.created</c>/
    /// <c>entity.destroyed</c> 通用事件覆盖"生成"/"销毁"两个时机，命中后效果本身产生的事件
    /// （如 <c>combat.damage_dealt</c>）经 <see cref="IEffectSink.ApplyEffect"/> 走既有管线正常
    /// 发出，不需要本类额外发一条"投射物命中了"的事件。
    /// </para>
    /// </summary>
    public sealed class ProjectileHost : IProjectileSpawner
    {
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;
        private readonly ISpatialQuery _spatial;
        private readonly INavigation2D? _navigation;
        private readonly ProjectileOptions _options;
        private readonly IProjectileDiagnostics _diagnostics;

        private readonly Dictionary<Id, ProjectileState> _states = new Dictionary<Id, ProjectileState>();

        public ProjectileHost(
            IWorldSim world,
            IUnitAccess units,
            ISpatialQuery spatial,
            INavigation2D? navigation = null,
            ProjectileOptions? options = null,
            IProjectileDiagnostics? diagnostics = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _navigation = navigation;
            _options = options ?? new ProjectileOptions();
            _diagnostics = diagnostics ?? new InMemoryProjectileDiagnostics();
        }

        /// <summary>当前存活（未销毁）的投射物数量，供测试/诊断查看。</summary>
        public int ActiveCount => _states.Count;

        // -----------------------------------------------------------------
        // IProjectileSpawner
        // -----------------------------------------------------------------

        public void Spawn(EffectContext context, IEffectSink effectSink)
        {
            if (effectSink == null) throw new ArgumentNullException(nameof(effectSink));

            var sourceEntity = _world.GetEntity(context.SourceId);
            if (sourceEntity == null)
            {
                _diagnostics.Warn(
                    $"ProjectileHost.Spawn: 发射者 \"{context.SourceId}\" 不存在于世界中，已跳过生成");
                return;
            }

            var travelMode = GetString(context.Params, "travel_mode", "straight");
            var hitBehavior = GetString(context.Params, "hit_behavior", "impact_on_first");
            var speed = GetNumber(context.Params, "speed", _options.DefaultSpeed);
            var maxRange = GetNumber(context.Params, "max_range", _options.DefaultMaxRange);
            var arcHeight = GetNumber(context.Params, "arc_height", _options.DefaultArcHeight);
            var impactRadius = GetNumber(context.Params, "impact_radius", _options.DefaultImpactRadius);
            var displayRef = GetIdOpt(context.Params, "display_ref");
            var onHitEffects = ParseEffectRefs(context.Params, "on_hit_effects", _diagnostics);
            var maxPierceCount = context.Params.TryGetValue("max_pierce_count", out var mpcVal) && mpcVal is JsonNumber mpcNum
                ? (int?)mpcNum.Value
                : null;

            var sourcePos = _units.GetPosition(context.SourceId);
            var hasTarget = !context.TargetId.Equals(context.SourceId) && _units.Exists(context.TargetId);
            Vec2 aimPoint;
            if (hasTarget)
            {
                aimPoint = _units.GetPosition(context.TargetId);
            }
            else
            {
                var facing = _units.GetFacing(context.SourceId);
                aimPoint = sourcePos + new Vec2(Math.Cos(facing), Math.Sin(facing)) * Math.Max(maxRange, 1.0);
            }

            var direction = aimPoint - sourcePos;
            var length = direction.Length;
            var normalized = length > 1e-9 ? direction * (1.0 / length) : new Vec2(1, 0);

            var entityId = _world.AllocateEntityId(EntityKinds.Projectile);
            var entity = new ProjectileEntity(
                entityId, sourceEntity.MapId, context.SourceId, context.SkillId, travelMode, hitBehavior)
            {
                Position = sourcePos,
                Facing = Math.Atan2(normalized.Y, normalized.X),
                Velocity = normalized * speed,
                TemplateId = displayRef,
            };

            _world.AddEntity(entity);

            _states[entityId] = new ProjectileState
            {
                SourceUnitId = context.SourceId,
                TargetUnitId = hasTarget ? context.TargetId : (Id?)null,
                Speed = speed,
                MaxRange = maxRange,
                ArcHeight = arcHeight,
                ImpactRadius = impactRadius,
                HitBehavior = hitBehavior,
                TravelMode = travelMode,
                School = context.School,
                SkillId = context.SkillId,
                Tags = context.Tags,
                OnHitEffects = onHitEffects,
                EffectSink = effectSink,
                MaxPierceCount = maxPierceCount,
            };
        }

        // -----------------------------------------------------------------
        // 逐 tick 推进（见 ProjectileTickHandler）
        // -----------------------------------------------------------------

        /// <summary>按 <paramref name="dt"/> 推进全部存活投射物一步（惯例同
        /// <c>Core.Rules.Combat.CombatHost.Update</c>：按 <see cref="Id"/> 序数遍历，保证确定性）。
        /// <paramref name="dt"/> 非正数时空操作。</summary>
        public void Advance(double dt)
        {
            if (dt <= 0 || _states.Count == 0)
            {
                return;
            }

            var ids = new List<Id>(_states.Keys);
            ids.Sort();

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!_states.TryGetValue(id, out var state))
                {
                    continue; // 本次循环中已被前面的处理销毁（如穿透命中过程中另一发触发的联动，理论上不会发生，防御性检查）。
                }

                if (!(_world.GetEntity(id) is ProjectileEntity entity))
                {
                    _states.Remove(id);
                    continue;
                }

                AdvanceOne(entity, state, dt);
            }
        }

        private void AdvanceOne(ProjectileEntity entity, ProjectileState state, double dt)
        {
            // homing：每 tick 按当前目标位置重新瞄准（目标死亡/消失后保持最后一次已知方向直线飞行，
            // 见判断记录——05 未规定"追踪目标丢失"时的行为，任务拍板按"继续沿最后方向飞"处理，
            // 不是"立即销毁"，理由：追踪导弹目标恰好在命中前一刻死亡是正常游戏场景，不应凭空消失
            // 而应该有个"飞完这一下"的观感）。
            if (state.TravelMode == "homing" && state.TargetUnitId.HasValue &&
                _units.Exists(state.TargetUnitId.Value) && _units.IsAlive(state.TargetUnitId.Value))
            {
                var toTarget = _units.GetPosition(state.TargetUnitId.Value) - entity.Position;
                var len = toTarget.Length;
                if (len > 1e-9)
                {
                    var normalized = toTarget * (1.0 / len);
                    entity.Velocity = normalized * state.Speed;
                    entity.Facing = Math.Atan2(normalized.Y, normalized.X);
                }
            }

            var prevPos = entity.Position;
            var newPos = prevPos + entity.Velocity * dt;

            // 地形遮挡（命中判定的另一半：ISpatialQuery 找单位、INavigation2D.raycast 找地形阻挡，
            // 见 core/carriers/projectile/README.md"命中判定"一节）：先算出本 tick 位移线段是否
            // 被地形挡住、挡在哪一点，但不立即销毁——把单位命中检测的查询线段裁到"挡住点"之前
            // （见下），保证"墙前面站着一个单位"时单位命中判定优先于撞墙（先到先得，同一 tick
            // 内一个单位与一堵墙不可能同时决定结果，需要有个先后顺序，任务书未规定，本类拍板
            // "先查单位再查墙"更符合直觉——玩家更关心"箭有没有射中人"而不是"箭有没有被墙挡住"）。
            Vec2? wallHit = null;
            if (_navigation != null)
            {
                wallHit = _navigation.Raycast(entity.MapId, prevPos, newPos);
            }
            var effectiveNewPos = wallHit ?? newPos;

            var segmentLength = (effectiveNewPos - prevPos).Length;
            state.DistanceTraveled += segmentLength;
            state.ElapsedTime += dt;

            if (state.HitBehavior != "impact_on_expiry" && segmentLength > 1e-9 &&
                TryResolveUnitHits(entity, state, prevPos, effectiveNewPos))
            {
                return; // 命中已在 TryResolveUnitHits 内销毁投射物（impact_on_first / 穿透耗尽）。
            }

            if (wallHit.HasValue)
            {
                entity.Position = wallHit.Value;
                Destroy(entity.EntityId); // 撞墙：无命中后效果。
                return;
            }

            // 走到这里说明本 tick 没有撞墙（wallHit 为空），effectiveNewPos 等于 newPos。
            if (state.DistanceTraveled >= state.MaxRange)
            {
                entity.Position = newPos;
                ResolveExpiry(entity, state);
                return;
            }

            entity.Position = newPos;
            if (entity.Velocity.Length > 1e-9)
            {
                entity.Facing = Math.Atan2(entity.Velocity.Y, entity.Velocity.X);
            }

            if (state.TravelMode == "arc")
            {
                // 抛物线峰值高度曲线（4h·t·(1-t)，t∈[0,1] 在 t=0.5 时取峰值 h，t=0/1 时为
                // 0——纯表现参数，不参与本方法上面的平面距离/命中判定，见 ProjectileEntity.
                // HeightOffset 判断记录）。
                var t = state.MaxRange > 1e-9 ? Math.Min(1.0, state.DistanceTraveled / state.MaxRange) : 1.0;
                entity.HeightOffset = 4.0 * state.ArcHeight * t * (1.0 - t);
            }
        }

        /// <summary>沿本 tick 位移线段（<paramref name="from"/>→<paramref name="to"/>）查询命中的
        /// 单位并按 <see cref="ProjectileState.HitBehavior"/> 处理；返回 true 表示投射物已在本方法
        /// 内被销毁（调用方不应再继续本 tick 剩余逻辑）。</summary>
        private bool TryResolveUnitHits(ProjectileEntity entity, ProjectileState state, Vec2 from, Vec2 to)
        {
            var filter = new QueryFilter(_options.HitQueryTags);
            var candidateIds = _spatial.QueryLine(from, to, filter);
            if (candidateIds.Count == 0)
            {
                return false;
            }

            // 按到起点的距离升序排序，保证"先经过先命中"的确定性顺序（QueryLine 本身不保证顺序）。
            var candidates = new List<(Id Id, double Dist)>();
            for (var i = 0; i < candidateIds.Count; i++)
            {
                var candidateId = candidateIds[i];
                if (candidateId.Equals(state.SourceUnitId)) continue;
                if (state.HitUnitIds.Contains(candidateId)) continue;
                if (!_units.Exists(candidateId) || !_units.IsAlive(candidateId)) continue;

                var dist = (_units.GetPosition(candidateId) - from).Length;
                candidates.Add((candidateId, dist));
            }

            candidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));

            for (var i = 0; i < candidates.Count; i++)
            {
                var targetId = candidates[i].Id;
                state.HitUnitIds.Add(targetId);
                ApplyOnHitEffects(state, targetId);

                if (state.HitBehavior != "pierce")
                {
                    Destroy(entity.EntityId);
                    return true;
                }

                state.PierceCount++;
                if (state.MaxPierceCount.HasValue && state.PierceCount >= state.MaxPierceCount.Value)
                {
                    Destroy(entity.EntityId);
                    return true;
                }
            }

            return false;
        }

        /// <summary>射程耗尽/到期时的处理：<c>impact_on_expiry</c> 按 <see cref="ProjectileState.ImpactRadius"/>
        /// 查询落点周围的单位并逐个回灌命中后效果（06 第 3.2 节"命中后效果列表"）；其余两种命中行为
        /// 到期即视为未命中，直接销毁不产生任何效果（见 README"未命中处理"）。</summary>
        private void ResolveExpiry(ProjectileEntity entity, ProjectileState state)
        {
            if (state.HitBehavior == "impact_on_expiry")
            {
                var filter = new QueryFilter(_options.HitQueryTags);
                var candidates = _spatial.QueryRadius(entity.Position, state.ImpactRadius, filter);
                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidateId = candidates[i];
                    if (candidateId.Equals(state.SourceUnitId)) continue;
                    if (!_units.Exists(candidateId) || !_units.IsAlive(candidateId)) continue;

                    ApplyOnHitEffects(state, candidateId);
                }
            }

            Destroy(entity.EntityId);
        }

        private void ApplyOnHitEffects(ProjectileState state, Id targetId)
        {
            for (var i = 0; i < state.OnHitEffects.Count; i++)
            {
                var effect = state.OnHitEffects[i];
                var school = GetIdOpt(effect.Params, "school") ?? state.School;
                var baseValue = GetNumber(effect.Params, "base_value", 0);
                var coefficient = GetNumber(effect.Params, "coefficient", 0);
                var canMiss = effect.Kind != EffectKind.Heal;

                var context = new EffectContext(
                    state.SourceUnitId, targetId, state.SkillId, effect.Kind, school,
                    baseValue, coefficient, effect.Params, auraInstanceId: null, isPeriodic: false,
                    canCrit: true, canMiss: canMiss, tags: state.Tags);

                state.EffectSink.ApplyEffect(context);
            }
        }

        private void Destroy(Id entityId)
        {
            _states.Remove(entityId);
            _world.MarkForDestruction(entityId);
        }

        // -----------------------------------------------------------------
        // 参数解析帮助方法（惯例同 core/rules/skill/core/ParamsX.cs，但那是 Core.Rules 程序集内部
        // internal 类型，跨程序集不可见，本类自带一份最小子集，只解析本模块实际用到的字段）。
        // -----------------------------------------------------------------

        private static string GetString(JsonObject o, string key, string fallback) =>
            o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : fallback;

        private static double GetNumber(JsonObject o, string key, double fallback) =>
            o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : fallback;

        private static Id? GetIdOpt(JsonObject o, string key)
        {
            if (o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            return null;
        }

        /// <summary><c>on_hit_effects</c> 字段：形状同 <c>skill.def.effects[]</c>（06 第 3.1 节
        /// <c>EffectRef</c>），即 <c>[{kind, params}, ...]</c>。未知 <c>kind</c> 的项记诊断警告并
        /// 跳过（不中断其余项，惯例同 <c>SkillDefCache</c> 对未知取值的处理原则）。</summary>
        private static IReadOnlyList<EffectRef> ParseEffectRefs(JsonObject o, string key, IProjectileDiagnostics diagnostics)
        {
            var list = new List<EffectRef>();
            if (!o.TryGetValue(key, out var v) || !(v is JsonArray arr))
            {
                return list;
            }

            for (var i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonObject entry) || !entry.TryGetValue("kind", out var kindVal) ||
                    !(kindVal is JsonString kindStr))
                {
                    diagnostics.Warn($"ProjectileHost: on_hit_effects[{i}] 缺少合法的 \"kind\" 字段，已跳过");
                    continue;
                }

                if (!EffectKindNames.TryParse(kindStr.Value, out var kind))
                {
                    diagnostics.Warn($"ProjectileHost: on_hit_effects[{i}].kind \"{kindStr.Value}\" 不是已知的 EffectKind，已跳过");
                    continue;
                }

                var effectParams = entry.TryGetValue("params", out var p) && p is JsonObject po
                    ? po
                    : new JsonObjectBuilder().Build();

                list.Add(new EffectRef(kind, effectParams));
            }

            return list;
        }

        /// <summary>单发投射物的运行期簿记（见类型顶部判断记录）。</summary>
        private sealed class ProjectileState
        {
            public Id SourceUnitId;
            public Id? TargetUnitId;
            public double Speed;
            public double MaxRange;
            public double DistanceTraveled;
            public double ElapsedTime;
            public double ArcHeight;
            public double ImpactRadius;
            public string HitBehavior = "impact_on_first";
            public string TravelMode = "straight";
            public Id School;
            public Id SkillId;
            public IReadOnlyList<Id> Tags = Array.Empty<Id>();
            public IReadOnlyList<EffectRef> OnHitEffects = Array.Empty<EffectRef>();
            public IEffectSink EffectSink = null!;
            public readonly HashSet<Id> HitUnitIds = new HashSet<Id>();
            public int? MaxPierceCount;
            public int PierceCount;
        }
    }
}

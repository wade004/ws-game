using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
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

        /// <summary>
        /// ADR-0028：可选阵营矩阵注入，供 <c>relation_policy</c>（<c>hostile_only</c>/
        /// <c>friendly_only</c>）与 <c>pierce_order</c>（<c>hostile_first</c>）取值判定阵营关系
        /// 使用。判断记录（构造后回填而非构造参数）：<see cref="Core.Carriers.Assembly.
        /// CarriersAssembly"/> 装配时 <see cref="ProjectileHost"/>（第 2.5 步）先于
        /// <c>RulesAssembly</c>（第 3 步，<c>Factions</c> 在其内部构造）构造完成——后者的构造又
        /// 需要把本类实例当 <see cref="Core.Rules.Common.IProjectileSpawner"/> 传入，存在"谁先构造"
        /// 的循环依赖，惯例同本仓库 <c>PowerHost powers = null!</c> 一类"先构造不需要对方的一侧，
        /// 再用可写属性回填另一侧引用"的处理手法（见 <c>CarriersAssembly</c> 构造函数第 1 步同款
        /// 判断记录），不新增构造参数（<see cref="ProjectileHost(IWorldSim,IUnitAccess,ISpatialQuery,
        /// INavigation2D?,ProjectileOptions?,IProjectileDiagnostics?)"/> 签名保持不变，ABI 只增不改）。
        /// 缺省 <c>null</c>：任何要求阵营判定的取值在缺省下均退化为不做该项过滤/排序（见
        /// <see cref="PassesRelationPolicy"/>、<see cref="BuildPierceComparer"/> 判断记录），
        /// <c>relation_policy</c> 缺省值 <c>default</c> 本身从不读取本属性，行为与 ADR-0028 之前
        /// 逐字节一致。
        /// </summary>
        public IFactionMatrix? Factions { get; set; }

        /// <summary>当前存活（未销毁）的投射物数量，供测试/诊断查看。ADR-0028 契约明确：调用
        /// <see cref="ClearAll"/> 后立即归零，不需要等待下一次正 dt 的 <see cref="Advance"/>。</summary>
        public int ActiveCount => _states.Count;

        /// <summary>ADR-0028 新增只读查询：当前是否"静默"——无存活投射物、且（本类命中判定为同步
        /// 调用内一次性回灌效果，不维护跨 tick 的待处理命中队列，见 <see cref="TryResolveUnitHits"/>/
        /// <see cref="ResolveExpiry"/> 判断记录）因此没有任何待处理命中。<see cref="ActiveCount"/>
        /// 为 0 时恒为 true，供调用方在 <c>IWorldSim.ClearAll</c>/<see cref="ClearAll"/> 之后判断
        /// "重置是否已经完成"，不必自行猜测本类内部是否还有残留簿记。</summary>
        public bool IsQuiescent => _states.Count == 0;

        /// <summary>
        /// ADR-0028 新增：立即清空本类全部运行期簿记（<see cref="_states"/>），使
        /// <see cref="ActiveCount"/>/<see cref="IsQuiescent"/> 同步反映"无存活投射物"，不等待下一次
        /// 正 dt 的 <see cref="Advance"/>（该方法此前只在 <c>_world.GetEntity(id)</c> 返回 null 时
        /// 防御性移除簿记，见该方法内注释，<c>IWorldSim.ClearAll</c> 只 Enqueue <c>entity.destroyed</c>
        /// 不改变本类自己的字典，二者此前脱节，见消费方反馈第 4 节 P3）。
        /// <para>
        /// 判断记录（不触碰 <see cref="_world"/>）：<c>IWorldSim.ClearAll</c> 已经负责把实体从世界
        /// 集合移除、Enqueue 销毁事件——本方法只清自己的簿记，不重复调用
        /// <see cref="IWorldSim.MarkForDestruction"/>/不发任何事件（投射物本就不发专属事件，见类型
        /// 顶部判断记录 3）。调用顺序与 <c>IWorldSim.ClearAll</c> 无先后要求（先调用哪个都不会遗留
        /// 幽灵伤害——世界侧实体没了、这一侧簿记没了，二者独立生效），但要让 <see cref="ActiveCount"/>
        /// 在 <c>world.ClearAll()</c> 后立即可信，调用方需要显式把两者接在一起调用（无法在本类内部
        /// 自动感知"外部某个 <see cref="IWorldSim"/> 实例被清空了"，本类构造期未持有事件总线，见
        /// <c>core/carriers/projectile/README.md</c> 判断记录 3"不新增专属事件"同一顾虑——不为这一
        /// 个契约缺口新开一条订阅关系；<see cref="Core.Gameplay.Assembly.GameplayAssembly.LeaveMap"/>
        /// 已接线本方法，见该方法判断记录）。
        /// </para>
        /// </summary>
        public void ClearAll()
        {
            _states.Clear();
        }

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
            var relationPolicy = ResolveRelationPolicy(GetString(context.Params, "relation_policy", RelationPolicyDefault));
            var pierceOrder = ResolvePierceOrder(GetString(context.Params, "pierce_order", PierceOrderNearest));

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
                RelationPolicy = relationPolicy,
                PierceOrder = pierceOrder,
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
            var fullStep = entity.Velocity * dt;

            // RC-09 收边补齐：先按"本发剩余射程"截断本 tick 的位移线段，再做墙/单位/终点测试——
            // 原实现直接用整个 dt 算出的 newPos 做单位命中判定（TryResolveUnitHits 用的线段就是
            // prevPos→newPos），到达/超过 MaxRange 只在命中判定、撞墙判定都做完之后才检查（见本
            // 方法下方 "state.DistanceTraveled >= state.MaxRange" 分支）。速度较快或 dt 较大时，
            // 一个 tick 的整步位移可能整段越过射程上限，越界的那一段本不该存在，却仍然可能在里面
            // 查到并命中射程外的单位；`impact_on_expiry` 的爆炸中心（<see cref="ResolveExpiry"/>
            // 用 <c>entity.Position</c> 当查询圆心）也会错误地落在整步终点而不是射程终点（见外部
            // 审计 RC-09）。现在先把 <c>newPos</c> 本身截到"剩余射程"以内，后续墙检测/单位命中
            // 查询/"是否到达 MaxRange"判断、以及 <c>impact_on_expiry</c> 的爆炸中心全部统一基于
            // 这个已经截断的端点，不需要再额外补一次射程校验分支。
            var remainingRange = state.MaxRange - state.DistanceTraveled;
            var stepLength = fullStep.Length;
            Vec2 newPos;
            if (remainingRange <= 0)
            {
                // 防御性兜底：正常情况下 DistanceTraveled 达到 MaxRange 的那次 tick 已经在下方
                // ResolveExpiry 并销毁，不会再有后续 tick 调用到本方法。
                newPos = prevPos;
            }
            else if (stepLength > remainingRange)
            {
                var stepDirection = stepLength > 1e-9 ? fullStep * (1.0 / stepLength) : new Vec2(1, 0);
                newPos = prevPos + stepDirection * remainingRange;
            }
            else
            {
                newPos = prevPos + fullStep;
            }

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
        /// 内被销毁（调用方不应再继续本 tick 剩余逻辑）。
        /// <para>
        /// ADR-0028 裁决优先级（施法者排除 → 目标锁定 → 关系筛选 → 标签筛选 → 穿透计数 → 命中效果
        /// 回灌）：五个必要条件按文档顺序逐条列出供阅读，运行时是逻辑与门（同时满足才算候选），
        /// 不是严格分阶段串行——标签筛选（<see cref="ProjectileOptions.HitQueryTags"/>）复用既有
        /// <see cref="ISpatialQuery.QueryLine"/> 在集合层面先行收窄候选面（性能优化，不改变最终
        /// 结果），施法者排除/目标锁定/关系筛选三步在下方循环内对每个候选逐一核对，见
        /// <see cref="PassesRelationPolicy"/>。</para>
        /// </summary>
        private bool TryResolveUnitHits(ProjectileEntity entity, ProjectileState state, Vec2 from, Vec2 to)
        {
            var filter = new QueryFilter(_options.HitQueryTags);
            var candidateIds = _spatial.QueryLine(from, to, filter);
            if (candidateIds.Count == 0)
            {
                return false;
            }

            // 按到起点的距离升序排序，保证"先经过先命中"的确定性顺序（QueryLine 本身不保证顺序）；
            // ADR-0028 relation_policy=Default 时排序键/候选集合与之前逐字节一致（仍是纯距离升序，
            // PassesRelationPolicy 对 Default 恒为 true，不淘汰任何候选）。
            var candidates = new List<(Id Id, double Dist)>();
            for (var i = 0; i < candidateIds.Count; i++)
            {
                var candidateId = candidateIds[i];
                if (candidateId.Equals(state.SourceUnitId)) continue; // 施法者排除：无条件、不受 RelationPolicy 影响。
                if (state.HitUnitIds.Contains(candidateId)) continue;
                if (!_units.Exists(candidateId) || !_units.IsAlive(candidateId)) continue;
                if (!PassesRelationPolicy(state, candidateId)) continue; // 目标锁定 + 关系筛选。

                var dist = (_units.GetPosition(candidateId) - from).Length;
                candidates.Add((candidateId, dist));
            }

            candidates.Sort(BuildPierceComparer(state));

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
                    if (candidateId.Equals(state.SourceUnitId)) continue; // 施法者排除：同 TryResolveUnitHits。
                    if (!_units.Exists(candidateId) || !_units.IsAlive(candidateId)) continue;
                    if (!PassesRelationPolicy(state, candidateId)) continue; // ADR-0028：目标锁定 + 关系筛选，同一裁决优先级。

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
        // ADR-0028：投射物碰撞的敌友关系策略
        // -----------------------------------------------------------------

        private const string RelationPolicyDefault = "default";
        private const string RelationPolicyHostileOnly = "hostile_only";
        private const string RelationPolicyFriendlyOnly = "friendly_only";
        private const string RelationPolicyLockedTargetOnly = "locked_target_only";

        private const string PierceOrderNearest = "nearest";
        private const string PierceOrderHostileFirst = "hostile_first";

        /// <summary>把 <c>params.relation_policy</c> 原始字符串规范化为本类内部使用的取值：未知
        /// 取值按本类惯例（同 <c>travel_mode</c>/<c>hit_behavior</c>，见 <see cref="AdvanceOne"/>
        /// 对未知飞行方式的处理——不匹配任何已知分支时隐式落到最保守的默认行为）静默按
        /// <see cref="RelationPolicyDefault"/> 处理，不额外记诊断（数据校验交给
        /// <c>core/rules/skill/schema/SkillSchemas.cs</c> 的 <c>Enum</c> 字段声明，运行时不重复
        /// 校验、只做安全兜底）。<c>hostile_only</c>/<c>friendly_only</c> 在
        /// <see cref="Factions"/> 未注入时于生成当下（不是每 tick）记一次诊断警告并规范化为
        /// <see cref="RelationPolicyDefault"/>（ADR-0028"缺省 null 时退化为 Default"）。</summary>
        private string ResolveRelationPolicy(string raw)
        {
            switch (raw)
            {
                case RelationPolicyHostileOnly:
                case RelationPolicyFriendlyOnly:
                    if (Factions == null)
                    {
                        _diagnostics.Warn(
                            $"ProjectileHost.Spawn: relation_policy=\"{raw}\" 需要阵营矩阵，但本实例未注入 " +
                            "IFactionMatrix（见 ProjectileHost.Factions 判断记录），已退化为 \"default\"（不做关系过滤）");
                        return RelationPolicyDefault;
                    }

                    return raw;

                case RelationPolicyLockedTargetOnly:
                case RelationPolicyDefault:
                    return raw;

                default:
                    return RelationPolicyDefault;
            }
        }

        /// <summary>同 <see cref="ResolveRelationPolicy"/> 判断记录：<c>hostile_first</c> 需要阵营
        /// 矩阵判定候选敌友关系用于排序，未注入时退化为 <see cref="PierceOrderNearest"/>（现行按
        /// 距离升序的默认行为）。</summary>
        private string ResolvePierceOrder(string raw)
        {
            if (raw == PierceOrderHostileFirst)
            {
                if (Factions == null)
                {
                    _diagnostics.Warn(
                        "ProjectileHost.Spawn: pierce_order=\"hostile_first\" 需要阵营矩阵，但本实例未注入 " +
                        "IFactionMatrix，已退化为 \"nearest\"（按距离升序）");
                    return PierceOrderNearest;
                }

                return raw;
            }

            return PierceOrderNearest;
        }

        /// <summary>ADR-0028 裁决优先级第 2、3 步（施法者排除已在调用方无条件完成，见调用处注释）：
        /// <list type="bullet">
        /// <item><c>locked_target_only</c>：只有 <see cref="ProjectileState.TargetUnitId"/>
        /// 本身通过（未锁定目标——自由瞄准发射——时恒不通过，穿透/范围判定都不会命中任何候选，
        /// 这是"选中目标"这一策略字面意义的直接推论，不是遗漏）。</item>
        /// <item><c>hostile_only</c>/<c>friendly_only</c>：用 <see cref="Factions"/> 查
        /// <see cref="ProjectileState.SourceUnitId"/> 对候选的阵营反应（惯例同
        /// <c>core/rules/skill/core/SkillHost.PassesRelation</c> 对 <c>UnitFilter.Relation</c> 的
        /// 判定手法，两处独立实现、同一判定口径：以施法者/发射者为参照方）；<see cref="Spawn"/>
        /// 已把 <see cref="Factions"/> 缺失的情形规范化成 <see cref="RelationPolicyDefault"/>，本
        /// 方法内不会再遇到"要求阵营矩阵但矩阵为空"的组合。</item>
        /// <item><c>default</c>：不做任何关系过滤，恒通过（ADR-0028 之前的逐字节兼容行为）。</item>
        /// </list>
        /// </summary>
        private bool PassesRelationPolicy(ProjectileState state, Id candidateId)
        {
            switch (state.RelationPolicy)
            {
                case RelationPolicyLockedTargetOnly:
                    return state.TargetUnitId.HasValue && candidateId.Equals(state.TargetUnitId.Value);

                case RelationPolicyHostileOnly:
                case RelationPolicyFriendlyOnly:
                    // Factions 不为 null：ResolveRelationPolicy 已在 Spawn 阶段兜底，见该方法判断记录。
                    var reaction = Factions!.GetReaction(_units.GetFaction(state.SourceUnitId), _units.GetFaction(candidateId));
                    return state.RelationPolicy == RelationPolicyHostileOnly
                        ? reaction == Reaction.Hostile
                        : reaction == Reaction.Friendly;

                default:
                    return true;
            }
        }

        /// <summary>穿透（<c>pierce</c>）命中多个候选时的处理顺序（ADR-0028"穿透优先级"）：
        /// <see cref="PierceOrderNearest"/>（缺省）按到线段起点距离升序——与本方法引入之前逐字节
        /// 一致；<see cref="PierceOrderHostileFirst"/> 先处理阵营反应为 <see cref="Reaction.Hostile"/>
        /// 的候选（同为 Hostile 或同为非 Hostile 的候选之间仍按距离升序），供"箭矢先扎穿路径上的
        /// 敌人、最后才可能波及友军"一类内容需求使用——独立于 <c>relation_policy</c> 是否也要求
        /// 阵营过滤，<see cref="ResolvePierceOrder"/> 已保证走到本方法 <c>hostile_first</c> 分支时
        /// <see cref="Factions"/> 非空（Spawn 阶段兜底，同 <see cref="PassesRelationPolicy"/>）。</summary>
        private Comparison<(Id Id, double Dist)> BuildPierceComparer(ProjectileState state)
        {
            if (state.PierceOrder != PierceOrderHostileFirst)
            {
                return (a, b) => a.Dist.CompareTo(b.Dist);
            }

            var sourceFaction = _units.GetFaction(state.SourceUnitId);
            bool IsHostile(Id candidateId) => Factions!.GetReaction(sourceFaction, _units.GetFaction(candidateId)) == Reaction.Hostile;

            return (a, b) =>
            {
                var aHostile = IsHostile(a.Id);
                var bHostile = IsHostile(b.Id);
                if (aHostile != bHostile)
                {
                    return aHostile ? -1 : 1; // Hostile 候选排在非 Hostile 候选之前。
                }

                return a.Dist.CompareTo(b.Dist);
            };
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

            /// <summary>ADR-0028：已规范化的关系策略取值（<c>default</c>/<c>hostile_only</c>/
            /// <c>friendly_only</c>/<c>locked_target_only</c> 之一，见 <see cref="ResolveRelationPolicy"/>）。</summary>
            public string RelationPolicy = RelationPolicyDefault;

            /// <summary>ADR-0028：已规范化的穿透命中顺序（<c>nearest</c>/<c>hostile_first</c> 之一，
            /// 见 <see cref="ResolvePierceOrder"/>）。</summary>
            public string PierceOrder = PierceOrderNearest;
        }
    }
}

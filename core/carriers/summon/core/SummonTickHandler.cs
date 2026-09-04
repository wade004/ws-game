using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Summon
{
    /// <summary>
    /// 挂在 <see cref="TickPhase.AiDecision"/> 的召唤物逻辑（见 07 第 4 节"召唤与宠物"跟随/联动/
    /// 生命周期）：每 tick 依次处理到期销毁、拥有者丢失销毁、进出战斗联动、跟随移动四件事。
    /// <para>
    /// 判断记录（挂载阶段）：任务书拍板注册在 <see cref="TickPhase.AiDecision"/> 阶段，且组装时
    /// 需要先于 <c>core/rules/ai</c> 的 <c>AiTickHandler</c> 注册（同阶段内多个处理器按注册顺序
    /// 依次执行，见 <see cref="ITickPhaseHandler"/> 注释），使跟随意图相对生物自身的
    /// AI 决策意图优先——本类自身只负责"注册在这一阶段"，先后顺序由组装层（不在本任务范围）
    /// 的 <c>RegisterPhaseHandler</c> 调用顺序保证。跟随产生的 <c>move</c> 意图通过
    /// <see cref="IWorldSim.AppendCurrentIntent"/>（而非 <see cref="IWorldSim.SubmitIntent"/>）
    /// 追加进本 tick 的 <see cref="IWorldSim.CurrentIntents"/>，使随后 <see cref="TickPhase.MovementAndNavigation"/>
    /// 阶段的 <c>MovementTickHandler</c> 能在同一 tick 内消费到，不必等到下一 tick（见
    /// <see cref="IWorldSim.AppendCurrentIntent"/> 注释）。
    /// </para>
    /// </summary>
    public sealed class SummonTickHandler : ITickPhaseHandler
    {
        private readonly SummonHost _summonHost;
        private readonly IUnitAccess _units;
        private readonly ICombatHost _combat;
        private readonly SummonOptions _options;
        private readonly IExprDiagnostics _diagnostics;

        public SummonTickHandler(
            SummonHost summonHost,
            IUnitAccess units,
            ICombatHost combatHost,
            SummonOptions? options = null,
            IExprDiagnostics? diagnostics = null)
        {
            _summonHost = summonHost ?? throw new ArgumentNullException(nameof(summonHost));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _combat = combatHost ?? throw new ArgumentNullException(nameof(combatHost));
            _options = options ?? new SummonOptions();
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (step.Kind != SimStepKind.Continuous)
            {
                _diagnostics.Warn(
                    "SummonTickHandler 收到 Discrete 步，本项目未启用离散时间模型（见 ADR-0013），" +
                    "本次 tick 不推进召唤物逻辑");
                return;
            }

            var dt = step.Dt;

            // 快照本 tick 开始时的召唤物集合：循环体内的 Dismiss 会修改 SummonHost 内部集合，
            // 边遍历边修改容易漏处理/多处理，先拷贝一份保证本 tick 处理的对象集合是确定的。
            var ids = new List<Id>(_summonHost.ActiveSummonIds);

            for (var i = 0; i < ids.Count; i++)
            {
                ProcessOne(world, ids[i], dt);
            }
        }

        private void ProcessOne(IWorldSim world, Id summonId, double dt)
        {
            var ownerId = _summonHost.GetOwner(summonId);
            if (!ownerId.HasValue)
            {
                // 本 tick 更早处理的另一条记录不可能影响这里（每个 summonId 只出现一次），
                // 防御性跳过：召唤物已经不在登记表中。
                return;
            }

            if (_summonHost.AdvanceAndCheckExpired(summonId, dt))
            {
                _summonHost.Dismiss(summonId, "expired");
                return;
            }

            if (!_units.Exists(ownerId.Value) || !_units.IsAlive(ownerId.Value))
            {
                _summonHost.Dismiss(summonId, "owner_lost");
                return;
            }

            if (!_units.Exists(summonId) || !_units.IsAlive(summonId))
            {
                // 召唤物自身已死亡但尚未被销毁：死亡结算/复活策略是 combat 模块职责，
                // 本处理器不重复处理，也不在此时机跟随移动。
                return;
            }

            if (_options.SyncCombatState && _combat.IsInCombat(ownerId.Value) && !_combat.IsInCombat(summonId))
            {
                _combat.NotifyCombatEvent(summonId);
            }

            TryFollow(world, summonId, ownerId.Value);
        }

        /// <summary>07 第 4 节"owner 移动超出跟随距离时优先转入向 owner 靠拢的移动"：目标点选在
        /// 沿"召唤物 → owner"连线、距 owner <see cref="SummonOptions.FollowStopDistance"/> 处（不是
        /// owner 的精确坐标），避免召唤物与 owner 位置完全重合。</summary>
        private void TryFollow(IWorldSim world, Id summonId, Id ownerId)
        {
            var inCombat = _combat.IsInCombat(summonId);
            var shouldConsiderFollow = !inCombat || !_options.JoinCombat;
            if (!shouldConsiderFollow)
            {
                return;
            }

            var summonPos = _units.GetPosition(summonId);
            var ownerPos = _units.GetPosition(ownerId);
            var toOwner = ownerPos - summonPos;
            var distance = toOwner.Length;

            if (distance <= _options.FollowDistance)
            {
                return;
            }

            var stopAt = _options.FollowStopDistance < distance ? _options.FollowStopDistance : distance;
            var direction = distance > double.Epsilon
                ? new Vec2(toOwner.X / distance, toOwner.Y / distance)
                : Vec2.Zero;
            var targetPoint = ownerPos - direction * stopAt;

            var args = new JsonObjectBuilder()
                .Add("x", new JsonNumber(targetPoint.X))
                .Add("y", new JsonNumber(targetPoint.Y))
                .Add("mode", new JsonString(MoveMode.Run.ToString()))
                .Build();

            world.AppendCurrentIntent(new Intent(summonId, "move", args));
        }
    }
}

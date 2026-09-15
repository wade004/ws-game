using Core.Foundation.Common;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-4（ADR-0035 决策 3；落地计划 T-N6-4 行"简化移动模型"）：仿真运行器自己驱动的玩家移动——
    /// 每 tick 若玩家与目标距离超过技能可用射程，按固定速度朝目标直线移动一步；距离已在射程内则不动。
    /// <para>
    /// 判断记录（为什么只有"玩家侧"移动模型，生物侧不重复实现）：<c>core/rules/ai</c> 的
    /// <c>AiHost.HandleChase</c>（<c>BehaviorState.Chase</c>）已经实现了"朝最近敌对目标移动直至进入
    /// <c>AiOptions.AttackRange</c>"这一整套追击逻辑，经 <c>AiTickHandler</c>（<c>TickPhase.AiDecision</c>）
    /// 随 <c>SimClockHost.Advance</c>/<c>WorldSim.Tick</c> 自动为全部已注册 AI 单位驱动——生物在
    /// <c>CreatureFactory.SpawnCore</c> 生成时若模板登记了 <c>ai_behavior_ref</c> 即自动注册（见该
    /// 类型 <c>SpawnCore</c> 判断记录），战斗仿真运行器（<see cref="FightRunner"/>）不需要、也不应该
    /// 重新实现一遍同样的追击行为（"禁止修改被仿真模块的行为"这条硬性规则的另一面：既有能力够用时
    /// 直接复用，不重复造轮子）。本类型只覆盖"标准玩家"这一侧——玩家不经 <c>AiHost</c> 驱动（智能
    /// 释放优先级表由 <c>Core.Rules.Ai.RotationEvaluator</c> 独立求值，不依赖 <c>ai.behavior_profile</c>/
    /// <c>BehaviorState</c>，见该类型判断记录"无状态保证"），因此优先级表选不出可施放技能时（多半是
    /// 因为超出全部候选技能的射程）需要一个同等的移动兜底，本类型补上这一块。
    /// </para>
    /// <para>
    /// 判断记录（无状态纯函数，不持有任何单位状态）：与 <see cref="Core.Rules.Ai.RotationEvaluator"/>
    /// 同一惯例——本类型不缓存、不注册任何单位，<see cref="Step"/> 每次调用只读入参与只写
    /// <c>IUnitAccess</c>/<c>ISpatialQuery</c> 两个外部宿主，调用方（<see cref="FightRunner"/>）逐 tick
    /// 调用即可，不需要构造实例持有状态（本类型全部方法为 <c>static</c>）。
    /// </para>
    /// </summary>
    public static class SimpleMoveModel
    {
        /// <summary>默认移速：场景未显式提供单位移速属性时使用（单位/秒）。任务书"默认取单位移速属性
        /// 或 1 单位/秒"——本数据集/框架当前 <c>stat.definition</c> 没有登记任何"移动速度"属性（见
        /// <c>core/sim/tests/data/stat/stat.definition.json</c> 全表 9 项，无一项类目/命名与移速相关），
        /// <see cref="Step"/> 因此总是退化到 <paramref name="moveSpeed"/> 显式传入值或本常量——调用方
        /// 如果未来接入了移速属性，应在调用 <see cref="Step"/> 之前自行查询该属性并作为
        /// <paramref name="moveSpeed"/> 传入，本类型不反过来猜测属性 id。</summary>
        public const double DefaultMoveSpeed = 1.0;

        /// <summary>
        /// 推进 <paramref name="unitId"/> 朝 <paramref name="targetPosition"/> 移动最多一步（本次 tick
        /// 允许的最大位移 = <paramref name="moveSpeed"/> × <paramref name="dtSeconds"/>）。若当前距离
        /// 已 ≤ <paramref name="engageRange"/>，不移动，返回 <c>false</c>（"已在射程内，不需要移动"）；
        /// 否则朝目标方向移动一步（若一步的位移超过剩余距离，移动到目标位置——不会移动过头），同步写入
        /// <see cref="Core.Rules.Common.IUnitAccess.SetPosition"/> 与 <paramref name="spatial"/>
        /// 的位置索引（同 <c>CreatureFactory.SpawnCore</c>"冗余但幂等地再写一次位置"一贯惯例，
        /// 保证目标链/命中判定读到最新位置），返回 <c>true</c>。
        /// </summary>
        public static bool Step(
            Core.Rules.Common.IUnitAccess units,
            Adapters.Stub.StubSpatialQuery spatial,
            Id unitId,
            Vec2 targetPosition,
            double engageRange,
            double dtSeconds,
            double moveSpeed = DefaultMoveSpeed)
        {
            var current = units.GetPosition(unitId);
            var toTarget = targetPosition - current;
            var distance = toTarget.Length;

            if (distance <= engageRange)
            {
                return false;
            }

            var step = moveSpeed * dtSeconds;
            if (step <= 0)
            {
                return false;
            }

            Vec2 next;
            if (step >= distance)
            {
                next = targetPosition;
            }
            else
            {
                var direction = new Vec2(toTarget.X / distance, toTarget.Y / distance);
                next = current + direction * step;
            }

            units.SetPosition(unitId, next);
            spatial.UpdatePosition(unitId, next);
            return true;
        }
    }
}

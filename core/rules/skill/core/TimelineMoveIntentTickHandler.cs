using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 把"移动输入到来"接给动作时间线（手感设计/01 第 3.4 节 <c>move</c> 类取消窗口）：挂在 <see cref="TickPhase.SkillPipeline"/>，
    /// 紧随 <see cref="SkillTickHandler"/> 之后，扫描本 tick 的 <c>move</c>/<c>move_to_unit</c> 意图并调用
    /// <see cref="SkillHost.NotifyMoveIntent"/>。时间线动作进行中且 <c>move</c> 类取消窗口此刻打开才会取消，其余情形是空操作。
    /// <para>
    /// 判断记录（什么算"移动输入"）：<c>move</c> 意图带目标点（<c>x</c>/<c>y</c>）或非零方向（<c>dx</c>/<c>dy</c> 不全为 0）、
    /// 以及 <c>move_to_unit</c> 都算；零方向的 <c>move</c> 是"松开摇杆"，不是移动输入；<c>move_stop</c> 不算。
    /// </para>
    /// <para>
    /// 判断记录（离散步不生效）：离散（回合制）步下没有取消窗口语义（时间线本身只在连续步推进），一律跳过。
    /// </para>
    /// 没有时间线动作时 <see cref="SkillHost.NotifyMoveIntent"/> 立刻返回，既有行为逐位不变。
    /// </summary>
    public sealed class TimelineMoveIntentTickHandler : ITickPhaseHandler
    {
        private readonly SkillHost _skill;

        public TimelineMoveIntentTickHandler(SkillHost skill)
        {
            _skill = skill ?? throw new System.ArgumentNullException(nameof(skill));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind != SimStepKind.Continuous)
            {
                return;
            }

            foreach (var intent in world.CurrentIntents)
            {
                if (intent.Kind == "move_to_unit" || (intent.Kind == "move" && IsMovingInput(intent.Args)))
                {
                    _skill.NotifyMoveIntent(intent.ActorId);
                }
            }
        }

        private static bool IsMovingInput(JsonObject args)
        {
            if (args.TryGetValue("x", out var x) && x is JsonNumber && args.TryGetValue("y", out var y) && y is JsonNumber)
            {
                return true;
            }

            var dx = args.TryGetValue("dx", out var dxv) && dxv is JsonNumber dxn ? dxn.Value : 0.0;
            var dy = args.TryGetValue("dy", out var dyv) && dyv is JsonNumber dyn ? dyn.Value : 0.0;
            return dx != 0.0 || dy != 0.0;
        }
    }
}

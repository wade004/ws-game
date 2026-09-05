using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 把 <see cref="SkillHost"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.SkillPipeline"/>（见 03 第 4.2 节步骤 3"技能管线"）。消费
    /// <see cref="IWorldSim.CurrentIntents"/> 中 <see cref="Intent.Kind"/> 为 <c>"cast"</c> 的意图
    /// （<c>Args</c> 形状：<c>{skill_id: Id, targets: [Id], point: {x,y}?}</c>，<c>targets</c>
    /// 缺省为空数组——由 <see cref="ISkillHost.CastSkill"/>/<c>CastPipeline</c> 步骤 6 落到
    /// <c>ITargetHost.Resolve</c>；<c>point</c> 当前未被 <c>CastSkill</c> 使用，落点类技能改经
    /// <c>SkillCastRequest.TargetPoint</c> 由更上层 AI/玩家辅助施法适配层处理，见契约缺口清单），
    /// 随后按步所处模式推进：连续步调用 <see cref="SkillHost.Update"/> 推进读条/引导/冷却/光环/Proc
    /// 内部冷却（行为不变）；离散步只推进当前行动者自己的读条/引导（见
    /// <see cref="SkillHost.AdvanceCastForActor"/> 判断记录——"一步 = 该行动者的一回合"，不能对
    /// 全场统一推进）,冷却/充能/光环/Proc 改由构造期订阅的 <c>sim.round_ended</c> 驱动（见
    /// <paramref name="bus"/> 参数判断记录），惯例与 <c>core/rules/combat.CombatTickHandler</c>
    /// 完全一致。
    /// </summary>
    public sealed class SkillTickHandler : ITickPhaseHandler
    {
        private readonly SkillHost _host;
        private readonly ISkillDiagnostics _diagnostics;

        /// <summary>
        /// H4 补齐：<paramref name="bus"/> 非空时订阅 <c>sim.round_ended</c>，每轮结束调用一次
        /// <see cref="SkillHost.AdvanceRoundTimers"/>(1.0)（"1 轮"）——与
        /// <c>core/rules/combat.CombatTickHandler</c> 构造函数的既有惯例一致（见该类型注释：
        /// <c>bus?.Subscribe&lt;SimRoundEndedEvent&gt;(SimEventKeys.RoundEnded, _ =&gt;
        /// _host.Update(1.0))</c>）。<paramref name="bus"/> 为空（未装配离散模式的调用方，如既有
        /// 直接构造本类型的单元测试）时不订阅，行为与此前完全一致。
        /// </summary>
        public SkillTickHandler(SkillHost host, ISkillDiagnostics? diagnostics = null, IEventBus? bus = null)
        {
            _host = host;
            _diagnostics = diagnostics ?? new InMemorySkillDiagnostics();

            bus?.Subscribe<SimRoundEndedEvent>(SimEventKeys.RoundEnded, _ => _host.AdvanceRoundTimers(1.0));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            foreach (var intent in world.CurrentIntents)
            {
                if (intent.Kind != "cast")
                {
                    continue;
                }

                var skillId = intent.Args.TryGetValue("skill_id", out var skillIdVal) && skillIdVal is JsonString skillIdStr
                    ? new Id(skillIdStr.Value)
                    : (Id?)null;

                if (skillId == null)
                {
                    _diagnostics.Warn($"cast 意图缺少 skill_id 参数（actorId=\"{intent.ActorId}\"），已忽略");
                    continue;
                }

                var targets = new List<Id>();
                if (intent.Args.TryGetValue("targets", out var targetsVal) && targetsVal is JsonArray targetsArr)
                {
                    for (var i = 0; i < targetsArr.Count; i++)
                    {
                        if (targetsArr[i] is JsonString targetStr)
                        {
                            targets.Add(new Id(targetStr.Value));
                        }
                    }
                }

                _host.CastSkill(intent.ActorId, skillId.Value, targets);
            }

            if (step.Kind == SimStepKind.Continuous)
            {
                _host.Update(step.Dt);
                return;
            }

            // H4 补齐：离散步只推进当前行动者自己的读条/引导（见类型注释、
            // SkillHost.AdvanceCastForActor 判断记录），"一步 = 该行动者的一回合"；冷却/充能/
            // 光环/Proc 由构造期订阅的 sim.round_ended 统一推进（见构造函数判断记录），不在这里
            // 重复处理。step.ActorId 恒非空（SimStep.Discrete 工厂方法保证，见该类型）。
            if (step.ActorId.HasValue)
            {
                _host.AdvanceCastForActor(step.ActorId.Value, 1.0);
            }
        }
    }
}

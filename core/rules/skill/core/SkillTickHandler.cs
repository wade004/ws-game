using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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
    /// 随后调用 <see cref="SkillHost.Update"/> 推进读条/引导/冷却/光环/Proc 内部冷却。
    /// </summary>
    public sealed class SkillTickHandler : ITickPhaseHandler
    {
        private readonly SkillHost _host;
        private readonly ISkillDiagnostics _diagnostics;

        public SkillTickHandler(SkillHost host, ISkillDiagnostics? diagnostics = null)
        {
            _host = host;
            _diagnostics = diagnostics ?? new InMemorySkillDiagnostics();
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

            _diagnostics.Warn(
                "SkillTickHandler 收到 Discrete 步，本项目未启用离散时间模型（见 ADR-0013、" +
                "落地方案与分阶段计划.md T1-5 禁止事项），本次 tick 不推进读条/冷却/光环");
        }
    }
}

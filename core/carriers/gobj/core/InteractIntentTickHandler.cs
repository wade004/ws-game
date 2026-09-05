using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// 把 <see cref="GameObjectHost"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.TriggerEvaluation"/>（见 03_运行时骨架.md 第 4.2 节步骤 6"触发评估"：
    /// "交互意图（kind = "interact"，args 含目标物件实例 id）在本步由载体层的物件宿主
    /// （GameObjectHost）消费"）。消费 <see cref="IWorldSim.CurrentIntents"/> 中
    /// <see cref="Intent.Kind"/> 为 <c>"interact"</c> 的意图（<c>Args</c> 形状：
    /// <c>{gobj_instance_id: Id}</c>，键名沿用 <c>Core.Rules.Skill.SkillTickHandler</c> 消费
    /// <c>"cast"</c> 意图时 <c>skill_id</c>/<c>targets</c> 的 snake_case 惯例，见该类型顶部注释），
    /// 调用 <see cref="GameObjectHost.Interact"/>（<see cref="Intent.ActorId"/> 即触发交互的
    /// <c>unitId</c>）——物件状态变化与 <c>gobj.interacted</c> 事件均由 <see cref="GameObjectHost.Interact"/>
    /// 自身负责，本处理器只负责"从意图队列取出参数、调用一次"这一件事，交互失败
    /// （<see cref="InteractResult.Success"/> 为 false）不抛异常，只记一条诊断。
    /// <para>
    /// 判断记录（此前 tick 八步表实现未跟上文档的情形，见 ADR-0016 背景一节末段）：03 第 4.2 节
    /// 步骤 1"输入意图收集"早已把交互意图列为意图种类之一，步骤 6"触发评估"也早已写明由
    /// <c>GameObjectHost</c> 消费，但阶段 4 之前从未有任何 <see cref="ITickPhaseHandler"/> 真正把
    /// 提交的 <c>interact</c> 意图接到 <see cref="GameObjectHost.Interact"/>——调用方（Unity 引擎侧
    /// 的 <c>GameFoundationBootstrap</c>/<c>FrameworkResidentHost</c>）此前只能绕开意图系统、直接
    /// 持有 <see cref="GameObjectHost"/> 引用窄契约调用 <c>Interact</c>。本类型补齐这条链路后，
    /// 引擎侧改为统一提交 <c>interact</c> 意图（见 adapters/unity 对应改动），不再需要那个窄契约。
    /// </para>
    /// </summary>
    public sealed class InteractIntentTickHandler : ITickPhaseHandler
    {
        private readonly GameObjectHost _host;
        private readonly IGobjDiagnostics _diagnostics;

        public InteractIntentTickHandler(GameObjectHost host, IGobjDiagnostics? diagnostics = null)
        {
            _host = host;
            _diagnostics = diagnostics ?? new InMemoryGobjDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            foreach (var intent in world.CurrentIntents)
            {
                if (intent.Kind != "interact")
                {
                    continue;
                }

                var gobjInstanceId = intent.Args.TryGetValue("gobj_instance_id", out var gobjIdVal) && gobjIdVal is JsonString gobjIdStr
                    ? new Id(gobjIdStr.Value)
                    : (Id?)null;

                if (gobjInstanceId == null)
                {
                    _diagnostics.Warn($"interact 意图缺少 gobj_instance_id 参数（actorId=\"{intent.ActorId}\"），已忽略");
                    continue;
                }

                var result = _host.Interact(intent.ActorId, gobjInstanceId.Value);
                if (!result.Success)
                {
                    _diagnostics.Warn(
                        $"interact 意图未成功（actorId=\"{intent.ActorId}\"，gobjInstanceId=\"{gobjInstanceId.Value}\"）：{result.Outcome}");
                }
            }
        }
    }
}

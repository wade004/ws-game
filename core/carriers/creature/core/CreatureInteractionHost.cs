using System;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// ADR-0051：<see cref="ICreatureInteractionHost"/> 的默认实现——消费方反馈第 2 条根治，让生物
    /// 可以作为交互目标被直接 <c>interact</c>，不必先包装成 <c>gobj.template</c>（见
    /// <c>architecture/adr/0051-生物原生交互路径.md</c>）。
    /// <para>
    /// 判断记录（不静默降级，见任务硬性规则"运行时路径不许静默降级"）：
    /// </para>
    /// <list type="bullet">
    /// <item><description><paramref name="creatureInstanceId"/> 未登记为生物实例，或
    /// <paramref name="unitId"/> 不存在：记一条诊断，返回 <see cref="InteractOutcome.Unknown"/>
    /// （<c>Success=false</c>）——"目标 id 未登记"必须留下可见线索，不能只靠调用方自己翻
    /// <c>InteractResult.Success</c> 才发现。</description></item>
    /// <item><description>超出 <see cref="CreatureInteractOptions.InteractRange"/>：不记诊断，直接
    /// 返回 <see cref="InteractOutcome.Unknown"/>——这是正常游玩中随时会发生的瞬时状态（玩家还在
    /// 走近途中），惯例同 <c>Core.Carriers.Gobj.GameObjectHost.Interact</c> 对同一情形的既有处理，
    /// 逐帧重复交互尝试不应该刷诊断。</description></item>
    /// <item><description>ADR-0067：目标生物已死亡（<see cref="IUnitAccess.Exists"/>×
    /// <see cref="IUnitAccess.IsAlive"/> 合取为假，与 ADR-0061/0065 同一权威口径，不看生命值资源池
    /// 是否 <c>&lt;= 0</c>）：不记诊断，返回 <see cref="InteractOutcome.TargetDead"/>
    /// （<c>Success=false</c>）——玩家点中尸体是正常游玩操作（击杀后按交互键、鼠标点选发起交互都
    /// 会命中尸体），是与"距离过远"同类的正常临时状态，不是内容错误，不应该刷诊断。</description></item>
    /// <item><description>ADR-0067：交互发起者已死亡（同一存活口径）：不记诊断，返回
    /// <see cref="InteractOutcome.ActorDead"/>（<c>Success=false</c>）——死亡单位不能发起交互（不能
    /// 和 NPC 对话），同样按正常临时状态处理。</description></item>
    /// <item><description>生物模板未配置 <see cref="CreatureTemplate.GossipMenuRef"/>："生物没有
    /// 可交互内容"，记一条诊断，返回 <see cref="InteractOutcome.NoAction"/>（<c>Success=true</c>，
    /// 惯例同 <c>GameObjectHost</c> 对 <c>SaveRequester</c>/<c>QuestActionDispatcher</c> 未注入时的
    /// 既有处理——诊断已经不是"看起来正常却无声"，只是不阻断调用方的既有集成方式）。</description></item>
    /// <item><description>已配置 <see cref="CreatureTemplate.GossipMenuRef"/> 但未注入
    /// <see cref="CreatureInteractOptions.GossipOpener"/>："没登记对话"，记一条诊断，返回
    /// <see cref="InteractOutcome.NoAction"/>（<c>Success=false</c>，与
    /// <c>GameObjectHost.DispatchOnUse</c> 对 <c>DialogOpener</c>/<c>DialogOpenerWithSource</c> 都未
    /// 注入时的既有处理一致）。</description></item>
    /// </list>
    /// </summary>
    public sealed class CreatureInteractionHost : ICreatureInteractionHost
    {
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;
        private readonly ICreatureTemplateQuery _templates;
        private readonly CreatureInteractOptions _options;
        private readonly ICreatureDiagnostics _diagnostics;

        /// <summary>诊断出口只读暴露（惯例同 <c>Core.Carriers.Gobj.GameObjectHost.Diagnostics</c>，
        /// ADR-0042）：供 adapters/unity 侧统一诊断转发机制轮询本实例累积的 Warnings/Errors。</summary>
        public ICreatureDiagnostics Diagnostics => _diagnostics;

        public CreatureInteractionHost(
            IWorldSim world,
            IUnitAccess units,
            ICreatureTemplateQuery templates,
            CreatureInteractOptions? options = null,
            ICreatureDiagnostics? diagnostics = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _options = options ?? new CreatureInteractOptions();
            _diagnostics = diagnostics ?? new InMemoryCreatureDiagnostics();
        }

        public InteractResult Interact(Id unitId, Id creatureInstanceId)
        {
            if (!(_world.GetEntity(creatureInstanceId) is CreatureUnit creature))
            {
                _diagnostics.Warn($"interact 目标 \"{creatureInstanceId}\" 未登记为生物实例，已忽略");
                return new InteractResult(false, InteractOutcome.Unknown);
            }

            if (!_units.Exists(unitId))
            {
                _diagnostics.Warn($"interact 发起者 \"{unitId}\" 不存在，已忽略（目标生物实例 \"{creatureInstanceId}\"）");
                return new InteractResult(false, InteractOutcome.Unknown);
            }

            if (!(_units.Exists(creatureInstanceId) && _units.IsAlive(creatureInstanceId)))
            {
                return new InteractResult(false, InteractOutcome.TargetDead);
            }

            if (!_units.IsAlive(unitId))
            {
                return new InteractResult(false, InteractOutcome.ActorDead);
            }

            var distance = Vec2.Distance(_units.GetPosition(unitId), creature.Position);
            if (distance > _options.InteractRange)
            {
                return new InteractResult(false, InteractOutcome.Unknown);
            }

            var template = _templates.Get(creature.TemplateId!.Value);

            if (!template.GossipMenuRef.HasValue)
            {
                _diagnostics.Warn(
                    $"生物 \"{creatureInstanceId}\"（模板 \"{template.Id}\"）没有配置 gossip_menu_ref，interact 无可交互内容");
                return new InteractResult(true, InteractOutcome.NoAction);
            }

            if (_options.GossipOpener == null)
            {
                _diagnostics.Warn(
                    $"生物 \"{creatureInstanceId}\" 的 gossip_menu_ref 已配置但未注入 CreatureInteractOptions.GossipOpener");
                return new InteractResult(false, InteractOutcome.NoAction);
            }

            _options.GossipOpener(unitId, creatureInstanceId, template.GossipMenuRef.Value);
            return new InteractResult(true, InteractOutcome.Dialog, template.GossipMenuRef.Value);
        }
    }
}

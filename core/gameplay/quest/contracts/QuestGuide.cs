using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 任务目标指引（ADR-0173；08 第 2.3 节"当前激活目标的位置查询接口"的落地）：一条 <c>quest.def.guide[].targets[]</c>
    /// 的强类型表示——"指向哪里"。三种写法恰好选一种（<see cref="QuestContentValidationRule"/> 的
    /// <c>quest_guide_target_form</c> 检查）：<see cref="AreaRef"/>（<c>area.trigger_def</c>，取形状中心）、
    /// <see cref="SpawnRef"/>（<c>spawn.table</c>，取刷新点位置）、<see cref="MapId"/> + <see cref="Position"/>（字面坐标）。
    /// <see cref="VisibleIf"/> 为空视为恒可见；求值为假的目标本次不参与候选（例如"未清理的房间"）。
    /// </summary>
    public sealed class QuestGuideTargetDef
    {
        public Id? AreaRef { get; }

        public Id? SpawnRef { get; }

        public Id? MapId { get; }

        public Vec2? Position { get; }

        public ExprNode? VisibleIf { get; }

        public QuestGuideTargetDef(Id? areaRef, Id? spawnRef, Id? mapId, Vec2? position, ExprNode? visibleIf)
        {
            var forms = (areaRef.HasValue ? 1 : 0) + (spawnRef.HasValue ? 1 : 0) + (mapId.HasValue || position.HasValue ? 1 : 0);
            if (forms != 1)
            {
                throw new ArgumentException("引导目标必须恰好指定 area_ref、spawn_ref、map_id+position 三种写法之一");
            }

            if (mapId.HasValue != position.HasValue)
            {
                throw new ArgumentException("字面坐标写法需要同时给出 map_id 与 position");
            }

            AreaRef = areaRef;
            SpawnRef = spawnRef;
            MapId = mapId;
            Position = position;
            VisibleIf = visibleIf;
        }
    }

    /// <summary>
    /// 一条 <c>quest.def.guide[]</c>（ADR-0173）：任务处于某阶段时，玩家"下一步该做什么、去哪里"。
    /// 同一任务的步骤按数组顺序求值，取第一条"<see cref="MapId"/>（若声明）等于玩家当前地图、<see cref="When"/> 成立且至少有一个可达目标"的步骤
    /// （没有目标的步骤只显示文案，不要求可达）。<see cref="When"/> 是 Expr（<c>quest.is_active(...)</c>、
    /// <c>world.has(...)</c> 等），为空视为恒真；<see cref="TextKey"/> 是文案键，文案里可含占位符
    /// <c>{done}</c>/<c>{total}</c>（见 <see cref="QuestGuideInfo.FormatText"/>）；
    /// <see cref="ProgressFlags"/> 是一组世界标志，已成立的个数/总数即进度（例如"已清理 2/4 个房间"）。
    /// </summary>
    public sealed class QuestGuideStep
    {
        public ExprNode? When { get; }

        public Id TextKey { get; }

        public IReadOnlyList<QuestGuideTargetDef> Targets { get; }

        public IReadOnlyList<Id> ProgressFlags { get; }

        /// <summary>只在玩家当前处于该地图时适用（<c>quest.def.guide[].map_id</c>）；为空表示不限地图。
        /// 用来写"在小镇：走进拱门进入地牢""在一层：清理房间""在二层：击败首领"这类随玩家所在地图变化的文案。</summary>
        public Id? MapId { get; }

        public QuestGuideStep(ExprNode? when, Id textKey, IReadOnlyList<QuestGuideTargetDef>? targets, IReadOnlyList<Id>? progressFlags, Id? mapId = null)
        {
            MapId = mapId;
            When = when;
            TextKey = textKey;
            Targets = targets ?? Array.Empty<QuestGuideTargetDef>();
            ProgressFlags = progressFlags ?? Array.Empty<Id>();
        }
    }

    /// <summary>引导目标解析出的"落点"：所在地图与世界坐标，以及来源数据行 id（字面坐标时为空）。</summary>
    public readonly struct QuestGuideAnchor
    {
        public Id MapId { get; }

        public Vec2 Position { get; }

        public Id? SourceId { get; }

        public QuestGuideAnchor(Id mapId, Vec2 position, Id? sourceId)
        {
            MapId = mapId;
            Position = position;
            SourceId = sourceId;
        }
    }

    /// <summary>一条地图切换通道（<c>area.trigger_def</c> 里 <c>map_transition</c> 类型）：从 <see cref="FromMapId"/> 走进
    /// <see cref="Center"/> 处的触发区，到达 <see cref="ToMapId"/>；<see cref="Condition"/> 求值为假表示通道暂时关闭。</summary>
    public sealed class QuestGuideTransition
    {
        public Id AreaId { get; }

        public Id FromMapId { get; }

        public Id ToMapId { get; }

        public Vec2 Center { get; }

        public ExprNode? Condition { get; }

        public QuestGuideTransition(Id areaId, Id fromMapId, Id toMapId, Vec2 center, ExprNode? condition)
        {
            AreaId = areaId;
            FromMapId = fromMapId;
            ToMapId = toMapId;
            Center = center;
            Condition = condition;
        }
    }

    /// <summary>把引导目标里的数据行引用解析成落点，并列出全部地图切换通道。组装层的默认实现读数据注册表
    /// （<c>RegistryQuestGuideLocator</c>）；测试可自行实现。</summary>
    public interface IQuestGuideLocator
    {
        bool TryResolveArea(Id areaId, out QuestGuideAnchor anchor);

        bool TryResolveSpawn(Id spawnId, out QuestGuideAnchor anchor);

        IReadOnlyList<QuestGuideTransition> Transitions { get; }
    }

    /// <summary>
    /// 引导箭头此刻应指向的地方。<see cref="MapId"/>/<see cref="Position"/> 是最终目标；<see cref="ArrowPosition"/> 是玩家
    /// 当前所在地图里箭头该指的点：目标就在当前地图时等于 <see cref="Position"/>，否则是通往目标地图的下一条切换通道
    /// 的中心（<see cref="ViaTransitionId"/> 非空）。
    /// </summary>
    public sealed class QuestGuideTarget
    {
        public Id MapId { get; }

        public Vec2 Position { get; }

        public Id? SourceId { get; }

        public Vec2 ArrowPosition { get; }

        public Id? ViaTransitionId { get; }

        public QuestGuideTarget(Id mapId, Vec2 position, Id? sourceId, Vec2 arrowPosition, Id? viaTransitionId)
        {
            MapId = mapId;
            Position = position;
            SourceId = sourceId;
            ArrowPosition = arrowPosition;
            ViaTransitionId = viaTransitionId;
        }
    }

    /// <summary><see cref="QuestGuideHost.Evaluate"/> 的结果：命中的任务与步骤、文案键、进度、箭头目标。</summary>
    public sealed class QuestGuideInfo
    {
        public Id QuestId { get; }

        public int StepIndex { get; }

        public Id TextKey { get; }

        /// <summary>步骤声明了 <c>progress_flags</c> 时的已成立个数，否则为空。</summary>
        public int? ProgressDone { get; }

        public int? ProgressTotal { get; }

        /// <summary>箭头目标；步骤没有声明目标时为空（只显示文案）。</summary>
        public QuestGuideTarget? Target { get; }

        public QuestGuideInfo(Id questId, int stepIndex, Id textKey, int? progressDone, int? progressTotal, QuestGuideTarget? target)
        {
            QuestId = questId;
            StepIndex = stepIndex;
            TextKey = textKey;
            ProgressDone = progressDone;
            ProgressTotal = progressTotal;
            Target = target;
        }

        /// <summary>把本地化后的文案模板里的 <c>{done}</c>/<c>{total}</c> 换成进度数字（没有进度时原样返回）。</summary>
        public string FormatText(string template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!ProgressDone.HasValue || !ProgressTotal.HasValue)
            {
                return template;
            }

            return template
                .Replace("{done}", ProgressDone.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{total}", ProgressTotal.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

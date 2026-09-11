using System;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 地面坐标来源（ADR-0027《地面坐标施法请求》）：仅诊断用途，不参与任何校验/裁决——纯粹供
    /// 消费方在诊断/回放里标注这份坐标是从哪种输入产生的，<see cref="Core.Rules.Skill.CastPipeline"/>
    /// 不读取本字段。
    /// </summary>
    public enum GroundCastSource
    {
        /// <summary>请求发起时刻鼠标/触摸指针在世界空间的投影点。</summary>
        PointerAtRequest,

        /// <summary>锚定到某个单位当前位置（调用方在构造 <see cref="GroundCastRequest"/> 前自行取该单位
        /// 坐标填入 <see cref="GroundCastRequest.Point"/>；本枚举只标注来源，不持有单位 id）。</summary>
        UnitAnchor,

        /// <summary>脚本/AI/回放等显式指定的坐标，不经过任何指针输入。</summary>
        Explicit,
    }

    /// <summary>
    /// 地面坐标施法请求的坐标快照策略（ADR-0027 决策 1）：读条/引导期间到了"效果落地"那一刻
    /// （瞬发本身、读条/引导完成、引导每一次周期跳），<see cref="Core.Rules.Skill.CastPipeline"/>
    /// 该用哪一份坐标——与既有单位目标施法"步骤 6 解析出的目标列表，读条完成时原样使用，不随后续
    /// 单位移动而重新解析"是同一类"请求时固定 vs 释放时重新取值"的设计问题，本次移植同一选择给
    /// 调用方，而不是替调用方拍板。
    /// </summary>
    public enum GroundCastSnapshotPolicy
    {
        /// <summary>默认：效果落地时仍使用请求发起时刻的 <see cref="GroundCastRequest.Point"/> 快照，
        /// 不随后续输入变化——与既有单位目标施法固定目标列表同一惯例。</summary>
        AtRequest,

        /// <summary>效果落地时改用 <see cref="GroundCastRequest.Sampler"/> 重新采样一次当前坐标；
        /// <see cref="GroundCastRequest.Sampler"/> 未提供时退化为 <see cref="AtRequest"/> 的行为
        /// （原样使用 <see cref="GroundCastRequest.Point"/>），不抛异常。</summary>
        AtRelease,
    }

    /// <summary>
    /// 动态地面坐标施法请求（ADR-0027《地面坐标施法请求》）：<see cref="ISkillHost.CastSkillAtGround"/>
    /// 的输入 DTO。与既有 <see cref="ISkillHost.CastSkill"/> 的单位目标列表是两条互斥的入口——见该
    /// 方法判断记录"互斥"：本类型描述的是一次以世界坐标点（而非某个单位）为落点的施法请求。
    /// <para>
    /// 与既有 <see cref="SkillCastRequest.TargetPoint"/> 的关系（见该类型、
    /// <c>Core.Rules.Skill.SkillTickHandler</c> 判断记录"point 当前未被 CastSkill 使用，落点类技能
    /// 改经 SkillCastRequest.TargetPoint 由更上层 AI/玩家辅助施法适配层处理，见契约缺口清单"）：
    /// <see cref="SkillCastRequest"/> 是"玩家输入层/AI 统一产出的一次施法请求"这一更上层概念，其
    /// <c>TargetPoint</c> 字段本身不变、仍未被 <see cref="ISkillHost.CastSkill"/> 消费；本类型是
    /// <see cref="ISkillHost.CastSkillAtGround"/> 这条新增入口专用的、语义更完整的请求（坐标来源/
    /// 采样时点/快照策略），补齐的是"框架侧如何裁决地面坐标"这一缺口，不是要取代
    /// <see cref="SkillCastRequest"/>。更上层适配层今后可以在 <c>TargetPoint</c> 非空时构造一个
    /// <see cref="GroundCastRequest"/> 并改调 <see cref="ISkillHost.CastSkillAtGround"/>（如何改造
    /// 该适配层不在本次改动范围内，见 ADR-0027 判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（放置位置）：<see cref="ISkillHost"/>/<see cref="CastResult"/>/
    /// <see cref="CastFailureReason"/>/<see cref="EffectContext"/> 均定义在
    /// <c>core/rules/common/contracts</c>（"L2 四模块共享契约与事件类型"，见 01_分层与依赖.md 第 4
    /// 节、core/rules/common/README.md"只依赖本目录的类型，不直接引用对方模块内部的类型"）。本
    /// 类型作为 <see cref="ISkillHost.CastSkillAtGround"/> 签名的一部分，必须与该接口同处
    /// <c>Core.Rules.Common</c>，否则会让"L2 四模块共享契约"反向依赖 <c>core/rules/skill</c> 模块
    /// 内部类型，违反上述分层方向；因此落在 <c>core/rules/common/contracts</c>，与设计草案原文
    /// "core/rules/skill/contracts" 不一致处以本记录为准。
    /// </para>
    /// </summary>
    public sealed class GroundCastRequest
    {
        /// <summary>请求发起时刻的逻辑坐标快照（见 <see cref="GroundCastSnapshotPolicy.AtRequest"/>：
        /// 默认策略下，效果落地时仍使用这份坐标，不随后续输入变化）。</summary>
        public Vec2 Point { get; }

        /// <summary>坐标来源，仅诊断用途，不参与任何校验/裁决（见 <see cref="GroundCastSource"/>）。</summary>
        public GroundCastSource Source { get; }

        /// <summary>请求发起时刻的时间戳（调用方自行定义单位，通常是世界模拟时间/tick 序号），仅诊断
        /// 用途——<see cref="Core.Rules.Skill.CastPipeline"/> 不解释、不比较该值。</summary>
        public double RequestedAtTick { get; }

        /// <summary>坐标快照策略（见 <see cref="GroundCastSnapshotPolicy"/>），默认
        /// <see cref="GroundCastSnapshotPolicy.AtRequest"/>。</summary>
        public GroundCastSnapshotPolicy SnapshotPolicy { get; }

        /// <summary>
        /// <see cref="SnapshotPolicy"/> 为 <see cref="GroundCastSnapshotPolicy.AtRelease"/> 时，效果
        /// 落地那一刻（瞬发本身、读条/引导完成、引导每一次周期跳）用于重新采样当前坐标的委托——调用
        /// 时机与调用方持有的坐标来源（跟随鼠标/跟随某单位等）无关，本类型只负责在恰当时机调用它。
        /// <see cref="SnapshotPolicy"/> 为 <see cref="GroundCastSnapshotPolicy.AtRequest"/> 时本字段
        /// 被忽略（可为 null）。<see cref="SnapshotPolicy"/> 为
        /// <see cref="GroundCastSnapshotPolicy.AtRelease"/> 但本字段为 null 时，退化为使用
        /// <see cref="Point"/>（不抛异常，见 <see cref="GroundCastSnapshotPolicy.AtRelease"/> 判断
        /// 记录）。
        /// </summary>
        public Func<Vec2>? Sampler { get; }

        public GroundCastRequest(
            Vec2 point,
            GroundCastSource source = GroundCastSource.Explicit,
            double requestedAtTick = 0,
            GroundCastSnapshotPolicy snapshotPolicy = GroundCastSnapshotPolicy.AtRequest,
            Func<Vec2>? sampler = null)
        {
            Point = point;
            Source = source;
            RequestedAtTick = requestedAtTick;
            SnapshotPolicy = snapshotPolicy;
            Sampler = sampler;
        }
    }
}

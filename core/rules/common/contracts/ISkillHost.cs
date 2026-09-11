using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Numbers.StatBlock;
using Core.Rules.Skill;

namespace Core.Rules.Common
{
    /// <summary>
    /// 技能模块对外契约（见 06 第 7 节 <c>SkillHost</c>）。由 <c>core/rules/skill</c> 实现，供
    /// combat/targeting/ai 三个模块调用（见 README"谁实现、谁调用"矩阵）。
    /// </summary>
    public interface ISkillHost
    {
        Vec2 GetPosition(Id unitId);

        /// <summary>
        /// 按范围形状查找单位（见 06 第 7 节 <c>findUnits(shape, filter)</c>）。<paramref name="origin"/>
        /// 是任务书拍板补充的锚点参数——判断记录：05 第 3.5 节的 <see cref="Shape"/> 各变体本身已经
        /// 携带绝对坐标（<c>circle.center</c>/<c>cone.origin</c>/<c>line.origin</c>/<c>rect.origin</c>），
        /// 但 <c>skill.def.target_shape_ref</c>（见 06 第 3.1 节）指向的是可被多个技能、多个施法者
        /// 复用的形状模板，模板本身不预先绑定某个具体施法者的坐标；调用方在技能解析期用当前施法者
        /// （或其它锚点，如目标单位、地面点）的位置填充 <paramref name="origin"/>，实现内部据此重建
        /// 出一个绝对坐标的 <see cref="Shape"/> 再委托 <see cref="ISpatialQuery.QueryShape"/>——
        /// 与 06 §7 原始签名 <c>findUnits(shape, filter)</c> 的差异仅在于把"模板 + 锚点"拆成了两个
        /// 参数，语义不变。
        /// </summary>
        IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter);

        /// <summary>
        /// 对某单位施加一条属性修正（见 06 第 7 节 <c>applyStatMod(sourceId, unitId, stat, op, value)</c>）。
        /// <paramref name="op"/> 复用 L1 <see cref="StatModifierOp"/>（任务书拍板："op 用字符串常量
        /// flat|pct|mult 或复用 L1 的 StatModifierOp——拍板：复用 Core.Numbers.StatBlock.StatModifierOp"），
        /// 不在本层重新定义一套等价的字符串常量。
        /// </summary>
        void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value);

        CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets);

        /// <summary>
        /// ADR-0027《地面坐标施法请求》补充：以显式世界坐标点（而非选中的单位列表）为落点的施法
        /// 请求入口——消费方反馈 2026-09-11"动态地面坐标施法请求"（见
        /// architecture/落地计划/消费方反馈-2026-09-11-地面坐标施法.md）：<see cref="CastSkill"/>
        /// 的施法者/选中目标校验已有距离/视线保护，但没有独立的"动态地面坐标"施法请求通道；本方法
        /// 补上这条通道。裁决口径与 <see cref="CastSkill"/> 共用步骤 1～5（存活/控制、学派锁定、
        /// 冷却/充能、公共冷却、资源），随后换成地面坐标专属校验（技能是否声明允许地面目标、射程、
        /// 视线、可行走）取代步骤 6/7 的单位目标解析/距离/视线；<see cref="CastResult.CastInstanceId"/>
        /// 与生命周期事件（<c>skill.cast_start</c>/<c>skill.cast_success</c>/<c>skill.cast_interrupted</c>）
        /// 语义与 <see cref="CastSkill"/> 完全一致，事件额外携带 <c>GroundPoint</c>。
        /// <para>
        /// 互斥（见 <see cref="GroundCastRequest"/> 判断记录）：本方法与 <see cref="CastSkill"/> 是
        /// 两条独立入口，不共享目标解析管线——调用本方法不会读取、也不会被任何"当前选中单位目标"
        /// 字段静默替代（消费方反馈验证条件原文）；<c>request.Point</c> 是效果结算唯一的落点来源。
        /// </para>
        /// <para>
        /// C# 8 默认接口成员：本默认实现恒拒绝（<see cref="CastFailureReason.GroundTargetUnsupported"/>，
        /// 不分配施法实例 id、不发任何事件——与 <see cref="CastResult.Fail"/> 既有"校验阶段失败不
        /// 分配 id"惯例一致），供未实现本能力的 <see cref="ISkillHost"/>（如旧版本编译产物、未升级
        /// 的自定义实现）源码/二进制兼容——新增接口成员不破坏既有实现类的编译。生产实现
        /// <c>Core.Rules.Skill.SkillHost</c> 显式覆盖，转发到 <c>CastPipeline.CastSkillAtGround</c>。
        /// 任何组合/包装 <see cref="ISkillHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个
        /// 降级默认值——同 <see cref="GetSkillReadiness"/> 判断记录"框架内 InterfaceDefaultMemberForwardingTests
        /// 门禁"。
        /// </para>
        /// </summary>
        CastResult CastSkillAtGround(Id casterId, Id skillId, GroundCastRequest request) =>
            CastResult.Fail(CastFailureReason.GroundTargetUnsupported);

        /// <summary>某技能（或其所属冷却分类）距下次可用的剩余时间；就绪返回 0。</summary>
        double GetCooldown(Id unitId, Id skillId);

        /// <summary>补充：该单位当前是否处于读条/引导中（见 06 第 3.6 节步骤 8）。</summary>
        bool IsCasting(Id unitId);

        /// <summary>
        /// 补充：打断某单位当前的读条/引导（见 06 第 3.2 节 <c>interrupt</c> 效果原语、第 3.6 节
        /// "打断与学派锁定"）。<paramref name="lockSchool"/> 非空时同时锁定对应学派
        /// <paramref name="lockDuration"/> 时间单位；<paramref name="lockSchool"/> 为 null 时只打断
        /// 不锁定学派。未在读条/引导中的单位调用本方法无效果（幂等）。
        /// </summary>
        void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration);

        /// <summary>
        /// 补充（消费方反馈 2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"，见
        /// architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md）：技能冷却/充能/公共
        /// 冷却的统一只读就绪快照。裁决口径与 <see cref="CastSkill"/> 施法管线步骤 3（冷却/充能）、
        /// 步骤 4（公共冷却）一致，<b>不含</b>步骤 1/2/5/6/7（存活/控制、学派锁定、资源、目标合法性、
        /// 距离与视线）等其它拒绝原因——<see cref="SkillReadiness.IsReady"/> 为 <c>true</c> 只表示
        /// "不会被冷却/充能/公共冷却挡下"，不等价于"此刻 <c>CastSkill</c> 一定成功"；<see
        /// cref="CastSkill"/> 的返回值始终是唯一的最终裁决。只读取宿主现有账本，不推进时间、不创建
        /// 第二套计时器、不修改任何状态（查询前后再次查询/再次 <see cref="GetCooldown"/> 得到的值
        /// 不变）。<paramref name="skillId"/> 与 <see cref="GetCooldown"/> 同一惯例——按调用方传入的
        /// id 原样查询，不做 <c>override_skill</c> 光环重定向。
        /// <para>
        /// C# 8 默认接口成员：本默认实现只能用 <see cref="GetCooldown"/> 拼一个降级快照——<see
        /// cref="SkillReadiness.IsReady"/> 直接取 <c>GetCooldown(unitId, skillId) &lt;= 0</c>，<see
        /// cref="SkillReadiness.BlockingSources"/> 不为就绪时粗略标记 <see
        /// cref="SkillReadinessBlockers.SkillCooldown"/>（无法进一步区分究竟是技能自身冷却、分类
        /// 冷却还是充能未恢复——<c>GetCooldown</c> 本身就不区分，见该方法文档），公共冷却完全不参与
        /// 判定（默认实现不知道 <c>SkillOptions</c>/GCD 状态）；其余字段一律为 <c>null</c>，表示
        /// "未知"。任何组合/包装 <see cref="ISkillHost"/>（若存在）都应显式转发到内层实现，而不是
        /// 悄悄吃掉这个降级默认值——参见框架内 <c>InterfaceDefaultMemberForwardingTests</c> 门禁；
        /// 生产实现 <c>Core.Rules.Skill.SkillHost</c> 显式覆盖，直接从 <c>CooldownTracker</c>/GCD/
        /// 充能状态读取精确字段。
        /// </para>
        /// </summary>
        SkillReadiness GetSkillReadiness(Id unitId, Id skillId)
        {
            var remaining = GetCooldown(unitId, skillId);
            var blocking = remaining > 0 ? SkillReadinessBlockers.SkillCooldown : SkillReadinessBlockers.None;
            return new SkillReadiness(
                skillId: skillId,
                isReady: remaining <= 0,
                blockingSources: blocking,
                skillCooldownRemaining: null,
                categoryCooldownRemaining: null,
                globalCooldownRemaining: null,
                maxCharges: null,
                currentCharges: null,
                nextChargeRemaining: null,
                effectiveCooldownDuration: null);
        }
    }
}

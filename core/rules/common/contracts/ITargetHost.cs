using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 目标选择模块对外契约（见 06 第 5/7 节 <c>TargetHost.resolve(chainId, casterId) -&gt; List&lt;UnitRef&gt;</c>）。
    /// 由 <c>core/rules/targeting</c> 实现，供 skill（施法管线步骤 6）与 ai（Rotation 条件里选定
    /// <c>target</c> 分组的求值对象）调用。
    /// </summary>
    public interface ITargetHost
    {
        /// <summary>按 <paramref name="chainId"/> 指向的 <c>target.chain_def</c> 解析出候选目标列表
        /// （见 06 第 5 节，顺序即排序结果）。</summary>
        IReadOnlyList<Id> Resolve(Id chainId, Id casterId);

        /// <summary>
        /// 补充：带"当前目标"上下文的解析重载，供链的 <c>source = current_target</c>（见 06 第 5 节
        /// <c>TargetChainDef.source</c> 枚举示例）在无法从调用方状态隐式取得当前目标时显式传入；
        /// <paramref name="currentTarget"/> 为 null 时行为与 <see cref="Resolve(Id, Id)"/> 等价。
        /// </summary>
        IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget);

        /// <summary>
        /// N10 补充（外部审计 68c9bed，P2）：调用方已经显式给定 <paramref name="targets"/>（如 UI
        /// 点选、外部系统直接指定，见 <c>CastPipeline</c> 施法管线步骤 6"调用方传入非空 targets"
        /// 分支）时，<see cref="Resolve(Id, Id)"/> 的"来源收集 → 过滤 → 排序 → 截断 → 空则回退"整条
        /// 管线都不会跑，<paramref name="chainId"/> 对应链声明的 <c>filters</c>（tag/expr 等额外目标
        /// 条件，见 06 第 5 节）此前完全不会对显式目标生效——本方法只做"过滤"这一步：不做来源收集
        /// （显式目标本身就是来源）、不排序、不按 <c>max_targets</c> 截断、不触发 <c>fallback</c>，
        /// 逐个校验 <paramref name="targets"/> 是否满足链的 <c>filters</c>，返回其中通过的子集
        /// （保持传入顺序）。<see cref="Core.Rules.Skill.CastPipeline"/> 用本方法的结果是否为空判定
        /// <c>CastFailureReason.NoValidTarget</c>（与"由链自行收集但过滤后为空"复用同一失败码，
        /// 06 未单独为"显式目标不满足额外条件"定义原因码）。
        /// </summary>
        IReadOnlyList<Id> FilterExplicitTargets(Id chainId, Id casterId, IReadOnlyList<Id> targets);

        /// <summary>
        /// ADR-0027《地面坐标施法请求》补充：按 <paramref name="chainId"/> 指向的
        /// <c>target.chain_def</c> 解析候选目标，但把链 <c>shape</c> 字段的锚点从"施法者当前坐标/
        /// 朝向"换成显式传入的 <paramref name="point"/>（朝向固定为 0，见
        /// <c>Core.Rules.Targeting.TargetHost.ResolveAtPoint</c> 判断记录"无朝向可继承"）——
        /// <c>source</c>/<c>filters</c>/<c>sort_by</c>/<c>max_targets</c>/<c>fallback</c> 均照常生效，
        /// 只是形状查询的原点/排序距离基准换了；<paramref name="casterId"/> 仍参与 <c>filters</c>
        /// 中依赖施法者本身的条件（如阵营关系、排除自身）。供
        /// <c>Core.Rules.Skill.CastPipeline.CastSkillAtGround</c> 复用同一条 <c>target.chain_def</c>
        /// 定义解析地面坐标施法命中的单位集合——与 <see cref="Resolve(Id, Id, Id?)"/> 是两条独立
        /// 入口，互不调用，保证地面坐标请求不会被"施法者当前位置"这一隐式锚点静默替代。
        /// <para>
        /// C# 8 默认接口成员：本默认实现退化为 <see cref="Resolve(Id, Id)"/>（按施法者位置解析，不做
        /// 地面点重锚定）——供未实现本能力的 <see cref="ITargetHost"/>（如测试假实现）源码/二进制
        /// 兼容；生产实现 <see cref="Core.Rules.Targeting.TargetHost"/> 显式覆盖，真正按
        /// <paramref name="point"/> 重锚定。同 <see cref="Core.Rules.Common.ISkillHost.GetSkillReadiness"/>
        /// 判断记录"框架内 InterfaceDefaultMemberForwardingTests 门禁"：任何组合/包装
        /// <see cref="ITargetHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个降级默认值。
        /// </para>
        /// </summary>
        IReadOnlyList<Id> ResolveAtPoint(Id chainId, Id casterId, Vec2 point) => Resolve(chainId, casterId);
    }
}

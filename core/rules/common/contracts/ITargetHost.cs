using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

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

        /// <summary>
        /// T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：按
        /// <paramref name="chainId"/> 指向的 <c>target.chain_def</c> 解析候选目标，随后按链声明的
        /// <c>overflow_policy</c>（<see cref="TargetOverflowPolicy"/>，缺省
        /// <see cref="TargetOverflowPolicy.Truncate"/>）与 <c>max_targets</c> 给每个命中目标分配一个
        /// "分配系数"（三态定义见 <see cref="TargetOverflowPolicy"/> 各成员注释、<see cref="TargetResolution"/>
        /// 判断记录），供 <c>Core.Rules.Skill.EffectDispatcher</c> 按系数缩放群体效果值（<c>value ×
        /// coefficient</c>，系数恒为 1 时逐位不变）。<paramref name="currentTarget"/> 语义同
        /// <see cref="Resolve(Id, Id, Id?)"/>。旧签名 <see cref="Resolve(Id, Id)"/>/
        /// <see cref="Resolve(Id, Id, Id?)"/> 保留、行为不变（硬性规则"禁止改旧 Resolve 签名"）——
        /// <see cref="TargetOverflowPolicy.Truncate"/>（缺省）策略下，旧签名返回的目标列表与本方法
        /// <see cref="TargetResolution.Targets"/> 投影（只取 <c>Target</c>）逐一对应；<see
        /// cref="TargetOverflowPolicy.Split"/>/<see cref="TargetOverflowPolicy.Cap"/> 策略下旧签名
        /// 返回全部候选（不做数量截断，因为这两种策略本就不丢弃候选，只稀释系数——旧签名无法表达
        /// 系数，调用方若仍用旧签名，只能拿到未稀释的目标集合，需改用本方法才能拿到正确的分配系数，
        /// 见 <c>Core.Rules.Targeting.TargetHost</c>"旧签名投影关系"判断记录）。
        /// <para>
        /// C# 8 默认接口成员：默认实现退化为对旧 <see cref="Resolve(Id, Id, Id?)"/> 结果整体赋系数
        /// 1、策略 <see cref="TargetOverflowPolicy.Truncate"/>、<c>cap=0</c>（不限）——供未实现本
        /// 能力的 <see cref="ITargetHost"/>（如测试假实现）源码/二进制兼容；生产实现
        /// <see cref="Core.Rules.Targeting.TargetHost"/> 显式覆盖，真正按链声明的 <c>overflow_policy</c>
        /// 分配系数。同 <see cref="ResolveAtPoint"/> 判断记录"框架内 InterfaceDefaultMemberForwardingTests
        /// 门禁"：任何组合/包装 <see cref="ITargetHost"/>（若存在）都应显式转发到内层实现，不应悄悄
        /// 吃掉这个降级默认值。
        /// </para>
        /// </summary>
        TargetResolution ResolveWithCoefficients(Id chainId, Id casterId, Id? currentTarget = null) =>
            new TargetResolution(
                Resolve(chainId, casterId, currentTarget).Select(id => (id, 1.0)),
                TargetOverflowPolicy.Truncate,
                cap: 0);

        /// <summary>
        /// 手感落地（时间线空间命中，手感设计/03 第 2.2 节）：取 <paramref name="chainId"/> 链声明的形状模板
        /// （<c>shape</c> 字段，Origin 为零、方向为 0 的未锚定形态）；链没有声明 <c>shape</c> 返回 false。
        /// 时间线据此判断"技能有没有命中形状"（有则走空间命中，没有则保持 instant 结算）、做扫掠采样的步长估算、
        /// 以及算接触点。
        /// <para>
        /// C# 8 默认接口成员：默认实现恒返回 false（"没有形状"，时间线退回 instant 结算，等价于本成员引入之前）；
        /// 生产实现 <see cref="Core.Rules.Targeting.TargetHost"/> 显式覆盖。同 <see cref="ResolveAtPoint"/> 判断记录
        /// "InterfaceDefaultMemberForwardingTests 门禁"：包装实现方必须显式转发。
        /// </para>
        /// </summary>
        bool TryGetChainShape(Id chainId, out Shape template)
        {
            template = default;
            return false;
        }

        /// <summary>
        /// 手感落地（时间线空间命中）：按 <paramref name="chainId"/> 解析目标，但把形状锚点换成显式给定的位姿
        /// （<paramref name="origin"/> + <paramref name="facing"/>，弧度）。<c>continuous</c> 命中在两个 tick 的攻击方位姿
        /// 之间插值采样时用它；与 <see cref="ResolveAtPoint"/> 的区别是后者朝向固定为 0。不发布 <c>targeting.resolved</c>
        /// （同 <see cref="ResolveAtPoint"/>：逐 tick 多次采样不适合叠进每次解析一事件的既有流）。
        /// <para>
        /// 默认实现退化为 <see cref="ResolveWithCoefficients"/>（按施法者当前位姿解析，忽略给定位姿）——
        /// 供未实现本能力的假实现源码/二进制兼容；生产实现 <see cref="Core.Rules.Targeting.TargetHost"/> 显式覆盖。
        /// </para>
        /// </summary>
        TargetResolution ResolveAtPose(Id chainId, Id casterId, Vec2 origin, double facing, Id? currentTarget = null) =>
            ResolveWithCoefficients(chainId, casterId, currentTarget);
    }
}

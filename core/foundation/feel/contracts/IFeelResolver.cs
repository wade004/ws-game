using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 规则层的只读入口：只能取判定型视图（手感设计/05 第 5 节"判定型消费者只经 resolve 读判定型字段"）。
    /// 规则层模块的构造参数写这个接口，不写 <see cref="IFeelResolver"/>。
    /// </summary>
    public interface IFeelJudgingSource
    {
        /// <summary>取单位当前的判定型视图（缓存命中直接返回）。</summary>
        JudgingFeelView ResolveJudging(Id unitId);
    }

    /// <summary>表现层的只读入口：只能取呈现型视图。</summary>
    public interface IFeelPresentingSource
    {
        /// <summary>取单位当前的呈现型视图（缓存命中直接返回）。</summary>
        PresentingFeelView ResolvePresenting(Id unitId);
    }

    /// <summary>
    /// 手感解析器（手感设计/05 第 5 节）：纯函数式（输入只有数据表与经提供者读到的实体只读状态，不读时钟、不读随机），
    /// 按单位缓存，失效事件驱动重算。
    /// </summary>
    public interface IFeelResolver : IFeelJudgingSource, IFeelPresentingSource
    {
        /// <summary>字段登记。</summary>
        FeelFieldSet Fields { get; }

        /// <summary>本解析器使用的标定。</summary>
        FeelCalibration Calibration { get; }

        /// <summary>取单位当前完整解析结果（缓存命中直接返回同一个对象）。</summary>
        ResolvedFeel Resolve(Id unitId);

        /// <summary>
        /// 使某单位缓存失效，下一次 <see cref="Resolve"/> 重算。<paramref name="reason"/> 说明触发原因
        /// （装备变化、外形变化、光环施加/移除、动作开始/结束/取消、标签变化、调试覆盖变化……），只用于诊断。
        /// 上层模块在装配根自行订阅各自的事件并调用本方法；解析器本身不依赖任何上层事件类型。
        /// </summary>
        void Invalidate(Id unitId, string reason);

        /// <summary>使全部单位缓存失效（热加载数据、全局调试覆盖变化时用）。</summary>
        void InvalidateAll(string reason);

        /// <summary>字段的溯源链：层号、来源 id、操作、前值、后值（标定前相对值）。字段无值时为空。</summary>
        IReadOnlyList<FeelProvenanceEntry> GetProvenance(Id unitId, string field);

        /// <summary>单位当前的解析版本号：每次重算递增，消费者据此判断是否需要重读。</summary>
        int GetVersion(Id unitId);

        /// <summary>
        /// 动作开始快照：以"进行中动作的手感引用 <paramref name="actionFeelRef"/>（可为空）"为第 6 层、
        /// 并把单位视为处于动作中（武器的"攻击期间临时覆盖"生效）重算一份<b>独立于缓存</b>的结果，
        /// 挂在施法实例 <paramref name="castInstanceId"/> 上返回。之后无论单位重算、数据热加载，
        /// <see cref="GetSnapshot"/> 取到的始终是这份对象；动作结束（完成/取消/打断）调用
        /// <see cref="EndAction"/> 释放。
        /// </summary>
        ResolvedFeel BeginAction(Id unitId, Id castInstanceId, string? actionFeelRef);

        /// <summary>取施法实例上的快照；未快照或已释放返回 null。</summary>
        ResolvedFeel? GetSnapshot(Id castInstanceId);

        /// <summary>释放施法实例上的快照（幂等）。</summary>
        void EndAction(Id castInstanceId);
    }

    /// <summary>
    /// 实体"体型/角色引用"的提供者（第 2 层原型、第 5 层角色）：返回单位模板上的
    /// <c>feel_archetype_ref</c>/<c>feel_ref</c>（行 id），无则 null。解析器在 L0，不依赖载体层，
    /// 由装配根注册实现（读单位模板）。
    /// </summary>
    public interface IFeelBodyProvider
    {
        string? GetArchetypeRef(Id unitId);

        string? GetCharacterRef(Id unitId);
    }

    /// <summary>实体标签提供者（第 3 层）：返回单位当前持有的标签（形如 <c>gender:female</c>），顺序无关紧要（解析器自行排序）。</summary>
    public interface IFeelTagProvider
    {
        IReadOnlyList<string> GetTags(Id unitId);
    }

    /// <summary>装备提供者（第 4 层）：主手与副手武器的 <c>feel_weapon_ref</c>（行 id），无则 null。</summary>
    public interface IFeelEquipmentProvider
    {
        string? GetMainWeaponRef(Id unitId);

        string? GetOffhandWeaponRef(Id unitId);
    }

    /// <summary>单位当前动作状态（第 6 层与"攻击期间武器临时覆盖"的判据）。</summary>
    public readonly struct FeelActionState : IEquatable<FeelActionState>
    {
        /// <summary>是否有进行中的动作（决定武器层 <c>AttackOverride</c> 字段是否生效）。</summary>
        public bool InAction { get; }

        /// <summary>进行中动作的 <c>feel_ref</c>（<c>feel.action</c> 行 id），可空。</summary>
        public string? ActionFeelRef { get; }

        public FeelActionState(bool inAction, string? actionFeelRef)
        {
            InAction = inAction;
            ActionFeelRef = actionFeelRef;
        }

        /// <summary>无进行中动作。</summary>
        public static FeelActionState Idle => default;

        public bool Equals(FeelActionState other) =>
            InAction == other.InAction && string.Equals(ActionFeelRef, other.ActionFeelRef, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is FeelActionState other && Equals(other);

        public override int GetHashCode() => (InAction ? 1 : 0) ^ (ActionFeelRef == null ? 0 : StringComparer.Ordinal.GetHashCode(ActionFeelRef));
    }

    /// <summary>当前动作提供者（第 6 层）。</summary>
    public interface IFeelActionProvider
    {
        FeelActionState GetActionState(Id unitId);
    }

    /// <summary>临时状态提供者（第 7 层）：返回单位当前所有携带手感修饰的光环条目（键 = 光环实例 id）。</summary>
    public interface IFeelTemporaryProvider
    {
        IReadOnlyList<FeelTemporaryEntry> GetEntries(Id unitId);
    }

    /// <summary>调试覆盖提供者（第 8 层，仅开发期）：全局覆盖与单位覆盖，先全局后单位。</summary>
    public interface IFeelDebugProvider
    {
        IReadOnlyList<FeelWrite> GetGlobalOverrides();

        IReadOnlyList<FeelWrite> GetUnitOverrides(Id unitId);
    }

    /// <summary>
    /// 解析器读取实体只读状态的全部提供者（依赖倒置，手感设计/05 第 5 节）。任一为 null 表示"该来源恒为空"。
    /// </summary>
    public sealed class FeelProviders
    {
        public IFeelBodyProvider? Body { get; set; }

        public IFeelTagProvider? Tags { get; set; }

        public IFeelEquipmentProvider? Equipment { get; set; }

        public IFeelActionProvider? Action { get; set; }

        public IFeelTemporaryProvider? Temporary { get; set; }

        public IFeelDebugProvider? Debug { get; set; }
    }

    /// <summary>
    /// 装配参数（<see cref="FeelAssembly.Assemble"/>）：模拟步长（秒，毫秒→tick 的依据，通常取
    /// <c>SimLoopOptions.StepSeconds</c>）、标定行 id（数据里恰有一行标定时可省略，多行必须指定）、提供者、字段登记。
    /// </summary>
    public sealed class FeelAssemblyOptions
    {
        /// <summary>模拟固定步长（秒），必须为正。</summary>
        public double StepSeconds { get; set; } = 1.0 / 60.0;

        /// <summary>要用的 <c>feel.calibration</c> 行 id；数据里恰有一行时可为空。</summary>
        public string? CalibrationId { get; set; }

        /// <summary>解析器读取实体状态的提供者；可为空（视为全部来源为空）。</summary>
        public FeelProviders? Providers { get; set; }

        /// <summary>字段登记；缺省 <see cref="FeelFields.Default"/>。</summary>
        public FeelFieldSet? Fields { get; set; }
    }
}

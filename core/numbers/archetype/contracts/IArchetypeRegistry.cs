using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// 职业与种族模板契约（见 01_分层与依赖.md L1 模块表 <c>archetype</c> 行"契约接口名：
    /// ArchetypeRegistry"，本模块按仓库既有惯例把契约命名为 <c>IArchetypeRegistry</c>、默认实现
    /// 命名为 <see cref="ArchetypeRegistry"/>）：只读查询职业/种族/天赋树模板，并把某个模板应用
    /// 到某个单位的初始状态。<b>本接口与其实现不得出现任何具体游戏的职业/种族名称</b>
    /// （落地方案与分阶段计划.md T2-3 行"禁止 archetype 模块内写死具体游戏的职业名称"）——全部
    /// 具体职业/种族由 <c>arch.class</c>/<c>arch.race</c> 数据行定义，本模块只认 id。
    /// </summary>
    public interface IArchetypeRegistry
    {
        ClassDefinition? GetClass(Id id);

        RaceDefinition? GetRace(Id id);

        TalentTree? GetTalentTree(Id id);

        /// <summary>全部已加载的职业模板，顺序为 <c>arch.class</c> 数据行的加载顺序。</summary>
        IReadOnlyList<ClassDefinition> Classes { get; }

        /// <summary>
        /// 把职业 <paramref name="classId"/>（必填）与可选种族 <paramref name="raceId"/> 应用到
        /// <paramref name="unitId"/> 的初始状态：按顺序——
        /// (1) 用 <see cref="StatBaseWriter"/> 写 <c>classId</c> 的 <c>base_stats</c>；
        /// (2) 若给了 <paramref name="raceId"/>，用 <see cref="StatModifierWriter"/> 以
        /// <c>"flat"</c> 运算类型写该种族的 <c>stat_mods</c>，来源固定为 <paramref name="raceId"/>
        /// 本身；不给种族时完全跳过这一步（不调用 <see cref="StatModifierWriter"/>）；
        /// (3) 用 <see cref="PowerRegistrar"/> 注册 <c>classId</c> 的 <c>power_types</c>；
        /// (4) 发 <see cref="ArchetypeAppliedEvent"/>。
        /// <paramref name="classId"/>/<paramref name="raceId"/> 指向未登记的模板时抛
        /// <see cref="System.ArgumentException"/>。
        /// </summary>
        AppliedArchetype ApplyTo(Id unitId, Id classId, Id? raceId);
    }
}

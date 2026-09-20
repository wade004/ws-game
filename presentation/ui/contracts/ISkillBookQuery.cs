using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// UI 框架对"已知技能 + 冷却"的只读查询需求（见任务书"core/rules/skill（SkillHost.GetKnownSkills/
    /// 冷却查询）"）。
    /// <para>
    /// 判断记录：<c>Core.Rules.Skill.SkillHost.GetKnownSkills</c> 是该具体类的公开方法，不在
    /// <c>Core.Rules.Common.ISkillHost</c> 契约接口上（见 06 第 7 节 <c>SkillHost</c> 原始签名只有
    /// <c>castSkill</c>/<c>getCooldown</c> 等战斗相关方法，已知技能簿是 <c>core/rules/skill</c> 模块
    /// 自行补充的公开能力，见该模块 <c>SkillHost.cs</c> "已知技能 / 技能书"一节）。本模块不修改
    /// <c>core/rules</c>（任务范围禁止），也不能直接把 <c>ISkillHost</c> 塞进
    /// <see cref="SkillBookViewModel"/>（缺 <c>GetKnownSkills</c>）；于是在本模块内定义这个只读窄
    /// 接口，只声明 UI 侧真正用到的两个方法，由 <c>core/SkillHostSkillBookQuery.cs</c> 适配真实的
    /// <c>Core.Rules.Skill.SkillHost</c>，测试则可以直接写一个内存态 Fake，不必搭建
    /// <c>SkillHost</c> 的完整构造依赖链（<c>IDataRegistryView</c>/<c>IRngHost</c>/<c>ICombatHost</c>
    /// 等一整套 rules 模块内部协作对象）。
    /// </para>
    /// </summary>
    public interface ISkillBookQuery
    {
        /// <summary>该单位当前已知技能 id 列表（见 <c>Core.Rules.Skill.SkillHost.GetKnownSkills</c>，
        /// 按 id 序数排序）。</summary>
        IReadOnlyList<Id> GetKnownSkills(Id unitId);

        /// <summary>该技能距下次可用的剩余时间；就绪返回 0（见
        /// <c>Core.Rules.Common.ISkillHost.GetCooldown</c>）。</summary>
        double GetCooldown(Id unitId, Id skillId);

        /// <summary>
        /// 消费方反馈第 3 条（2026-09-20，ADR-0048）：该技能的名称文本键（<c>skill.def.name_key</c>，
        /// 见 <c>Core.Rules.Skill.SkillHost.GetSkillNameKey</c>）；未声明或技能不存在时为 <c>null</c>，
        /// 消费方（<c>ActionBarViewModel</c>/<c>ActionBarPanel</c>）据此不渲染名称，不回退占位文案。
        /// 判断记录：以默认接口成员新增（同 <c>Core.Gameplay.Quest.IQuestHost.
        /// GetObjectiveRequiredCounts</c> 既有 ABI 兼容惯例）——本接口新增方法不破坏既有实现者
        /// （测试用的内存态 Fake）的二进制兼容性，未覆盖时默认返回 <c>null</c>，等价于"未声明"。
        /// </summary>
        Id? GetNameKey(Id skillId) => null;
    }
}

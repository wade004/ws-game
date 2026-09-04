using Core.Foundation.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 装备联动"授予/撤销主动技能"的具名委托（见 07 第 1.4 节"按 grants.skills 加入可用技能
    /// 集合"）。<see cref="Core.Rules.Common.ISkillHost"/> 契约缺口：没有 <c>LearnSkill</c>/
    /// <c>ForgetSkill</c> 一类方法（见 <c>core/rules/skill</c> README 的 <c>SkillBook</c> 相关内容，
    /// 该能力目前只在具体 <c>SkillHost</c> 实现里，未提升到共享契约——本模块改动范围不允许修改
    /// <c>core/rules/*</c>，见任务书"契约缺口用模块内委托绕过并汇报"）。<see cref="EquipmentHost"/>
    /// 构造时注入一个把真实 <c>SkillHost.LearnSkill</c>/<c>ForgetSkill</c>（阶段 3 整理补齐，见
    /// <c>CarriersAssembly</c>）适配成本委托签名的闭包；
    /// <paramref name="learn"/> 为 true 时学习，false 时遗忘。
    /// </summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="skillId">要学习/遗忘的技能 id。</param>
    /// <param name="learn">true=学习，false=遗忘。</param>
    public delegate void SkillGranter(Id unitId, Id skillId, bool learn);
}

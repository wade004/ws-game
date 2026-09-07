using Core.Foundation.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 装备联动"授予/撤销主动技能"的具名委托（见 07 第 1.4 节"按 grants.skills 加入可用技能
    /// 集合"）。<see cref="Core.Rules.Common.ISkillHost"/> 契约缺口：没有 <c>LearnSkill</c>/
    /// <c>ForgetSkill</c> 一类方法（见 <c>core/rules/skill</c> README 的 <c>SkillBook</c> 相关内容，
    /// 该能力目前只在具体 <c>SkillHost</c> 实现里，未提升到共享契约——本模块用委托绕过，见任务书
    /// "契约缺口用模块内委托绕过并汇报"）。<see cref="EquipmentHost"/> 构造时注入一个把真实
    /// <c>SkillHost.LearnSkill</c>/<c>ForgetSkill</c>（阶段 3 整理补齐，见 <c>CarriersAssembly</c>）
    /// 适配成本委托签名的闭包；<paramref name="learn"/> 为 true 时学习，false 时遗忘。
    /// <para>
    /// RC-05 收边勘误：新增 <paramref name="sourceId"/>（<see cref="EquipmentHost"/> 传入授予它的
    /// 装备实例 id，见 <see cref="EquipmentHost.ApplyGrants"/>/<see cref="EquipmentHost.RevertGrants"/>）
    /// ——原委托没有来源概念，卸下一件装备遗忘技能是"不计来源"的整体撤销：两件装备都授予同一
    /// 技能时卸下一件会连另一件仍在授予的技能一起遗忘，永久学习（天赋/任务/技能书/读档）与装备
    /// 授予也无法区分（卸装备会连永久学到的技能一起遗忘，见外部审计 RC-05）。<c>SkillHost</c> 侧
    /// 现按 <paramref name="sourceId"/> 做引用计数（见 <c>SkillHost.LearnSkill(Id,Id,Id)</c>/
    /// <c>ForgetSkill(Id,Id,Id)</c> 判断记录），只有同一技能的全部来源都被撤销才真正遗忘。
    /// </para>
    /// </summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="skillId">要学习/遗忘的技能 id。</param>
    /// <param name="sourceId">本次学习/遗忘归属的来源 id（装备实例 id）——供接收端做按来源的引用
    /// 计数，见类型判断记录。</param>
    /// <param name="learn">true=学习，false=遗忘。</param>
    public delegate void SkillGranter(Id unitId, Id skillId, Id sourceId, bool learn);
}

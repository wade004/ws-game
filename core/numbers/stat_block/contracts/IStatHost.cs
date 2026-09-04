using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// 属性表契约（见 06_规则层_属性技能战斗AI.md 第 1.3 节 <c>StatHost</c>、
    /// 01_分层与依赖.md L1 模块表 <c>stat_block</c> 行）。属性 id（06 记法 <c>StatKey</c>）就是
    /// <see cref="Id"/> 本身的用法（<c>stat.&lt;name&gt;</c>），本模块不为它新造类型。
    /// <para>
    /// 06 原文只给出 <c>getStat</c>/<c>addModifier</c>/<c>removeModifiersBySource</c> 三个方法；
    /// 其余成员是任务书"补充"要求的最小必要扩展（单位注册/注销、显式设置与读取基础值、
    /// 只读列出某属性当前修正列表），均为只读查询或直接对应设计拍板的写操作，不扩大契约
    /// 语义范围。
    /// </para>
    /// </summary>
    public interface IStatHost
    {
        void RegisterUnit(Id unitId);

        void UnregisterUnit(Id unitId);

        bool IsRegistered(Id unitId);

        /// <summary>显式设置某单位某属性的基础值（未设置过时默认取
        /// <c>stat.definition.default_base</c>，见 <see cref="GetBase"/>）。若最终值因此变化，
        /// 发出一次 <c>stat.changed</c>；取值与设置前相同则不发（见 06 第 1.3 节"事件"）。</summary>
        void SetBase(Id unitId, Id stat, double value);

        /// <summary>读取某单位某属性当前的基础值（未显式 <see cref="SetBase"/> 过时返回
        /// <c>stat.definition.default_base</c>，缺省为 0）。</summary>
        double GetBase(Id unitId, Id stat);

        /// <summary>按三段式公式（06 第 1.1 节）聚合出的最终值。</summary>
        double GetStat(Id unitId, Id stat);

        void AddModifier(Id unitId, StatModifier modifier);

        /// <summary>按来源整体撤销：移除 <paramref name="unitId"/> 名下全部属性上、
        /// <see cref="StatModifier.SourceId"/> 等于 <paramref name="sourceId"/> 的修正
        /// （不局限于单一属性，见 06 第 1.2 节"移除来源时按 sourceId 整体撤销其名下全部修正"）。</summary>
        void RemoveModifiersBySource(Id unitId, Id sourceId);

        /// <summary>只读列出某单位某属性当前生效的修正（按 <see cref="AddModifier"/> 调用顺序，
        /// 即三段式聚合实际使用的插入顺序）。</summary>
        IReadOnlyList<StatModifier> GetModifiers(Id unitId, Id stat);
    }
}

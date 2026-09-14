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

        /// <summary>
        /// T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段"目标乘区"）：按 <c>stat.definition.category</c>
        /// 列出全部已注册的属性定义 id——定义层面的静态列表，不区分单位（属性是否存在只取决于内容
        /// 数据，与某个单位是否在 <see cref="IStatHost"/> 注册无关）。结算管线"目标乘区"/
        /// "被暴击减免"步骤据此遍历某一类别下的全部属性定义，逐条按 <see cref="GetScope"/> 与
        /// 结算上下文的来源类别匹配后对目标单位求和（见 <c>Core.Rules.Combat.Resolver</c>
        /// 判断记录）。返回列表按 <see cref="Id"/> 序数字符串序排序，保证遍历顺序确定——结算管线
        /// 不允许依赖字典枚举顺序（同 06 第 4.1 节"每一步只依赖上一步的输出"确定性要求）。
        /// <para>
        /// 用 C#8 默认接口方法（恒返回空列表）而不是必须实现的抽象成员：本接口已有若干模块各自的
        /// 测试假实现，本任务改动范围限定在 <c>core/numbers/stat_block</c>（真正实现）与
        /// <c>core/rules/combat</c>（消费方），不允许连带修改其余模块的测试假实现文件；默认实现
        /// 恒返回空列表（"没有任何属性登记在这个类别下"）保证既有假实现不必跟着改也能继续通过
        /// 编译——结算管线据此遍历出的集合为空，等价于"新扫描机制未生效"，不改变集成前既有行为
        /// （见 <c>Core.Rules.Combat.CombatOptions.DamageTakenCategory</c>/
        /// <c>CritTakenReductionCategory</c> 判断记录"零成本退化"一节）。真正接入内容数据的实现
        /// （本任务 <see cref="Core.Numbers.StatBlock.StatHost"/>）应 override 本方法，按加载期已
        /// 解析好的 <c>stat.definition.category</c> 返回真实列表。
        /// </para>
        /// </summary>
        IReadOnlyList<Id> GetDefinitionIdsByCategory(string category) => System.Array.Empty<Id>();

        /// <summary>
        /// T-N1-7（同上决策/章节）：该属性定义登记的作用域（<c>stat.definition.scope</c>，取值
        /// <c>any</c>/<c>from_player</c>/<c>from_creature</c>），供
        /// <see cref="GetDefinitionIdsByCategory"/> 遍历出的每条属性再按来源类别过滤。属性未在
        /// <c>stat.definition</c> 登记（内容缺失、或测试替身从未注册该属性）时返回 <c>"any"</c>——
        /// 与 <c>stat.definition.scope</c> 字段本身缺省 <c>any</c>（ADR-0030 决策 5）同一语义，不是
        /// "未知属性"的特殊标记；本接口不提供"该属性是否存在"的独立查询，只服务于本扫描/求和场景。
        /// <para>
        /// 用 C#8 默认接口方法（恒返回 <c>"any"</c>）而不是必须实现的抽象成员，理由同
        /// <see cref="GetDefinitionIdsByCategory"/>；因为默认实现下 <see cref="GetDefinitionIdsByCategory"/>
        /// 恒返回空列表，本方法在既有假实现上实际永远不会被结算管线调用到，默认值只是防御性兜底，
        /// 不影响既有行为。
        /// </para>
        /// </summary>
        string GetScope(Id stat) => "any";
    }
}

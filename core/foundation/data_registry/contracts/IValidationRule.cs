using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 模块专属校验规则的扩展点（见 04 第 5 节"效果数上限""预算超标""叠加类别冲突""循环引用
    /// 检测""孤儿记录检测"等由各内容模块自行登记的检查项；本模块——L0
    /// <c>data_registry</c>——不实现任何具体业务规则，只提供注册入口
    /// <see cref="IDataRegistry.RegisterValidationRule"/>）。实现方在 <see cref="Validate"/>
    /// 内部经 <see cref="IDataRegistryView"/> 只读查询已加载数据、产出问题列表；不得修改数据。
    /// <para>
    /// 规则元数据（分阶段落地计划 T-N0-2；落地清单 2.2 V1；04 第 5 节数值类校验项分级表）：
    /// <see cref="RuleId"/>/<see cref="DefaultSeverity"/>/<see cref="NonEscalatable"/> 三个带默认实现
    /// 的成员，让校验报告能列出"哪些规则跑过、各命中几条"（<see cref="ValidationReport.Rules"/>）、
    /// 让注册入口按 <see cref="RuleId"/> 去重、让"抓意图不抓手滑"的警告级数值规则在
    /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下也不阻断。三者均带默认实现（11 第 7 节
    /// "默认实现"路线，不破坏既有实现方的编译与 ABI）；非组合型规则沿用默认值即是正确语义，
    /// 只有组合/转发型实现（内部持有并委托给另一份 <see cref="IValidationRule"/>）须显式转发——
    /// 由 <c>InterfaceDefaultMemberForwardingTests</c> 门禁按 11 第 7 节勘误核对。
    /// </para>
    /// </summary>
    public interface IValidationRule
    {
        IEnumerable<ValidationIssue> Validate(IDataRegistryView view);

        /// <summary>规则 id：报告 <c>rules[]</c> 的键、<see cref="IDataRegistry.RegisterValidationRule"/>
        /// 去重的键、以及 <see cref="DataRegistry"/> 给本规则产出的每条 <see cref="ValidationIssue"/>
        /// 补上的 <see cref="ValidationIssue.RuleId"/>（规则自己已填时不覆盖）。默认取具体类型名
        /// （与 1.29.0 <c>OptionalRuleDescriptor.RuleName</c>"规则类型名"口径一致）。</summary>
        string RuleId => GetType().Name;

        /// <summary>默认级别：本规则产出的问题通常所处的级别，只供报告与工具展示（如问题面板按
        /// 规则分组时的默认着色），不改变每条 <see cref="ValidationIssue.Severity"/> 自身的取值。
        /// 默认 <see cref="ValidationSeverity.Error"/>（既有规则绝大多数是阻断级）。</summary>
        ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        /// <summary>不可提升：为 true 时本规则产出的 <see cref="ValidationSeverity.Warning"/> 在
        /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下也不计入阻断（仍进入报告）。用于
        /// 04 第 5 节数值类校验项分级表里的警告级——它们抓的是"意图"（技能预算比值超出带宽、装备预算
        /// 利用率过低……多数是策划有意为之），不是"手滑"，整体把警告升为阻断时不应把这组一并升上去
        /// （数值总纲第 6 节）。默认 false（既有规则的警告仍按 <see cref="DataRegistryOptions.Strictness"/>
        /// 整体判定，行为不变）。</summary>
        bool NonEscalatable => false;
    }
}

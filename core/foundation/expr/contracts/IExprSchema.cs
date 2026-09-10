using System;
using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// 静态校验用的引用登记表：登记全架构范围内哪些 <c>group.key</c> 组合是合法引用，
    /// 以及各自的返回类型与参数类型（见 04 第 5 节"表达式可解析"校验项、第 6.4 节解析期错误）。
    /// 实现方通常是某个具体宿主分组的注册表（如战斗、任务系统各自登记自己开放的 key）。
    /// </summary>
    public interface IExprSchema
    {
        bool TryGetSignature(string group, string key, out ExprSignature signature);

        /// <summary>
        /// 消费方反馈第三批第 18 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 18 条）：按分组枚举
        /// 该分组下全部已登记的 key（供编辑器等工具做自动补全/字段清单展示，不必先枚举全部
        /// group.key 组合再自行过滤）。<paramref name="group"/> 未知或没有任何已登记 key 时返回
        /// 空集合，不抛异常。带默认实现（恒返回空集合）新增——不要求已有 <see cref="IExprSchema"/>
        /// 实现方必须提供，不构成"公开 API 表面"意义上的破坏性变更；<see cref="ExprSchema"/>
        /// 显式覆盖为返回该分组全部已登记 key 的有序、确定性列表（见该类型同名成员判断记录）。
        /// </summary>
        IReadOnlyCollection<string> KnownKeys(string group) => Array.Empty<string>();

        /// <summary>
        /// 消费方反馈第三批第 18 条：本登记表已出现过至少一个已登记 key 的全部分组（有序、
        /// 确定性，与 <see cref="KnownKeys"/> 同一批判断记录）。带默认实现（恒返回空集合）新增，
        /// 同样不构成破坏性变更。
        /// </summary>
        IReadOnlyCollection<string> KnownGroups => Array.Empty<string>();
    }
}

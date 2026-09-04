using System;
using System.Collections.Generic;
using Core.Foundation.Expr;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// 供多个模块共享同一个 <c>player</c>（或任意其它）Expr 引用分组时使用的链式包装（见
    /// <see cref="EconomyExprSchemaEntries"/> 类型注释"链式包装"选项）：按传入顺序依次尝试每个
    /// <see cref="IExprGroupProvider"/>，某个 provider 对某个 <c>key</c> 不认识时约定抛
    /// <see cref="KeyNotFoundException"/>，本类型捕获后继续尝试下一个；全部尝试完仍未命中时，记一条
    /// 警告并返回 <see cref="ExprValue.OfBool(bool)"/>（false）——同
    /// <c>core/rules/expr_host.RulesExprHostFactory</c>"未知 key 按默认值处理"的惯例，不向
    /// <c>ExprEvaluator</c> 抛出（04 第 6.4 节"宿主 Query 抛异常……整个表达式判定为 false"，但那是
    /// 意料之外的运行期异常；这里"未知 key"是可预期分支，不应该让整条表达式求值失败，只让这一个引用
    /// 退化为默认值）。
    /// </summary>
    public sealed class ChainedExprGroupProvider : IExprGroupProvider
    {
        private readonly IReadOnlyList<IExprGroupProvider> _providers;
        private readonly IExprDiagnostics? _diagnostics;

        public ChainedExprGroupProvider(IReadOnlyList<IExprGroupProvider> providers, IExprDiagnostics? diagnostics = null)
        {
            if (providers == null || providers.Count == 0)
            {
                throw new ArgumentException("至少需要一个 IExprGroupProvider", nameof(providers));
            }

            _providers = providers;
            _diagnostics = diagnostics;
        }

        public ExprValue Query(string key, IReadOnlyList<ExprValue> args)
        {
            for (var i = 0; i < _providers.Count; i++)
            {
                try
                {
                    return _providers[i].Query(key, args);
                }
                catch (KeyNotFoundException)
                {
                    // 约定：某 provider 不认识该 key 时抛 KeyNotFoundException，链继续尝试下一个
                    // （见类型注释）。
                }
            }

            _diagnostics?.Warn($"ChainedExprGroupProvider：链上没有任何 provider 认识 key \"{key}\"，按默认值 Bool(false) 处理");
            return ExprValue.OfBool(false);
        }
    }
}

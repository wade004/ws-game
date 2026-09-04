using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>target.*</c> 路径的解答者（见任务书路径小语法 "target.&lt;...&gt;"）。09_表现层.md 第 7.1
    /// 节把"目标框"列为 UI 组成之一，目标框典型只需要生命/资源条与关键属性——本 Provider 因此支持
    /// <c>target.power.&lt;powerType&gt;.current|max</c>、<c>target.stat.&lt;statId&gt;</c> 两条子路径
    /// （与 <c>player.*</c> 同款语法，见 <see cref="UnitSubQueries"/>），不支持 player 专有的
    /// inventory/equipment/quest/currency/skills 系列（当前目标不是玩家自己的容器）。
    /// <para>
    /// "当前目标是谁"由构造期注入的 <paramref name="targetResolver"/> 委托给出（见任务书
    /// "target.&lt;...&gt;（当前目标经注入的 Func&lt;Id?&gt; targetResolver）"）——本 Provider 不
    /// 自己维护目标状态，目标选择/切换属于玩法层或表现层其它模块（如战斗目标锁定）的职责。
    /// </para>
    /// </summary>
    public sealed class TargetPathProvider : IUiPathProvider
    {
        private readonly Func<Id?> _targetResolver;
        private readonly IStatHost _statHost;
        private readonly IPowerHost _powerHost;

        public TargetPathProvider(Func<Id?> targetResolver, IStatHost statHost, IPowerHost powerHost)
        {
            _targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
        }

        public string Root => "target";

        public ExprValue? Resolve(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            var targetId = _targetResolver();
            if (!targetId.HasValue)
            {
                // 当前无目标：合法查询、无值，不是路径错误，不记诊断。
                return null;
            }

            if (remaining.Count == 0)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 在 \"target\" 之后缺少子路径");
                return null;
            }

            switch (remaining[0].Name)
            {
                case "power":
                    return UnitSubQueries.Power(targetId.Value, _powerHost, remaining, fullPath, diagnostics);
                case "stat":
                    return UnitSubQueries.Stat(targetId.Value, _statHost, remaining, fullPath, diagnostics);
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 target 子路径关键字 \"{remaining[0].Name}\" 未知（只支持 power/stat）");
                    return null;
            }
        }
    }
}

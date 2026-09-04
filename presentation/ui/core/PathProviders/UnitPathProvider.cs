using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>unit.&lt;id&gt;.*</c> 路径的解答者（见任务书路径小语法
    /// "unit.&lt;id&gt;.power.&lt;type&gt;.current"）——按任意运行期实体 id 查询，供世界内单位血条
    /// （非玩家、非当前目标，如小队队友、可交互 NPC 头顶血条一类场景）复用与 <c>player.*</c>/
    /// <c>target.*</c> 相同的 power/stat 子语法。
    /// <para>
    /// 判断记录（单位 id 边界的消歧）：<c>unit.&lt;id&gt;.power.&lt;type&gt;.current</c> 里
    /// <c>&lt;id&gt;</c>（运行期实体 id，见 <c>IWorldSim.AllocateEntityId</c> "&lt;kind&gt;.inst_&lt;n&gt;"
    /// 惯例）与 <c>&lt;type&gt;</c>（资源类型 id，如 <c>arch.power.health</c>）本身都可能含多段
    /// 点分——与 <see cref="UnitSubQueries"/> 用"固定关键字位置"消歧不同，这里两段变长 Id 相邻，
    /// 无法从两端同时反推。本类按"从 <c>unit</c> 之后第一次出现字面量 <c>power</c> 或 <c>stat</c>
    /// （且不带下标）的位置"切分：该关键字之前的全部段拼成 unitId，之后复用
    /// <see cref="UnitSubQueries"/> 的既有逻辑。多数运行期单位 id 形如 <c>unit.inst_3</c>、内容
    /// 模板挂载的固定单位 id 形如 <c>unit.hero</c>，均不含 "power"/"stat" 字面量段，本策略在
    /// 现有命名惯例下是安全的；若未来出现名字恰好含 "power"/"stat" 段的单位 id，调用方需要避免
    /// 通过本路径查询它（属于已知限制，见本模块 README）。
    /// </para>
    /// </summary>
    public sealed class UnitPathProvider : IUiPathProvider
    {
        private readonly IStatHost _statHost;
        private readonly IPowerHost _powerHost;

        public UnitPathProvider(IStatHost statHost, IPowerHost powerHost)
        {
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
        }

        public string Root => "unit";

        public ExprValue? Resolve(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            var keywordIndex = -1;
            for (var i = 0; i < remaining.Count; i++)
            {
                if (!remaining[i].Index.HasValue && (remaining[i].Name == "power" || remaining[i].Name == "stat"))
                {
                    keywordIndex = i;
                    break;
                }
            }

            if (keywordIndex <= 0)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 找不到 unit.<id>.power|stat 的关键字分界，或缺少单位 id");
                return null;
            }

            if (!UnitSubQueries.TryBuildId(remaining.Take(keywordIndex), out var unitId))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的单位 id 片段不是合法 Id");
                return null;
            }

            var rest = remaining.Skip(keywordIndex).ToList();
            return remaining[keywordIndex].Name == "power"
                ? UnitSubQueries.Power(unitId, _powerHost, rest, fullPath, diagnostics)
                : UnitSubQueries.Stat(unitId, _statHost, rest, fullPath, diagnostics);
        }
    }
}

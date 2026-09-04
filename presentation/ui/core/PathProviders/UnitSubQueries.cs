using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>&lt;root&gt;.power.&lt;powerType&gt;.current|max</c>、<c>&lt;root&gt;.stat.&lt;statId&gt;</c>
    /// 两条子路径的共享解析逻辑（见任务书路径小语法），供 <see cref="PlayerPathProvider"/>、
    /// <see cref="TargetPathProvider"/>、<see cref="UnitPathProvider"/> 三者复用——三者唯一的差异
    /// 是"unitId 从哪里来"（玩家自身 id / 当前目标解析结果 / 路径里显式携带的 id），子路径语法本身
    /// 完全一致。
    /// </summary>
    internal static class UnitSubQueries
    {
        /// <summary>把一串路径段的 <see cref="UiPathSegment.Name"/> 拼成一个 <see cref="Id"/>；任一段
        /// 携带方括号下标、或拼接结果不是合法 <see cref="Id"/> 格式时返回 false（该 Id 片段本就应该
        /// 是纯粹的点分名字，不应该出现数组下标）。</summary>
        public static bool TryBuildId(IEnumerable<UiPathSegment> segments, out Id id)
        {
            var list = segments as IReadOnlyList<UiPathSegment> ?? segments.ToList();
            if (list.Count == 0 || list.Any(s => s.Index.HasValue))
            {
                id = default;
                return false;
            }

            var joined = string.Join(".", list.Select(s => s.Name));
            return Id.TryParse(joined, out id);
        }

        /// <summary><paramref name="remaining"/>[0] 必须是字面量 <c>"power"</c>（不带下标）；形状为
        /// <c>power.&lt;idSegs...&gt;.(current|max)</c>。</summary>
        public static ExprValue? Power(
            Id unitId,
            IPowerHost powerHost,
            IReadOnlyList<UiPathSegment> remaining,
            string fullPath,
            IUiDiagnostics diagnostics)
        {
            if (remaining.Count < 3)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 power 子路径段数不足（需要 power.<id...>.current|max）");
                return null;
            }

            var suffix = remaining[remaining.Count - 1];
            if (suffix.Index.HasValue || (suffix.Name != "current" && suffix.Name != "max"))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 power 子路径末段必须是 current 或 max，实际 \"{suffix}\"");
                return null;
            }

            var idSegs = remaining.Skip(1).Take(remaining.Count - 2);
            if (!TryBuildId(idSegs, out var powerType))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 power 类型片段不是合法 Id");
                return null;
            }

            if (!powerHost.HasPower(unitId, powerType))
            {
                return null;
            }

            var value = suffix.Name == "current" ? powerHost.GetPower(unitId, powerType) : powerHost.GetPowerMax(unitId, powerType);
            return ExprValue.OfNumber(value);
        }

        /// <summary><paramref name="remaining"/>[0] 必须是字面量 <c>"stat"</c>；形状为
        /// <c>stat.&lt;idSegs...&gt;</c>（无固定后缀，剩余全部段拼成属性 id）。</summary>
        public static ExprValue? Stat(
            Id unitId,
            IStatHost statHost,
            IReadOnlyList<UiPathSegment> remaining,
            string fullPath,
            IUiDiagnostics diagnostics)
        {
            if (remaining.Count < 2)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 stat 子路径缺少属性 id");
                return null;
            }

            if (!TryBuildId(remaining.Skip(1), out var statId))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的属性片段不是合法 Id");
                return null;
            }

            if (!statHost.IsRegistered(unitId))
            {
                return null;
            }

            try
            {
                return ExprValue.OfNumber(statHost.GetStat(unitId, statId));
            }
            catch (System.ArgumentException)
            {
                // 属性 id 未在 stat.definition 登记：语法合法但引用了不存在的属性，视为"查不到"，
                // 记一条诊断（区别于"合法查询、当前无值"——这是内容配置问题，值得留痕）。
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 引用的属性 \"{statId}\" 未登记");
                return null;
            }
        }
    }
}

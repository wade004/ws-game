using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>&lt;root&gt;.power.&lt;powerType&gt;.current|max</c>、<c>&lt;root&gt;.stat.&lt;statId&gt;</c>
    /// 两条子路径的共享解析逻辑（见任务书路径小语法），供 <see cref="PlayerPathProvider"/>、
    /// <see cref="TargetPathProvider"/>、<see cref="UnitPathProvider"/> 三者复用——三者唯一的差异
    /// 是"unitId 从哪里来"（玩家自身 id / 当前目标解析结果 / 路径里显式携带的 id），子路径语法本身
    /// 完全一致。
    /// <para>
    /// 消费方反馈第 1/4 条（2026-09-21，ADR-0056）：新增 <see cref="Casting"/>/<see cref="Auras"/>
    /// 两条共享解析逻辑，供 <see cref="PlayerPathProvider"/>/<see cref="TargetPathProvider"/> 复用
    /// （<see cref="UnitPathProvider"/> 面向"路径里显式携带的任意单位 id"，不装配施法/光环查询依赖，
    /// 不复用这两条——同 <c>power</c>/<c>stat</c> 两条既有子路径三者皆可复用不同，本次两条新子路径
    /// 只有前两者需要）。
    /// </para>
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

        /// <summary>
        /// 消费方反馈第 1 条（2026-09-21，ADR-0056）：<paramref name="remaining"/>[0] 必须是字面量
        /// <c>"casting"</c>（不带下标）；形状为 <c>casting.(skill|remaining|total)</c>。
        /// <paramref name="skillBook"/> 为 <c>null</c> 表示调用方未装配施法查询能力（同
        /// <c>TargetPathProvider._unitAccess</c> 既有惯例：可选能力未装配时静默返回"无"，不记诊断
        /// ——这是部署选择，不是数据缺口）。
        /// <para>
        /// 判断记录（"未在读条"与"读条中但取不到剩余/总时长"两种 null 的区分，AGENTS.md §3"运行时
        /// 路径不静默降级"）：<c>casting.skill</c> 为空即"当前无人读条"，同 <c>target.id</c> 无目标
        /// 时的既有口径，不记诊断；但一旦 <c>GetCastingSkillId</c> 返回非空（确认正在读条），
        /// <c>remaining</c>/<c>total</c> 仍取不到就是数据不一致（目标存在且确认在读条，但取不到
        /// 数据），记一条诊断后返回"无"，不让调用方把这种情况误当成"当前没有人在读条"。
        /// </para>
        /// </summary>
        public static ExprValue? Casting(
            Id unitId,
            ISkillBookQuery? skillBook,
            IReadOnlyList<UiPathSegment> remaining,
            string fullPath,
            IUiDiagnostics diagnostics)
        {
            if (skillBook == null)
            {
                return null;
            }

            if (remaining.Count != 2 || remaining[1].Index.HasValue)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 casting 子路径必须形如 \"casting.skill|remaining|total\"");
                return null;
            }

            var castingSkillId = skillBook.GetCastingSkillId(unitId);

            switch (remaining[1].Name)
            {
                case "skill":
                    return castingSkillId.HasValue ? ExprValue.OfId(castingSkillId.Value) : (ExprValue?)null;
                case "remaining":
                    return ResolveCastingTiming(unitId, castingSkillId, skillBook.GetCastingRemaining(unitId), fullPath, diagnostics);
                case "total":
                    return ResolveCastingTiming(unitId, castingSkillId, skillBook.GetCastingTotal(unitId), fullPath, diagnostics);
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 casting 子路径关键字 \"{remaining[1].Name}\" 未知（只支持 skill/remaining/total）");
                    return null;
            }
        }

        private static ExprValue? ResolveCastingTiming(Id unitId, Id? castingSkillId, double? value, string fullPath, IUiDiagnostics diagnostics)
        {
            if (!castingSkillId.HasValue)
            {
                // 当前未读条：合法查询、无值，同"当前无目标"既有惯例，不记诊断。
                return null;
            }

            if (!value.HasValue)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的单位 \"{unitId}\" 正在读条（技能 \"{castingSkillId.Value}\"），但取不到剩余/总时长——技能宿主适配层未完整支持读条查询");
                return null;
            }

            return ExprValue.OfNumber(value.Value);
        }

        /// <summary>
        /// 消费方反馈第 4 条（2026-09-21，ADR-0056）：<paramref name="remaining"/>[0] 必须是字面量
        /// <c>"auras"</c>；形状为 <c>auras.count</c> 或 <c>auras[i].(def|stacks|remaining|total|
        /// name_key|polarity|icon_ref)</c>（惯例同既有 <c>player.inventory[i].&lt;field&gt;</c>；
        /// <c>polarity</c>/<c>icon_ref</c> 为 ADR-0060 一个发现的交付缺口新增）。<paramref
        /// name="auraQuery"/> 为 <c>null</c> 表示调用方未装配光环查询能力，惯例同 <see
        /// cref="Casting"/> 判断记录。
        /// </summary>
        public static ExprValue? Auras(
            Id unitId,
            IAuraQuery? auraQuery,
            IReadOnlyList<UiPathSegment> remaining,
            string fullPath,
            IUiDiagnostics diagnostics)
        {
            if (auraQuery == null)
            {
                return null;
            }

            var head = remaining[0];
            if (!head.Index.HasValue)
            {
                if (remaining.Count == 2 && remaining[1].Name == "count" && !remaining[1].Index.HasValue)
                {
                    return ExprValue.OfInt(auraQuery.GetActiveAuraSnapshots(unitId).Count);
                }

                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 auras 子路径只认识 \"auras.count\" 或 \"auras[i].<field>\"");
                return null;
            }

            if (remaining.Count != 2)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 auras[i] 子路径缺少字段名");
                return null;
            }

            var list = auraQuery.GetActiveAuraSnapshots(unitId);
            var idx = head.Index.Value;
            if (idx < 0 || idx >= list.Count)
            {
                return null;
            }

            var snap = list[idx];
            switch (remaining[1].Name)
            {
                case "def":
                    return ExprValue.OfId(snap.AuraDefId);
                case "stacks":
                    return ExprValue.OfInt(snap.Stacks);
                case "remaining":
                    return snap.Remaining.HasValue ? ExprValue.OfNumber(snap.Remaining.Value) : (ExprValue?)null;
                case "total":
                    return snap.Total.HasValue ? ExprValue.OfNumber(snap.Total.Value) : (ExprValue?)null;
                case "name_key":
                    return snap.NameKey.HasValue ? ExprValue.OfId(snap.NameKey.Value) : (ExprValue?)null;
                // 一个发现的交付缺口（2026-09-21，ADR-0060）：polarity/icon_ref 两条子路径，惯例同
                // 上方 name_key——未声明时静默返回"无"，不记诊断（字段本身可选，惯例同 ADR-0056）。
                // 判断记录（polarity 这一层仍输出字符串，不是原样透传 snap.Polarity 这个枚举）：
                // Expr/UI 路径小语法当前没有枚举这一等级的值类型（见 Core.Foundation.Expr.ExprValue
                // 判别联合 Bool|Int|Number|String|Id），本仓库既有做法是"枚举在跨越到这一层时降级
                // 为它在数据表上对应的 snake_case 文本"（同 target.casting.skill 等既有路径只搬运
                // Bool/Int/Number/String/Id 五种之一，不新增值类型）；因此这里用 AuraPolarityNames.
                // ToText 把规则层已经解析好的强类型枚举转回文本，而不是让规则层重新透出裸字符串
                // （字符串仍是"表现层查询的值类型"，不是"规则层对外的存储类型"，两者不矛盾——规则层
                // 内部与快照仍是结构化枚举，接入方拼字面量比较错了在规则层就会被类型系统挡住；只有
                // 到了这条通用小语法的边界，才不得不退化为字符串）。Undeclared 没有对应文本，同
                // "未声明"既有口径静默返回"无"，不调用 ToText（避免其判断记录里那条"Undeclared 不
                // 参与互转"的异常在这条路径上被触发）。
                case "polarity":
                    return snap.Polarity != AuraPolarity.Undeclared
                        ? ExprValue.OfString(AuraPolarityNames.ToText(snap.Polarity))
                        : (ExprValue?)null;
                case "icon_ref":
                    return snap.IconRef.HasValue ? ExprValue.OfId(snap.IconRef.Value) : (ExprValue?)null;
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 auras[i] 字段名 \"{remaining[1].Name}\" 未知（只支持 def/stacks/remaining/total/name_key/polarity/icon_ref）");
                    return null;
            }
        }
    }
}

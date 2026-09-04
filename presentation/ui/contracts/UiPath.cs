using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Presentation.Ui
{
    /// <summary>
    /// 一个解析后的路径段：<c>name</c> 部分（如 <c>"power"</c>、<c>"inventory"</c>）加一个可选的
    /// 数组下标（如 <c>"inventory[0]"</c> 中的 <c>0</c>，见 09_表现层.md 第 7.2 节
    /// <c>UiDataSource.query(path)</c>、任务书路径小语法
    /// <c>player.inventory[i].template|count|instance</c>）。
    /// </summary>
    public readonly struct UiPathSegment
    {
        public string Name { get; }

        public int? Index { get; }

        public UiPathSegment(string name, int? index)
        {
            Name = name;
            Index = index;
        }

        public override string ToString() => Index.HasValue ? $"{Name}[{Index.Value}]" : Name;
    }

    /// <summary>
    /// 把 <see cref="IUiDataSource.Query"/> 收到的路径字符串（点分小语法，见任务书路径清单）切分成
    /// <see cref="UiPathSegment"/> 序列。本解析器只做"按 '.' 切分 + 识别 '[idx]' 下标"这一层语法，
    /// 不理解任何具体业务含义——业务含义（哪一段是关键字、哪几段拼起来是一个 <c>Id</c>）由
    /// <see cref="IUiPathProvider"/> 各自解释（见该接口注释"由注册的 IUiPathProvider 按首段分派
    /// 回答"）。判断记录：路径里出现的逻辑 Id（如 <c>skill.fireball</c>、
    /// <c>quest.find_the_missing_child</c>）本身也含 '.'，与路径分隔符同一个字符，本解析器不试图
    /// 消歧——它只管切分，消歧（"从哪到哪是一个 Id"）留给各 Provider 按自己的关键字位置重新拼接
    /// （见 <c>core/PathProviders/UnitSubQueries.cs</c> 的 <c>TryBuildId</c>）。
    /// </summary>
    public static class UiPathParser
    {
        private static readonly Regex SegmentRegex = new Regex(
            "^([a-z][a-z0-9_]*)(\\[(\\d+)\\])?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>解析失败（空串、空段、段格式非法）返回 false，<paramref name="segments"/> 为空列表。</summary>
        public static bool TryParse(string? path, out IReadOnlyList<UiPathSegment> segments)
        {
            segments = System.Array.Empty<UiPathSegment>();
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var parts = path.Split('.');
            var list = new List<UiPathSegment>(parts.Length);
            foreach (var part in parts)
            {
                var m = SegmentRegex.Match(part);
                if (!m.Success)
                {
                    return false;
                }

                var name = m.Groups[1].Value;
                int? index = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : (int?)null;
                list.Add(new UiPathSegment(name, index));
            }

            segments = list;
            return true;
        }
    }

    /// <summary>
    /// 按路径首段分派的查询回答者（见 09_表现层.md 第 7.2 节、任务书
    /// "解析成 UiPath 段树，由注册的 IUiPathProvider（按首段分派）回答"）。<see cref="UiDataSource"/>
    /// 按 <see cref="Root"/>（如 <c>"player"</c>/<c>"target"</c>/<c>"unit"</c>）把整条路径的第一段
    /// 路由给对应 Provider，自身其余段交给 <see cref="Resolve"/> 解释。
    /// </summary>
    public interface IUiPathProvider
    {
        /// <summary>本 Provider 认领的路径首段字面量，如 <c>"player"</c>。</summary>
        string Root { get; }

        /// <summary>
        /// 解释路径首段之后的剩余段。返回 null 且不必调用 <paramref name="diagnostics"/> 表示
        /// "路径语法合法、语义上查得到但当前无值"（如未选中目标、槽位为空、下标越界——这些是正常
        /// 查询结果，不是错误）；路径语法不认识/不完整/引用了格式非法的 Id 时返回 null 且调用
        /// <c>diagnostics.Warn(...)</c> 记一条诊断（见 <see cref="IUiDiagnostics"/>）。
        /// </summary>
        Core.Foundation.Expr.ExprValue? Resolve(
            IReadOnlyList<UiPathSegment> remaining,
            string fullPath,
            IUiDiagnostics diagnostics);
    }
}

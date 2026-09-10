namespace Toolchain.AbiSurface
{
    internal sealed class AllowlistEntry
    {
        public string Signature { get; init; } = "";
        public string Reason { get; init; } = "";
        public string AdrId { get; init; } = "";
    }

    internal sealed class BreakItem
    {
        public string Line { get; init; } = "";
        public string Category { get; init; } = "";
        public string Detail { get; init; } = "";
    }

    internal sealed class CompareResult
    {
        public List<BreakItem> Breaks { get; } = new List<BreakItem>();
        public List<BreakItem> Allowed { get; } = new List<BreakItem>();
        public List<string> Additions { get; } = new List<string>();
        public bool HasBreaks => Breaks.Count > 0;
    }

    /// <summary>
    /// 判定 baseline ⊆ current（<see cref="SurfaceDumper"/> 输出的排序文本行集合），外加接口新增
    /// abstract 成员的专项规则（见 <see cref="ComparerImpl.Compare"/> 判断记录）。
    /// </summary>
    internal static class SurfaceCompareLogic
    {
        public static CompareResult Compare(IReadOnlyList<string> baselineLines, IReadOnlyList<string> currentLines, IReadOnlyList<AllowlistEntry> allowlist)
        {
            var result = new CompareResult();
            var currentSet = new HashSet<string>(currentLines, StringComparer.Ordinal);
            var baselineSet = new HashSet<string>(baselineLines, StringComparer.Ordinal);
            var allowlistBySignature = new Dictionary<string, AllowlistEntry>(StringComparer.Ordinal);
            foreach (var entry in allowlist)
            {
                allowlistBySignature[entry.Signature] = entry;
            }

            // 规则 1：baseline 存在、current 缺失的行——不管是整条 TYPE 行（类型被删）还是单条
            // MEMBER 行（成员被删/改签名，改签名在这套"整行即签名"的表示下等价于"旧行消失+可能新增
            // 一条新行"，旧行消失已经足以判定为破坏，不需要额外分辨"删除"和"改动"）。
            foreach (var line in baselineLines)
            {
                if (currentSet.Contains(line)) continue;
                if (allowlistBySignature.TryGetValue(line, out var allowed))
                {
                    result.Allowed.Add(new BreakItem { Line = line, Category = "removed_or_changed", Detail = allowed.Reason + "（ADR " + allowed.AdrId + "）" });
                    continue;
                }
                result.Breaks.Add(new BreakItem { Line = line, Category = "removed_or_changed", Detail = "baseline 存在的公开签名在 current 中缺失（被删除，或参数/返回类型/可见性变化导致物理签名不同）" });
            }

            // 规则 2：baseline 里已存在的接口，current 新增了 abstract 成员——实现方（在 baseline
            // 时点就已经实现了该接口的既有 consumer 源码）不会自动获得新成员的实现，重新编译会报
            // "未实现接口成员"，是源码兼容破坏（不是二进制破坏，但同属"未声明的破坏性变更"，与
            // PJ114-01/02 同一批治理目标一致，见任务书 A2 额外规则）。
            var baselineInterfaceTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in baselineLines)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 3 && parts[0] == "TYPE" && parts[2] == "interface")
                {
                    baselineInterfaceTypes.Add(parts[1]);
                }
            }
            foreach (var line in currentLines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 5 || parts[0] != "MEMBER") continue;
                var declaringType = parts[1];
                if (!baselineInterfaceTypes.Contains(declaringType)) continue;
                var kind = parts[2];
                if (kind != "method" && kind != "property") continue;
                var flags = parts[4];
                // method 行的 abstract 标记见 SurfaceDumper.MethodFlags；property 行没有单独的
                // method 子行（get_/set_ 访问器被 IsSpecialName 过滤掉了，见该处判断记录），abstract
                // 标记改记在 property 行 flags 末尾的 ",abstract"（SurfaceDumper 属性分支）。两种
                // kind 分开判定，不会重复计数同一处新增。
                bool isAbstract = flags.Split(',').Contains("abstract");
                if (baselineSet.Contains(line)) continue; // 只关心新增的（baseline 没有的）成员。
                if (!isAbstract) continue;

                var breakLine = string.Join("\t", "TYPE", declaringType, "interface", "(既有接口新增 abstract 成员)") + " -> " + line;
                if (allowlistBySignature.TryGetValue(line, out var allowedIface))
                {
                    result.Allowed.Add(new BreakItem { Line = line, Category = "interface_new_abstract_member", Detail = allowedIface.Reason + "（ADR " + allowedIface.AdrId + "）" });
                    continue;
                }
                result.Breaks.Add(new BreakItem { Line = line, Category = "interface_new_abstract_member", Detail = "既有接口 " + declaringType + " 新增了 abstract 成员，没有默认实现——已有实现方（旧 consumer）不会自动获得实现，重新编译会报未实现接口成员" });
            }

            foreach (var line in currentLines)
            {
                if (!baselineSet.Contains(line)) result.Additions.Add(line);
            }

            return result;
        }

        public static List<AllowlistEntry> ParseAllowlist(IEnumerable<string> lines)
        {
            var result = new List<AllowlistEntry>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                result.Add(new AllowlistEntry
                {
                    Signature = parts[0].Trim(),
                    Reason = parts[1].Trim(),
                    AdrId = parts[2].Trim(),
                });
            }
            return result;
        }
    }
}

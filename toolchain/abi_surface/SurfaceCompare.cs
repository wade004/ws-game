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
        // ABI-116-01 根治：可见性放宽（例如 protected -> public）不是破坏，但也不该悄悄混进
        // Additions（那是"新签名"的语义）——单独记一类，供报告展示、不影响 exit code。
        public List<BreakItem> Widened { get; } = new List<BreakItem>();
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

            // ABI-116-01 根治（codex 第十六轮，audit-24a11fe-20260910）：可见性放宽豁免——
            // SurfaceDumper 现在把可见性编进了 TYPE/MEMBER 行（TYPE 与 ctor/method/field/event 的
            // flags 首 token，property 仍是既有的 get:X,set:Y），这让"整行即签名"规则 1 能自动抓到
            // 可见性收窄（旧行因为可见性词变了而消失，判破坏，见下方规则 1）；但同一套机制会误伤
            // 可见性放宽（protected -> public 时旧的 "...protected..." 行同样会消失）——放宽不算破坏
            // （调用方能力只增不减），必须单独识别、从 Breaks 里剔除。做法：为每条"baseline 有、
            // current 没有"的行算出"身份"（去掉可见性 token 后的其余部分）与它的可见性，再看
            // current-only 的行里有没有同身份、可见性更宽的——有就是放宽，不计破坏；其余（可见性
            // 收窄、或身份本身就变了，比如参数/返回值/virtual/静态/约束/常量值变化）保持规则 1 原有
            // 判破坏逻辑不变。
            var currentOnlyLines = new List<string>();
            foreach (var line in currentLines)
            {
                if (!baselineSet.Contains(line)) currentOnlyLines.Add(line);
            }
            var currentOnlyByIdentity = new Dictionary<string, List<(string Line, string Vis)>>(StringComparer.Ordinal);
            var currentOnlyPropsByIdentity = new Dictionary<string, (string Line, string GetVis, string SetVis)>(StringComparer.Ordinal);
            foreach (var cLine in currentOnlyLines)
            {
                var parsed = SplitVisibility(cLine);
                if (parsed.Vis != null)
                {
                    if (!currentOnlyByIdentity.TryGetValue(parsed.Identity, out var list))
                    {
                        list = new List<(string, string)>();
                        currentOnlyByIdentity[parsed.Identity] = list;
                    }
                    list.Add((cLine, parsed.Vis));
                    continue;
                }
                var propParsed = SplitPropertyIdentity(cLine);
                if (propParsed.Identity != null)
                {
                    currentOnlyPropsByIdentity[propParsed.Identity] = (cLine, propParsed.GetVis!, propParsed.SetVis!);
                }
            }

            // 规则 1：baseline 存在、current 缺失的行——不管是整条 TYPE 行（类型被删）还是单条
            // MEMBER 行（成员被删/改签名，改签名在这套"整行即签名"的表示下等价于"旧行消失+可能新增
            // 一条新行"，旧行消失已经足以判定为破坏，不需要额外分辨"删除"和"改动"）。
            foreach (var line in baselineLines)
            {
                if (currentSet.Contains(line)) continue;

                var parsed = SplitVisibility(line);
                if (parsed.Vis != null && currentOnlyByIdentity.TryGetValue(parsed.Identity, out var candidates))
                {
                    var baseRank = VisRank(parsed.Vis);
                    var widerCandidate = candidates.FirstOrDefault(c => VisRank(c.Vis) > baseRank);
                    if (widerCandidate.Line != null)
                    {
                        result.Widened.Add(new BreakItem { Line = line, Category = "visibility_widened", Detail = "可见性从 " + parsed.Vis + " 放宽为 " + widerCandidate.Vis + "（" + widerCandidate.Line + "），不计入破坏" });
                        continue;
                    }
                }
                else
                {
                    var propParsed = SplitPropertyIdentity(line);
                    if (propParsed.Identity != null && currentOnlyPropsByIdentity.TryGetValue(propParsed.Identity, out var propCandidate))
                    {
                        bool getNotNarrower = VisRank(propCandidate.GetVis) >= VisRank(propParsed.GetVis!);
                        bool setNotNarrower = VisRank(propCandidate.SetVis) >= VisRank(propParsed.SetVis!);
                        bool strictlyWider = VisRank(propCandidate.GetVis) > VisRank(propParsed.GetVis!) || VisRank(propCandidate.SetVis) > VisRank(propParsed.SetVis!);
                        if (getNotNarrower && setNotNarrower && strictlyWider)
                        {
                            result.Widened.Add(new BreakItem { Line = line, Category = "visibility_widened", Detail = "属性 get/set 可见性放宽为 " + propCandidate.Line + "，不计入破坏" });
                            continue;
                        }
                    }
                }

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

        private static readonly HashSet<string> KnownVisTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "protected", "protected-internal", "nested-public", "nested-protected", "nested-protected-internal",
        };

        private static readonly Dictionary<string, int> VisibilityRank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["public"] = 3,
            ["nested-public"] = 3,
            ["protected-internal"] = 2,
            ["nested-protected-internal"] = 2,
            ["protected"] = 1,
            ["nested-protected"] = 1,
            ["none"] = 0,
        };

        private static int VisRank(string? vis) => vis != null && VisibilityRank.TryGetValue(vis, out var r) ? r : 0;

        /// <summary>
        /// 把一条 TYPE 或 ctor/method/field/event 的 MEMBER 行拆成"身份"（去掉可见性 token 后的
        /// 剩余部分，仍按 tab 拼回、可整体比较）与"可见性" token——TYPE 行可见性是 flags 的首
        /// token（<see cref="SurfaceDumper"/>.DumpType 判断记录），ctor/method/field/event 的
        /// MEMBER 行同理（见该类 MethodFlags/字段/事件分支判断记录）。property 走单独的
        /// <see cref="SplitPropertyIdentity"/>（get/set 各自独立可见性，不是单一 token）。
        /// 无法识别（不是 TYPE/MEMBER、kind=property、或首 flag 不是已知可见性词汇——例如本文件
        /// 测试里手写的旧格式行 "MEMBER\t...\t-"）时 Vis 返回 null，调用方据此退化为规则 1 原有的
        /// 整行 diff，不做放宽豁免（向后兼容手写测试数据与尚未升级格式的输入）。
        /// </summary>
        private static (string Identity, string? Vis) SplitVisibility(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 0) return (line, null);
            if (parts[0] == "TYPE" && parts.Length >= 4)
            {
                var tokens = parts[3] == "-" ? Array.Empty<string>() : parts[3].Split(',');
                if (tokens.Length == 0 || !KnownVisTokens.Contains(tokens[0])) return (line, null);
                var rest = tokens.Length > 1 ? string.Join(",", tokens.Skip(1)) : "-";
                return (string.Join("\t", parts[0], parts[1], parts[2], rest), tokens[0]);
            }
            if (parts[0] == "MEMBER" && parts.Length >= 5 && parts[2] != "property")
            {
                var tokens = parts[4] == "-" ? Array.Empty<string>() : parts[4].Split(',');
                if (tokens.Length == 0 || !KnownVisTokens.Contains(tokens[0])) return (line, null);
                var rest = tokens.Length > 1 ? string.Join(",", tokens.Skip(1)) : "-";
                return (string.Join("\t", parts[0], parts[1], parts[2], parts[3], rest), tokens[0]);
            }
            return (line, null);
        }

        /// <summary>
        /// property 行专用：sig（<c>parts[3]</c>）本来就不含可见性，可见性打包在 flags 里的
        /// <c>get:VIS,set:VIS[,static][,abstract|virtual|sealed-override]</c>（见 SurfaceDumper
        /// 属性分支判断记录）——"身份"是 type+kind+sig+其余非可见性 token（static/abstract/
        /// virtual/sealed-override 的并集文本，与 method 行 SplitVisibility 的"去掉可见性 token
        /// 后其余部分参与身份比较"同一口径，TOOL-118-ABI 根治：以前这里的身份只保留 abstract，
        /// 不含 static/virtual/sealed-override，会让"属性从实例变 static 同时可见性放宽"这种
        /// 复合变化被误判成纯可见性放宽而放过——现在这些 token 变化会让身份不同，直接走规则 1
        /// 的"行消失即破坏"判定，不再经过放宽豁免），get/set 可见性各自返回供调用方分别比较。
        /// </summary>
        private static (string? Identity, string? GetVis, string? SetVis) SplitPropertyIdentity(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length < 5 || parts[0] != "MEMBER" || parts[2] != "property") return (null, null, null);
            string getVis = "none", setVis = "none";
            var restTokens = new List<string>();
            foreach (var tok in parts[4].Split(','))
            {
                if (tok.StartsWith("get:", StringComparison.Ordinal)) getVis = tok.Substring(4);
                else if (tok.StartsWith("set:", StringComparison.Ordinal)) setVis = tok.Substring(4);
                else restTokens.Add(tok);
            }
            var rest = restTokens.Count > 0 ? string.Join(",", restTokens) : "-";
            var identity = string.Join("\t", parts[0], parts[1], parts[2], parts[3], rest);
            return (identity, getVis, setVis);
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

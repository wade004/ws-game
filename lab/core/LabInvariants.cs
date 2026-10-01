using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>一条跨格子不变量的一次检查结果（一个脚本 × 一组被比较的格子/变体）。</summary>
    public sealed class InvariantResult
    {
        public string Invariant { get; }

        public string Script { get; }

        /// <summary>被比较的格子/变体描述，如 <c>2d_targeted = 2_5d_targeted = 3d_targeted</c>。</summary>
        public string Subject { get; }

        public bool Ok => Differences.Count == 0;

        /// <summary>不一致的度量（<c>组.度量：左 -> 右</c>）；一致时为空。</summary>
        public IReadOnlyList<string> Differences { get; }

        public InvariantResult(string invariant, string script, string subject, IReadOnlyList<string> differences)
        {
            Invariant = invariant;
            Script = script;
            Subject = subject;
            Differences = differences;
        }

        public override string ToString() =>
            (Ok ? "通过   " : "不一致 ") + Invariant + " | " + Script + " | " + Subject
            + (Ok ? string.Empty : "\n    " + string.Join("\n    ", Differences));
    }

    /// <summary>
    /// 跨格子不变量检查（ADR-0122 决定 4；手感设计 06 第 1.1 节）：
    /// <list type="number">
    /// <item><see cref="PlanarCombos"/>：同一脚本、同一结算模式（同一预设）下，2D / 2.5D / 3D 三个平面组合的<b>逻辑组</b>逐字节一致
    /// （表现组不比较，各格子有自己的基线——06 第 1.1 节不变量 3）。</item>
    /// <item><see cref="ActionStrippedEqualsTargeted"/>：动作式格子在"剥掉 timeline 块 + 改用目标选择式格子的预设"的变体下，
    /// 逻辑组与同组合的目标选择式格子逐字节一致——证明"动作式 = 目标选择式 + timeline + 手感预设"，没有别的差别。</item>
    /// <item><see cref="FeelAssemblyIsTransparentUnderClassic"/>：目标选择式格子（经典预设、无 timeline）下，手感装配开启与旧路径（不装配）
    /// 在世界结局度量（移动、命中结算）上一致——证明手感装配在经典预设下是透明的。</item>
    /// </list>
    /// 逻辑组 = 度量类别为 <see cref="MetricClass.Logic"/> 的全部度量（按注册表声明）；实时类（墙钟/分配）与表现类不参与。
    /// 运行方式：<c>feellab invariants</c>（见 <c>toolchain/feellab</c>）或测试 <c>Tests.Lab.CrossCellInvariantTests</c>。
    /// </summary>
    public static class LabInvariants
    {
        public const string PlanarCombos = "planar_combos_logic_equal";

        public const string ActionStrippedEqualsTargeted = "action_stripped_equals_targeted";

        public const string FeelAssemblyIsTransparentUnderClassic = "feel_assembly_transparent_under_classic";

        private static readonly string[] Combos = { "2d", "2_5d", "3d" };

        /// <summary>
        /// 手感装配透明性只比较世界结局度量：旧路径下输入经"按钮边沿直接提交施放意图"，手感路径经输入缓冲与动作层，
        /// 提交/结算时序类度量（<c>casts_submitted</c>、响应/结算 tick、事件总数）按构造不同，不在比较范围。
        /// </summary>
        private static readonly HashSet<string> TransparentMetrics = new HashSet<string>(StringComparer.Ordinal)
        {
            "movement.ticks_requested", "movement.path_length", "movement.net_displacement", "movement.speed_steady",
            "movement.blocked_ticks", "movement.obstructed_jitter_ticks",
            "attack.cast_success", "attack.attack_instances", "attack.damage_events", "attack.damage_total",
            "attack.targets_hit", "attack.kills", "attack.dedupe_violations",
        };

        public static List<InvariantResult> Check(LabRunner runner, IEnumerable<InputScript> scripts)
        {
            var results = new List<InvariantResult>();
            foreach (var script in scripts)
            {
                results.AddRange(CheckPlanarCombos(runner, script));
                results.AddRange(CheckActionStripped(runner, script));
                if (script.Meta.Feel)
                {
                    results.AddRange(CheckFeelTransparent(runner, script));
                }
            }

            return results;
        }

        public static List<InvariantResult> CheckPlanarCombos(LabRunner runner, InputScript script)
        {
            var results = new List<InvariantResult>();
            foreach (var settlement in new[] { "targeted", "action" })
            {
                var cells = new List<string>();
                foreach (var combo in Combos)
                {
                    cells.Add(combo + "_" + settlement);
                }

                var reference = runner.Run(script, cells[0]);
                for (var i = 1; i < cells.Count; i++)
                {
                    var other = runner.Run(script, cells[i]);
                    results.Add(new InvariantResult(
                        PlanarCombos, script.Meta.ScriptId, cells[0] + " == " + cells[i],
                        DiffLogic(runner.Registry, reference, other, null)));
                }
            }

            return results;
        }

        public static List<InvariantResult> CheckActionStripped(LabRunner runner, InputScript script)
        {
            var results = new List<InvariantResult>();
            foreach (var combo in Combos)
            {
                var targeted = runner.Dataset.Catalog.GetScenario(combo + "_targeted");
                var variant = new LabRunVariant { StripTimelines = true, PresetId = targeted.DefaultPreset };
                var actionStripped = runner.Run(script, combo + "_action", variant);
                var reference = runner.Run(script, combo + "_targeted");
                results.Add(new InvariantResult(
                    ActionStrippedEqualsTargeted, script.Meta.ScriptId,
                    combo + "_action[strip timelines, " + targeted.DefaultPreset + "] == " + combo + "_targeted",
                    DiffLogic(runner.Registry, reference, actionStripped, null)));
            }

            return results;
        }

        public static List<InvariantResult> CheckFeelTransparent(LabRunner runner, InputScript script)
        {
            var results = new List<InvariantResult>();
            foreach (var combo in Combos)
            {
                var cell = combo + "_targeted";
                var on = runner.Run(script, cell);
                var off = runner.Run(script, cell, new LabRunVariant { FeelOff = true });
                results.Add(new InvariantResult(
                    FeelAssemblyIsTransparentUnderClassic, script.Meta.ScriptId,
                    cell + "[feel on] == " + cell + "[feel off] (world-outcome metrics)",
                    DiffLogic(runner.Registry, on, off, TransparentMetrics)));
            }

            return results;
        }

        /// <summary>两份指纹的逻辑类度量逐项比较（<paramref name="only"/> 非空时只看其中列出的 <c>组.度量</c>）；返回不一致项。</summary>
        public static List<string> DiffLogic(MetricRegistry registry, Fingerprint left, Fingerprint right, ISet<string>? only)
        {
            var differences = new List<string>();
            foreach (var group in registry.Groups)
            {
                var leftGroup = left.Groups.TryGetValue(group.Name, out var lg) ? lg as JsonObject : null;
                var rightGroup = right.Groups.TryGetValue(group.Name, out var rg) ? rg as JsonObject : null;
                if ((leftGroup == null) != (rightGroup == null))
                {
                    if (only == null)
                    {
                        differences.Add(group.Name + "：一侧没有该组");
                    }

                    continue;
                }

                if (leftGroup == null || rightGroup == null)
                {
                    continue;
                }

                foreach (var spec in group.Specs)
                {
                    if (spec.Class != MetricClass.Logic)
                    {
                        continue;
                    }

                    var name = group.Name + "." + spec.Name;
                    if (only != null && !only.Contains(name))
                    {
                        continue;
                    }

                    leftGroup.TryGetValue(spec.Name, out var lv);
                    rightGroup.TryGetValue(spec.Name, out var rv);
                    var l = lv == null ? "(无)" : FingerprintComparer.Render(lv);
                    var r = rv == null ? "(无)" : FingerprintComparer.Render(rv);
                    if (!string.Equals(l, r, StringComparison.Ordinal))
                    {
                        differences.Add(name + "：" + l + " -> " + r);
                    }
                }
            }

            return differences;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.InputMap;

namespace Core.Rules.Skill
{
    /// <summary>剪辑标记导入结果：可直接抄写进 <c>skill.def.timeline</c> 的分相毫秒与判定标记（<c>source: clip</c> 的"派生数据步骤"）。</summary>
    public sealed class ClipTimeline
    {
        public double StartupMs { get; }

        public double ActiveMs { get; }

        public double RecoveryMs { get; }

        /// <summary>判定类标记（<c>hit</c>/<c>invuln_*</c>/<c>armor_*</c>/<c>motion_*</c>/<c>release</c>），已归一 <c>hit:&lt;段&gt;</c>。</summary>
        public IReadOnlyList<TimelineMarker> Markers { get; }

        public IReadOnlyList<TimelineCancelWindow> CancelWindows { get; }

        public double? ComboOpenMs { get; }

        public double? ComboCloseMs { get; }

        /// <summary>导入过程的提示（缺少 <c>active_start</c>/<c>active_end</c>、未知事件名等）。</summary>
        public IReadOnlyList<string> Notes { get; }

        public ClipTimeline(
            double startupMs, double activeMs, double recoveryMs, IReadOnlyList<TimelineMarker> markers,
            IReadOnlyList<TimelineCancelWindow> cancelWindows, double? comboOpenMs, double? comboCloseMs, IReadOnlyList<string> notes)
        {
            StartupMs = startupMs;
            ActiveMs = activeMs;
            RecoveryMs = recoveryMs;
            Markers = markers;
            CancelWindows = cancelWindows;
            ComboOpenMs = comboOpenMs;
            ComboCloseMs = comboCloseMs;
            Notes = notes;
        }
    }

    /// <summary>
    /// 剪辑事件 → 时间线字段的导入（手感设计/01 第 3.2 节 <c>source: clip</c>）：规则层不读表现域，所以内容导入工具在数据构建阶段
    /// 把剪辑标记时间抄写进 <c>skill.def.timeline</c>；本类是这一步的纯函数实现。事件命名约定（取自
    /// <c>display.anim_set.clips[*].events</c>）：<c>active_start</c>/<c>active_end</c> 界定判定相，<c>hit</c> 或 <c>hit:&lt;段&gt;</c>
    /// 为命中，<c>combo_open</c>/<c>combo_close</c> 为连招窗口，<c>cancel_open:&lt;类别&gt;</c>（可选 <c>cancel_close:&lt;类别&gt;</c>）为取消窗口，
    /// <c>invuln_start/end</c>、<c>armor_start/end</c>、<c>motion_start/end</c>、<c>release</c> 原样抄写；表现类事件（<c>trail_*</c>、<c>footstep</c>、<c>fx</c>）不抄。
    /// </summary>
    public static class TimelineClipImporter
    {
        private static readonly HashSet<string> CopiedMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "invuln_start", "invuln_end", "armor_start", "armor_end", "motion_start", "motion_end", "release",
        };

        public static ClipTimeline Import(ClipMarkerSet clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));

            var total = clip.TotalMs;
            var notes = new List<string>();
            double? activeStart = null;
            double? activeEnd = null;
            double? comboOpen = null;
            double? comboClose = null;
            var markers = new List<TimelineMarker>();
            var cancelOpen = new List<(ActionClass Class, double At)>();
            var cancelClose = new Dictionary<ActionClass, double>();
            var hitOrdinal = 0;

            foreach (var e in clip.Events)
            {
                var at = e.TimePct * total;
                var name = e.Name;
                if (name == "active_start") { activeStart = at; continue; }
                if (name == "active_end") { activeEnd = at; continue; }
                if (name == "combo_open") { comboOpen = at; continue; }
                if (name == "combo_close") { comboClose = at; continue; }

                if (name.StartsWith("cancel_open:", StringComparison.Ordinal))
                {
                    cancelOpen.Add((TimelineDef.ParseActionClass(name.Substring("cancel_open:".Length)), at));
                    continue;
                }

                if (name.StartsWith("cancel_close:", StringComparison.Ordinal))
                {
                    cancelClose[TimelineDef.ParseActionClass(name.Substring("cancel_close:".Length))] = at;
                    continue;
                }

                if (name == TimelineDef.HitMarker || name.StartsWith(TimelineDef.HitMarker + ":", StringComparison.Ordinal))
                {
                    var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["segment"] = name == TimelineDef.HitMarker
                            ? hitOrdinal.ToString(CultureInfo.InvariantCulture)
                            : name.Substring(TimelineDef.HitMarker.Length + 1),
                    };
                    hitOrdinal++;
                    markers.Add(new TimelineMarker(TimelineDef.HitMarker, at, args));
                    continue;
                }

                if (CopiedMarkers.Contains(name))
                {
                    markers.Add(new TimelineMarker(name, at, new Dictionary<string, string>()));
                    continue;
                }

                if (name.StartsWith("trail_", StringComparison.Ordinal) || name == "footstep" || name == "fx")
                {
                    continue; // 表现类事件：只在剪辑元数据里，规则层不读。
                }

                notes.Add("未识别的剪辑事件 \"" + name + "\" 已忽略");
            }

            if (!activeStart.HasValue)
            {
                notes.Add("剪辑没有 active_start 事件：前摇记为 0");
            }

            if (!activeEnd.HasValue)
            {
                notes.Add("剪辑没有 active_end 事件：判定相延续到剪辑末尾");
            }

            var s = activeStart ?? 0;
            var a = (activeEnd ?? total) - s;
            if (a < 0) a = 0;
            var r = total - s - a;
            if (r < 0) r = 0;

            var windows = new List<TimelineCancelWindow>();
            foreach (var (cls, openAt) in cancelOpen)
            {
                windows.Add(new TimelineCancelWindow(cls, openAt, cancelClose.TryGetValue(cls, out var closeAt) ? closeAt : (double?)null));
            }

            return new ClipTimeline(s, a, r, markers, windows, comboOpen, comboClose, notes);
        }
    }

    /// <summary>剪辑标记与 <c>skill.def.timeline</c> 的一致性比对（手感设计/01 第 3.2 节）。</summary>
    public static class TimelineClipConsistency
    {
        /// <summary><c>source: clip</c> 的"一致"容差：抄写时百分比取整带来的毫秒误差，不是作者可调的容差。</summary>
        public const double CopyEpsilonMs = 0.5;

        /// <summary>
        /// 比对。<c>source: clip</c> 时任一偏差（超过 <see cref="CopyEpsilonMs"/>）为 Error（说明忘了重新导入）；
        /// <c>source: data</c> 时偏差超过 <paramref name="toleranceMs"/>（标定表 <c>marker_tolerance_ms</c>）给 Warning。
        /// </summary>
        public static IReadOnlyList<ValidationIssue> Compare(string recordKey, TimelineDef def, ClipTimeline clip, double toleranceMs)
        {
            var issues = new List<ValidationIssue>();
            var isClip = def.Source == TimelineSource.Clip;
            var limit = isClip ? CopyEpsilonMs : toleranceMs;
            var severity = isClip ? ValidationSeverity.Error : ValidationSeverity.Warning;
            var check = isClip ? "timeline_clip_mismatch" : "timeline_clip_deviation";

            void Add(string message) =>
                issues.Add(new ValidationIssue(severity, "skill.def", check, message, recordKey: recordKey, field: "timeline"));

            void Cmp(string what, double dataMs, double clipMs)
            {
                if (Math.Abs(dataMs - clipMs) > limit)
                {
                    Add(what + "：timeline=" + dataMs.ToString("R", CultureInfo.InvariantCulture) + " 毫秒，剪辑="
                        + clipMs.ToString("R", CultureInfo.InvariantCulture) + " 毫秒（容差 "
                        + limit.ToString("R", CultureInfo.InvariantCulture) + "）");
                }
            }

            Cmp("startup_ms", def.StartupMs, clip.StartupMs);
            Cmp("active_ms", def.ActiveMs, clip.ActiveMs);
            Cmp("recovery_ms", def.RecoveryMs, clip.RecoveryMs);

            foreach (var cm in clip.Markers)
            {
                TimelineMarker? match = null;
                foreach (var dm in def.Markers)
                {
                    if (dm.Name != cm.Name)
                    {
                        continue;
                    }

                    if (cm.Name == TimelineDef.HitMarker
                        && !(dm.Args.TryGetValue("segment", out var ds) && cm.Args.TryGetValue("segment", out var cs) && ds == cs))
                    {
                        continue;
                    }

                    match = dm;
                    break;
                }

                var label = cm.Name + (cm.Args.TryGetValue("segment", out var seg) ? ":" + seg : string.Empty);
                if (match == null)
                {
                    Add("剪辑有标记 \"" + label + "\"，timeline.markers 里没有");
                }
                else
                {
                    Cmp("标记 " + label, match.AtMs, cm.AtMs);
                }
            }

            foreach (var cw in clip.CancelWindows)
            {
                TimelineCancelWindow? match = null;
                foreach (var dw in def.CancelWindows)
                {
                    if (dw.Class == cw.Class)
                    {
                        match = dw;
                        break;
                    }
                }

                var label = "cancel_windows[" + TimelineDef.ActionClassName(cw.Class) + "]";
                if (match == null)
                {
                    Add("剪辑有取消窗口 \"" + label + "\"，timeline.cancel_windows 里没有");
                    continue;
                }

                Cmp(label + ".open_ms", match.OpenMs, cw.OpenMs);
                if (cw.CloseMs.HasValue && match.CloseMs.HasValue)
                {
                    Cmp(label + ".close_ms", match.CloseMs.Value, cw.CloseMs.Value);
                }
            }

            if (clip.ComboOpenMs.HasValue)
            {
                if (def.Combo == null)
                {
                    Add("剪辑有 combo_open 事件，timeline 没有 combo 块");
                }
                else
                {
                    Cmp("combo.open_ms", def.Combo.OpenMs, clip.ComboOpenMs.Value);
                    if (clip.ComboCloseMs.HasValue)
                    {
                        Cmp("combo.close_ms", def.Combo.CloseMs, clip.ComboCloseMs.Value);
                    }
                }
            }

            return issues;
        }
    }

    /// <summary>
    /// 剪辑标记一致性校验规则（手感设计/01 第 3.2 节）：对每个声明了 timeline 的技能，经 <see cref="IClipMarkerSource"/> 取剪辑标记并比对。
    /// 取不到剪辑时：<c>source: clip</c> 为 Error（权威来源缺失），<c>source: data</c> 跳过。
    /// <para>
    /// 判断记录（为什么不在 <c>RulesSchemaCatalog</c> 登记）：生产用 <see cref="IClipMarkerSource"/> 需要"技能 → 动画集 → 剪辑"的对应，
    /// 当前数据没有这条链接（见 skill 模块 README 已知局限），装配层不应登记一个永远取不到剪辑的规则；有生产实现后由装配方
    /// <c>RegisterValidationRule</c> 即可。
    /// </para>
    /// </summary>
    public sealed class TimelineClipConsistencyRule : IValidationRule
    {
        private readonly IClipMarkerSource _source;
        private readonly double _toleranceMs;

        public TimelineClipConsistencyRule(IClipMarkerSource source, double toleranceMs)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _toleranceMs = toleranceMs;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!System.Linq.Enumerable.Contains(view.Tables, "skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetObject("timeline", out var obj))
                {
                    continue;
                }

                var def = TimelineDef.Parse(obj);
                ClipMarkerSet clip = default!;
                if (!(record.Id.HasValue && _source.TryGetClip(record.Id.Value, out clip)))
                {
                    if (def.Source == TimelineSource.Clip)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "timeline_clip_missing",
                            "timeline.source 为 clip 但取不到该技能对应的剪辑标记", recordKey: record.Key, field: "timeline.source");
                    }

                    continue;
                }

                foreach (var issue in TimelineClipConsistency.Compare(record.Key, def, TimelineClipImporter.Import(clip), _toleranceMs))
                {
                    yield return issue;
                }
            }
        }
    }
}

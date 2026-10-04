using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <c>skill.def.timeline</c> 块的语义校验（手感设计/01 第 3.1～3.6 节；ADR-0115）。字段的类型/范围/枚举由 schema 登记
    /// （<see cref="SkillSchemas.Def"/>）覆盖，本规则只做 schema 表达不了的跨字段业务判断。未声明 <c>timeline</c> 的行整体跳过，
    /// 与本规则登记之前逐位一致。
    /// <para>
    /// 判断记录（阻断与警告的划分）：运行期会得到错误行为的（总时长与 <c>cast_time</c> 不符、标记超出动作时长、位移块缺起止
    /// 标记、连招指向不存在的技能……）为 Error；只是作者可能没意识到的（表现类标记写进了判定块、声明了无敌开始没有结束、
    /// 有效果却没有 <c>hit</c> 标记、目标辅助的缩放没有位移块可缩）为 Warning；<c>continuous</c>/<c>spatial</c> 命中缺命中形状为 Error（运行期会静默退回 instant）。
    /// </para>
    /// </summary>
    public sealed class SkillTimelineRule : IValidationRule
    {
        private const string Table = "skill.def";

        /// <summary>毫秒比较的浮点容差（<c>cast_time</c> 为秒，乘 1000 后与毫秒和比较）。</summary>
        private const double MsEpsilon = 1e-3;

        private static readonly HashSet<string> AuthorableMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "hit", "invuln_start", "invuln_end", "armor_start", "armor_end", "guard_start", "guard_end", "motion_start", "motion_end", "release",
        };

        private static readonly HashSet<string> DerivedMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "combo_open", "combo_close", "cost", "charge_ready",
        };

        private static readonly HashSet<string> PresentationMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "trail_start", "trail_end", "footstep", "fx", "afterimage_start", "afterimage_end",
        };

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains(Table))
            {
                yield break;
            }

            foreach (var record in view.GetAll(Table))
            {
                if (!record.TryGetObject("timeline", out var tl))
                {
                    continue;
                }

                foreach (var issue in CheckRecord(view, record, tl))
                {
                    yield return issue;
                }
            }
        }

        private static IEnumerable<ValidationIssue> CheckRecord(IDataRegistryView view, DataRecord record, JsonObject tl)
        {
            var key = record.Key;
            ValidationIssue Err(string check, string message, string? field = "timeline") =>
                new ValidationIssue(ValidationSeverity.Error, Table, check, message, recordKey: key, field: field);
            ValidationIssue Warn(string check, string message, string? field = "timeline") =>
                new ValidationIssue(ValidationSeverity.Warning, Table, check, message, recordKey: key, field: field);

            var issues = new List<ValidationIssue>();

            var startup = Num(tl, "startup_ms");
            var active = Num(tl, "active_ms");
            var recovery = Num(tl, "recovery_ms");
            var total = startup + active + recovery;

            if (total <= 0)
            {
                issues.Add(Err("timeline_zero_duration", "timeline 三相之和必须大于 0"));
            }

            // cast_time 为秒，三相为毫秒；声明了 timeline 的技能 cast_time 必须等于三相之和（手感设计/01 第 3.1 节）。
            var castTimeMs = (record.TryGetNumber("cast_time", out var ct) ? ct : 0) * 1000.0;
            if (Math.Abs(castTimeMs - total) > MsEpsilon)
            {
                issues.Add(Err("timeline_cast_time_mismatch",
                    "声明了 timeline 的技能 cast_time（秒）必须等于 startup_ms + active_ms + recovery_ms 之和（毫秒）：cast_time="
                    + (castTimeMs / 1000.0).ToString("R", CultureInfo.InvariantCulture) + " 秒，三相之和="
                    + total.ToString("R", CultureInfo.InvariantCulture) + " 毫秒", "cast_time"));
            }

            if (record.TryGetString("kind", out var kind) && kind == "passive")
            {
                issues.Add(Err("timeline_on_passive", "被动技能不能声明 timeline"));
            }

            if (record.TryGetNumber("channel_time", out var channel) && channel != 0)
            {
                issues.Add(Err("timeline_channel_time_exclusive", "声明了 timeline 的技能不能同时声明 channel_time", "channel_time"));
            }

            if (record.TryGetBool("ground_target", out var ground) && ground)
            {
                issues.Add(Warn("timeline_ground_target_unsupported",
                    "地面坐标施法请求（ground_target）不进入时间线模式，仍按 cast_time 读条结算；timeline 在该路径上被忽略", "ground_target"));
            }

            // charge
            if (tl.TryGetValue("charge", out var chargeVal) && chargeVal is JsonObject charge)
            {
                if (Num(charge, "max_ms") <= Num(charge, "min_ms"))
                {
                    issues.Add(Err("timeline_charge_range", "charge.max_ms 必须大于 charge.min_ms", "timeline.charge"));
                }
            }

            // markers
            var hasHit = false;
            var motionStart = (double?)null;
            var motionEnd = (double?)null;
            var invulnStart = (double?)null;
            var invulnEnd = (double?)null;
            var armorStart = (double?)null;
            var armorEnd = (double?)null;
            var guardStart = (double?)null;
            var guardEnd = (double?)null;
            var hitSegments = new HashSet<string>(StringComparer.Ordinal);
            if (tl.TryGetValue("markers", out var markersVal) && markersVal is JsonArray markers)
            {
                for (var i = 0; i < markers.Count; i++)
                {
                    if (!(markers[i] is JsonObject m))
                    {
                        continue;
                    }

                    var rawName = m.TryGetValue("name", out var nv) && nv is JsonString ns ? ns.Value : string.Empty;
                    var at = Num(m, "at_ms");
                    var field = "timeline.markers[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    var name = rawName;
                    string? segment = null;

                    if (rawName.StartsWith("hit:", StringComparison.Ordinal))
                    {
                        name = "hit";
                        segment = rawName.Substring(4);
                        if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        {
                            issues.Add(Err("timeline_marker_segment", "hit:<段> 的段序号必须是非负整数：\"" + rawName + "\"", field));
                        }
                    }
                    else if (name == "hit" && m.TryGetValue("args", out var av) && av is JsonObject ao
                        && ao.TryGetValue("segment", out var sv))
                    {
                        segment = sv is JsonNumber sn ? sn.Value.ToString("R", CultureInfo.InvariantCulture) : (sv as JsonString)?.Value;
                    }

                    if (at > total + MsEpsilon)
                    {
                        issues.Add(Err("timeline_marker_out_of_range",
                            "标记 \"" + rawName + "\" 的 at_ms=" + at.ToString("R", CultureInfo.InvariantCulture) + " 超出动作总时长 "
                            + total.ToString("R", CultureInfo.InvariantCulture) + " 毫秒", field));
                    }

                    if (DerivedMarkers.Contains(name) || name.StartsWith("cancel_open:", StringComparison.Ordinal)
                        || name.StartsWith("cancel_close:", StringComparison.Ordinal))
                    {
                        issues.Add(Err("timeline_marker_derived",
                            "标记 \"" + name + "\" 由 combo/cancel_windows/cost_at 等块派生，不能单独写在 markers 里", field));
                        continue;
                    }

                    if (PresentationMarkers.Contains(name))
                    {
                        issues.Add(Warn("timeline_marker_presentation",
                            "标记 \"" + name + "\" 是表现类标记，只存在于剪辑元数据，规则层不读；写在 timeline.markers 里不会被触发", field));
                        continue;
                    }

                    if (!AuthorableMarkers.Contains(name))
                    {
                        issues.Add(Err("timeline_marker_unknown", "未知的时间标记名 \"" + rawName + "\"", field));
                        continue;
                    }

                    switch (name)
                    {
                        case "hit":
                            hasHit = true;
                            if (segment != null && !hitSegments.Add(segment))
                            {
                                issues.Add(Err("timeline_marker_duplicate_segment", "hit 标记的 segment=" + segment + " 重复", field));
                            }

                            break;
                        case "motion_start": motionStart = at; break;
                        case "motion_end": motionEnd = at; break;
                        case "invuln_start": invulnStart = at; break;
                        case "invuln_end": invulnEnd = at; break;
                        case "armor_start": armorStart = at; break;
                        case "armor_end": armorEnd = at; break;
                        case "guard_start": guardStart = at; break;
                        case "guard_end": guardEnd = at; break;
                    }
                }
            }

            // 无敌窗口
            if (invulnEnd.HasValue && !invulnStart.HasValue)
            {
                issues.Add(Err("timeline_invuln_unpaired", "声明了 invuln_end 但没有 invuln_start", "timeline.markers"));
            }
            else if (invulnStart.HasValue && !invulnEnd.HasValue)
            {
                issues.Add(Warn("timeline_invuln_unterminated", "声明了 invuln_start 但没有 invuln_end，无敌将持续到动作结束", "timeline.markers"));
            }
            else if (invulnStart.HasValue && invulnEnd.HasValue && invulnEnd.Value < invulnStart.Value)
            {
                issues.Add(Err("timeline_invuln_order", "invuln_end 早于 invuln_start", "timeline.markers"));
            }

            // 霸体窗口（与无敌窗口同级别）
            if (armorEnd.HasValue && !armorStart.HasValue)
            {
                issues.Add(Err("timeline_armor_unpaired", "声明了 armor_end 但没有 armor_start", "timeline.markers"));
            }
            else if (armorStart.HasValue && !armorEnd.HasValue)
            {
                issues.Add(Warn("timeline_armor_unterminated", "声明了 armor_start 但没有 armor_end，霸体将持续到动作结束", "timeline.markers"));
            }
            else if (armorStart.HasValue && armorEnd.HasValue && armorEnd.Value < armorStart.Value)
            {
                issues.Add(Err("timeline_armor_order", "armor_end 早于 armor_start", "timeline.markers"));
            }

            // 格挡窗口（与霸体窗口同级别，手感设计/03 第 4 节，ADR-0145）
            if (guardEnd.HasValue && !guardStart.HasValue)
            {
                issues.Add(Err("timeline_guard_unpaired", "声明了 guard_end 但没有 guard_start", "timeline.markers"));
            }
            else if (guardStart.HasValue && !guardEnd.HasValue)
            {
                issues.Add(Warn("timeline_guard_unterminated", "声明了 guard_start 但没有 guard_end，格挡将持续到动作结束", "timeline.markers"));
            }
            else if (guardStart.HasValue && guardEnd.HasValue && guardEnd.Value < guardStart.Value)
            {
                issues.Add(Err("timeline_guard_order", "guard_end 早于 guard_start", "timeline.markers"));
            }

            // 位移块与 motion_start/motion_end
            var hasMotion = tl.TryGetValue("motion", out var motionVal) && motionVal is JsonObject;
            if (hasMotion)
            {
                if (!motionStart.HasValue || !motionEnd.HasValue)
                {
                    issues.Add(Err("timeline_motion_markers",
                        "声明了 motion 块必须同时声明 motion_start 与 motion_end 标记（位移只在两者之间发生）", "timeline.motion"));
                }
                else if (motionEnd.Value < motionStart.Value)
                {
                    issues.Add(Err("timeline_motion_order", "motion_end 早于 motion_start", "timeline.markers"));
                }

                var motion = (JsonObject)motionVal!;
                if (motion.TryGetValue("driver", out var drv) && drv is JsonString drvs && drvs.Value != "code")
                {
                    // ADR-0147：根运动驱动已删除，旧数据加载期报明确错误并给迁移说明（ADR-0039 破坏性变更流程）。
                    issues.Add(Err("timeline_motion_driver_removed",
                        drvs.Value == "root_motion"
                            ? "timeline.motion.driver: " + ActionMotion.RootMotionMigrationNote
                            : "timeline.motion.driver 唯一合法取值是 code（缺省），不认识 \"" + drvs.Value + "\"", "timeline.motion.driver"));
                }

                if (motion.TryGetValue("curve", out var crv) && crv is JsonString crvs
                    && crvs.Value.StartsWith("custom:", StringComparison.Ordinal))
                {
                    // ADR-0147：位移曲线引用 custom:<id> 必须指向导入期烘焙出的 skill.motion_curve 行，运行期不静默改线性。
                    var curveId = crvs.Value.Substring("custom:".Length);
                    if (!view.Tables.Contains("skill.motion_curve") || view.Get("skill.motion_curve", curveId) == null)
                    {
                        issues.Add(Err("timeline_motion_curve_missing",
                            "timeline.motion.curve 引用的位移曲线 \"" + curveId + "\" 不存在于 skill.motion_curve（先用工具链 bake-motion 烘焙）",
                            "timeline.motion.curve"));
                    }
                }

                var motionKind = motion.TryGetValue("kind", out var mk) && mk is JsonString mks ? mks.Value : string.Empty;
                if (motionKind == "charge")
                {
                    if (!(motion.TryGetValue("max_turn_deg", out var mt) && mt is JsonNumber))
                    {
                        issues.Add(Err("timeline_motion_charge_incomplete",
                            "charge 类位移必须声明 max_turn_deg（距离上限、转角上限与有效时间三者都必须声明，手感设计/02 第 4 节）", "timeline.motion"));
                    }

                    if (Num(motion, "distance") <= 0)
                    {
                        issues.Add(Err("timeline_motion_charge_incomplete", "charge 类位移的 distance（最大距离）必须大于 0", "timeline.motion"));
                    }
                }
            }
            else if (motionStart.HasValue || motionEnd.HasValue)
            {
                issues.Add(Warn("timeline_motion_markers_without_block", "声明了 motion_start/motion_end 标记但没有 motion 块，位移不会发生", "timeline.markers"));
            }

            // 取消窗口
            if (tl.TryGetValue("cancel_windows", out var winVal) && winVal is JsonArray wins)
            {
                for (var i = 0; i < wins.Count; i++)
                {
                    if (!(wins[i] is JsonObject w))
                    {
                        continue;
                    }

                    var field = "timeline.cancel_windows[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    var open = Num(w, "open_ms");
                    if (open > total + MsEpsilon)
                    {
                        issues.Add(Err("timeline_window_out_of_range", "取消窗口 open_ms 超出动作总时长", field));
                    }

                    if (w.TryGetValue("close_ms", out var cv) && cv is JsonNumber cn && cn.Value < open)
                    {
                        issues.Add(Err("timeline_window_order", "取消窗口 close_ms 早于 open_ms", field));
                    }
                }
            }

            // 连招
            if (tl.TryGetValue("combo", out var comboVal) && comboVal is JsonObject combo)
            {
                var open = Num(combo, "open_ms");
                var close = Num(combo, "close_ms");
                if (close < open)
                {
                    issues.Add(Err("timeline_combo_order", "combo.close_ms 早于 combo.open_ms", "timeline.combo"));
                }

                if (open > total + MsEpsilon)
                {
                    issues.Add(Err("timeline_combo_out_of_range", "combo.open_ms 超出动作总时长", "timeline.combo"));
                }

                if (combo.TryGetValue("next", out var nextVal) && nextVal is JsonString nextStr)
                {
                    var nextRecord = view.Get(Table, nextStr.Value);
                    if (nextRecord == null)
                    {
                        issues.Add(Err("timeline_combo_next_missing", "combo.next 指向的技能 \"" + nextStr.Value + "\" 不存在", "timeline.combo.next"));
                    }
                    else if (!nextRecord.TryGetObject("timeline", out _))
                    {
                        issues.Add(Err("timeline_combo_next_no_timeline", "combo.next 指向的技能 \"" + nextStr.Value + "\" 没有声明 timeline（连招段必须各有独立的 timeline）", "timeline.combo.next"));
                    }
                }
            }

            // 命中解析
            var hitPolicy = tl.TryGetValue("hit_policy", out var hp) && hp is JsonString hps ? hps.Value : "marker";
            var hitMode = tl.TryGetValue("hit_mode", out var hm) && hm is JsonString hms ? hms.Value : "auto";
            var continuous = hitPolicy == "continuous";

            // 命中形状：目标选择链（target_shape_ref）有没有声明 shape（空间命中的前提）。链不存在由 schema 的软引用/别处报，这里按"未知"处理。
            bool? chainHasShape = null;
            var chainTableLoaded = view.Tables.Contains("target.chain_def");
            if (chainTableLoaded && record.TryGetString("target_shape_ref", out var shapeRef))
            {
                var chainRecord = view.Get("target.chain_def", shapeRef);
                if (chainRecord != null)
                {
                    chainHasShape = chainRecord.TryGetObject("shape", out _);
                }
            }

            if (chainHasShape == false && hitMode == "spatial")
            {
                issues.Add(Err("timeline_spatial_without_shape",
                    "hit_mode: spatial 要求 target_shape_ref 指向的目标选择链声明 shape（命中形状）", "timeline.hit_mode"));
            }

            if (continuous && hitMode == "instant")
            {
                issues.Add(Err("timeline_continuous_instant_conflict",
                    "hit_policy: continuous 需要空间命中，不能与 hit_mode: instant 同时声明", "timeline.hit_mode"));
            }

            if (continuous && hitMode != "instant" && chainHasShape == false)
            {
                issues.Add(Err("timeline_continuous_without_shape",
                    "hit_policy: continuous 逐 tick 用目标选择链的 shape 做空间命中，target_shape_ref 指向的链没有声明 shape", "timeline.hit_policy"));
            }

            var costAt = tl.TryGetValue("cost_at", out var ca) && ca is JsonString cas ? cas.Value : "commit";
            if (costAt == "first_hit" && !hasHit && !continuous)
            {
                issues.Add(Err("timeline_cost_first_hit_without_hit", "cost_at: first_hit 要求至少一个 hit 标记，否则资源永远不会被扣除", "timeline.cost_at"));
            }

            // 目标辅助（手感设计/02 第 5 节）
            if (tl.TryGetValue("target_assist", out var assistVal) && assistVal is JsonObject assist)
            {
                if (chainTableLoaded && assist.TryGetValue("chain_ref", out var chainVal) && chainVal is JsonString chainStr
                    && view.Get("target.chain_def", chainStr.Value) == null)
                {
                    issues.Add(Err("timeline_target_assist_chain_missing",
                        "target_assist.chain_ref 指向的目标选择链 \"" + chainStr.Value + "\" 不存在", "timeline.target_assist.chain_ref"));
                }

                var closeDistance = assist.TryGetValue("mode", out var amv) && amv is JsonString ams && ams.Value == "close_distance";
                if (closeDistance && !tl.TryGetValue("motion", out _))
                {
                    issues.Add(Warn("timeline_target_assist_close_distance_without_motion",
                        "target_assist.mode: close_distance 缩放的是 motion 块的位移距离，但没有声明 motion，缩放不会发生", "timeline.target_assist.mode"));
                }

                if (closeDistance && chainHasShape == false)
                {
                    issues.Add(Warn("timeline_target_assist_close_distance_without_shape",
                        "target_assist.mode: close_distance 按判定形状的覆盖深度缩放距离，target_shape_ref 的链没有 shape，覆盖深度按 0 处理（冲到贴身）", "timeline.target_assist.mode"));
                }
            }

            if (!hasHit && !continuous && record.TryGetArray("effects", out var effects) && effects.Count > 0)
            {
                issues.Add(Warn("timeline_effects_without_hit",
                    "技能声明了 effects 但 timeline 没有 hit 标记：时间线模式下效果只在 hit 标记处结算，当前永远不会结算"));
            }

            return issues;
        }

        private static double Num(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : 0.0;
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Rules.Skill;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// 生产用剪辑标记来源（手感设计/01 第 3.2 节、04 第 8 节，ADR-0147）：从数据表推出"技能 → 剪辑标记"，不读引擎资源。
    /// <para>
    /// 技能到剪辑的对应来自两处武器表现数据（<c>display.weapon_style</c>）：①<c>cast_anim_override</c>（技能 id → 剪辑资源引用）；
    /// ②普攻：物品 <c>item.template.feel_weapon_ref</c> → <c>feel.weapon.auto_attack_timeline_ref</c>（普攻技能）与物品
    /// <c>display_ref</c> → <c>display.map.weapon_style_ref</c> → <c>display.weapon_style.auto_attack_anim</c>（普攻剪辑）。
    /// 技能同时被两处指到时，<c>cast_anim_override</c> 优先（它是作者对该技能的显式指定）。资源引用再经姿势集
    /// （<c>display.anim_set.clips[*].resource_ref</c>，已合并继承链）找到剪辑条目，取其 <c>events</c> 与 <c>duration_ms</c>。
    /// </para>
    /// <para>
    /// 判断记录：①<b>剪辑没有声明 <c>duration_ms</c> 就取不到标记</b>——事件是百分比，没有总时长无法换算毫秒；总时长由导入工具从资源量出后写入
    /// （<c>import_assets check</c> 会对照资源报告缺失/不一致）。取不到时 <c>source: data</c> 的技能跳过、<c>source: clip</c> 的技能报错
    /// （权威来源缺失，见 <see cref="TimelineClipConsistencyRule"/>），所以这条链没接通的数据不会被静默放过。
    /// ②同一资源引用被多个剪辑键引用（别名）时取键名序最前者；同一资源在多套姿势集里出现时取姿势集 id 序最前者——确定性，不依赖加载顺序。
    /// ③读不懂的行（继承成环等）跳过，由各自表的规则报错，本来源不重复报。
    /// ④本类型在规则组装层（读得到表现域数据表）而不是技能模块：规则层运行期不读表现域，作者态校验读的是数据表本身。
    /// </para>
    /// </summary>
    public sealed class DisplayClipMarkerSource : IClipMarkerSource
    {
        private readonly Dictionary<Id, ClipMarkerSet> _bySkill = new Dictionary<Id, ClipMarkerSet>();

        public DisplayClipMarkerSource(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            var tables = new HashSet<string>(view.Tables, StringComparer.Ordinal);
            if (!tables.Contains("display.anim_set") || !tables.Contains("display.weapon_style"))
            {
                return;
            }

            var clipByResource = BuildClipIndex(view);
            if (clipByResource.Count == 0)
            {
                return;
            }

            // 普攻：技能 -> 武器风格 -> 普攻剪辑。
            if (tables.Contains("item.template") && tables.Contains("feel.weapon") && tables.Contains("display.map"))
            {
                foreach (var item in Sorted(view.GetAll("item.template")))
                {
                    if (!item.TryGetId("feel_weapon_ref", out var weaponRef)
                        || !item.TryGetId("display_ref", out var displayRef))
                    {
                        continue;
                    }

                    var weapon = view.Get("feel.weapon", weaponRef);
                    var display = view.Get("display.map", displayRef);
                    if (weapon == null || display == null
                        || !weapon.TryGetId("auto_attack_timeline_ref", out var skillId)
                        || !display.TryGetId("weapon_style_ref", out var styleRef))
                    {
                        continue;
                    }

                    var style = view.Get("display.weapon_style", styleRef);
                    if (style != null && style.TryGetId("auto_attack_anim", out var clipRef))
                    {
                        Add(skillId, clipRef, clipByResource, overwrite: false);
                    }
                }
            }

            // 施法覆盖：技能 -> 剪辑（优先，覆盖普攻推出的同一技能）。
            foreach (var style in Sorted(view.GetAll("display.weapon_style")))
            {
                if (!style.TryGetObject("cast_anim_override", out var overrides))
                {
                    continue;
                }

                foreach (var entry in overrides)
                {
                    if (entry.Value is JsonString clip && Id.TryParse(entry.Key, out var skillId) && Id.TryParse(clip.Value, out var clipRef))
                    {
                        Add(skillId, clipRef, clipByResource, overwrite: true);
                    }
                }
            }
        }

        public bool TryGetClip(Id skillId, out ClipMarkerSet clip) => _bySkill.TryGetValue(skillId, out clip!);

        private void Add(Id skillId, Id clipRef, Dictionary<Id, (string Key, AnimClipDef Def)> index, bool overwrite)
        {
            if (!index.TryGetValue(clipRef, out var entry) || !entry.Def.DurationMs.HasValue)
            {
                return;
            }

            if (!overwrite && _bySkill.ContainsKey(skillId))
            {
                return;
            }

            var events = new List<ClipEvent>(entry.Def.Events.Count);
            foreach (var e in entry.Def.Events)
            {
                events.Add(new ClipEvent(e.Name, e.TimePct));
            }

            _bySkill[skillId] = new ClipMarkerSet(entry.Key, entry.Def.DurationMs.Value, events);
        }

        private static Dictionary<Id, (string Key, AnimClipDef Def)> BuildClipIndex(IDataRegistryView view)
        {
            var index = new Dictionary<Id, (string, AnimClipDef)>();
            foreach (var record in Sorted(view.GetAll("display.anim_set")))
            {
                AnimSetDef set;
                try
                {
                    set = AnimSetDef.FromRecord(record, view);
                }
                catch (DataFieldException)
                {
                    continue;
                }

                var keys = new List<string>(set.Clips.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    var def = set.Clips[key];
                    if (!index.ContainsKey(def.ResourceRef))
                    {
                        index[def.ResourceRef] = (key, def);
                    }
                }
            }

            return index;
        }

        private static List<DataRecord> Sorted(IReadOnlyList<DataRecord> records)
        {
            var list = new List<DataRecord>(records);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }

    /// <summary>
    /// 技能时间线与剪辑标记一致性的生产校验（手感设计/01 第 3.2 节，ADR-0147）：用 <see cref="DisplayClipMarkerSource"/> 取剪辑标记，
    /// 交给 <see cref="TimelineClipConsistencyRule"/> 比对。容差取标定表 <c>marker_tolerance_ms</c>（优先 <c>feel.calibration.framework_default</c>，
    /// 否则表内第一行，都没有取 50 毫秒）。没有声明 <c>timeline</c> 的技能、没有对应剪辑的 <c>source: data</c> 技能不受影响。
    /// </summary>
    public sealed class SkillClipConsistencyRule : IValidationRule
    {
        public const double FallbackToleranceMs = 50.0;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var source = new DisplayClipMarkerSource(view);
            return new TimelineClipConsistencyRule(source, ResolveToleranceMs(view)).Validate(view);
        }

        public static double ResolveToleranceMs(IDataRegistryView view)
        {
            var tables = new HashSet<string>(view.Tables, StringComparer.Ordinal);
            if (!tables.Contains("feel.calibration"))
            {
                return FallbackToleranceMs;
            }

            DataRecord? chosen = null;
            foreach (var record in view.GetAll("feel.calibration"))
            {
                if (record.Key == "feel.calibration.framework_default")
                {
                    chosen = record;
                    break;
                }

                if (chosen == null || string.CompareOrdinal(record.Key, chosen.Key) < 0)
                {
                    chosen = record;
                }
            }

            return chosen != null && chosen.TryGetNumber("marker_tolerance_ms", out var tolerance) ? tolerance : FallbackToleranceMs;
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 交互式试玩的会话脚本工厂与约定（ADR-0141，手感设计 06 第 4 节"场景控制"的内核侧）。
    /// <para>
    /// 判断记录（试玩会话的数据根可换）：会话脚本的 <see cref="ScriptMeta.ExtraDataRoots"/> 与 <see cref="ScriptMeta.DummySetId"/> 由调用方给，
    /// 缺省是实验室动作式数据根（<c>data/_feel</c> + <c>data/_lab_action</c>，靶子集 <c>lab.dummy_set.lab_action</c>）；
    /// 游戏接入时传自己的数据根与靶子集即可，宿主不读任何写死的路径。技能槽位到技能的绑定同样可传（缺省是实验室动作式输入动作的四个槽位）。
    /// </para>
    /// </summary>
    public static class LabLive
    {
        public const string DefaultDummySet = "lab.dummy_set.lab_action";

        public static readonly string[] DefaultExtraDataRoots = { "data/_feel", "data/_lab_action" };

        /// <summary>缺省技能槽位绑定：普攻三连击、闪避冲刺、技能重击、蓄力（输入动作见 <c>data/_lab_action/found/found.input_action.json</c>）。</summary>
        public static readonly KeyValuePair<string, string>[] DefaultSkillSlots =
        {
            new KeyValuePair<string, string>("lab_a.attack", "skill.lab_a_combo1"),
            new KeyValuePair<string, string>("lab_a.dodge", "skill.lab_a_dodge"),
            new KeyValuePair<string, string>("lab_a.skill", "skill.lab_a_slam"),
            new KeyValuePair<string, string>("lab_a.charge", "skill.lab_a_charge"),
        };

        public static readonly string[] DefaultLearnSkills = { "skill.lab_a_combo2", "skill.lab_a_combo3" };

        /// <summary>
        /// 造一份空事件的实时会话脚本（手感场景）。返回的脚本事件清单是空的 <c>List&lt;ScriptEvent&gt;</c>，同时是会话日志
        /// （见 <see cref="LabSession"/>）；调用方不要自己往里加事件。
        /// </summary>
        public static InputScript CreateScript(
            string scriptId, int tickRate = 60, int frameRateCap = 60, IEnumerable<string>? extraDataRoots = null,
            string dummySetId = DefaultDummySet, IEnumerable<KeyValuePair<string, string>>? skillSlots = null,
            IEnumerable<string>? learnSkills = null)
        {
            var meta = new ScriptMeta
            {
                ScriptId = scriptId,
                ScriptVersion = 1,
                Description = "交互式试玩会话（人手实时输入录成的脚本）",
                TickRate = tickRate,
                FrameRateCap = frameRateCap,
                DurationTicks = 1,
                PlayerStart = Vec2.Zero,
                Feel = true,
                DummySetId = dummySetId,
            };
            foreach (var root in extraDataRoots ?? DefaultExtraDataRoots)
            {
                meta.ExtraDataRoots.Add(root);
            }

            foreach (var pair in skillSlots ?? DefaultSkillSlots)
            {
                meta.SkillSlots.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));
            }

            foreach (var skill in learnSkills ?? DefaultLearnSkills)
            {
                meta.LearnSkills.Add(skill);
            }

            return new InputScript(meta, new List<ScriptEvent>());
        }

        /// <summary>预设切换把一局分成的一段：[<see cref="StartTick"/>, <see cref="EndTick"/>) 内生效的基础预设。</summary>
        public sealed class PresetSegment
        {
            public int StartTick { get; }

            public int EndTick { get; }

            public string Preset { get; }

            public PresetSegment(int startTick, int endTick, string preset)
            {
                StartTick = startTick;
                EndTick = endTick;
                Preset = preset;
            }
        }

        /// <summary>
        /// 按脚本里的 <see cref="ScriptEventKind.Preset"/> 事件把一局切成若干"预设段"（手感设计 06 第 3.4 节"切换本身写进录制，指纹按槽位分开"）。
        /// <paramref name="initialPreset"/> 是第一段的预设（脚本开局生效的预设）。
        /// </summary>
        public static List<PresetSegment> PresetSegments(InputScript script, string initialPreset)
        {
            var segments = new List<PresetSegment>();
            var start = 0;
            var current = initialPreset;
            foreach (var e in script.Events)
            {
                if (e.Kind != ScriptEventKind.Preset)
                {
                    continue;
                }

                if (e.Tick > start)
                {
                    segments.Add(new PresetSegment(start, e.Tick, current));
                    start = e.Tick;
                }

                current = e.Action;
            }

            segments.Add(new PresetSegment(start, Math.Max(start, script.Meta.DurationTicks), current));
            return segments;
        }

        /// <summary>
        /// 前缀脚本：只保留 tick 早于 <paramref name="endTick"/> 的事件、时长定为 <paramref name="endTick"/>（JSON 往返复制，不动原脚本）。
        /// 逻辑是因果的，前缀脚本的记录就是"这一局在 <paramref name="endTick"/> 停下来"的记录——按预设段的结束 tick 取前缀，
        /// 即得到每一段结束时的累计指纹（相邻两段指纹之差就是该段预设的贡献）。
        /// </summary>
        public static InputScript PrefixScript(InputScript script, int endTick)
        {
            var clone = InputScript.Parse(script.ToJson());
            var kept = new List<ScriptEvent>();
            foreach (var e in clone.Events)
            {
                if (e.Tick < endTick)
                {
                    kept.Add(e);
                }
            }

            clone.Meta.DurationTicks = Math.Max(1, endTick);
            return new InputScript(clone.Meta, kept);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Lab
{
    /// <summary>
    /// 体型 × 武器矩阵脚本的生成器（M5-S7，ADR-0151）：枚举数据里<b>每一个</b>体型原型（<c>feel.archetype</c>）与每一个武器原型（<c>feel.weapon</c>，
    /// 不含预设模板变体行 <c>feel.weapon.tpl_*</c>），每个组合生成一个同形状的实验室脚本，各自带六格基线，作为"手感核心可复用"的机器证据（06 第 6 节"预设复用"）。
    /// <list type="bullet">
    /// <item>体型走生产路径：每个体型原型生成一个只多 <c>feel_archetype_ref</c> 的职业行（<see cref="ClassId"/>，ADR-0146），脚本 <c>meta.playerClass</c> 指向它；</item>
    /// <item>武器走 <c>loadout</c> 事件（第 0 tick 叠加成玩家单位的覆盖层，同调参面板的武器切换）；</item>
    /// <item>脚本个数、体型与武器清单都来自数据，不在代码里写死；新增体型/武器行后只需重新生成（<c>feellab matrix</c>），测试会核对夹具与生成结果一致。</item>
    /// </list>
    /// 脚本形状（对所有组合相同，差异只来自体型与武器数据）：第 5 tick 挥一次（命中 2 单位外的木桩）→ 第 60 tick 向上走 30 tick 后停（体型的加减速）→
    /// 第 120 tick 再挥一次（够不到，空挥）→ 第 150 tick 闪避。声明可选度量组 <c>latency</c>、<c>attackx</c>、<c>hitx</c>、<c>crowd</c>。
    /// </summary>
    public static class LabMatrix
    {
        public const string ScriptPrefix = "feel_matrix_";

        public const string ClassPrefix = "arch.class.matrix_";

        public const string TemplateWeaponPrefix = "feel.weapon.tpl_";

        /// <summary>矩阵脚本叠加的数据根（相对仓库根）：体型/武器原型、预设模板里的武器原型、动作实验室、本矩阵的职业行。</summary>
        public static readonly string[] DataRoots =
        {
            "data/_feel", "data/_feel_templates", "data/_lab_action", "lab/fixtures/data/matrix",
        };

        /// <summary>枚举矩阵用的数据根（不含职业行本身）：用来读体型与武器清单。</summary>
        public static readonly string[] EnumerationRoots = { "data/_feel", "data/_feel_templates" };

        public static string Short(string rowId) => rowId.Substring(rowId.LastIndexOf('.') + 1);

        public static string ClassId(string archetypeId) => ClassPrefix + Short(archetypeId);

        public static string ScriptId(string archetypeId, string weaponId) => ScriptPrefix + Short(archetypeId) + "_" + Short(weaponId);

        /// <summary>矩阵计划：体型清单、武器清单、职业行表文本与每个脚本的文本（按脚本 id 排序）。</summary>
        public sealed class Plan
        {
            public List<string> Archetypes { get; } = new List<string>();

            public List<string> Weapons { get; } = new List<string>();

            /// <summary>动作式结算的格子短名（来自格子数据）。</summary>
            public List<string> ActionCells { get; } = new List<string>();

            public string ClassTable { get; set; } = string.Empty;

            public SortedDictionary<string, string> Scripts { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 从数据来源（框架 + 实验室 + <see cref="EnumerationRoots"/>，调用方给）读出体型与武器清单，算出整套矩阵的职业行与脚本文本。
        /// 脚本个数 = 体型数 × 武器数，全部来自数据。
        /// </summary>
        public static Plan BuildPlan(IReadOnlyList<Core.Foundation.DataRegistry.IDataSource> sources)
        {
            var dataset = LabDataset.Load(sources);
            var registry = LabHost.BuildProbe(dataset.HostOptions).Registry;
            var plan = new Plan();
            plan.Archetypes.AddRange(Archetypes(registry));
            plan.Weapons.AddRange(Weapons(registry));
            plan.ClassTable = ClassTableText(plan.Archetypes);
            // 首次可见响应只有动作式结算的格子才有（目标选择式格子瞬发，没有动作开始/姿势请求）：延迟期望只对这些格子成立；格子清单来自数据。
            var actionCells = new List<string>();
            foreach (var scenario in dataset.Catalog.Scenarios())
            {
                if (string.Equals(scenario.Settlement, "action", StringComparison.Ordinal))
                {
                    actionCells.Add(scenario.Cell);
                }
            }

            plan.ActionCells.AddRange(actionCells);
            foreach (var archetype in plan.Archetypes)
            {
                foreach (var weapon in plan.Weapons)
                {
                    plan.Scripts[ScriptId(archetype, weapon)] = BuildScript(archetype, weapon, actionCells).ToJson();
                }
            }

            return plan;
        }

        /// <summary>数据里全部体型原型行 id（按 id 排序）。</summary>
        public static List<string> Archetypes(IDataRegistryView registry)
        {
            var ids = new List<string>();
            foreach (var record in registry.GetAll("feel.archetype"))
            {
                ids.Add(record.Key);
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>数据里全部武器原型行 id（按 id 排序；预设模板变体行不在内）。</summary>
        public static List<string> Weapons(IDataRegistryView registry)
        {
            var ids = new List<string>();
            foreach (var record in registry.GetAll("feel.weapon"))
            {
                if (!record.Key.StartsWith(TemplateWeaponPrefix, StringComparison.Ordinal))
                {
                    ids.Add(record.Key);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>矩阵的职业行表文本（<c>arch.class</c>）：照抄实验室职业，只多 <c>feel_archetype_ref</c>；换行 <c>\n</c>，末尾带换行。</summary>
        public static string ClassTableText(IEnumerable<string> archetypeIds)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"table\": \"arch.class\",\n  \"schema_version\": 1,\n  \"rows\": [");
            var first = true;
            foreach (var archetype in archetypeIds)
            {
                sb.Append(first ? "\n" : ",\n");
                first = false;
                sb.Append("    {\n");
                sb.Append("      \"id\": \"").Append(ClassId(archetype)).Append("\",\n");
                sb.Append("      \"name_key\": \"l10n.arch.lab_hero.name\",\n");
                sb.Append("      \"primary_stat\": \"stat.lab_power\",\n");
                sb.Append("      \"base_stats\": { \"stat.lab_power\": 10, \"stat.stamina\": 100 },\n");
                sb.Append("      \"power_types\": [\"arch.power.health\"],\n");
                sb.Append("      \"skill_book_ref\": \"skill.book.lab_hero\",\n");
                sb.Append("      \"level_curve_ref\": \"prog.curve.lab_default\",\n");
                sb.Append("      \"feel_archetype_ref\": \"").Append(archetype).Append("\"\n");
                sb.Append("    }");
            }

            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>一个（体型，武器）组合的脚本。</summary>
        public static InputScript BuildScript(string archetypeId, string weaponId, IReadOnlyList<string> actionCells)
        {
            var meta = new ScriptMeta
            {
                ScriptId = ScriptId(archetypeId, weaponId),
                ScriptVersion = 1,
                Description =
                    $"体型 × 武器矩阵（M5-S7，ADR-0151，生成器 LabMatrix）：玩家职业经 feel_archetype_ref 取体型 {archetypeId}，第 0 tick 叠加武器 {weaponId}；"
                    + "同形状输入：第 5 tick 挥击命中木桩、第 60 tick 起向上走 30 tick 后停、第 120 tick 空挥、第 150 tick 闪避。差异只来自体型与武器数据。",
                DatasetRoot = "data/_lab",
                PresetId = string.Empty,
                TickRate = 60,
                FrameRateCap = 60,
                DurationTicks = 240,
                Feel = true,
                DummySetId = "lab.dummy_set.lab_action",
                PlayerClass = ClassId(archetypeId),
            };
            meta.DummyGroups.Add("stake");
            meta.SkillSlots.Add(new KeyValuePair<string, string>("lab_a.attack", "skill.lab_a_slash"));
            meta.SkillSlots.Add(new KeyValuePair<string, string>("lab_a.dodge", "skill.lab_a_dodge"));
            foreach (var root in DataRoots)
            {
                meta.ExtraDataRoots.Add(root);
            }

            foreach (var name in new[] { LabExtraMetrics.Latency, LabExtraMetrics.AttackExt, LabExtraMetrics.HitExt, LabExtraMetrics.Crowd })
            {
                meta.ExtraMetrics.Add(name);
            }

            const string attack = "input.action.lab_a_attack";
            const string dodge = "input.action.lab_a_dodge";
            const string move = "input.action.move";
            var events = new List<ScriptEvent>
            {
                new ScriptEvent(0, weaponId, ScriptEventKind.Loadout),
                new ScriptEvent(5, attack, ScriptEventKind.Press),
                new ScriptEvent(6, attack, ScriptEventKind.Release),
                new ScriptEvent(60, move, ScriptEventKind.Axis, new Vec2(0, 1)),
                new ScriptEvent(90, move, ScriptEventKind.Axis, new Vec2(0, 0)),
                new ScriptEvent(120, attack, ScriptEventKind.Press),
                new ScriptEvent(121, attack, ScriptEventKind.Release),
                new ScriptEvent(150, dodge, ScriptEventKind.Press),
                new ScriptEvent(151, dodge, ScriptEventKind.Release),
            };
            // 全组合通用的不变量（与体型、武器数据无关，写成期望；随体型/武器取值变化的量由 lab/tests/MatrixTests 按数据算出期望）。
            var expectations = new List<Expectation>
            {
                new Expectation(
                    "matrix.no_residual_hits", null, new MetricSelector("attackx.residual_hits"), ExpectOp.Eq, LabJson.Num(0), null, null, null, null,
                    "取消之后不应再有命中确认落地"),
                new Expectation(
                    "matrix.visible_excess_nonneg", actionCells, new MetricSelector("latency.visible_excess_ms_min"), ExpectOp.Ge, LabJson.Num(0), null, null, null, null,
                    "首次可见时刻不早于逻辑必经时间"),
                new Expectation(
                    "matrix.visible_excess_lt_frame", actionCells, new MetricSelector("latency.visible_excess_ms_max"), ExpectOp.Lt, null, null, null, null,
                    new ExpectVersus(string.Empty, new MetricSelector("latency.frame_ms")),
                    "帧量化多出的一截小于一个表现帧步长（随帧率上限变化，不写死数）"),
            };
            return new InputScript(meta, events, expectations);
        }
    }
}

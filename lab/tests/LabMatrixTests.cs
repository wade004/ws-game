using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 体型 × 武器矩阵（M5-S7，ADR-0151）：矩阵的个数、清单与脚本文本都由数据枚举得出（夹具必须与生成结果一致），
    /// 每个组合的实测值等于由体型/武器数据行与毫秒换算算出的期望（用例里不写裸数）。
    /// 证据含义（06 第 6 节"预设复用"）：同一套手感核心，换体型只改移动手感、换武器只改打击反馈，两者互不串扰。
    /// </summary>
    public sealed class LabMatrixTests
    {
        private const string ActionCell = "2d_action";

        private static readonly string[] Roots =
        {
            Path.Combine("data", "_feel", "feel"), Path.Combine("data", "_feel_templates", "feel"),
        };

        // ---------- 数据读数 ----------

        private static List<JsonObject> Rows(string relativePath)
        {
            var path = Path.Combine(LabTestSupport.RepoRoot(), relativePath);
            var rows = new List<JsonObject>();
            if (!File.Exists(path))
            {
                return rows;
            }

            var root = LabJson.ParseObject(File.ReadAllText(path, Encoding.UTF8), path);
            foreach (var row in (JsonArray)root["rows"])
            {
                rows.Add((JsonObject)row);
            }

            return rows;
        }

        private static string IdOf(JsonObject row) => ((JsonString)row["id"]).Value;

        private static JsonObject Row(string table, string id)
        {
            foreach (var root in Roots)
            {
                foreach (var row in Rows(Path.Combine(root, table + ".json")))
                {
                    if (IdOf(row) == id)
                    {
                        return row;
                    }
                }
            }

            throw new InvalidOperationException($"数据里没有 {table} 行 {id}");
        }

        /// <summary>行的 <c>writes</c> 里某字段的 (op, 数值)；没写这个字段返回 null。</summary>
        private static (string Op, double Value)? Write(JsonObject row, string field)
        {
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return (((JsonString)o["op"]).Value, ((JsonNumber)o["value"]).Value);
                }
            }

            return null;
        }

        private static string WriteText(JsonObject row, string field)
        {
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return ((JsonString)o["value"]).Value;
                }
            }

            throw new InvalidOperationException($"{IdOf(row)} 没有写入 {field}");
        }

        private static LabMatrix.Plan BuildPlan()
        {
            var root = LabTestSupport.RepoRoot();
            var sources = new List<IDataSource>
            {
                LabDataSources.FromDirectory(Path.Combine(root, "data", "_framework")),
                LabDataSources.FromDirectory(Path.Combine(root, "data", "_lab")),
            };
            foreach (var rel in LabMatrix.EnumerationRoots)
            {
                sources.Add(LabDataSources.FromDirectory(Path.Combine(root, rel)));
            }

            return LabMatrix.BuildPlan(sources);
        }

        private static List<string> DataArchetypes() =>
            Rows(Path.Combine("data", "_feel", "feel", "feel.archetype.json")).Select(IdOf).OrderBy(x => x, StringComparer.Ordinal).ToList();

        private static List<string> DataWeapons()
        {
            var ids = new List<string>();
            foreach (var root in Roots)
            {
                ids.AddRange(Rows(Path.Combine(root, "feel.weapon.json")).Select(IdOf).Where(id => !id.StartsWith(LabMatrix.TemplateWeaponPrefix, StringComparison.Ordinal)));
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        // ---------- 清单来自数据，夹具与生成结果一致 ----------

        [Fact]
        public void Plan_ListsEveryArchetypeAndWeaponInData_AndFixturesAreUpToDate()
        {
            var plan = BuildPlan();
            var archetypes = DataArchetypes();
            var weapons = DataWeapons();

            Assert.NotEmpty(archetypes);
            Assert.NotEmpty(weapons);
            Assert.Equal(archetypes, plan.Archetypes);
            Assert.Equal(weapons, plan.Weapons);
            Assert.Equal(archetypes.Count * weapons.Count, plan.Scripts.Count);

            // 每个原型都在矩阵里；预设模板变体行不在。
            Assert.DoesNotContain(plan.Weapons, id => id.StartsWith(LabMatrix.TemplateWeaponPrefix, StringComparison.Ordinal));
            foreach (var a in archetypes)
            {
                foreach (var w in weapons)
                {
                    Assert.True(plan.Scripts.ContainsKey(LabMatrix.ScriptId(a, w)), $"缺 {a} × {w}");
                }
            }

            // 夹具与生成结果一致：新增体型/武器行后必须重新生成（feellab matrix），否则这里变红。
            var fixtures = LabTestSupport.FixturesDir;
            foreach (var pair in plan.Scripts)
            {
                var path = LabFixtures.ScriptPath(fixtures, pair.Key);
                Assert.True(File.Exists(path), $"缺脚本夹具 {path}（运行 feellab matrix）");
                Assert.Equal(Normalize(pair.Value).TrimEnd('\n'), Normalize(File.ReadAllText(path, Encoding.UTF8)).TrimEnd('\n'));
            }

            var classPath = Path.Combine(fixtures, "data", "matrix", "arch", "arch.class.json");
            Assert.Equal(Normalize(plan.ClassTable).TrimEnd('\n'), Normalize(File.ReadAllText(classPath, Encoding.UTF8)).TrimEnd('\n'));

            // 夹具目录里的矩阵脚本不多不少：前缀相同的脚本文件恰为计划里的那些。
            var onDisk = Directory.GetFiles(Path.Combine(fixtures, "scripts"), LabMatrix.ScriptPrefix + "*" + LabFixtures.ScriptSuffix)
                .Select(f => Path.GetFileName(f).Replace(LabFixtures.ScriptSuffix, string.Empty))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(plan.Scripts.Keys.ToList(), onDisk);
        }

        [Fact]
        public void EveryMatrixScript_HasBaselineForEveryApplicableCell()
        {
            var runner = LabTestSupport.Runner;
            var plan = BuildPlan();
            foreach (var id in plan.Scripts.Keys)
            {
                var script = LabTestSupport.Script(id);
                var path = LabFixtures.BaselinePath(LabTestSupport.FixturesDir, id);
                Assert.True(File.Exists(path), $"缺基线 {path}");
                var baseline = LabFixtures.ParseBaseline(File.ReadAllText(path, Encoding.UTF8), path);
                var cells = runner.ApplicableCells(script).Select(c => c.Cell).ToList();
                Assert.NotEmpty(cells);
                foreach (var cell in cells)
                {
                    Assert.True(baseline.ContainsKey(cell), $"{id} 的基线缺格子 {cell}");
                }
            }
        }

        // ---------- 武器数据驱动打击反馈 ----------

        [Fact]
        public void WeaponRows_DriveHitFeel_ForEveryArchetype()
        {
            var plan = BuildPlan();
            var sawKnockback = false;
            var sawNoKnockback = false;
            foreach (var archetype in plan.Archetypes)
            {
                foreach (var weapon in plan.Weapons)
                {
                    var id = LabMatrix.ScriptId(archetype, weapon);
                    var fp = FeelFp.Of(id, ActionCell);
                    var row = Row("feel.weapon", weapon);

                    // 冲击等级：命中确认的第一条冲击等级等于武器声明的等级。
                    Assert.Equal(WriteText(row, "impact_class"), fp.Text("reaction.hit_classes").Split(';')[0]);

                    // 顿帧：攻击方/受击方 tick = 武器声明的毫秒 × 生产换算（同一次命中，相同 tick 数时合并成一条）。
                    var attackerTicks = FeelRules.T(Write(row, "attacker_hitstop_ms")!.Value.Value);
                    var targetTicks = FeelRules.T(Write(row, "target_hitstop_ms")!.Value.Value);
                    var seenAttacker = -1;
                    var seenTarget = -1;
                    foreach (var item in fp.Items("hitstop.started"))
                    {
                        var parts = item.Split(':');
                        var ticks = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                        var units = parts[1].Split('+');
                        if (units.Contains("player"))
                        {
                            seenAttacker = ticks;
                        }

                        if (units.Contains("stake"))
                        {
                            seenTarget = ticks;
                        }
                    }

                    Assert.Equal(attackerTicks, seenAttacker);
                    Assert.Equal(targetTicks, seenTarget);

                    // 击退：出现击退反应时，距离等于武器声明的击退距离；没有击退反应时靶子没有被推开。
                    var declared = Write(row, "knockback_distance")!.Value.Value;
                    var knockbackSeen = false;
                    foreach (var item in fp.Items("hitx.reaction_distances"))
                    {
                        var parts = item.Split(':');
                        var distance = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                        if (parts[2] == "Knockback")
                        {
                            knockbackSeen = true;
                            Assert.Equal(declared, distance, 6);
                        }
                        else
                        {
                            Assert.Equal(0.0, distance, 6);
                        }
                    }

                    sawKnockback |= knockbackSeen;
                    sawNoKnockback |= !knockbackSeen;
                }
            }

            // 不是空转：矩阵里既有会击退的武器，也有不击退的武器。
            Assert.True(sawKnockback, "矩阵里没有任何组合出现击退反应，击退检查是空的");
            Assert.True(sawNoKnockback, "矩阵里没有任何组合没有击退，击退检查没有对照");
        }

        // ---------- 体型数据驱动移动手感 ----------

        [Fact]
        public void ArchetypeRows_DriveLocomotion_ForEveryWeapon()
        {
            var plan = BuildPlan();
            var preset = FeelRules.ForCell(ActionCell);
            var stepMs = FeelRules.StepSeconds * 1000.0;

            double Effective(string archetype, string field)
            {
                var baseMs = preset.N(field);
                var w = Write(Row("feel.archetype", archetype), field);
                if (w == null)
                {
                    return baseMs;
                }

                Assert.Equal("multiply", w.Value.Op);
                return baseMs * w.Value.Value;
            }

            var accelByArchetype = new List<(double Ms, double Ticks, double StopDistance, double DecelMs, double StopTicks)>();
            foreach (var archetype in plan.Archetypes)
            {
                var accelTicks = new List<double>();
                foreach (var weapon in plan.Weapons)
                {
                    var fp = FeelFp.Of(LabMatrix.ScriptId(archetype, weapon), ActionCell);
                    accelTicks.Add(fp.Num("movement.accel_ticks"));

                    // 起步加速：达速 tick 数落在"声明毫秒折成 tick"的一个 tick 之内（量化）。
                    Assert.InRange(fp.Num("movement.accel_ticks"), Effective(archetype, "accel_ms") / stepMs - 1.0, Effective(archetype, "accel_ms") / stepMs + 1.0);
                }

                // 武器不改自由移动：同一体型下，起步 tick 数与各武器无关。
                Assert.Single(accelTicks.Distinct());
                var any = FeelFp.Of(LabMatrix.ScriptId(archetype, plan.Weapons[0]), ActionCell);
                accelByArchetype.Add((Effective(archetype, "accel_ms"), accelTicks[0], any.Num("movement.stop_distance"), Effective(archetype, "decel_ms"), any.Num("movement.stop_ticks")));
            }

            // 体型之间的次序：声明的加速毫秒越大，达速 tick 数越多；声明的制动毫秒越大，滑行距离越长（相差一个 tick 以上时严格大于）。
            foreach (var a in accelByArchetype)
            {
                foreach (var b in accelByArchetype)
                {
                    if (a.Ms < b.Ms)
                    {
                        Assert.True(a.Ticks <= b.Ticks, $"加速 {a.Ms}ms 的体型不应比 {b.Ms}ms 的更慢到位");
                        if (b.Ms - a.Ms >= stepMs)
                        {
                            Assert.True(a.Ticks < b.Ticks);
                        }
                    }

                    if (a.DecelMs < b.DecelMs)
                    {
                        Assert.True(a.StopDistance < b.StopDistance, $"制动 {a.DecelMs}ms 的体型滑行距离应更短");
                        Assert.True(a.StopTicks <= b.StopTicks);
                    }
                }
            }

            // 不是空转：体型之间的起步 tick 数确实不全相同。
            Assert.True(accelByArchetype.Select(x => x.Ticks).Distinct().Count() > 1, "体型之间没有任何起步差异，体型入口可能没生效");
        }

        [Fact]
        public void HitFeel_IsIndependentOfArchetype_ForEveryWeapon()
        {
            var plan = BuildPlan();
            foreach (var weapon in plan.Weapons)
            {
                var texts = new List<string>();
                foreach (var archetype in plan.Archetypes)
                {
                    var fp = FeelFp.Of(LabMatrix.ScriptId(archetype, weapon), ActionCell);
                    texts.Add(string.Join("|", fp.Text("hitstop.started"), fp.Text("reaction.hit_classes"), fp.Text("hitx.reaction_distances"), fp.Text("crowd.ops_by_kind")));
                }

                Assert.Single(texts.Distinct());
            }
        }
    }
}

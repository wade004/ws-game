using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 字段登记的落地状态（active / planned，手感设计/05 第 4 节，ADR-0146）、字段目录文档片段与游戏自有字段的扩展位。
    /// 复现：登记了却没有消费方的字段被标 planned；不变量：planned ⇔ 全仓生产代码没有引用，登记与入库的目录片段逐字节一致，
    /// 标定的 base_speed 只影响绝对值视图、不影响解析结果。
    /// </summary>
    public class FeelFieldStatusTests
    {
        // ------------------------------------------------------------------ 状态扫描

        private static readonly string[] ScanRoots = { "core", "presentation", "adapters" };

        private static IEnumerable<string> ProductionSources()
        {
            var repo = FindRepoRoot();
            foreach (var root in ScanRoots)
            {
                var dir = Path.Combine(repo, root);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(repo.Length).Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("/tests/") || rel.Contains("/Library/")) continue;
                    if (rel.EndsWith("feel/contracts/FeelFields.cs", StringComparison.Ordinal)) continue; // 登记本身与常量定义
                    yield return file;
                }
            }
        }

        /// <summary>字段名 → 常量名（FeelFieldNames 里值等于字段名的常量）。</summary>
        private static Dictionary<string, string> ConstantNames()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in typeof(FeelFieldNames).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (f.IsLiteral && f.FieldType == typeof(string)) map[(string)f.GetRawConstantValue()!] = f.Name;
            }
            return map;
        }

        [Fact]
        public void ActiveFieldsHaveProductionConsumers_AndFieldsWithoutConsumersAreMarkedPlanned()
        {
            var names = ConstantNames();
            var texts = ProductionSources().Select(File.ReadAllText).ToList();
            var problems = new List<string>();
            foreach (var def in FeelFields.Default.Fields)
            {
                Assert.True(names.ContainsKey(def.Name), "字段缺 FeelFieldNames 常量：" + def.Name);
                var pattern = new Regex(@"FeelFieldNames\." + Regex.Escape(names[def.Name]) + @"\b", RegexOptions.CultureInvariant);
                var consumed = texts.Any(t => pattern.IsMatch(t));
                if (consumed && def.Status == FeelFieldStatus.Planned)
                {
                    problems.Add(def.Name + " 已有消费方引用，应改回 active（从 FeelFields.PlannedFields 删除）");
                }
                if (!consumed && def.Status == FeelFieldStatus.Active)
                {
                    problems.Add(def.Name + " 没有任何生产代码引用，应标 planned（写明原因）或接上消费方");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void PlannedFields_CarryAReason_ActiveFieldsCarryNone()
        {
            foreach (var def in FeelFields.Default.Fields)
            {
                if (def.Status == FeelFieldStatus.Planned) Assert.False(string.IsNullOrWhiteSpace(def.StatusNote), def.Name);
                else Assert.Null(def.StatusNote);
            }
            Assert.Contains(FeelFields.Default.Fields, f => f.Status == FeelFieldStatus.Planned);
            Assert.Contains(FeelFields.Default.Fields, f => f.Status == FeelFieldStatus.Active);

            var active = FeelFields.Default.Fields.First(f => f.Status == FeelFieldStatus.Active);
            Assert.Throws<ArgumentException>(() => active.WithStatus(FeelFieldStatus.Active, "active 不带原因"));
        }

        // ------------------------------------------------------------------ 字段目录文档片段

        [Fact]
        public void CatalogDoc_ListsEveryField_AndMatchesTheCommittedFragmentByteForByte()
        {
            var rendered = FeelFieldCatalogDoc.Render(FeelFields.Default);
            foreach (var def in FeelFields.Default.Fields) Assert.Contains("`" + def.Name + "`", rendered);

            var planned = FeelFields.Default.Fields.Count(f => f.Status == FeelFieldStatus.Planned);
            Assert.Contains("已生效 " + (FeelFields.Default.Count - planned) + " 个，已登记未生效 " + planned + " 个", rendered);
            Assert.DoesNotContain("\r", rendered);

            var path = Path.Combine(FindRepoRoot(), FeelFieldCatalogDoc.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "字段目录片段未生成：" + FeelFieldCatalogDoc.RelativePath + "（用 feellab fields 重新生成）");
            Assert.Equal(rendered, File.ReadAllText(path).Replace("\r\n", "\n"));
        }

        [Fact]
        public void CatalogDoc_IsDeterministic_AndRendersGameFieldsToo()
        {
            Assert.Equal(FeelFieldCatalogDoc.Render(FeelFields.Default), FeelFieldCatalogDoc.Render(FeelFields.Default));

            var extended = FeelFields.Extend(new[] { ParryField() });
            var text = FeelFieldCatalogDoc.Render(extended);
            Assert.Contains("`game.parry_ms`", text);
            Assert.Contains("共 " + (FeelFields.Default.Count + 1) + " 个字段", text);
        }

        // ------------------------------------------------------------------ base_speed：只影响绝对值视图

        [Fact]
        public void CalibrationBaseSpeed_ChangesOnlyTheAbsoluteView_NeverTheResolvedRelativeValues()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.speed", new[] { Set(MoveRatio, 1.5), Set(Reach, 2.0) });
            var state = new FakeState { Character = "c.speed" };
            var slow = new FeelCalibration("cal.slow", "p.base", 2, 4, 30, 10, 1, 32, 50);
            var fast = new FeelCalibration("cal.fast", "p.base", 2, 9, 30, 10, 1, 32, 50);

            var a = new FeelResolver(SmallProfiles(character), slow, 1.0 / 60.0, state.AsProviders()).Resolve(Unit1);
            var b = new FeelResolver(SmallProfiles(character), fast, 1.0 / 60.0, state.AsProviders()).Resolve(Unit1);

            // 不变量：每个字段的原始（相对）值逐位相同——运动层读的就是它，乘移动速度属性得到目标速度。
            foreach (var def in SmallFields().Fields) Assert.Equal(a.GetRaw(def.Name), b.GetRaw(def.Name));
            // 速度倍数字段的绝对值视图 = 相对值 × 参考基础移速（公式算出，不写裸数）；身高倍数字段不受 base_speed 影响。
            Assert.Equal(a.GetRaw(MoveRatio).AsNumber() * slow.BaseSpeed, a.GetAbsolute(MoveRatio).AsNumber(), 9);
            Assert.Equal(b.GetRaw(MoveRatio).AsNumber() * fast.BaseSpeed, b.GetAbsolute(MoveRatio).AsNumber(), 9);
            Assert.NotEqual(a.GetAbsolute(MoveRatio).AsNumber(), b.GetAbsolute(MoveRatio).AsNumber());
            Assert.Equal(a.GetAbsolute(Reach).AsNumber(), b.GetAbsolute(Reach).AsNumber());
        }

        // ------------------------------------------------------------------ 游戏自有字段的扩展位

        private static FeelFieldDef ParryField() => new FeelFieldDef(
            "game.parry_ms", FeelFieldKind.Number,
            new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Action, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add,
                FeelComposition.CharacterPrimary, FeelUnit.Milliseconds),
            "招架窗口毫秒（游戏自有字段）", 0, 500, optional: true);

        [Fact]
        public void Extend_RequiresGamePrefix_RejectsDuplicatesAndPlanned()
        {
            var meta = new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Action, FeelOpSet.Set, FeelComposition.CharacterPrimary, FeelUnit.None);
            Assert.Throws<ArgumentException>(() => FeelFields.Extend(new[]
            {
                new FeelFieldDef("parry_ms", FeelFieldKind.Number, meta, "没有前缀", 0, 1, optional: true),
            }));
            Assert.ThrowsAny<Exception>(() => FeelFields.Extend(new[] { ParryField(), ParryField() }));
            Assert.Throws<ArgumentException>(() => FeelFields.Extend(new[] { ParryField().WithStatus(FeelFieldStatus.Planned, "未生效") }));

            var set = FeelFields.Extend(new[] { ParryField() });
            Assert.Equal(FeelFields.Default.Count + 1, set.Count);
            Assert.True(set.TryGet("game.parry_ms", out var def) && def.Status == FeelFieldStatus.Active);
            // 框架默认登记本身不受影响。
            Assert.False(FeelFields.Default.TryGet("game.parry_ms", out _));
        }

        private const string GameCharacterJson = "{\"table\":\"feel.character\",\"schema_version\":1,\"rows\":[{\"id\":\"feel.character.game_parry\","
            + "\"writes\":[{\"field\":\"game.parry_ms\",\"op\":\"set\",\"value\":120}]}]}";

        private static (DataRegistry Registry, ValidationReport Report) LoadWith(bool extendedFirst, FeelFieldSet? extended)
        {
            var source = new InMemoryDataSource();
            foreach (var (table, json) in FrameworkFeelTables()) source.Add(table, json);
            source.Add("feel.calibration", CalibrationJson);
            source.Add("feel.character", GameCharacterJson);
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            if (extended != null && extendedFirst) FeelSchemas.RegisterAll(registry, extended);
            FeelSchemas.RegisterAll(registry); // 框架目录的默认注册：不得把游戏的扩展登记冲掉
            if (extended != null && !extendedFirst) FeelSchemas.RegisterAll(registry, extended);
            return (registry, registry.LoadAll());
        }

        [Fact]
        public void GameField_IsRejectedWithoutExtension_AndAcceptedWithIt_InEitherRegistrationOrder()
        {
            var extended = FeelFields.Extend(new[] { ParryField() });

            Assert.True(LoadWith(true, null).Report.ErrorCount > 0, "没有扩展登记时，写 game. 字段应被校验拒绝");

            foreach (var first in new[] { true, false })
            {
                var (registry, report) = LoadWith(first, extended);
                Assert.True(report.ErrorCount == 0, string.Join("\n", report.Issues));

                var profiles = FeelProfileSet.FromRegistry(registry, extended);
                var state = new FakeState { Character = "feel.character.game_parry" };
                var cal = new FeelCalibration("cal.x", "feel.preset.arpg_responsive", 2, 4, 30, 10, 1, 32, 50);
                var resolved = new FeelResolver(profiles, cal, 1.0 / 60.0, state.AsProviders()).Resolve(Unit1);
                Assert.Equal(120.0, resolved.GetRaw("game.parry_ms").AsNumber());
            }
        }
    }
}

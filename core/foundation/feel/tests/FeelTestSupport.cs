using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;

namespace Tests.Foundation.Feel
{
    /// <summary>手感模块测试公用夹具：小字段集、假提供者、标定、注册表构造、框架手感数据加载。</summary>
    internal static class FeelTestSupport
    {
        public static readonly Id Unit1 = new Id("unit.test_one");
        public static readonly Id Unit2 = new Id("unit.test_two");

        // ------------------------------------------------------------------ 标定（两组）

        /// <summary>标定 A：参考身高 2、基础移速 4、镜头高度 10。</summary>
        public static FeelCalibration CalA(string basePreset = "feel.preset.arpg_responsive") =>
            new FeelCalibration("feel.calibration.test_a", basePreset, 2.0, 4.0, 30.0, 10.0, 1.0, 32.0, 50.0);

        /// <summary>标定 B：参考身高 1.5、基础移速 6、镜头高度 12。</summary>
        public static FeelCalibration CalB(string basePreset = "feel.preset.arpg_responsive") =>
            new FeelCalibration("feel.calibration.test_b", basePreset, 1.5, 6.0, 60.0, 12.0, 1.5, 16.0, 30.0);

        /// <summary>
        /// 测试标定数据（<c>tests/data/feel/feel.calibration.json</c>，单行标定 A）：同一份文件也可作为数据根交给
        /// <c>validate_data.py</c>，与 <c>data/_feel</c> 一起校验框架手感数据（见模块 README 判断记录 5）。
        /// </summary>
        public static string CalibrationJson =>
            File.ReadAllText(Path.Combine(FindRepoRoot(), "core", "foundation", "feel", "tests", "data", "feel", "feel.calibration.json"))
                .Replace("\r\n", "\n");

        // ------------------------------------------------------------------ 小字段集（手算用）

        public const string AccMs = "acc_ms";
        public const string WeaponScale = "weapon_scale";
        public const string MoveRatio = "move_ratio";
        public const string Lock = "lock";
        public const string Vfx = "vfx";
        public const string Tags = "tag_list";
        public const string Level = "level";
        public const string Reach = "reach";
        public const string Kind = "kind";
        public const string Opt = "opt_ms";

        private static readonly FeelFieldSet SmallSet = BuildSmallFields();

        /// <summary>小字段集（同一实例，热加载要求字段登记同一实例）。</summary>
        public static FeelFieldSet SmallFields() => SmallSet;

        private static FeelFieldSet BuildSmallFields() => new FeelFieldSet(new[]
        {
            new FeelFieldDef(AccMs, FeelFieldKind.Number,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Movement, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.CharacterPrimary, FeelUnit.Milliseconds),
                "角色为主的毫秒字段", 0, 1000),
            new FeelFieldDef(WeaponScale, FeelFieldKind.Number,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Action, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.WeaponPrimary, FeelUnit.Ratio),
                "武器为主的倍率字段", 0, 10),
            new FeelFieldDef(MoveRatio, FeelFieldKind.Number,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Movement, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.AttackOverride, FeelUnit.BaseSpeedRatio),
                "攻击期间武器临时覆盖的速度倍率", 0, 2),
            new FeelFieldDef(Lock, FeelFieldKind.Bool,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Movement, FeelOpSet.Set, FeelComposition.AttackOverride),
                "攻击期间武器临时覆盖的朝向锁"),
            new FeelFieldDef(Vfx, FeelFieldKind.Number,
                new FeelFieldMeta(FeelHalf.Presenting, FeelGroup.Effects, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.WeaponPrimary, FeelUnit.Ratio, offhandStackable: true),
                "副手可叠加的特效强度", 0, 4),
            new FeelFieldDef(Tags, FeelFieldKind.List,
                new FeelFieldMeta(FeelHalf.Presenting, FeelGroup.Effects, FeelOpSet.Set | FeelOpSet.Add | FeelOpSet.Remove, FeelComposition.CharacterPrimary),
                "文本列表字段"),
            new FeelFieldDef(Level, FeelFieldKind.Int,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Action, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.CharacterPrimary, FeelUnit.Count),
                "整数字段", 0, 10),
            new FeelFieldDef(Reach, FeelFieldKind.Number,
                new FeelFieldMeta(FeelHalf.Presenting, FeelGroup.Camera, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.CharacterPrimary, FeelUnit.BodyHeights),
                "身高倍数的呈现字段", 0, 10),
            new FeelFieldDef(Kind, FeelFieldKind.Enum,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Reaction, FeelOpSet.Set, FeelComposition.WeaponPrimary),
                "枚举字段", enumValues: new[] { "a", "b" }),
            new FeelFieldDef(Opt, FeelFieldKind.Number,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Input, FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add, FeelComposition.CharacterPrimary, FeelUnit.Milliseconds),
                "可选毫秒字段", 0, 1000, optional: true),
        });

        public static FeelWrite Set(string field, double v) => new FeelWrite(field, FeelOp.Set, FeelValue.Of(v));

        public static FeelWrite Set(string field, bool v) => new FeelWrite(field, FeelOp.Set, FeelValue.Of(v));

        public static FeelWrite Set(string field, string v) => new FeelWrite(field, FeelOp.Set, FeelValue.Of(v));

        public static FeelWrite Mul(string field, double v) => new FeelWrite(field, FeelOp.Multiply, FeelValue.Of(v));

        public static FeelWrite Add(string field, double v) => new FeelWrite(field, FeelOp.Add, FeelValue.Of(v));

        public static FeelWrite ListOp(string field, FeelOp op, params string[] items) =>
            new FeelWrite(field, op, FeelValue.OfList(items));

        /// <summary>小字段集的基础预设（全部非可选字段）。</summary>
        public static FeelRow SmallPreset(string id = "p.base") => FeelRow.Preset(id, null, new[]
        {
            Set(AccMs, 100), Set(WeaponScale, 1), Set(MoveRatio, 0.5), Set(Lock, false), Set(Vfx, 1),
            new FeelWrite(Tags, FeelOp.Set, FeelValue.OfList(new[] { "base" })), Set(Level, 3), Set(Reach, 1), Set(Kind, "a"),
        });

        public static FeelProfileSet SmallProfiles(params FeelRow[] extra)
        {
            var rows = new List<FeelRow> { SmallPreset() };
            rows.AddRange(extra);
            return new FeelProfileSet(SmallFields(), rows);
        }

        public static FeelCalibration SmallCal() => new FeelCalibration("cal.small", "p.base", 2, 4, 30, 10, 1, 32, 50);

        // ------------------------------------------------------------------ 假提供者

        public sealed class FakeState : IFeelBodyProvider, IFeelTagProvider, IFeelEquipmentProvider, IFeelActionProvider, IFeelTemporaryProvider
        {
            public string? Archetype;
            public string? Character;
            public List<string> TagList = new List<string>();
            public string? Main;
            public string? Off;
            public FeelActionState Action = FeelActionState.Idle;
            public List<FeelTemporaryEntry> Temp = new List<FeelTemporaryEntry>();

            public string? GetArchetypeRef(Id unitId) => Archetype;

            public string? GetCharacterRef(Id unitId) => Character;

            public IReadOnlyList<string> GetTags(Id unitId) => TagList;

            public string? GetMainWeaponRef(Id unitId) => Main;

            public string? GetOffhandWeaponRef(Id unitId) => Off;

            public FeelActionState GetActionState(Id unitId) => Action;

            public IReadOnlyList<FeelTemporaryEntry> GetEntries(Id unitId) => Temp.ToArray();

            public FeelProviders AsProviders() => new FeelProviders
            {
                Body = this, Tags = this, Equipment = this, Action = this, Temporary = this,
            };
        }

        // ------------------------------------------------------------------ 数据注册表

        public static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        /// <summary>注册 feel 八张表与规则（可附加额外表 schema），加载给定表文本，返回注册表与报告。</summary>
        public static (IDataRegistry Registry, ValidationReport Report) Load(
            IEnumerable<(string Table, string Json)> tables, IEnumerable<TableSchema>? extraSchemas = null)
        {
            var source = new InMemoryDataSource();
            foreach (var (table, json) in tables) source.Add(table, json);
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            FeelSchemas.RegisterAll(registry);
            if (extraSchemas != null)
            {
                foreach (var s in extraSchemas) registry.RegisterSchema(s);
            }
            var report = registry.LoadAll();
            return (registry, report);
        }

        public static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException("源文件路径层级不足：" + sourceFilePath);
            }
            return dir.FullName;
        }

        /// <summary>框架手感数据目录（<c>data/_feel/feel</c>）里的全部表 JSON。</summary>
        public static List<(string Table, string Json)> FrameworkFeelTables()
        {
            var dir = Path.Combine(FindRepoRoot(), "data", "_feel", "feel");
            var result = new List<(string, string)>();
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                result.Add((Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
            }
            return result;
        }

        /// <summary>框架手感数据 + 测试标定行，加载成功的注册表与报告。</summary>
        public static (IDataRegistry Registry, ValidationReport Report) LoadFrameworkWithCalibration()
        {
            var tables = FrameworkFeelTables();
            tables.Add(("feel.calibration", CalibrationJson));
            return Load(tables);
        }

        public static FeelProfileSet FrameworkProfiles()
        {
            var (registry, report) = LoadFrameworkWithCalibration();
            if (report.ErrorCount > 0) throw new InvalidOperationException("框架手感数据加载有错误：" + string.Join("\n", report.Issues));
            return FeelProfileSet.FromRegistry(registry, FeelFields.Default);
        }
    }
}

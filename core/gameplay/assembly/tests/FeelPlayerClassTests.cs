// FeelPlayerClassTests：玩家的手感入口（ADR-0146）——职业行（arch.class）的 feel_archetype_ref / feel_ref 经生产装配接进解析器的第 2、5 层。
// 复现：声明了引用的职业，玩家的手感溯源链里出现第 2、5 层，数值等于"预设值 ∘ 原型/角色写入"（期望值由数据行里的写入算出，不写死裸数）。
// 不变量：没有声明引用的职业（框架实验室数据的 lab_hero），解析结果与此前逐位一致——溯源链没有第 2、5 层；读档改写了玩家职业后，玩家缓存按新职业重算。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class FeelPlayerClassTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id PlainClass = new Id("arch.class.lab_hero");
        private static readonly Id HeavyClass = new Id("arch.class.pc_heavy");
        private static readonly Id CharClass = new Id("arch.class.pc_char");
        private const string HeavyArchetype = "feel.archetype.heavy";
        private const string CharacterRow = "feel.character.pc_hero";
        private const double CharacterAccelMs = 37;

        // 两个职业行：完全照抄实验室职业，只多手感引用；一行角色手感把 accel_ms 直接设成常量。
        private const string ClassJson = @"{
  ""table"": ""arch.class"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""arch.class.pc_heavy"", ""name_key"": ""l10n.arch.lab_hero.name"", ""primary_stat"": ""stat.lab_power"",
      ""base_stats"": { ""stat.lab_power"": 10, ""stat.stamina"": 100 }, ""power_types"": [""arch.power.health""],
      ""skill_book_ref"": ""skill.book.lab_hero"", ""level_curve_ref"": ""prog.curve.lab_default"", ""feel_archetype_ref"": ""feel.archetype.heavy"" },
    { ""id"": ""arch.class.pc_char"", ""name_key"": ""l10n.arch.lab_hero.name"", ""primary_stat"": ""stat.lab_power"",
      ""base_stats"": { ""stat.lab_power"": 10, ""stat.stamina"": 100 }, ""power_types"": [""arch.power.health""],
      ""skill_book_ref"": ""skill.book.lab_hero"", ""level_curve_ref"": ""prog.curve.lab_default"", ""feel_ref"": ""feel.character.pc_hero"" }
  ]
}";

        private const string CharacterJson = @"{
  ""table"": ""feel.character"", ""schema_version"": 1,
  ""rows"": [ { ""id"": ""feel.character.pc_hero"", ""description"": ""玩家职业的角色手感（测试）"", ""maturity"": ""experimental"", ""profile_version"": 1,
                ""writes"": [ { ""field"": ""accel_ms"", ""op"": ""set"", ""value"": 37 } ] } ]
}";

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 4; i++) dir = dir.Parent ?? throw new InvalidOperationException();
            return dir.FullName;
        }

        private static void CopyDisk(StubFileSystem fs, string repoRoot, string relativeRoot)
        {
            var absoluteRoot = Path.Combine(repoRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(absoluteRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic(relativeRoot + "/" + rel, File.ReadAllText(file));
            }
        }

        private static HeadlessWorld Build(Id playerClass, bool overlay)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            var sources = new List<IDataSource>
            {
                new FileSystemDataSource(fs, "data/_framework"),
                new FileSystemDataSource(fs, "data/_feel"),
                new FileSystemDataSource(fs, "data/_lab"),
            };
            if (overlay)
            {
                fs.WriteTextAtomic("test/_pc/arch/arch.class.json", ClassJson);
                fs.WriteTextAtomic("test/_pc/feel/feel.character.json", CharacterJson);
                sources.Add(new FileSystemDataSource(fs, "test/_pc"));
            }

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = sources,
                FileSystem = fs,
                Seed = 20261004UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = playerClass,
                PlayerLevel = 1,
                GameId = new Id("game.lab"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = FrameworkCalibration },
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));
            return world;
        }

        private static double PresetAccel(HeadlessWorld world)
        {
            // 无引用的玩家（框架实验室职业）解析出的值就是标定基础预设的值。
            var plain = world.Gameplay.Feel!.Resolver.Resolve(PlayerId);
            Assert.Equal(PlainClass.Value, world.Registry.Get("arch.class", PlainClass)!.Key);
            return plain.Judging.GetNumber(FeelFieldNames.AccelMs);
        }

        // ------------------------------------------------------------------ 复现

        [Fact]
        public void ClassWithArchetypeRef_PlayerResolvesLayer2_FromTheArchetypeRowWrites()
        {
            var baseline = PresetAccel(Build(PlainClass, overlay: true));
            var world = Build(HeavyClass, overlay: true);
            var feel = world.Gameplay.Feel!;

            // 期望值由原型行里的写入算出：accel_ms 的 multiply 因子。
            var archetype = world.Registry.Get("feel.archetype", new Id(HeavyArchetype))!;
            double factor = 0;
            var found = false;
            foreach (var write in archetype.GetArray("writes"))
            {
                var obj = (Core.Foundation.Common.Json.JsonObject)write;
                if (((Core.Foundation.Common.Json.JsonString)obj["field"]).Value == FeelFieldNames.AccelMs)
                {
                    factor = ((Core.Foundation.Common.Json.JsonNumber)obj["value"]).Value;
                    found = true;
                }
            }

            Assert.True(found && factor > 1);
            Assert.Equal(baseline * factor, feel.Resolver.Resolve(PlayerId).Judging.GetNumber(FeelFieldNames.AccelMs), 6);
            var chain = feel.Resolver.GetProvenance(PlayerId, FeelFieldNames.AccelMs);
            Assert.Contains(chain, e => e.Layer == (int)FeelLayer.Archetype && e.SourceId == HeavyArchetype);
        }

        [Fact]
        public void ClassWithCharacterRef_PlayerResolvesLayer5_AndOverridesTheEarlierLayers()
        {
            var world = Build(CharClass, overlay: true);
            var feel = world.Gameplay.Feel!;

            Assert.Equal(CharacterAccelMs, feel.Resolver.Resolve(PlayerId).Judging.GetNumber(FeelFieldNames.AccelMs), 6);
            var chain = feel.Resolver.GetProvenance(PlayerId, FeelFieldNames.AccelMs);
            Assert.Contains(chain, e => e.Layer == (int)FeelLayer.Character && e.SourceId == CharacterRow);
        }

        // ------------------------------------------------------------------ 不变量

        [Fact]
        public void ClassWithoutFeelRefs_PlayerHasNoArchetypeOrCharacterLayer_ExactlyAsBefore()
        {
            foreach (var overlay in new[] { false, true })
            {
                var world = Build(PlainClass, overlay);
                var feel = world.Gameplay.Feel!;
                feel.Resolver.Resolve(PlayerId);
                for (var f = 0; f < feel.Resolver.Fields.Count; f++)
                {
                    foreach (var entry in feel.Resolver.GetProvenance(PlayerId, feel.Resolver.Fields[f].Name))
                    {
                        Assert.NotEqual((int)FeelLayer.Archetype, entry.Layer);
                        Assert.NotEqual((int)FeelLayer.Character, entry.Layer);
                    }
                }
            }
        }

        [Fact]
        public void ClassRowsWithoutRefs_ResolveSameValues_WhetherOrNotOtherClassesDeclareRefs()
        {
            var bare = Build(PlainClass, overlay: false).Gameplay.Feel!.Resolver.Resolve(PlayerId).Judging;
            var withOverlay = Build(PlainClass, overlay: true).Gameplay.Feel!.Resolver.Resolve(PlayerId).Judging;
            var fields = Build(PlainClass, overlay: false).Gameplay.Feel!.Resolver.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                var def = fields[i];
                if (def.Half != FeelHalf.Judging || def.Kind != FeelFieldKind.Number) continue;
                Assert.Equal(bare.TryGetNumber(def.Name, out var a), withOverlay.TryGetNumber(def.Name, out var b));
                Assert.Equal(a, b);
            }
        }

        [Fact]
        public void SaveLoaded_RereadsPlayerClassRefs_WhenTheLoadedClassChanged()
        {
            // 读档会改写 PlayerUnit.ArchetypeId 而不经任何手感事件；读档完成后玩家缓存必须按新职业重算（缓存里是旧职业的值就是错的）。
            var world = Build(PlainClass, overlay: true);
            var resolver = world.Gameplay.Feel!.Resolver;
            var plain = resolver.Resolve(PlayerId).Judging.GetNumber(FeelFieldNames.AccelMs);

            world.Player.ArchetypeId = HeavyClass;
            Assert.Equal(plain, resolver.Resolve(PlayerId).Judging.GetNumber(FeelFieldNames.AccelMs)); // 没有事件，缓存仍是旧值（这正是需要订阅的原因）
            world.Bus.PublishImmediate(new SaveLoadedEvent(new Id("save.slot.pc")));
            Assert.True(resolver.Resolve(PlayerId).Judging.GetNumber(FeelFieldNames.AccelMs) > plain);
        }
    }
}

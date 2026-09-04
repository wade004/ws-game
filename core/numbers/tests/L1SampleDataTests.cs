using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Numbers.Archetype;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Xunit;

// 判断记录：Progression 与 Archetype 两个模块各自声明了同名委托 StatModifierWriter（签名相同，
// 但属于不同命名空间——两模块并行开发、互不引用对方类型，见各自模块 README"依赖"一节），
// 同时 using 两个命名空间会在裸写 "StatModifierWriter" 时产生二义性，这里用别名区分。
using ProgStatModifierWriter = Core.Numbers.Progression.StatModifierWriter;
using ProgStatModifierRemover = Core.Numbers.Progression.StatModifierRemover;
using ArchStatModifierWriter = Core.Numbers.Archetype.StatModifierWriter;

namespace Tests.Numbers
{
    /// <summary>
    /// L1 整理任务"事项三"：把 L0 数据注册表 + L1 五个数值模块（stat_block/power_set/
    /// progression/archetype/faction）接到一起，用 <c>data/_sample</c> 的真实示例数据端到端跑一遍
    /// "注册表联调"——验证 <c>LoadAll()</c> 在全部 L1 schema/校验规则登记齐全后 0 错误 0 警告，
    /// 并验证跨模块的具名委托装配（<see cref="StatLookup"/>/<see cref="ProgStatModifierWriter"/>/
    /// <see cref="ArchStatModifierWriter"/>/<see cref="PowerRegistrar"/>）在真实数据上算出的最终
    /// 数值符合预期。
    /// <para>
    /// 与 <c>core/foundation/data_registry/tests/DataRegistryTests.cs</c> 的 real-sample 测试
    /// 分工：那边是 L0 自己的冒烟测试，不引用任何 L1 模块类型，data/_sample 里 L1 贡献的表按
    /// "无 schema 表"加载（只做信封/主键检查）；本类是 L1 的联调测试，真实注册全部 L1
    /// <c>TableSchema</c>/<c>IValidationRule</c>，做完整的字段级校验。
    /// </para>
    /// </summary>
    public class L1SampleDataTests
    {
        private static readonly Id ClassSampleA = new Id("arch.class.sample_a");
        private static readonly Id RaceSampleA = new Id("arch.race.sample_a");
        private static readonly Id CurveSample = new Id("prog.curve.sample");
        private static readonly Id XpKillSample = new Id("prog.xp.kill_sample");
        private static readonly Id StatStrength = new Id("stat.strength");
        private static readonly Id StatStamina = new Id("stat.stamina");
        private static readonly Id StatCritRating = new Id("stat.crit_rating");
        private static readonly Id PowerHealth = new Id("arch.power.health");
        private static readonly Id PowerMana = new Id("arch.power.mana");
        private static readonly Id FacPlayer = new Id("fac.player");
        private static readonly Id FacWildlife = new Id("fac.wildlife");

        // -----------------------------------------------------------------
        // 定位 data/_sample（同 DataRegistryTests.cs 的做法：用 [CallerFilePath] 拿到本源文件
        // 在磁盘上的绝对路径，不依赖运行期程序集目录——测试按任务书要求用 --artifacts-path
        // 输出到仓库外的 scratch 目录。本文件路径固定是
        // <repoRoot>/core/numbers/tests/L1SampleDataTests.cs，向上 3 级
        // （tests → numbers → core）即仓库根。
        // -----------------------------------------------------------------
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (int i = 0; i < 3; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        private static FileSystemDataSource BuildRealSampleSource()
        {
            var repoRoot = FindRepoRoot();
            var sampleRoot = Path.Combine(repoRoot, "data", "_sample");
            var fs = new StubEngine().FileSystem;

            foreach (var file in Directory.GetFiles(sampleRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(sampleRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic("data/_sample/" + rel, File.ReadAllText(file));
            }

            return new FileSystemDataSource(fs, "data/_sample");
        }

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
                new EventDefinition(PowerEventKeys.Changed, "power",
                    new[] { "unitId", "powerType", "oldValue", "newValue" }),
                new EventDefinition(PowerEventKeys.Depleted, "power",
                    new[] { "unitId", "powerType" }),
                new EventDefinition(ProgressionEventKeys.LevelUp, "progression",
                    new[] { "unitId", "oldLevel", "newLevel" }),
                new EventDefinition(ProgressionEventKeys.XpGained, "progression",
                    new[] { "unitId", "sourceId", "amount" }),
                new EventDefinition(ArchetypeEventKeys.Applied, "archetype",
                    new[] { "unitId", "classId", "raceId" }),
                new EventDefinition(FactionEventKeys.RelationChanged, "faction",
                    new[] { "from", "to", "oldReaction", "newReaction" }),
            });
            return new EventBus(catalog);
        }

        /// <summary>装好一整套 L0+L1 的世界：注册全部 L0 内置 schema 与 L1 五个模块的
        /// <c>TableSchema</c>/<c>IValidationRule</c>，用 <c>DeclareReference</c> 补上模块 README
        /// 标注"待动态声明"的引用，<c>LoadAll()</c> 后装配五个 L1 host（跨模块具名委托全部接到
        /// 同一个 <see cref="StatHost"/> 上）。</summary>
        private static (
            IDataRegistry Registry,
            ValidationReport Report,
            StatHost StatHost,
            PowerHost PowerHost,
            ProgressionHost ProgressionHost,
            ArchetypeRegistry ArchetypeRegistry,
            FactionMatrix FactionMatrix,
            IEventBus Bus) BuildWorld()
        {
            var source = BuildRealSampleSource();
            var bus = MakeBus();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());

            // L0 内置 schema。
            foreach (var schema in BuiltinSchemas.All) registry.RegisterSchema(schema);
            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);
            registry.RegisterSchema(InputActionSchema.Table);

            // L1 五个模块的全部 TableSchema。
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.RatingConversion);
            registry.RegisterSchema(PowerSchemas.PowerType);
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(ProgSchemas.XpSource);
            registry.RegisterSchema(ArchSchemas.Class);
            registry.RegisterSchema(ArchSchemas.Race);
            registry.RegisterSchema(ArchSchemas.TalentTree);
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);

            // L1 五个模块的全部 IValidationRule（power_set/faction 没有模块专属规则）。
            registry.RegisterValidationRule(new StatDefinitionValidationRule());
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            registry.RegisterValidationRule(new ArchTalentTreeCycleValidationRule());

            // 补上 archetype 模块 README"设计要点与判断记录"第 2 条标注的"待动态声明"引用。
            // arch.class.primary_stat 是标量 Id 字段，可以声明：
            registry.DeclareReference("arch.class", "primary_stat", "stat.definition");
            // arch.class.power_types（IdList）与 arch.power_type.max_source.stat（嵌在 Object
            // 字段内层）都不可声明——IDataRegistry.DeclareReference 只支持"某表某个标量 Id 字段
            // 整体指向另一张表"（内部按 DataRecord.TryGetString 取值，遇到数组/嵌套对象会直接
            // 取值失败、静默跳过，不产生任何校验效果，见 DataRegistry.RunFieldValidation 对
            // _declaredReferences 的处理），这两处引用完整性目前无法通过本机制声明，只能等对应
            // 字段本身在未来改声明为受支持的字段形状（或 DeclareReference 扩展支持这两种形状）
            // 后再补上。

            var report = registry.LoadAll();

            var statHost = new StatHost(registry, bus, new StatHostOptions());

            StatLookup statLookup = (unitId, stat) => statHost.GetStat(unitId, stat);
            var powerTypes = registry.GetAll("arch.power_type").Select(r => new PowerTypeDefinition(r)).ToList();
            var powerHost = new PowerHost(powerTypes, bus, statLookup);

            void WriteFlatModifier(Id unitId, Id stat, string op, double value, Id sourceId)
            {
                if (op != "flat")
                {
                    throw new InvalidOperationException($"未知的 StatModifier op \"{op}\"（本测试装配只处理 \"flat\"）");
                }
                statHost.AddModifier(unitId, new StatModifier(stat, StatModifierOp.Flat, value, sourceId));
            }

            ProgStatModifierWriter progressionWriter = WriteFlatModifier;
            ProgStatModifierRemover progressionRemover = (unitId, sourceId) => statHost.RemoveModifiersBySource(unitId, sourceId);
            var progressionHost = new ProgressionHost(registry, bus, progressionWriter, progressionRemover);

            StatBaseWriter archBaseWriter = (unitId, stat, value) => statHost.SetBase(unitId, stat, value);
            ArchStatModifierWriter archModifierWriter = WriteFlatModifier;
            PowerRegistrar archPowerRegistrar = (unitId, types) => powerHost.RegisterUnit(unitId, types);
            var archetypeRegistry = new ArchetypeRegistry(registry, bus, archBaseWriter, archModifierWriter, archPowerRegistrar);

            var factionMatrix = new FactionMatrix(registry, bus);

            return (registry, report, statHost, powerHost, progressionHost, archetypeRegistry, factionMatrix, bus);
        }

        // -----------------------------------------------------------------
        // 1. LoadAll 0 错误 0 警告
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_SampleData_ZeroErrorsAndZeroWarnings()
        {
            var world = BuildWorld();

            Assert.Equal(0, world.Report.ErrorCount);
            Assert.Equal(0, world.Report.WarningCount);
            Assert.False(world.Report.IsBlocking);
        }

        // -----------------------------------------------------------------
        // 2. ApplyTo：base_stats（绝对值）+ 种族 flat 修正 → 最终属性值
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyTo_WritesBaseStatsAndRaceModifiers_FinalValuesReflectBoth()
        {
            var world = BuildWorld();
            var unit = new Id("unit.applyto_1");
            world.StatHost.RegisterUnit(unit);

            world.ArchetypeRegistry.ApplyTo(unit, ClassSampleA, RaceSampleA);

            // base_stats: strength=10, stamina=8；arch.race.sample_a.stat_mods 各 +1（flat）。
            Assert.Equal(11.0, world.StatHost.GetStat(unit, StatStrength), 10);
            Assert.Equal(9.0, world.StatHost.GetStat(unit, StatStamina), 10);
        }

        // -----------------------------------------------------------------
        // 3. ApplyTo 注册资源池：health 上限引用 stamina（StatLookup 跨模块装配）
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyTo_RegistersPowerTypes_HealthCapReferencesStamina()
        {
            var world = BuildWorld();
            var unit = new Id("unit.applyto_2");
            world.StatHost.RegisterUnit(unit);

            world.ArchetypeRegistry.ApplyTo(unit, ClassSampleA, RaceSampleA);

            Assert.True(world.PowerHost.HasPower(unit, PowerHealth));
            Assert.True(world.PowerHost.HasPower(unit, PowerMana));

            // arch.power.health.max_source = {kind: stat, stat: stat.stamina}；此时最终 stamina=9。
            Assert.Equal(9.0, world.PowerHost.GetPowerMax(unit, PowerHealth), 10);
            Assert.Equal(9.0, world.PowerHost.GetPower(unit, PowerHealth), 10); // start_full 默认 true

            // arch.power.mana.max_source = {kind: fixed, value: 100}。
            Assert.Equal(100.0, world.PowerHost.GetPowerMax(unit, PowerMana), 10);
            Assert.Equal(100.0, world.PowerHost.GetPower(unit, PowerMana), 10);
        }

        // -----------------------------------------------------------------
        // 4. GrantFromSource 升级 → 成长写入 → 属性再变 → RecomputeMax 后资源上限跟着变
        // -----------------------------------------------------------------

        [Fact]
        public void GrantFromSource_LevelUp_AppliesGrowth_StatsAndPowerCapChange()
        {
            var world = BuildWorld();
            var unit = new Id("unit.growth_1");
            world.StatHost.RegisterUnit(unit);
            world.ArchetypeRegistry.ApplyTo(unit, ClassSampleA, RaceSampleA);
            world.ProgressionHost.RegisterUnit(unit, CurveSample);

            Assert.Equal(1, world.ProgressionHost.GetLevel(unit));
            Assert.Equal(9.0, world.PowerHost.GetPowerMax(unit, PowerHealth), 10); // 升级前：health 上限=stamina=9

            // prog.xp.kill_sample.base_xp=50；两次授予共 100 = level1.xp_to_next，恰好跨到 2 级。
            world.ProgressionHost.GrantFromSource(unit, XpKillSample);
            world.ProgressionHost.GrantFromSource(unit, XpKillSample);

            Assert.Equal(2, world.ProgressionHost.GetLevel(unit));

            // prog.curve.sample level2.growth: stat.strength+=2, stat.stamina+=3（来源 prog.growth）。
            Assert.Equal(13.0, world.StatHost.GetStat(unit, StatStrength), 10); // 11 + 2
            Assert.Equal(12.0, world.StatHost.GetStat(unit, StatStamina), 10);  // 9 + 3

            // PowerHost 不会自动感知 StatHost 的变化（跨模块无隐式依赖），需调用方显式
            // RecomputeMax；调用后 health 上限跟随新的 stamina 变化，当前值未超新上限，不夹取。
            world.PowerHost.RecomputeMax(unit);
            Assert.Equal(12.0, world.PowerHost.GetPowerMax(unit, PowerHealth), 10);
            Assert.Equal(9.0, world.PowerHost.GetPower(unit, PowerHealth), 10); // 当前值不因上限提高而自动回满
        }

        // -----------------------------------------------------------------
        // 5. 阵营矩阵：显式登记行覆盖默认反应，矩阵不对称
        // -----------------------------------------------------------------

        [Fact]
        public void FactionMatrix_ExplicitRowOverridesDefault_HostileAndAsymmetric()
        {
            var world = BuildWorld();

            // fac.reaction_matrix 显式登记 (fac.player -> fac.wildlife) = hostile，
            // 覆盖 fac.player 自己的 default_reaction=friendly。
            Assert.Equal(Reaction.Hostile, world.FactionMatrix.GetReaction(FacPlayer, FacWildlife));
            Assert.True(world.FactionMatrix.IsHostile(FacPlayer, FacWildlife));

            // 反方向没有显式登记，回退到 fac.wildlife 自己的 default_reaction=neutral
            // （矩阵不要求对称）。
            Assert.Equal(Reaction.Neutral, world.FactionMatrix.GetReaction(FacWildlife, FacPlayer));
        }

        // -----------------------------------------------------------------
        // 6. 事项一新增的 LevelLookup：接一个真实的 IProgressionHost.GetLevel，
        //    验证同一评级原始值随单位等级变化换算出不同结果（跨模块委托装配的端到端验证）。
        // -----------------------------------------------------------------

        [Fact]
        public void RatingConversion_LevelLookupWiredToProgressionHost_ChangesAsUnitLevelsUp()
        {
            var world = BuildWorld();
            var unit = new Id("unit.rating_bonus");

            // crit_rating 的成长写入会经由 progressionWriter 落到 world.StatHost 上，
            // 因此该单位也要在 world.StatHost 上注册（即便本用例不关心它的 strength/stamina）。
            world.StatHost.RegisterUnit(unit);
            world.ProgressionHost.RegisterUnit(unit, CurveSample);

            // 单独构造第二个 StatHost 开启评级换算，LevelLookup 接到同一个 ProgressionHost 实例
            // 的 GetLevel——演示"事项一"新增的委托如何在不引入编译期依赖的前提下接住真实等级来源。
            var statHost2 = new StatHost(world.Registry, world.Bus, new StatHostOptions
            {
                EnableRatingConversion = true,
                LevelLookup = uid => world.ProgressionHost.GetLevel(uid),
            });
            statHost2.RegisterUnit(unit);
            statHost2.SetBase(unit, StatCritRating, 100.0);

            // stat.rating.crit entries: level1 ppp=50，level3 ppp=20（见 data/_sample/stat/
            // stat.rating_conversion.json）。1 级（默认起始等级）在 entries[0] 端点上：
            // percent = 100 / 50 = 2。
            Assert.Equal(2.0, statHost2.GetStat(unit, StatCritRating), 10);

            // 授予 300 点经验（50*1*6，一次性用 multiplier=6）：level1→2 消耗 100，level2→3
            // 消耗 200，恰好升到曲线满级 3 级，残余 0。
            world.ProgressionHost.GrantFromSource(unit, XpKillSample, multiplier: 6);
            Assert.Equal(3, world.ProgressionHost.GetLevel(unit));

            // 判断记录（本用例发现的既有缓存行为，非事项一改动引入）：StatHost 按"变更路径"
            // （SetBase/AddModifier/RemoveModifiersBySource）驱动缓存重算（见 StatHost 类文档
            // "缓存与事件"一节），LevelLookup 背后的等级来自 StatHost 之外的 ProgressionHost，
            // 单独升级不会让 StatHost 知道需要重算——与 PowerHost.RecomputeMax 必须被显式调用
            // 才能感知 StatHost 变化（见上面第 4 个用例）是同一种"跨模块无隐式依赖"的设计后果。
            // 调用方必须在等级变化后主动触发一次受影响属性的重算（这里用 SetBase 同值重置）。
            statHost2.SetBase(unit, StatCritRating, 100.0);

            // 3 级落在 entries[1] 端点上：percent = 100 / 20 = 5。同一评级原始值 100，
            // 换算结果随单位等级变化——这正是事项一裁定"level 就是单位等级"的落地效果。
            Assert.Equal(5.0, statHost2.GetStat(unit, StatCritRating), 10);
        }
    }
}

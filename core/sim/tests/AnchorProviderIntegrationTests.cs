using System;
using System.IO;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-3a（ADR-0035 决策 4 锚点表接入）：验证锚点接入后 <c>skill_budget_*</c>/
    /// <c>item_grant_value_exceeds_share</c> 两条预算校验规则确有真实求值——嵌入数据集自身零告警
    /// （<c>core/sim/tests/data/README.md</c>"校验命令与结果"一节已证明），本文件额外叠加一条第三方
    /// 数据根（只含一条刻意超预算的 <c>skill.def</c> 行，multi-root 合并加载，见
    /// <c>DataRegistry.LoadAll(IReadOnlyList&lt;IDataSource&gt;)</c> 类型级判断记录"合并规则"），确认
    /// 装配根会因此真正阻断——这条阻断只有在 <see cref="Core.Sim.AnchorTableSkillBudgetAnchorProvider"/>
    /// 被真实装配、<c>SkillBudgetAnalyzer.Analyze</c> 用真实锚点秒伤/期望属性算出比值时才会发生（未接入
    /// 锚点、<c>anchorProvider == null</c> 时 <c>SkillBudgetValidationRule.Validate</c> 整条 <c>yield
    /// break</c>，见该规则类型判断记录，不会产生任何问题）。
    /// </summary>
    public sealed class AnchorProviderIntegrationTests
    {
        private const string OverBudgetSkillJson = @"{
  ""table"": ""skill.def"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""skill.sim_test_overbudget"",
      ""school"": ""school.physical"",
      ""kind"": ""active"",
      ""range"": 5,
      ""cast_time"": 0,
      ""respects_gcd"": false,
      ""target_shape_ref"": ""target.chain.sim_nearest_enemy"",
      ""effects"": [
        {
          ""kind"": ""school_damage"",
          ""params"": {
            ""base_value"": 100000,
            ""scaling"": [ { ""stat"": ""stat.attack_power"", ""coefficient"": 500.0 } ],
            ""school"": ""school.physical""
          }
        }
      ]
    }
  ]
}";

        /// <summary>惯例同 <see cref="SimTestWorldFactory"/>——本方法只是额外把
        /// <see cref="OverBudgetSkillJson"/> 作为第三个数据根叠加进去，其余装配参数与
        /// <see cref="SimTestWorldFactory.BuildFromEmbeddedDataset"/> 完全一致。</summary>
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 3; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException();
            }
            return dir.FullName;
        }

        [Fact]
        public void EmbeddedDataset_PlusOverBudgetSkill_TriggersRealSkillBudgetEvaluation()
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();

            void CopyDisk(string relativeRoot)
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

            CopyDisk("data/_framework");
            CopyDisk("core/sim/tests/data");

            const string extraRoot = "test/_overbudget_extra";
            fs.WriteTextAtomic(extraRoot + "/skill/skill.def.json", OverBudgetSkillJson);

            var frameworkSource = new FileSystemDataSource(fs, "data/_framework");
            var embeddedSource = new FileSystemDataSource(fs, "core/sim/tests/data");
            var extraSource = new FileSystemDataSource(fs, extraRoot);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
                {
                    DataSources = new IDataSource[] { frameworkSource, embeddedSource, extraSource },
                    Seed = 20260916400UL,
                    FileSystem = fs,
                    MapId = SimTestWorldFactory.EmbeddedMapId,
                    PlayerId = SimTestWorldFactory.PlayerId,
                    PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                    PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                    PlayerLevel = 1,
                    GameId = SimTestWorldFactory.EmbeddedGameId,
                    StepSeconds = SimTestWorldFactory.StepSeconds,
                    FailOnUnknownTable = false,
                }));

            Assert.Contains("skill_budget", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>回归：<c>data/_framework</c> + <c>data/_sample</c>（既有 <see
        /// cref="SimTestWorldFactory.BuildWorld"/> 路径，<c>data/_sample</c> 本身也含 5 行 <c>sim.anchor</c>
        /// 演示数据，见 <c>core/sim/README.md</c>"HeadlessWorldBuilder 自动装配锚点提供者"判断记录）
        /// 在锚点接入后仍能正常装配、不阻断——data/_sample 曾经因锚点接入产生偏离警告的三条技能
        /// （<c>skill.sample_strike</c>/<c>skill.sample_rest</c>/<c>skill.sample_burst</c>）经
        /// T-N6-7 把 <c>sim.anchor.l1.dps</c> 从占位值 10 校准为 45 后，比值已全部回落到
        /// <c>skill.budget_rule.default</c> 玩家档带宽内，不再产生任何 <c>skill_budget_deviation</c>
        /// 警告（不只是"已确认警告不阻断"，是真的不再警告，见 <c>core/sim/README.md</c>"T-N6-7
        /// 判断记录"40）；<c>skill.sample_rest</c>/<c>skill.sample_burst</c> 的 <c>budget_note</c>
        /// 字段本身按该判断记录保留（记录历史设计意图，当前不被任何规则分支实际读取）。</summary>
        [Fact]
        public void SampleData_StillLoadsWithoutBlockingAfterAnchorWiring()
        {
            var world = SimTestWorldFactory.BuildWorld(seed: 20260916401UL);

            Assert.False(world.LoadReport.IsBlocking);
        }
    }
}

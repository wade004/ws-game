using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：<c>target.chain_def.
    /// overflow_policy</c> 三态（<c>truncate</c>/<c>split</c>/<c>cap</c>）与 <c>ITargetHost.
    /// ResolveWithCoefficients</c> 新重载。复用 <see cref="TargetHostTests"/> 的公共夹具（<c>Build</c>/
    /// <c>Fixture</c> 等已改为 <c>internal</c>，见该类型判断记录）。
    /// <para>
    /// 判断记录（系数定义为临时判断，待设计层确认）：06 第 3.7 节修订段原文只给出策略名字与默认值，
    /// 未展开到"每个目标分配系数"的精确公式；本测试锁定的系数定义见
    /// <see cref="Core.Rules.Common.TargetOverflowPolicy"/> 判断记录——<c>truncate</c> 与改动前既有
    /// 截断行为逐一对应；<c>split</c> 总量守恒为"<c>max_targets</c> 个目标的满额值"（系数 =
    /// max_targets / 命中数）；<c>cap</c> 总量硬封顶为"单个目标的满额值"（系数 = 1 / 命中数）。
    /// </para>
    /// </summary>
    public class T_N3_8_TargetOverflowPolicyTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");

        /// <summary>登记 <paramref name="count"/> 个沿 +X 轴依次远离施法者的敌对单位（<c>unit.enemy_0</c>
        /// 最近），并声明一条 <c>all_in_shape</c> + <c>relation:hostile</c> + 按距离升序排序的链——
        /// 排序保证 <c>truncate</c> 策略"按既有排序截断"的结果确定可断言（同 <see cref="TargetHostTests"/>
        /// 第 7 组 <c>AllInShape_WithMaxTargets_LimitsResultCount</c> 的候选构造方式，额外加
        /// <c>sort_by</c> 锁定顺序）。</summary>
        private static TargetHostTests.Fixture BuildEnemies(int count, int? maxTargets, string? overflowPolicy)
        {
            var maxTargetsField = maxTargets.HasValue ? $@", ""max_targets"": {maxTargets.Value}" : string.Empty;
            var overflowPolicyField = overflowPolicy != null ? $@", ""overflow_policy"": ""{overflowPolicy}""" : string.Empty;
            var rows = $@"[
                {{ ""id"": ""target.chain.overflow_test"", ""source"": ""all_in_shape"",
                  ""shape"": {{ ""kind"": ""circle"", ""radius"": 100 }},
                  ""filters"": [""relation:hostile""],
                  ""sort_by"": {{ ""key"": ""distance"" }}{maxTargetsField}{overflowPolicyField} }}
            ]";

            return TargetHostTests.Build(rows, (units, spatial, powers, threat) =>
            {
                units.Add(new Id("unit.caster"), HeroFaction, new Vec2(0, 0));
                for (int i = 0; i < count; i++)
                {
                    var id = new Id($"unit.enemy_{i}");
                    units.Add(id, MonsterFaction, new Vec2(i + 1, 0));
                    spatial.Register(id, new Vec2(i + 1, 0), 0);
                }
            });
        }

        private static Id CasterId => new Id("unit.caster");
        private static Id ChainId => new Id("target.chain.overflow_test");

        private static Id Enemy(int i) => new Id($"unit.enemy_{i}");

        // -----------------------------------------------------------------
        // truncate（默认）：命中目标数 ≤ cap 且系数各 1
        // -----------------------------------------------------------------

        [Fact]
        public void Truncate_CandidatesWithinCap_AllHitWithCoefficientOne()
        {
            var fx = BuildEnemies(count: 3, maxTargets: 5, overflowPolicy: "truncate");

            var resolution = fx.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Truncate, resolution.Policy);
            Assert.Equal(5, resolution.Cap);
            Assert.Equal(3, resolution.Targets.Count);
            Assert.All(resolution.Targets, t => Assert.Equal(1.0, t.Coefficient));
            Assert.Equal(new[] { Enemy(0), Enemy(1), Enemy(2) }, resolution.Targets.Select(t => t.Target));
        }

        [Fact]
        public void Truncate_CandidatesExceedCap_KeepsNearestCapWithCoefficientOne()
        {
            var fx = BuildEnemies(count: 5, maxTargets: 2, overflowPolicy: "truncate");

            var resolution = fx.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Truncate, resolution.Policy);
            Assert.Equal(2, resolution.Targets.Count);
            Assert.All(resolution.Targets, t => Assert.Equal(1.0, t.Coefficient));
            // 按距离升序排序后截断到前 2 个（unit.enemy_0/1 最近），与改动前 ApplyMaxTargets 的既有
            // 截断行为逐一对应。
            Assert.Equal(new[] { Enemy(0), Enemy(1) }, resolution.Targets.Select(t => t.Target));
        }

        // -----------------------------------------------------------------
        // split（平摊）：全部候选命中，Σ 系数 = max_targets
        // -----------------------------------------------------------------

        [Fact]
        public void Split_CandidatesWithinCap_AllHitWithCoefficientOne()
        {
            var fx = BuildEnemies(count: 3, maxTargets: 5, overflowPolicy: "split");

            var resolution = fx.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Split, resolution.Policy);
            Assert.Equal(3, resolution.Targets.Count);
            Assert.All(resolution.Targets, t => Assert.Equal(1.0, t.Coefficient));
        }

        [Fact]
        public void Split_CandidatesExceedCap_AllHitWithDilutedCoefficientSummingToCap()
        {
            var fx = BuildEnemies(count: 5, maxTargets: 2, overflowPolicy: "split");

            var resolution = fx.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Split, resolution.Policy);
            // 不按数量截断——全部 5 个候选都命中，与 truncate 策略同参数下只保留 2 个不同。
            Assert.Equal(5, resolution.Targets.Count);
            Assert.All(resolution.Targets, t => Assert.Equal(2.0 / 5.0, t.Coefficient, precision: 12));
            Assert.Equal(2.0, resolution.Targets.Sum(t => t.Coefficient), precision: 9);
        }

        // -----------------------------------------------------------------
        // cap（总量封顶）：全部候选命中，Σ 系数 = 1
        // -----------------------------------------------------------------

        [Fact]
        public void Cap_CandidatesWithinCap_AllHitWithCoefficientOne()
        {
            var fx = BuildEnemies(count: 3, maxTargets: 5, overflowPolicy: "cap");

            var resolution = fx.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Cap, resolution.Policy);
            Assert.Equal(3, resolution.Targets.Count);
            Assert.All(resolution.Targets, t => Assert.Equal(1.0, t.Coefficient));
        }

        [Fact]
        public void Cap_CandidatesExceedCap_AllHitWithDilutedCoefficientSummingToOne()
        {
            var fx = BuildEnemies(count: 5, maxTargets: 2, overflowPolicy: "cap");

            var resolution = fx.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Cap, resolution.Policy);
            Assert.Equal(5, resolution.Targets.Count);
            Assert.All(resolution.Targets, t => Assert.Equal(1.0 / 5.0, t.Coefficient, precision: 12));
            Assert.Equal(1.0, resolution.Targets.Sum(t => t.Coefficient), precision: 9);
        }

        // -----------------------------------------------------------------
        // 旧签名行为不变（硬性规则"禁止改旧 Resolve 签名"；验收标准"旧签名行为不变"）
        // -----------------------------------------------------------------

        [Fact]
        public void OldResolveSignature_DefaultTruncatePolicy_MatchesCoefficientProjection()
        {
            // 未声明 overflow_policy——缺省 truncate，与本字段引入之前的既有行为一致（见
            // TargetChainDef.OverflowPolicy 判断记录）。
            var withinCap = BuildEnemies(count: 3, maxTargets: 5, overflowPolicy: null);
            var overCap = BuildEnemies(count: 5, maxTargets: 2, overflowPolicy: null);

            var oldWithinCap = withinCap.Host.Resolve(ChainId, CasterId);
            var oldOverCap = overCap.Host.Resolve(ChainId, CasterId);

            var newWithinCap = withinCap.Host.ResolveWithCoefficients(ChainId, CasterId);
            var newOverCap = overCap.Host.ResolveWithCoefficients(ChainId, CasterId);

            Assert.Equal(TargetOverflowPolicy.Truncate, newWithinCap.Policy);
            Assert.Equal(TargetOverflowPolicy.Truncate, newOverCap.Policy);
            Assert.Equal(oldWithinCap, newWithinCap.Targets.Select(t => t.Target).ToList());
            Assert.Equal(oldOverCap, newOverCap.Targets.Select(t => t.Target).ToList());
            // 与改动前的既有截断结果逐一对应（同 TargetHostTests.AllInShape_WithMaxTargets_LimitsResultCount
            // 的候选/参数构造，独立核对具体目标身份而不只是数量）。
            Assert.Equal(new[] { Enemy(0), Enemy(1), Enemy(2) }, oldWithinCap);
            Assert.Equal(new[] { Enemy(0), Enemy(1) }, oldOverCap);
        }

        // -----------------------------------------------------------------
        // schema 覆盖：overflow_policy 非法取值在加载期即阻断（FieldKind.Enum 通用校验）
        // -----------------------------------------------------------------

        [Fact]
        public void Schema_InvalidOverflowPolicyValue_BlocksLoad()
        {
            const string rows = @"[
                { ""id"": ""target.chain.bad_overflow"", ""source"": ""all_in_shape"",
                  ""overflow_policy"": ""explode"" }
            ]";

            var bus = TargetingTestSupport.MakeBus();
            var strategies = TargetHostTests.DefaultStrategies();
            var registry = TargetingTestSupport.BuildChainRegistry(bus, rows, strategies);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
        }
    }
}

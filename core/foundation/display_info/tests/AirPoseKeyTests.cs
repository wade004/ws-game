using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>
    /// 空中姿势键（ADR-0130 追加决定，<see cref="AirPoseRequest"/>）：固定回落链
    /// <c>jump.rise|fall → jump → idle</c>、<c>jump.land → idle</c>、<c>hit.air → hit.launch → hit</c>、
    /// <c>attack.air[.族] → attack.air → attack[.族] → attack</c>。每条链各有"键存在时命中"与"缺项逐级回落"的用例，
    /// 期望值由约定的链顺序算出；不变量：链末端恒为兜底（不咨询可用性探针）、已有的地面键解析不受影响。
    /// </summary>
    public class AirPoseKeyTests
    {
        private static Dictionary<string, string> Table(params string[] keys) =>
            keys.ToDictionary(k => k, k => k, StringComparer.Ordinal);

        private static PoseResolution Resolve(AirPoseRequest request, params string[] keys)
        {
            PoseResolver.TryResolve(request, Table(keys), out _, out var resolution);
            return resolution;
        }

        // ---------- 链形状 ----------

        [Fact]
        public void Chains_FollowTheFixedConvention()
        {
            Assert.Equal(new[] { "jump.rise", "jump", "idle" }, AirPoseRequest.Jump("rise").Chain());
            Assert.Equal(new[] { "jump.fall", "jump", "idle" }, AirPoseRequest.Jump("fall").Chain());
            Assert.Equal(new[] { "jump.land", "idle" }, AirPoseRequest.Jump("land").Chain());
            Assert.Equal(new[] { "hit.air", "hit.launch", "hit" }, AirPoseRequest.HitAir().Chain());
            Assert.Equal(new[] { "attack.air", "attack" }, AirPoseRequest.AttackAir().Chain());
            Assert.Equal(
                new[] { "attack.air.greatsword", "attack.air", "attack.greatsword", "attack" },
                AirPoseRequest.AttackAir("greatsword").Chain());
        }

        [Fact]
        public void Jump_RejectsUnknownPhases()
        {
            Assert.Throws<ArgumentException>(() => AirPoseRequest.Jump("hover"));
        }

        [Fact]
        public void AllAirKeys_AreWellFormedPoseKeys()
        {
            var requests = new[]
            {
                AirPoseRequest.Jump("rise"), AirPoseRequest.Jump("fall"), AirPoseRequest.Jump("land"),
                AirPoseRequest.HitAir(), AirPoseRequest.AttackAir(), AirPoseRequest.AttackAir("2h"),
            };
            foreach (var key in requests.SelectMany(r => r.Chain()))
            {
                Assert.True(PoseKeys.IsWellFormed(key), key);
            }
        }

        // ---------- 键存在：命中最具体的 ----------

        [Theory]
        [InlineData("rise", "jump.rise")]
        [InlineData("fall", "jump.fall")]
        [InlineData("land", "jump.land")]
        public void Jump_PicksTheExactPhaseKey_WhenPresent(string phase, string expected)
        {
            var resolution = Resolve(AirPoseRequest.Jump(phase), "idle", "jump", "jump.rise", "jump.fall", "jump.land");
            Assert.True(resolution.Found);
            Assert.Equal(expected, resolution.CanonicalKey);
            Assert.Equal(0, resolution.FallbackDepth);
        }

        [Fact]
        public void HitAir_PicksHitAir_WhenPresent()
        {
            var resolution = Resolve(AirPoseRequest.HitAir(), "hit", "hit.launch", "hit.air");
            Assert.Equal("hit.air", resolution.CanonicalKey);
            Assert.Equal(0, resolution.FallbackDepth);
        }

        [Fact]
        public void AttackAir_PicksTheFamilyAirKey_ThenTheAirKey()
        {
            var keys = new[] { "attack", "attack.greatsword", "attack.air", "attack.air.greatsword" };
            Assert.Equal("attack.air.greatsword", Resolve(AirPoseRequest.AttackAir("greatsword"), keys).CanonicalKey);
            Assert.Equal("attack.air", Resolve(AirPoseRequest.AttackAir(), keys).CanonicalKey);
        }

        // ---------- 缺项：沿固定链逐级回落 ----------

        [Fact]
        public void Jump_MissingPhaseKey_FallsBackToJump_ThenIdle()
        {
            var toJump = Resolve(AirPoseRequest.Jump("rise"), "idle", "jump");
            Assert.Equal("jump", toJump.CanonicalKey);
            Assert.Equal(1, toJump.FallbackDepth);
            Assert.Equal(new[] { "jump.rise", "jump" }, toJump.Tried);

            var toIdle = Resolve(AirPoseRequest.Jump("fall"), "idle");
            Assert.Equal("idle", toIdle.CanonicalKey);
            Assert.Equal(2, toIdle.FallbackDepth);
            Assert.Equal(new[] { "jump.fall", "jump", "idle" }, toIdle.Tried);
        }

        [Fact]
        public void JumpLand_MissingKey_SkipsPlainJump_AndGoesStraightToIdle()
        {
            // 约定：jump.land → idle（落地不借用 jump 的空中剪辑）。
            var resolution = Resolve(AirPoseRequest.Jump("land"), "idle", "jump");
            Assert.Equal("idle", resolution.CanonicalKey);
            Assert.Equal(1, resolution.FallbackDepth);
        }

        [Fact]
        public void HitAir_MissingKey_FallsBackToHitLaunch_ThenHit()
        {
            var toLaunch = Resolve(AirPoseRequest.HitAir(), "hit", "hit.launch");
            Assert.Equal("hit.launch", toLaunch.CanonicalKey);
            Assert.Equal(1, toLaunch.FallbackDepth);

            var toHit = Resolve(AirPoseRequest.HitAir(), "hit");
            Assert.Equal("hit", toHit.CanonicalKey);
            Assert.Equal(2, toHit.FallbackDepth);
        }

        [Fact]
        public void AttackAir_MissingKeys_FallBackThroughAirThenFamilyThenBase()
        {
            var request = AirPoseRequest.AttackAir("greatsword");
            Assert.Equal("attack.air", Resolve(request, "attack", "attack.greatsword", "attack.air").CanonicalKey);
            Assert.Equal("attack.greatsword", Resolve(request, "attack", "attack.greatsword").CanonicalKey);
            var toBase = Resolve(request, "attack");
            Assert.Equal("attack", toBase.CanonicalKey);
            Assert.Equal(3, toBase.FallbackDepth);
        }

        [Fact]
        public void NothingFound_ReportsNotFound_WithTheWholeChainTried()
        {
            var resolution = Resolve(AirPoseRequest.HitAir(), "idle");
            Assert.False(resolution.Found);
            Assert.Equal(-1, resolution.FallbackDepth);
            Assert.Equal(new[] { "hit.air", "hit.launch", "hit" }, resolution.Tried);
        }

        // ---------- 可用性探针（冷加载） ----------

        [Fact]
        public void NotReadyAirClips_AreSkipped_ButTheChainEndIsNeverAskedAbout()
        {
            var asked = new List<string>();
            var table = Table("hit", "hit.launch", "hit.air");

            PoseResolver.TryResolve(AirPoseRequest.HitAir(), table, out _, out var resolution, key => { asked.Add(key); return false; });

            Assert.True(resolution.Found);
            Assert.Equal("hit", resolution.CanonicalKey);          // 非末端候选全部未就绪 → 落到末端兜底
            Assert.Equal(new[] { "hit.air", "hit.launch" }, asked); // 兜底键不咨询
        }

        // ---------- 不变量：地面键解析不受影响 ----------

        [Fact]
        public void GroundRequests_AreUnchangedByTheAirAdditions()
        {
            var table = Table("hit", "hit.launch", "hit.air", "attack", "attack.air");
            PoseResolver.TryResolve(PoseRequest.Base("hit"), table, out var hit, out var hitResolution);
            Assert.Equal("hit", hit);
            Assert.Equal(0, hitResolution.FallbackDepth);
            PoseResolver.TryResolve(new PoseRequest("attack", family: "2h"), table, out var attack, out _);
            Assert.Equal("attack", attack);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Rng;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// T-M16（测试覆盖梳理 2026-10-01）：<see cref="AiHost"/> 的 7 个 <c>throw</c> 点——构造期
    /// <c>CombatReentryRangeRatio</c> 越界、<c>RegisterUnit</c> 重复注册/未知 profile、
    /// <c>UnregisterUnit</c> 未注册、<c>SetRotation</c> 未知 rotation、<c>ParseCombatReturnPolicy</c>
    /// 未知取值、<c>GetState</c> 未注册单位——每个点一条 <see cref="Assert.Throws{T}(Action)"/> 用例，
    /// 并断言抛出后宿主状态未变（登记集合、行为态、轮换选择、待派发事件都与抛出前一致）。
    /// </summary>
    public class AiHostErrorPathTests
    {
        private static readonly Id Mob = new Id("unit.err_mob");
        private static readonly Id OtherMob = new Id("unit.err_other_mob");
        private static readonly Id Ghost = new Id("unit.err_never_registered");
        private static readonly Id ProfileA = new Id("ai.profile.err_a");
        private static readonly Id ProfileB = new Id("ai.profile.err_b");
        private static readonly Id RotationA = new Id("ai.rotation.err_a");
        private static readonly Id RotationB = new Id("ai.rotation.err_b");
        private static readonly Id SkillA = new Id("skill.err_a");
        private static readonly Id SkillB = new Id("skill.err_b");

        private const string ProfilesJson = @"[
            { ""id"": ""ai.profile.err_a"", ""perception_radius"": 10, ""leash_range"": 20,
              ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.err_a"" },
            { ""id"": ""ai.profile.err_b"", ""perception_radius"": 10, ""leash_range"": 20,
              ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.err_b"" }
        ]";

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.err_a"", ""entries"": [
                { ""priority"": 1, ""condition"": ""true"", ""skill_id"": ""skill.err_a"" } ] },
            { ""id"": ""ai.rotation.err_b"", ""entries"": [
                { ""priority"": 1, ""condition"": ""true"", ""skill_id"": ""skill.err_b"" } ] }
        ]";

        private static AiTestHarness BuildWithMob()
        {
            var harness = AiTestHarness.Build(ProfilesJson, RotationsJson);
            harness.Units.Add(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Units.Add(OtherMob, new Vec2(3, 0), AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileA, new Vec2(1, 2));
            harness.Host.ForceState(Mob, BehaviorState.Combat);
            harness.Dispatch();
            harness.StateChanges.Clear();
            return harness;
        }

        /// <summary>宿主可观测状态快照：登记集合、<paramref name="unit"/> 的行为态，以及
        /// <paramref name="unit"/> 当前轮换选中的技能（Combat 态下 <see cref="AiHost.Evaluate"/> 的结果，
        /// 反映内部 <c>RotationId</c>）；同时返回已派发的状态变更条数。</summary>
        private static (string Registered, BehaviorState State, Id? Skill, int StateChangeCount) Snapshot(
            AiTestHarness harness, Id unit)
        {
            var registered = string.Join(",", harness.Host.RegisteredUnitIds.Select(i => i.Value));
            var state = harness.Host.GetBehaviorState(unit);
            var skill = harness.Host.Evaluate(unit)?.SkillId;
            harness.Dispatch();
            return (registered, state, skill, harness.StateChanges.Count);
        }

        // ---- throw 1（AiHost.cs:138）：构造期 CombatReentryRangeRatio 越界 -----------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.5)]
        [InlineData(1.0000001)]
        [InlineData(2.0)]
        public void Constructor_CombatReentryRangeRatioOutOfRange_ThrowsAndLeavesOptionsUntouched(double ratio)
        {
            var options = new AiOptions { CombatReentryRangeRatio = ratio };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => AiTestHarness.Build(ProfilesJson, RotationsJson, options: options));

            Assert.Equal("options", ex.ParamName);
            Assert.Equal(ratio, options.CombatReentryRangeRatio); // 不静默夹紧
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(0.0001)]
        public void Constructor_CombatReentryRangeRatioAtBoundary_IsAccepted(double ratio)
        {
            var harness = AiTestHarness.Build(
                ProfilesJson, RotationsJson, options: new AiOptions { CombatReentryRangeRatio = ratio });

            Assert.Empty(harness.Host.RegisteredUnitIds);
        }

        // ---- throw 2（AiHost.cs:186）：重复注册 ------------------------------------------------

        [Fact]
        public void RegisterUnit_AlreadyRegistered_ThrowsAndKeepsOriginalState()
        {
            var harness = BuildWithMob();
            var before = Snapshot(harness, Mob);
            Assert.Equal(SkillA, before.Skill);

            // 第二次登记换了 profile（rotation 不同）与出生点：若被静默覆盖，Combat 态会被重置为
            // Idle、Evaluate 会改选 skill.err_b。
            Assert.Throws<InvalidOperationException>(
                () => harness.Host.RegisterUnit(Mob, ProfileB, new Vec2(9, 9)));

            Assert.Equal(before, Snapshot(harness, Mob));
            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
        }

        // ---- throw 3（AiHost.cs:191）：未知 profile ---------------------------------------------

        [Fact]
        public void RegisterUnit_UnknownProfile_ThrowsAndRegistersNothing()
        {
            var harness = BuildWithMob();
            var before = Snapshot(harness, Mob);

            var ex = Assert.Throws<ArgumentException>(
                () => harness.Host.RegisterUnit(OtherMob, new Id("ai.profile.does_not_exist"), Vec2.Zero));

            Assert.Equal("profileId", ex.ParamName);
            Assert.Equal(before, Snapshot(harness, Mob));
            Assert.DoesNotContain(OtherMob, harness.Host.RegisteredUnitIds);
            // 失败后该单位仍未登记，且之后用合法 profile 登记不受残留影响。
            Assert.Throws<InvalidOperationException>(() => harness.Host.GetBehaviorState(OtherMob));
            harness.Host.RegisterUnit(OtherMob, ProfileB, Vec2.Zero);
            Assert.Equal(BehaviorState.Idle, harness.Host.GetBehaviorState(OtherMob));
        }

        // ---- throw 4（AiHost.cs:213）：注销未注册单位 -------------------------------------------

        [Fact]
        public void UnregisterUnit_NotRegistered_ThrowsAndLeavesOtherUnitsIntact()
        {
            var harness = BuildWithMob();
            var before = Snapshot(harness, Mob);

            Assert.Throws<InvalidOperationException>(() => harness.Host.UnregisterUnit(Ghost));

            Assert.Equal(before, Snapshot(harness, Mob));
        }

        [Fact]
        public void UnregisterUnit_SecondCallAfterSuccess_Throws()
        {
            var harness = BuildWithMob();

            harness.Host.UnregisterUnit(Mob);

            Assert.Empty(harness.Host.RegisteredUnitIds);
            Assert.Throws<InvalidOperationException>(() => harness.Host.UnregisterUnit(Mob));
            Assert.Empty(harness.Host.RegisteredUnitIds);
        }

        // ---- throw 5（AiHost.cs:234）：SetRotation 未知 rotation --------------------------------

        [Fact]
        public void SetRotation_UnknownRotation_ThrowsAndKeepsCurrentRotation()
        {
            var harness = BuildWithMob();
            var before = Snapshot(harness, Mob);
            Assert.Equal(SkillA, before.Skill);

            var ex = Assert.Throws<ArgumentException>(
                () => harness.Host.SetRotation(Mob, new Id("ai.rotation.does_not_exist")));

            Assert.Equal("rotationId", ex.ParamName);
            Assert.Equal(before, Snapshot(harness, Mob));

            // 阳性对照：合法 rotation 确实会改变选择，证明上面"未变"不是因为 SetRotation 整体无效。
            harness.Host.SetRotation(Mob, RotationB);
            Assert.Equal(SkillB, harness.Host.Evaluate(Mob)?.SkillId);
        }

        // ---- throw 6（AiHost.cs:899）：ParseCombatReturnPolicy 未知取值 -------------------------

        /// <summary>绕过 <see cref="DataRegistry"/> 的 Enum 校验，直接构造一条 <c>combat_return_policy</c>
        /// 取值非法的 <c>ai.behavior_profile</c> 记录（正常数据管线里这一取值在 schema 校验阶段已被
        /// 拦截，本分支是 <see cref="AiHost"/> 装载期自己的防御性检查）。</summary>
        private sealed class RawProfileRegistryView : IDataRegistryView
        {
            private readonly List<DataRecord> _profiles = new List<DataRecord>();

            public RawProfileRegistryView(string returnPolicy)
            {
                var json = "{\"id\": \"ai.profile.raw\", \"perception_radius\": 10, \"leash_range\": 20, " +
                    "\"combat_return_policy\": \"" + returnPolicy + "\", \"rotation_ref\": \"ai.rotation.raw\"}";
                var obj = (JsonObject)JsonReader.Parse(json);
                _profiles.Add(new DataRecord(AiSchemas.BehaviorProfile, "ai.profile.raw", new Id("ai.profile.raw"), obj));
            }

            public DataRecord? Get(string table, string key) => null;

            public DataRecord? Get(string table, Id id) => null;

            public IReadOnlyList<DataRecord> GetAll(string table) =>
                table == AiSchemas.BehaviorProfile.Name ? _profiles : Array.Empty<DataRecord>();

            public IReadOnlyList<DataRecord> Query(string table, Core.Foundation.Expr.ExprNode predicate) =>
                Array.Empty<DataRecord>();

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) =>
                Array.Empty<DataRecord>();

            public IReadOnlyList<string> Tables => Array.Empty<string>();

            public TableSchema? GetSchema(string table) => null;
        }

        private static AiHost BuildOverView(IDataRegistryView view, Core.Foundation.EventBus.IEventBus bus) =>
            new AiHost(
                view, new FakeUnitAccess(), AiTestSupport.MakeFactionMatrix(bus), AiTestSupport.MakePowerHost(bus),
                new StubSpatialQuery(), new FakeSkillHost(), new FakeThreatTable(), new FakeExprHostFactory(),
                bus, new RngHost(1));

        [Theory]
        [InlineData("teleport_home")]
        [InlineData("")]
        [InlineData("STAY")] // 取值区分大小写
        public void Constructor_UnknownCombatReturnPolicy_ThrowsNamingTheValue(string policy)
        {
            var bus = AiTestSupport.CreateBus();

            var ex = Assert.Throws<ArgumentException>(() => BuildOverView(new RawProfileRegistryView(policy), bus));

            Assert.Equal("value", ex.ParamName);
            Assert.Contains("\"" + policy + "\"", ex.Message);
        }

        [Theory]
        [InlineData("return_to_spawn")]
        [InlineData("stay")]
        [InlineData("patrol")]
        public void Constructor_KnownCombatReturnPolicy_IsAccepted(string policy)
        {
            var bus = AiTestSupport.CreateBus();

            var host = BuildOverView(new RawProfileRegistryView(policy), bus);

            Assert.Empty(host.RegisteredUnitIds);
            host.RegisterUnit(Mob, new Id("ai.profile.raw"), Vec2.Zero);
            Assert.Equal(BehaviorState.Idle, host.GetBehaviorState(Mob));
        }

        // ---- throw 7（AiHost.cs:910）：GetState 未注册单位 --------------------------------------

        [Fact]
        public void EntryPointsReadingState_UnregisteredUnit_ThrowInvalidOperationAndChangeNothing()
        {
            var harness = BuildWithMob();
            var before = Snapshot(harness, Mob);

            Assert.Throws<InvalidOperationException>(() => harness.Host.GetBehaviorState(Ghost));
            Assert.Throws<InvalidOperationException>(() => harness.Host.GetTarget(Ghost));
            Assert.Throws<InvalidOperationException>(() => harness.Host.ForceState(Ghost, BehaviorState.Combat));
            Assert.Throws<InvalidOperationException>(() => harness.Host.SetRotation(Ghost, RotationA));
            Assert.Throws<InvalidOperationException>(() => harness.Host.Evaluate(Ghost));
            Assert.Throws<InvalidOperationException>(() => harness.Host.Step(Ghost, 0.1));

            // 没有隐式登记，也没有给已登记单位造成任何副作用/事件。
            Assert.DoesNotContain(Ghost, harness.Host.RegisteredUnitIds);
            Assert.Equal(before, Snapshot(harness, Mob));
        }
    }
}

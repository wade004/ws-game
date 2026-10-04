using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    /// <summary>
    /// 手感体系新增契约（S0 只定义契约，不含实现）：事件类型与 found.event_catalog 登记一致、
    /// <see cref="HitResult.Invulnerable"/> 只追加不改既有数值、<see cref="IActionStateQuery"/> 可被假实现。
    /// </summary>
    public class FeelContractsTests
    {
        private static readonly Id Actor = new Id("unit.actor");
        private static readonly Id Target = new Id("unit.target");
        private static readonly Id Cast = new Id("cast.one");

        private static string RepoRoot([CallerFilePath] string path = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(path)!);
            for (var i = 0; i < 4; i++) dir = dir.Parent!;
            return dir.FullName;
        }

        private static Dictionary<string, string[]> LoadCatalogFields()
        {
            var file = Path.Combine(RepoRoot(), "data", "_framework", "found", "found.event_catalog.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                result[row.GetProperty("key").GetString()!] =
                    row.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray();
            }
            return result;
        }

        private static string Pascal(string camel) => char.ToUpperInvariant(camel[0]) + camel.Substring(1);

        public static IEnumerable<object[]> FeelEvents()
        {
            var hit = new CombatHitConfirmedEvent(Cast, 0, Actor, Target, null, HitResult.Hit, 10, 0.1, false, false,
                new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 0), "light", 2, 3, HitReaction.Flinch);
            yield return new object[] { new ActionStartedEvent(Actor, new Id("skill.slash"), Cast, 0, 30, 0), "action.started" };
            yield return new object[] { new ActionPhaseChangedEvent(Actor, Cast, ActionPhase.Active), "action.phase_changed" };
            yield return new object[] { new ActionProjectileLaunchedEvent(Actor, Cast, 0), "action.projectile_launched" };
            yield return new object[] { new ActionProjectileEndedEvent(Actor, Cast, 0, ProjectileEndReason.Expired), "action.projectile_ended" };
            yield return new object[] { new ActionMarkerEvent(Actor, Cast, "hit"), "action.marker" };
            yield return new object[] { new ActionCancelledEvent(Actor, Cast, ActionCancelReason.Stagger), "action.cancelled" };
            yield return new object[] { new ActionFinishedEvent(Actor, Cast), "action.finished" };
            yield return new object[] { new ActionTargetAssistedEvent(Actor, Cast, Target, 5, 0), "action.target_assisted" };
            yield return new object[] { hit, "combat.hit_confirmed" };
            yield return new object[] { new CombatPoiseChangedEvent(Target, Actor, 4, 2, 4, 2, false), "combat.poise_changed" };
            yield return new object[] { new CombatPoiseRecoveredEvent(Target, 4), "combat.poise_recovered" };
            yield return new object[] { new CombatReactionAppliedEvent(Target, HitReaction.Stagger, Actor, Cast, 9), "combat.reaction_applied" };
            yield return new object[] { new UnitKnockedDownEvent(Target, 18, 12), "unit.knocked_down" };
            yield return new object[] { new UnitGetupStartedEvent(Target, 12, 6), "unit.getup_started" };
            yield return new object[] { new UnitGetupFinishedEvent(Target), "unit.getup_finished" };
            yield return new object[] { new FeelHitstopStartedEvent(new[] { Actor, Target }, 3, Cast), "feel.hitstop_started" };
            yield return new object[] { new FeelHitstopEndedEvent(new[] { Actor, Target }), "feel.hitstop_ended" };
            yield return new object[] { new InputBufferDroppedEvent(Actor, new Id("input.attack"), BufferDropReason.Expired), "input.buffer_dropped" };
        }

        [Theory]
        [MemberData(nameof(FeelEvents))]
        public void FeelEvent_KeyIsRegisteredInCatalog_AndPropertiesCoverEveryCatalogField(IEvent evt, string key)
        {
            Assert.Equal(key, evt.Key.Value);
            Assert.Contains(evt.Key, EventKeys.All);

            var catalog = LoadCatalogFields();
            Assert.True(catalog.TryGetValue(key, out var fields), key + " 未登记");
            var type = evt.GetType();
            foreach (var field in fields)
            {
                Assert.True(type.GetProperty(Pascal(field)) != null, $"{type.Name} 缺少登记字段 {field} 对应的属性 {Pascal(field)}");
            }
        }

        [Fact]
        public void ActionEvents_CarryTheirConstructorArguments()
        {
            var started = new ActionStartedEvent(Actor, new Id("skill.slash"), Cast, 2, 45, 0.5);
            Assert.Equal(2, started.ComboIndex);
            Assert.Equal(45, started.DurationTicks);
            Assert.Equal(0.5, started.ChargeRatio);
            // 既有 6 参数构造取缺省"带攻击"，新的 7 参数构造显式给出；反馈侧据此决定是否为动作开挥空窗口。
            Assert.True(started.IsAttack);
            Assert.False(new ActionStartedEvent(Actor, new Id("skill.dodge"), Cast, 0, 20, 0, false).IsAttack);

            var launched = new ActionProjectileLaunchedEvent(Actor, Cast, 2);
            Assert.Equal(2, launched.Segment);
            Assert.Equal(Cast, launched.CastInstanceId);
            var ended = new ActionProjectileEndedEvent(Actor, Cast, 2, ProjectileEndReason.Blocked);
            Assert.Equal(ProjectileEndReason.Blocked, ended.Reason);
            Assert.Equal(new[] { "Hit", "Blocked", "Expired", "Cleared" }, Enum.GetNames(typeof(ProjectileEndReason)));

            var marker = new ActionMarkerEvent(Actor, Cast, "hit", new Dictionary<string, string> { { "segment", "1" } });
            Assert.Equal("1", marker.Args["segment"]);
            Assert.Empty(new ActionMarkerEvent(Actor, Cast, "release").Args);

            var cancelled = new ActionCancelledEvent(Actor, Cast, ActionCancelReason.CancelInto, new Id("skill.next"));
            Assert.Equal(new Id("skill.next"), cancelled.NextSkillId);
            Assert.Null(new ActionCancelledEvent(Actor, Cast, ActionCancelReason.Death).NextSkillId);
        }

        [Fact]
        public void HitConfirmed_ExposesExprFieldsForTriggerAndMetadataConsumers()
        {
            var hit = new CombatHitConfirmedEvent(Cast, 1, Actor, Target, new Id("skill.slash"), HitResult.Crit, 40, 0.25, true, true,
                new Vec2(1, 2), new Vec2(0, 1), new Vec2(1, 0), "heavy", 4, 5, HitReaction.Knockback);
            Assert.True(hit.TryGetField("impactClass", out var impact));
            Assert.Equal("heavy", impact.AsString);
            Assert.True(hit.TryGetField("isKill", out var kill));
            Assert.True(kill.AsBool);
            Assert.True(hit.TryGetField("reaction", out var reaction));
            Assert.Equal("Knockback", reaction.AsString);
            Assert.False(hit.TryGetField("no_such_field", out _));
            Assert.Equal(4, hit.AttackerHitStopTicks);
        }

        [Fact]
        public void ReactionPhaseEvents_CarryTheirConstructorArguments_AndOldConstructorsKeepTheirMeaning()
        {
            var down = new UnitKnockedDownEvent(Target, 18, 12);
            Assert.Equal(Target, down.UnitId);
            Assert.Equal(18, down.DownedTicks);
            Assert.Equal(12, down.GetupTicks);
            var started = new UnitGetupStartedEvent(Target, 12, 6);
            Assert.Equal(12, started.GetupTicks);
            Assert.Equal(6, started.InvulnerableTicks);
            Assert.Equal(Target, new UnitGetupFinishedEvent(Target).UnitId);

            // 旧的 5 参数构造：整段时长都算硬直段，倒地/起身为 0（既有发出方的含义不变）。
            var legacy = new CombatReactionAppliedEvent(Target, HitReaction.Stagger, Actor, Cast, 9);
            Assert.Equal(9, legacy.StunTicks);
            Assert.Equal(0, legacy.DownedTicks);
            Assert.Equal(0, legacy.GetupTicks);
            var split = new CombatReactionAppliedEvent(Target, HitReaction.Knockdown, Actor, Cast, 30, 9, 12, 9);
            Assert.Equal(9, split.StunTicks);
            Assert.Equal(12, split.DownedTicks);
            Assert.Equal(9, split.GetupTicks);

            // 命中确认事件带受击反应附带决定；旧构造没有（IsSet=false，宿主退回现算）。
            var plain = new CombatHitConfirmedEvent(Cast, 0, Actor, Target, null, HitResult.Hit, 10, 0.1, false, false,
                new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 0), "light", 2, 3, HitReaction.Flinch);
            Assert.False(plain.ReactionDetail.IsSet);
            Assert.Equal(HitReaction.None, plain.AttackerReaction);
        }

        [Fact]
        public void HitResultInvulnerable_IsAppendedLast_AndExistingValuesAreUnchanged()
        {
            var values = Enum.GetValues(typeof(HitResult)).Cast<HitResult>().ToArray();
            Assert.Equal(HitResult.Invulnerable, values.Max());
            Assert.Equal((int)HitResult.Immune + 1, (int)HitResult.Invulnerable);
            // 既有成员数值不变（ABI 只加法）。
            var expected = new[] { "Miss", "Dodge", "Parry", "GlancingBlow", "Block", "Hit", "Crit", "Immune", "Invulnerable" };
            Assert.Equal(expected.Length, values.Length);
            for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], ((HitResult)i).ToString());
            Assert.Equal("Invulnerable", HitResult.Invulnerable.ToString());
        }

        [Fact]
        public void ActionStateQuery_CanBeImplementedByFakes_ForRulesAndPresentationConsumers()
        {
            IActionStateQuery query = new FakeActions();
            var current = query.Current(Actor);
            Assert.True(current.HasValue);
            Assert.Equal(ActionPhase.Active, current!.Value.Phase);
            Assert.Equal(7, current.Value.ElapsedTicks);
            Assert.Null(query.Current(Target));
            Assert.True(query.IsCancelOpen(Actor, ActionClass.Dodge));
            Assert.False(query.IsCancelOpen(Actor, ActionClass.Attack));
            Assert.True(query.IsInvulnerable(Actor));
            Assert.False(query.IsInvulnerable(Target));
            Assert.False(query.IsActionClockPaused(Target));
        }

        private sealed class FakeActions : IActionStateQuery
        {
            public ActionState? Current(Id unitId) =>
                unitId == Actor ? new ActionState(new Id("skill.slash"), Cast, ActionPhase.Active, 7, 1) : (ActionState?)null;

            public bool IsCancelOpen(Id unitId, ActionClass actionClass) => unitId == Actor && actionClass == ActionClass.Dodge;

            public bool IsInvulnerable(Id unitId) => unitId == Actor;

            public bool IsActionClockPaused(Id unitId) => false;
        }
    }
}

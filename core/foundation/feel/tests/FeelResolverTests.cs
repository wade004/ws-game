using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 解析器的操作语义、八层顺序、合成来源、副手叠加、列表、取整限幅、缓存与版本
    /// （手感设计/05 第 10 节第 2/3 条与第 3.2～3.4 节）。期望值全部由 <see cref="FeelTestSupport.SmallFields"/>
    /// 的登记范围与手算得出，不写裸数魔法值以外的东西。
    /// </summary>
    public class FeelResolverTests
    {
        private static FeelResolver Make(FeelProfileSet profiles, FakeState state, FeelProviders? providers = null) =>
            new FeelResolver(profiles, SmallCal(), 1.0 / 60.0, providers ?? state.AsProviders());

        // ------------------------------------------------------------------ 05 §10-2：操作语义

        [Fact]
        public void SameLayer_OperationsApplySetThenMultiplyThenAdd_RegardlessOfSourceOrder()
        {
            var state = new FakeState();
            var resolver = Make(SmallProfiles(), state);
            // 第 7 层三个来源故意按"加、乘、设"的相反顺序登记。
            state.Temp.Add(new FeelTemporaryEntry("aura.k1", new[] { Add(AccMs, 10) }));
            state.Temp.Add(new FeelTemporaryEntry("aura.k2", new[] { Mul(AccMs, 3) }));
            state.Temp.Add(new FeelTemporaryEntry("aura.k3", new[] { Set(AccMs, 50) }));

            var r = resolver.Resolve(Unit1);

            // 手算：基础 100 → set 50 → ×3 = 150 → +10 = 160。
            Assert.Equal(160.0, r.GetRaw(AccMs).AsNumber());
            var chain = r.GetProvenance(AccMs).Where(e => e.Layer == (int)FeelLayer.Temporary).Select(e => e.Op).ToArray();
            Assert.Equal(new[] { "set", "multiply", "add" }, chain);
        }

        [Fact]
        public void EightLayers_ApplyInOrder_AndProvenanceRecordsEachLayer()
        {
            var archetype = FeelRow.Overlay(FeelTables.Archetype, "a.heavy", new[] { Mul(WeaponScale, 1.5) });
            var tag = FeelRow.TagMap("t.one", "gender:x", null, 0, new[] { Mul(WeaponScale, 1.0) });
            var weapon = FeelRow.Weapon("w.sword", new[] { Set(WeaponScale, 4) });
            var character = FeelRow.Overlay(FeelTables.Character, "c.hero", new[] { Mul(WeaponScale, 2) });
            var action = FeelRow.Overlay(FeelTables.Action, "x.slash", new[] { Add(WeaponScale, 1) });
            var state = new FakeState
            {
                Archetype = "a.heavy", Character = "c.hero", Main = "w.sword",
                Action = new FeelActionState(true, "x.slash"),
            };
            state.TagList.Add("gender:x");
            state.Temp.Add(new FeelTemporaryEntry("aura.slow", new[] { Mul(WeaponScale, 0.5) }));
            var debug = new FeelDebugOverrides(SmallFields());
            debug.SetUnit(Unit1, Add(WeaponScale, 0.5));
            var providers = state.AsProviders();
            providers.Debug = debug;
            var resolver = Make(SmallProfiles(archetype, tag, weapon, character, action), state, providers);

            var r = resolver.Resolve(Unit1);

            // 手算：1 → ×1.5=1.5 (L2) → ×1.0=1.5 (L3) → set 4 (L4) → ×2=8 (L5) → +1=9 (L6) → ×0.5=4.5 (L7) → +0.5=5 (L8)。
            Assert.Equal(5.0, r.GetRaw(WeaponScale).AsNumber());
            var layers = r.GetProvenance(WeaponScale).Select(e => e.Layer).ToArray();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, layers);
            var sources = r.GetProvenance(WeaponScale).Select(e => e.SourceId).ToArray();
            Assert.Equal(new[] { "p.base", "a.heavy", "t.one", "w.sword", "c.hero", "x.slash", "aura.slow", "debug:unit" }, sources);
            Assert.Empty(r.Diagnostics);
        }

        [Fact]
        public void ResultBeyondRegisteredRange_IsClampedAndTheFactIsRecorded()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.big", new[] { Mul(WeaponScale, 9) });
            var weapon = FeelRow.Weapon("w.big", new[] { Set(WeaponScale, 4) });
            var state = new FakeState { Character = "c.big", Main = "w.big" };
            var resolver = Make(SmallProfiles(character, weapon), state);

            var r = resolver.Resolve(Unit1);

            // 4 × 9 = 36 > 登记上限 10。
            var max = SmallFields().Get(WeaponScale).Max!.Value;
            Assert.Equal(max, r.GetRaw(WeaponScale).AsNumber());
            Assert.Contains(WeaponScale, r.ClampedFields);
            var last = r.GetProvenance(WeaponScale).Last();
            Assert.Equal(FeelProvenanceOps.Clamp, last.Op);
            Assert.Equal(36.0, last.ValueBefore.AsNumber());
            Assert.Equal(max, last.ValueAfter.AsNumber());
        }

        [Fact]
        public void IntegerField_RoundsHalfAwayFromZeroBeforeClamp_AndRecordsRound()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.lv", new[] { Mul(Level, 1.5) });
            var state = new FakeState { Character = "c.lv" };
            var r = Make(SmallProfiles(character), state).Resolve(Unit1);

            // 3 × 1.5 = 4.5 → 5。
            Assert.Equal(5.0, r.GetRaw(Level).AsNumber());
            Assert.Equal(FeelProvenanceOps.Round, r.GetProvenance(Level).Last().Op);
        }

        [Fact]
        public void OptionalField_WithoutValue_IsNoneWithEmptyProvenance_AndArithmeticOnItIsIgnoredWithDiagnostic()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.opt", new[] { Mul(Opt, 2) });
            var state = new FakeState { Character = "c.opt" };
            var r = Make(SmallProfiles(character), state).Resolve(Unit1);

            Assert.True(r.GetRaw(Opt).IsNone);
            Assert.Empty(r.GetProvenance(Opt));
            Assert.Equal(0, r.GetTicks(Opt));
            Assert.Contains(r.Diagnostics, d => d.Contains(Opt));
        }

        [Fact]
        public void OptionalField_SetCreatesValue()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.opt", new[] { Set(Opt, 120) });
            var state = new FakeState { Character = "c.opt" };
            var r = Make(SmallProfiles(character), state).Resolve(Unit1);

            Assert.Equal(120.0, r.GetRaw(Opt).AsNumber());
            Assert.Single(r.GetProvenance(Opt));
        }

        // ------------------------------------------------------------------ 列表：整体替换与显式 remove

        [Fact]
        public void ListField_SetReplacesWholesale_AddAppendsWithoutDuplicates_RemoveIsExplicit()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.list",
                new[] { ListOp(Tags, FeelOp.Add, "x", "base") });
            var state = new FakeState { Character = "c.list" };
            var resolver = Make(SmallProfiles(character), state);

            Assert.Equal(new[] { "base", "x" }, resolver.Resolve(Unit1).GetRaw(Tags).AsList());

            // 第 7 层显式 remove 删元素（不用空值暗示删除）。
            state.Temp.Add(new FeelTemporaryEntry("aura.rm", new[] { ListOp(Tags, FeelOp.Remove, "base") }));
            resolver.Invalidate(Unit1, "aura applied");
            Assert.Equal(new[] { "x" }, resolver.Resolve(Unit1).GetRaw(Tags).AsList());

            // 动作层缺省整体替换。
            var action = FeelRow.Overlay(FeelTables.Action, "x.replace", new[] { ListOp(Tags, FeelOp.Set, "only") });
            var replaced = Make(SmallProfiles(character, action), new FakeState { Character = "c.list", Action = new FeelActionState(true, "x.replace") });
            Assert.Equal(new[] { "only" }, replaced.Resolve(Unit1).GetRaw(Tags).AsList());
        }

        [Fact]
        public void ListField_SameLayerAddThenRemove_AppliesAddBeforeRemove()
        {
            var state = new FakeState();
            state.Temp.Add(new FeelTemporaryEntry("aura.a", new[] { ListOp(Tags, FeelOp.Remove, "y") }));
            state.Temp.Add(new FeelTemporaryEntry("aura.b", new[] { ListOp(Tags, FeelOp.Add, "y") }));
            var r = Make(SmallProfiles(), state).Resolve(Unit1);

            // 同层 add 先于 remove：y 被追加后又被删除。
            Assert.Equal(new[] { "base" }, r.GetRaw(Tags).AsList());
        }

        // ------------------------------------------------------------------ 05 §10-3：合成来源

        [Fact]
        public void CharacterPrimaryField_WrittenByWeaponLayer_IsIgnoredWithDiagnostic()
        {
            var weapon = FeelRow.Weapon("w.bad", new[] { Set(AccMs, 5) });
            var state = new FakeState { Main = "w.bad" };
            var r = Make(SmallProfiles(weapon), state).Resolve(Unit1);

            Assert.Equal(100.0, r.GetRaw(AccMs).AsNumber());
            Assert.Contains(r.Diagnostics, d => d.Contains(AccMs));
        }

        [Fact]
        public void WeaponPrimaryField_BodyLayerAllowsOnlyMultiply()
        {
            var archetype = FeelRow.Overlay(FeelTables.Archetype, "a.bad", new[] { Add(WeaponScale, 5) });
            var state = new FakeState { Archetype = "a.bad" };
            var r = Make(SmallProfiles(archetype), state).Resolve(Unit1);

            Assert.Equal(1.0, r.GetRaw(WeaponScale).AsNumber());
            Assert.Contains(r.Diagnostics, d => d.Contains(WeaponScale));
        }

        [Fact]
        public void AttackOverrideField_TakesEffectOnlyInAction_AndRevertsAfterActionFinished()
        {
            var weapon = FeelRow.Weapon("w.sword", new[] { Set(MoveRatio, 0.2), Set(Lock, true) });
            var state = new FakeState { Main = "w.sword" };
            var resolver = Make(SmallProfiles(weapon), state);

            var idle = resolver.Resolve(Unit1);
            Assert.Equal(0.5, idle.GetRaw(MoveRatio).AsNumber());
            Assert.False(idle.GetRaw(Lock).AsBool());

            // action.started → 上层调用失效。
            state.Action = new FeelActionState(true, null);
            resolver.Invalidate(Unit1, "action.started");
            var acting = resolver.Resolve(Unit1);
            Assert.Equal(0.2, acting.GetRaw(MoveRatio).AsNumber());
            Assert.True(acting.GetRaw(Lock).AsBool());
            Assert.Equal(idle.Version + 1, acting.Version);

            // action.finished → 撤回到动作前的精确值。
            state.Action = FeelActionState.Idle;
            resolver.Invalidate(Unit1, "action.finished");
            var after = resolver.Resolve(Unit1);
            Assert.Equal(idle.GetRaw(MoveRatio), after.GetRaw(MoveRatio));
            Assert.Equal(idle.GetRaw(Lock), after.GetRaw(Lock));
            Assert.Equal(acting.Version + 1, after.Version);
        }

        // ------------------------------------------------------------------ 副手叠加

        [Fact]
        public void Offhand_OnlyStacksOffhandStackableFields_WithAddAndMultiply()
        {
            var main = FeelRow.Weapon("w.main", new[] { Set(Vfx, 2) });
            var off = FeelRow.Weapon("w.off", new[] { Set(Vfx, 4) }, new[]
            {
                Mul(Vfx, 1.5), Add(Vfx, 0.25),
                Set(WeaponScale, 9),        // 非 offhand_stackable：忽略
            });
            var state = new FakeState { Main = "w.main", Off = "w.off" };
            var r = Make(SmallProfiles(main, off), state).Resolve(Unit1);

            // 手算：基础 1 → 主手 set 2 → 副手 ×1.5 = 3 → +0.25 = 3.25；副手的主手写入（set 4）不生效。
            Assert.Equal(3.25, r.GetRaw(Vfx).AsNumber());
            Assert.Equal(1.0, r.GetRaw(WeaponScale).AsNumber());
            Assert.Contains(r.Diagnostics, d => d.Contains(WeaponScale));
            var srcs = r.GetProvenance(Vfx).Where(e => e.Layer == (int)FeelLayer.Weapon).Select(e => e.SourceId + ":" + e.Op).ToArray();
            Assert.Equal(new[] { "w.main:set", "w.off:multiply", "w.off:add" }, srcs);
        }

        [Fact]
        public void Offhand_SetOnStackableField_IsIgnored()
        {
            var off = FeelRow.Weapon("w.off", System.Array.Empty<FeelWrite>(), new[] { Set(Vfx, 3) });
            var state = new FakeState { Off = "w.off" };
            var r = Make(SmallProfiles(off), state).Resolve(Unit1);

            Assert.Equal(1.0, r.GetRaw(Vfx).AsNumber());
            Assert.NotEmpty(r.Diagnostics);
        }

        // ------------------------------------------------------------------ 标签映射层

        [Fact]
        public void TagMaps_ApplyInPriorityThenIdOrder_LaterSetWins_AndOnlyHeldTagsApply()
        {
            var a = FeelRow.TagMap("t.a", "tag:a", null, 5, new[] { Set(Level, 7) });
            var b = FeelRow.TagMap("t.b", "tag:b", null, 1, new[] { Set(Level, 2) });
            var c = FeelRow.TagMap("t.c", "tag:c", null, 9, new[] { Set(Level, 9) });
            var state = new FakeState();
            state.TagList.Add("tag:b");
            state.TagList.Add("tag:a");
            var r = Make(SmallProfiles(a, b, c), state).Resolve(Unit1);

            // b(priority 1) 先、a(priority 5) 后；c 没有被持有。
            Assert.Equal(7.0, r.GetRaw(Level).AsNumber());
            Assert.DoesNotContain(r.GetProvenance(Level), e => e.SourceId == "t.c");

            // 登记顺序相反结果相同。
            var r2 = Make(SmallProfiles(c, b, a), state).Resolve(Unit1);
            Assert.Equal(7.0, r2.GetRaw(Level).AsNumber());
        }

        [Fact]
        public void TagMap_WithArchetypeRef_AppliesArchetypeWritesInLayer3()
        {
            var archetype = FeelRow.Overlay(FeelTables.Archetype, "a.tall", new[] { Mul(Reach, 2) });
            var tag = FeelRow.TagMap("t.tall", "size:tall", "a.tall", 0, new[] { Add(Reach, 1) });
            var state = new FakeState();
            state.TagList.Add("size:tall");
            var r = Make(SmallProfiles(archetype, tag), state).Resolve(Unit1);

            // 1 → ×2 = 2 → +1 = 3。
            Assert.Equal(3.0, r.GetRaw(Reach).AsNumber());
            Assert.All(r.GetProvenance(Reach).Skip(1), e => Assert.Equal((int)FeelLayer.TagMap, e.Layer));
        }

        // ------------------------------------------------------------------ 缓存、版本、不可变

        [Fact]
        public void Cache_ReturnsSameObjectUntilInvalidated_ThenRecomputesWithNextVersion_AndOldResultIsImmutable()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.one", new[] { Set(AccMs, 300) });
            var state = new FakeState();
            var resolver = Make(SmallProfiles(character), state);

            var first = resolver.Resolve(Unit1);
            Assert.Same(first, resolver.Resolve(Unit1));
            Assert.Equal(1, first.Version);
            Assert.Equal(1, resolver.GetVersion(Unit1));

            // 提供者状态变了但没人通知失效：缓存命中，值不变（失效是事件驱动，不是轮询）。
            state.Character = "c.one";
            Assert.Same(first, resolver.Resolve(Unit1));
            Assert.Equal(100.0, first.GetRaw(AccMs).AsNumber());

            // 别的单位失效不影响本单位。
            resolver.Invalidate(Unit2, "equip");
            Assert.Same(first, resolver.Resolve(Unit1));

            resolver.Invalidate(Unit1, "tag changed");
            var second = resolver.Resolve(Unit1);
            Assert.NotSame(first, second);
            Assert.Equal(2, second.Version);
            Assert.Equal(300.0, second.GetRaw(AccMs).AsNumber());
            Assert.Equal(100.0, first.GetRaw(AccMs).AsNumber()); // 旧结果不变
        }

        [Fact]
        public void InvalidateAll_RecomputesEveryUnit_AndEmptyReasonIsRejected()
        {
            var state = new FakeState();
            var resolver = Make(SmallProfiles(), state);
            var a = resolver.Resolve(Unit1);
            var b = resolver.Resolve(Unit2);
            resolver.InvalidateAll("reload");
            Assert.NotSame(a, resolver.Resolve(Unit1));
            Assert.NotSame(b, resolver.Resolve(Unit2));
            Assert.Throws<System.ArgumentException>(() => resolver.Invalidate(Unit1, ""));
            Assert.Throws<System.ArgumentException>(() => resolver.InvalidateAll(""));
        }

        [Fact]
        public void Resolution_IsPureAndDeterministic_AcrossFreshResolvers()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.one", new[] { Mul(AccMs, 1.7), Add(Reach, 0.3) });
            var state = new FakeState { Character = "c.one" };
            state.Temp.Add(new FeelTemporaryEntry("aura.b", new[] { Add(AccMs, 5) }));
            state.Temp.Add(new FeelTemporaryEntry("aura.a", new[] { Mul(AccMs, 0.9) }));
            var profiles = SmallProfiles(character);

            var r1 = Make(profiles, state).Resolve(Unit1);
            var r2 = Make(profiles, state).Resolve(Unit1);

            foreach (var def in SmallFields().Fields)
            {
                Assert.Equal(r1.GetRaw(def.Name), r2.GetRaw(def.Name));
                Assert.Equal(r1.GetAbsolute(def.Name), r2.GetAbsolute(def.Name));
                Assert.Equal(r1.GetProvenance(def.Name).Select(e => e.ToString()), r2.GetProvenance(def.Name).Select(e => e.ToString()));
            }
        }

        // ------------------------------------------------------------------ 调试覆盖层 API

        [Fact]
        public void DebugOverrides_LayerOnTop_UnitBeatsGlobal_AndClearingRestoresExactly()
        {
            var state = new FakeState();
            var debug = new FeelDebugOverrides(SmallFields());
            var providers = state.AsProviders();
            providers.Debug = debug;
            var resolver = Make(SmallProfiles(), state, providers);
            var baseline = resolver.Resolve(Unit1).GetRaw(AccMs);

            debug.Changed += unit =>
            {
                if (unit.HasValue) resolver.Invalidate(unit.Value, "debug");
                else resolver.InvalidateAll("debug");
            };

            debug.SetGlobal(Set(AccMs, 77));
            Assert.Equal(77.0, resolver.Resolve(Unit1).GetRaw(AccMs).AsNumber());
            Assert.Equal(77.0, resolver.Resolve(Unit2).GetRaw(AccMs).AsNumber());

            debug.SetUnit(Unit1, Set(AccMs, 55));
            Assert.Equal(55.0, resolver.Resolve(Unit1).GetRaw(AccMs).AsNumber());
            Assert.Equal(77.0, resolver.Resolve(Unit2).GetRaw(AccMs).AsNumber());
            Assert.All(resolver.Resolve(Unit1).GetProvenance(AccMs).Where(e => e.Layer == (int)FeelLayer.Debug),
                e => Assert.StartsWith("debug:", e.SourceId));

            debug.SetUnit(Unit1, Set(AccMs, 56)); // 同字段替换，不产生重复写
            Assert.Single(debug.GetUnitOverrides(Unit1));

            debug.ClearAll();
            Assert.Equal(baseline, resolver.Resolve(Unit1).GetRaw(AccMs));
            Assert.Throws<System.ArgumentException>(() => debug.SetGlobal(Set("nope", 1)));
        }

        // ------------------------------------------------------------------ 解析器构造的前置条件

        [Fact]
        public void Constructor_RejectsMissingBasePreset_AndBadStep()
        {
            var profiles = SmallProfiles();
            Assert.Throws<System.ArgumentException>(() =>
                new FeelResolver(profiles, new FeelCalibration("c", "p.missing", 1, 1, 1, 1, 1, 1, 0), 1.0 / 60.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FeelResolver(profiles, SmallCal(), 0));
        }

        [Fact]
        public void Preset_Extends_ChildOverridesParent_AndProvenanceNamesTheSupplyingRow()
        {
            var child = FeelRow.Preset("p.child", "p.base", new[] { Set(AccMs, 250) });
            var profiles = SmallProfiles(child);
            var resolver = new FeelResolver(profiles, new FeelCalibration("c", "p.child", 2, 4, 30, 10, 1, 32, 50), 1.0 / 60.0);

            var r = resolver.Resolve(Unit1);

            Assert.Equal(250.0, r.GetRaw(AccMs).AsNumber());
            Assert.Equal("p.child", r.GetProvenance(AccMs)[0].SourceId);
            Assert.Equal("p.base", r.GetProvenance(WeaponScale)[0].SourceId);
        }
    }
}

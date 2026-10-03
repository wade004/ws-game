using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 解析器性质测试（05 第 10 节第 1 条，字段登记自动枚举：框架数据里全部 原型 × 武器 × 标签 组合）与
    /// 半属隔离（第 8 条）。
    /// </summary>
    public class FeelPropertyAndIsolationTests
    {
        // ------------------------------------------------------------------ 05 §10-1：性质测试

        /// <summary>框架数据里的全部组合：每个原型（含"无"）× 每个武器（含"无"，加副手组合）× 标签子集 × 动作与否。</summary>
        public static IEnumerable<object[]> Combinations()
        {
            var profiles = FrameworkProfiles();
            var archetypes = new List<string?> { null };
            archetypes.AddRange(profiles.Archetypes.Select(a => a.Id));
            var weapons = new List<string?> { null };
            weapons.AddRange(profiles.Weapons.Select(w => w.Id));
            foreach (var arch in archetypes)
            {
                foreach (var main in weapons)
                {
                    foreach (var off in weapons)
                    {
                        foreach (var inAction in new[] { false, true })
                        {
                            yield return new object[] { arch ?? "-", main ?? "-", off ?? "-", inAction };
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Combinations))]
        public void EveryArchetypeWeaponCombination_ResolvesWithinRanges_Repeatably_WithConsistentProvenance(
            string archetype, string main, string off, bool inAction)
        {
            var profiles = FrameworkProfiles();
            var fields = profiles.Fields;
            var state = new FakeState
            {
                Archetype = archetype == "-" ? null : archetype,
                Main = main == "-" ? null : main,
                Off = off == "-" ? null : off,
                Action = inAction ? new FeelActionState(true, null) : FeelActionState.Idle,
            };
            // 标签：框架数据没有 tag_map 行，这里加一条游戏层标签映射，覆盖第 3 层路径。
            var tag = FeelRow.TagMap("feel.tag_map.test_tall", "size:tall", "feel.archetype.heavy", 0, new[]
            {
                new FeelWrite("stride_scale", FeelOp.Multiply, FeelValue.Of(1.05)),
            });
            var withTag = new FeelProfileSet(fields, profiles.Presets.Concat(profiles.Archetypes).Concat(profiles.Weapons).Append(tag),
                profiles.MotionModeRules, profiles.Calibrations);
            state.TagList.Add("size:tall");
            var cal = profiles.Calibrations[0];

            var resolver = new FeelResolver(withTag, cal, 1.0 / 60.0, state.AsProviders());
            var r = resolver.Resolve(Unit1);

            for (var i = 0; i < fields.Count; i++)
            {
                var def = fields[i];
                var raw = r.GetRaw(def.Name);

                // 可选字段可以没有值；其余字段必须有值。
                if (raw.IsNone)
                {
                    Assert.True(def.Optional, $"非可选字段 {def.Name} 解析后没有值");
                    Assert.Empty(r.GetProvenance(def.Name));
                    continue;
                }

                // 所有数值落在登记范围内。
                if (def.IsNumeric)
                {
                    var v = raw.AsNumber();
                    Assert.InRange(v, def.Min!.Value, def.Max!.Value);
                    if (def.Kind == FeelFieldKind.Int) Assert.Equal(Math.Round(v), v);
                }
                if (def.Kind == FeelFieldKind.Enum) Assert.Contains(raw.AsText(), def.EnumValues!);

                // 溯源链：至少一条，最后一条的 valueAfter 等于结果。
                var chain = r.GetProvenance(def.Name);
                Assert.NotEmpty(chain);
                Assert.Equal(raw, chain[chain.Count - 1].ValueAfter);

                // 判定型毫秒字段有 tick 且非零值至少 1。
                if (def.HasTicks)
                {
                    var ticks = r.Judging.GetTicks(def.Name);
                    Assert.True(raw.AsNumber() > 0 ? ticks >= 1 : ticks == 0, $"{def.Name} 的 tick 换算不满足非零至少 1");
                }
            }

            // 重复解析结果不变（缓存命中同对象；重算后逐字段相等）。
            Assert.Same(r, resolver.Resolve(Unit1));
            var fresh = new FeelResolver(withTag, cal, 1.0 / 60.0, state.AsProviders()).Resolve(Unit1);
            for (var i = 0; i < fields.Count; i++)
            {
                Assert.Equal(r.GetRaw(fields[i].Name), fresh.GetRaw(fields[i].Name));
                Assert.Equal(r.GetAbsolute(fields[i].Name), fresh.GetAbsolute(fields[i].Name));
            }
            Assert.Empty(r.Diagnostics);
        }

        [Fact]
        public void Combinations_AreEnumeratedFromTheRegistry_AndCoverEveryFrameworkArchetypeAndWeapon()
        {
            var profiles = FrameworkProfiles();
            Assert.Equal(3, profiles.Archetypes.Count);
            Assert.Equal(2, profiles.Weapons.Count);
            Assert.Equal((profiles.Archetypes.Count + 1) * (profiles.Weapons.Count + 1) * (profiles.Weapons.Count + 1) * 2,
                Combinations().Count());
        }

        // ------------------------------------------------------------------ 05 §10-8：半属隔离

        [Fact]
        public void JudgingView_CannotReadPresentingFields_AndPresentingView_CannotReadJudgingFields()
        {
            var profiles = SmallProfiles();
            var r = new FeelResolver(profiles, SmallCal(), 1.0 / 60.0).Resolve(Unit1);
            var fields = SmallFields();

            foreach (var def in fields.Fields)
            {
                if (def.Half == FeelHalf.Judging)
                {
                    Assert.True(r.Judging.Contains(def.Name));
                    Assert.False(r.Presenting.Contains(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Presenting.GetRaw(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Presenting.GetAbsolute(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Presenting.TryGetNumber(def.Name, out _));
                    _ = r.Judging.GetRaw(def.Name);
                }
                else
                {
                    Assert.True(r.Presenting.Contains(def.Name));
                    Assert.False(r.Judging.Contains(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Judging.GetRaw(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Judging.GetAbsolute(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Judging.GetBool(def.Name));
                    Assert.Throws<FeelHalfViolationException>(() => r.Judging.GetTicks(def.Name));
                    _ = r.Presenting.GetRaw(def.Name);
                }
            }

            Assert.Throws<FeelHalfViolationException>(() => r.Judging.GetRaw("no_such_field"));
        }

        [Fact]
        public void TheTwoViews_PartitionTheFrameworkFieldRegistry_WithNoOverlap()
        {
            var r = new FeelResolver(FrameworkProfiles(), FrameworkProfiles().Calibrations[0], 1.0 / 60.0).Resolve(Unit1);
            var judging = r.Judging.Names.ToHashSet();
            var presenting = r.Presenting.Names.ToHashSet();

            Assert.Empty(judging.Intersect(presenting));
            Assert.Equal(FeelFields.Default.Count, judging.Count + presenting.Count);
            Assert.All(r.Judging.Names, n => Assert.Equal(FeelHalf.Judging, FeelFields.Default.Get(n).Half));
            Assert.All(r.Presenting.Names, n => Assert.Equal(FeelHalf.Presenting, FeelFields.Default.Get(n).Half));
            Assert.NotEmpty(judging);
            Assert.NotEmpty(presenting);
        }

        [Fact]
        public void HalfSources_ExposeOnlyTheirOwnViewType_AndViewsHaveNoPathToTheFullResult()
        {
            // 规则层拿到的接口只返回判定型视图，表现层拿到的只返回呈现型视图。
            // M5-S2a：判定型来源多了"指定动作层"重载，仍只返回判定型视图（断言口径从"恰好一个方法"放宽为"每个方法都只返回本半视图"）。
            Assert.All(typeof(IFeelJudgingSource).GetMethods(), m => Assert.Equal(typeof(JudgingFeelView), m.ReturnType));
            Assert.Equal(typeof(PresentingFeelView), typeof(IFeelPresentingSource).GetMethods().Single().ReturnType);

            // 两个视图类型彼此没有继承关系，也不公开能取回完整结果（含另一半）的成员。
            Assert.False(typeof(JudgingFeelView).IsAssignableFrom(typeof(PresentingFeelView)));
            Assert.False(typeof(PresentingFeelView).IsAssignableFrom(typeof(JudgingFeelView)));
            foreach (var type in new[] { typeof(FeelHalfView), typeof(JudgingFeelView), typeof(PresentingFeelView) })
            {
                foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var memberType = m is MethodInfo mi ? mi.ReturnType : m is PropertyInfo pi ? pi.PropertyType : null;
                    Assert.NotEqual(typeof(ResolvedFeel), memberType);
                    Assert.NotEqual(typeof(JudgingFeelView), memberType == typeof(PresentingFeelView) ? typeof(JudgingFeelView) : memberType);
                }
            }
        }

        [Fact]
        public void ResolverResolveJudgingAndPresenting_ReturnViewsOfTheSameVersion()
        {
            var resolver = new FeelResolver(SmallProfiles(), SmallCal(), 1.0 / 60.0);
            var j = resolver.ResolveJudging(Unit1);
            var p = resolver.ResolvePresenting(Unit1);
            Assert.Equal(j.Version, p.Version);
            Assert.Equal(FeelHalf.Judging, j.Half);
            Assert.Equal(FeelHalf.Presenting, p.Half);
        }
    }
}

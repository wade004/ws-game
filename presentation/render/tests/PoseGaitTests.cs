using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.Feel;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// 手感设计/02 第 7 节步态派生 + 04 第 2 节姿势上下文：阈值读取、滞回、一次跨多档、上下文变化通知、与姿势回落链的衔接。
    /// 期望值由阈值字段与滞回宽度算出，不写死裸数。
    /// </summary>
    public class PoseGaitTests
    {
        private static readonly Id Hero = new Id("unit.pose_hero");
        private const double BaseSpeed = 4.0;

        /// <summary>手工拼一个只含给定相对值的呈现视图（绝对值 = 相对值 × 基础移速，用来证明阈值读的是相对值）。</summary>
        private static PresentingFeelView View(IReadOnlyDictionary<string, double> raw, int version = 1)
        {
            var fields = FeelFields.Default;
            var n = fields.Count;
            var rawArr = new FeelValue[n];
            var absArr = new FeelValue[n];
            var ticks = new int[n];
            var prov = new IReadOnlyList<FeelProvenanceEntry>[n];
            for (var i = 0; i < n; i++)
            {
                ticks[i] = -1;
                prov[i] = Array.Empty<FeelProvenanceEntry>();
            }
            foreach (var kv in raw)
            {
                var idx = fields.IndexOf(kv.Key);
                Assert.True(idx >= 0, kv.Key);
                rawArr[idx] = FeelValue.Of(kv.Value);
                absArr[idx] = FeelValue.Of(kv.Value * BaseSpeed);
            }
            return new ResolvedFeel(Hero, version, fields, rawArr, absArr, ticks, prov, Array.Empty<string>(), Array.Empty<string>()).Presenting;
        }

        private static Dictionary<string, double> Profile(double idleMax, double walkMax, double? sprintMin, double hysteresis)
        {
            var d = new Dictionary<string, double>
            {
                [FeelFieldNames.IdleMaxRatio] = idleMax,
                [FeelFieldNames.WalkMaxRatio] = walkMax,
                [FeelFieldNames.GaitHysteresisRatio] = hysteresis,
            };
            if (sprintMin.HasValue) d[FeelFieldNames.SprintMinRatio] = sprintMin.Value;
            return d;
        }

        // ------------------------------------------------------------------ 阈值

        [Fact]
        public void Thresholds_NoView_UseSpecDefaults_AndSeedFollowsTheTable()
        {
            var t = GaitThresholds.FromView(null);
            Assert.Equal(GaitThresholds.Default, t);

            var d = new GaitDeriver(t);
            Assert.Equal(LocomotionGait.Idle, d.Seed(t.IdleMaxRatio - 0.001));
            Assert.Equal(LocomotionGait.Walk, d.Seed(t.IdleMaxRatio));
            Assert.Equal(LocomotionGait.Walk, d.Seed(t.WalkMaxRatio - 0.001));
            Assert.Equal(LocomotionGait.Run, d.Seed(t.WalkMaxRatio));
            Assert.Equal(LocomotionGait.Run, d.Seed(10.0)); // 没声明冲刺：永不 sprint
        }

        [Fact]
        public void Thresholds_FromView_ReadsRelativeRatios_NotCalibratedAbsolutes_AndFallsBackPerField()
        {
            var view = View(Profile(0.12, 0.55, 1.3, 0.07));
            var t = GaitThresholds.FromView(view);
            Assert.Equal(0.12, t.IdleMaxRatio);
            Assert.Equal(0.55, t.WalkMaxRatio);
            Assert.Equal(1.3, t.SprintMinRatio);
            Assert.Equal(0.07, t.Hysteresis);

            // 字段未设置：该项取缺省（sprint 无值 = 没有冲刺步态）
            var partial = GaitThresholds.FromView(View(new Dictionary<string, double> { [FeelFieldNames.WalkMaxRatio] = 0.8 }));
            Assert.Equal(GaitThresholds.DefaultIdleMaxRatio, partial.IdleMaxRatio);
            Assert.Equal(0.8, partial.WalkMaxRatio);
            Assert.Null(partial.SprintMinRatio);
            Assert.Equal(GaitThresholds.DefaultHysteresisRatio, partial.Hysteresis);
        }

        // ------------------------------------------------------------------ 滞回

        [Fact]
        public void Hysteresis_JitterAroundWalkRunThreshold_SwitchesExactlyOnce()
        {
            var t = GaitThresholds.FromView(View(Profile(0.1, 0.5, null, 0.08)));
            var deriver = new GaitDeriver(t);
            var changes = 0;
            var last = deriver.Update(0.0);

            void Step(double ratio)
            {
                var g = deriver.Update(ratio);
                if (g != last) { changes++; last = g; }
            }

            // 先升到 walk 区间中部（1 次切换：idle -> walk）
            Step(t.WalkMaxRatio * 0.5);
            Assert.Equal(1, changes);
            Assert.Equal(LocomotionGait.Walk, last);

            // 在阈值两侧小幅抖动：上沿刚过阈值、下沿仍在 阈值 - 滞回宽度 之上。无滞回时每个来回都会切换。
            var hi = t.WalkMaxRatio + 0.001;
            var lo = t.WalkMaxRatio - t.Hysteresis + 0.001;
            var naiveFlips = 0;
            var naive = LocomotionGait.Walk;
            for (var i = 0; i < 200; i++)
            {
                var r = i % 2 == 0 ? hi : lo;
                Step(r);
                var naiveNow = r >= t.WalkMaxRatio ? LocomotionGait.Run : LocomotionGait.Walk;
                if (naiveNow != naive) { naiveFlips++; naive = naiveNow; }
            }
            Assert.Equal(2, changes);            // 又只切换了一次：walk -> run
            Assert.Equal(LocomotionGait.Run, last);
            Assert.True(naiveFlips >= 100);      // 复现：同一串输入在无滞回实现下闪烁

            // 降到 阈值 - 滞回宽度 以下才回落，且只回落一次
            Step(t.WalkMaxRatio - t.Hysteresis - 0.001);
            Step(t.WalkMaxRatio - t.Hysteresis - 0.002);
            Assert.Equal(3, changes);
            Assert.Equal(LocomotionGait.Walk, last);
        }

        [Fact]
        public void Hysteresis_WidthComesFromTheProfileField()
        {
            // 同一串抖动输入，滞回宽度越大越稳：宽度 0 时每个来回都切换，宽度覆盖抖动幅度时不再切换。
            int Flips(double hysteresis)
            {
                var t = new GaitThresholds(0.1, 0.5, null, hysteresis);
                var d = new GaitDeriver(t);
                d.Update(0.3);
                var last = d.Current;
                var flips = 0;
                for (var i = 0; i < 100; i++)
                {
                    var g = d.Update(i % 2 == 0 ? 0.52 : 0.46);
                    if (g != last) { flips++; last = g; }
                }
                return flips;
            }

            Assert.True(Flips(0.0) > 50);
            Assert.Equal(1, Flips(0.1)); // 抖动幅度 0.06 < 宽度 0.1：只在第一次越过阈值时升档一次
        }

        [Fact]
        public void Update_SpeedJumpAcrossSeveralThresholds_ConvergesInOneCall_AndSprintNeedsDeclaration()
        {
            var withSprint = new GaitDeriver(new GaitThresholds(0.1, 0.5, 1.2, 0.05));
            Assert.Equal(LocomotionGait.Sprint, withSprint.Update(2.0));
            Assert.Equal(LocomotionGait.Idle, withSprint.Update(0.0)); // 一次连降三档

            var noSprint = new GaitDeriver(new GaitThresholds(0.1, 0.5, null, 0.05));
            Assert.Equal(LocomotionGait.Run, noSprint.Update(2.0));

            // 正在冲刺时冲刺阈值被撤销（手感重算）：下一次观测落回 run
            var d = new GaitDeriver(new GaitThresholds(0.1, 0.5, 1.2, 0.05));
            d.Update(2.0);
            d.SetThresholds(new GaitThresholds(0.1, 0.5, null, 0.05));
            Assert.Equal(LocomotionGait.Run, d.Update(2.0));
        }

        [Fact]
        public void Update_SpeedAndBaseSpeedOverload_UsesRatio()
        {
            var d = new GaitDeriver(new GaitThresholds(0.1, 0.5, null, 0.05));
            Assert.Equal(LocomotionGait.Run, d.Update(speed: 3.0, baseSpeed: 4.0)); // 0.75
            Assert.Equal(LocomotionGait.Idle, d.Update(speed: 3.0, baseSpeed: 0.0)); // 基础移速无效视为静止
        }

        // ------------------------------------------------------------------ 上下文来源

        [Fact]
        public void PoseSelector_PublishesContextOnlyWhenItChanges_AndFollowsFeelRecalculation()
        {
            var selector = new PoseSelector();
            var notified = new List<Id>();
            selector.ContextChanged += notified.Add;

            var view = View(Profile(0.1, 0.5, null, 0.05), version: 1);

            selector.Observe(Hero, 0.0, view);
            Assert.Empty(notified);                                   // idle -> idle：没有变化
            selector.Observe(Hero, 0.3, view);
            Assert.Equal(LocomotionGait.Walk, selector.GetContext(Hero).Gait);
            selector.Observe(Hero, 0.35, view);                       // 仍是 walk：不重复通知
            Assert.Single(notified);

            // 手感重算（版本号递增）抬高 walk 上界：同一速度 0.55 以前是 run，现在仍是 walk
            selector.Observe(Hero, 0.55, view);
            Assert.Equal(LocomotionGait.Run, selector.GetContext(Hero).Gait);
            var recalculated = View(Profile(0.1, 0.9, null, 0.05), version: 2);
            selector.Observe(Hero, 0.55, recalculated);
            Assert.Equal(LocomotionGait.Walk, selector.GetContext(Hero).Gait);

            selector.SetFamily(Hero, "2h");
            selector.SetFamily(Hero, "2h");                           // 同值不重复通知
            Assert.Equal("2h", selector.GetContext(Hero).Family);
            var before = notified.Count;
            selector.SetVariant(Hero, "wounded");
            Assert.Equal(before + 1, notified.Count);
            Assert.All(notified, id => Assert.Equal(Hero, id));

            selector.Forget(Hero);
            Assert.Equal(PoseContext.Empty, selector.GetContext(Hero));
        }

        [Fact]
        public void PoseSelector_FirstObservationSeedsWithoutHysteresisBias()
        {
            var selector = new PoseSelector();
            // 第一次就观测到 run 速度：直接定到 run（不是从 idle 逐档升再带偏置）
            selector.Observe(Hero, 0.9, View(Profile(0.1, 0.5, null, 0.05)));
            Assert.Equal(LocomotionGait.Run, selector.GetContext(Hero).Gait);
        }

        [Fact]
        public void PoseContext_ToRequest_FeedsTheFallbackChain()
        {
            // move 状态：Idle 步态（动画状态机判定在动，但速度极低）按最慢的 walk 取姿势
            Assert.Equal("move.walk", new PoseContext(LocomotionGait.Idle).ToRequest("move", false).FullKey());
            Assert.Equal("move.run.combat.2h", new PoseContext(LocomotionGait.Run, "2h").ToRequest("move", true).FullKey());
            // 其它状态忽略步态
            Assert.Equal("attack.combat.2h", new PoseContext(LocomotionGait.Run, "2h").ToRequest("attack", true).FullKey());

            // 衔接姿势表：只有 move.run.combat 的表，请求 2h 跑步战斗 -> 去武器族命中 move.run.combat（链逐级记录）
            var table = new[] { "move.walk", "move.run", "move.run.combat" }.ToDictionary(k => k, k => k, StringComparer.Ordinal);
            var request = new PoseContext(LocomotionGait.Run, "2h").ToRequest("move", true);
            Assert.True(PoseResolver.TryResolve(request, table, out var clip, out var resolution));
            Assert.Equal("move.run.combat", clip);
            Assert.Equal(new[] { "move.run.combat.2h", "move.run.combat" }, resolution.Tried);

            // 缺省上下文（没有任何维度）+ 旧式表：move 状态请求 move.walk，表里没有则回落到旧基础键 move，选出的键与旧行为一致
            var legacy = new[] { "idle", "move" }.ToDictionary(k => k, k => k, StringComparer.Ordinal);
            Assert.True(PoseResolver.TryResolve(PoseContext.Empty.ToRequest("move", false), legacy, out var legacyClip, out _));
            Assert.Equal("move", legacyClip);
        }
    }
}

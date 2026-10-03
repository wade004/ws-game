using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 命中几何与手感缩放场景（手感落地 M5-S2a，手感设计/03 第 2.2、2.4 节）的运行期验收：
    /// <c>feel_hit_geometry</c>（受击半径决定边缘木桩是否被扇形命中）与 <c>feel_hit_charge_scale</c>（蓄力手感缩放与分段手感覆盖）。
    /// 期望值一律由数据行与几何规则在用例里算出（不写死裸数）。
    /// </summary>
    public sealed class HitGeometrySceneTests
    {
        private const string GeometryScript = "feel_hit_geometry";
        private const string ChargeScript = "feel_hit_charge_scale";
        private static readonly string GeometryDataDir = Path.Combine("lab", "fixtures", "data", "hit_geometry");
        private static readonly string ScaleDataDir = Path.Combine("lab", "fixtures", "data", "hit_scale");

        // ---------- 几何：点到扇形的最短距离（扇形顶点在原点、朝 +x） ----------

        private static double PointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var t = Math.Max(0.0, Math.Min(1.0, (((px - ax) * dx) + ((py - ay) * dy)) / ((dx * dx) + (dy * dy))));
            return Math.Sqrt(Math.Pow(px - (ax + (t * dx)), 2) + Math.Pow(py - (ay + (t * dy)), 2));
        }

        /// <summary>点到"半径 <paramref name="radius"/>、全张角 <paramref name="angle"/> 的扇形（顶点在原点、朝 +x）"的最短距离，点在扇形内为 0。</summary>
        private static double DistanceToSector(double px, double py, double radius, double angle)
        {
            var half = angle / 2.0;
            var bearing = Math.Atan2(py, px);
            var dist = Math.Sqrt((px * px) + (py * py));
            if (Math.Abs(bearing) <= half && dist <= radius)
            {
                return 0.0;
            }

            var best = Math.Min(
                PointToSegment(px, py, 0, 0, radius * Math.Cos(half), radius * Math.Sin(half)),
                PointToSegment(px, py, 0, 0, radius * Math.Cos(half), -radius * Math.Sin(half)));
            if (Math.Abs(bearing) <= half)
            {
                best = Math.Min(best, dist - radius);
            }

            return best;
        }

        private static (double Radius, double Angle) ArcShape()
        {
            var chain = FeelRules.Row(Path.Combine("data", "_lab_action", "target", "target.chain_def.json"), "target.chain.lab_a_arc");
            var shape = (JsonObject)chain["shape"];
            return (((JsonNumber)shape["radius"]).Value, ((JsonNumber)shape["angle"]).Value);
        }

        private static Dictionary<string, (double X, double Y)> DummyPositions()
        {
            var set = FeelRules.Row(Path.Combine(GeometryDataDir, "lab", "lab.dummy_set.json"), "lab.dummy_set.hit_geometry");
            var map = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
            foreach (var e in (JsonArray)set["entries"])
            {
                var o = (JsonObject)e;
                var pos = (JsonObject)o["position"];
                map[((JsonString)o["name"]).Value] = (((JsonNumber)pos["x"]).Value, ((JsonNumber)pos["y"]).Value);
            }

            return map;
        }

        /// <summary>某预设的受击半径（世界单位）：体积半径 × 标定参考身高 × 受击半径倍数（预设没声明倍数取 1）。</summary>
        private static double HurtRadius(string presetId)
        {
            var preset = FeelRules.Row(Path.Combine(GeometryDataDir, "feel", "feel.preset.json"), presetId);
            var scale = 1.0;
            var body = 0.0;
            // 沿 extends 链取值（本数据根内 hit_geometry_* 逐级继承 hit_geometry）。
            var current = preset;
            while (current != null)
            {
                var v = (JsonObject)current["values"];
                if (body == 0.0 && v.ContainsKey("unit_body_radius")) body = ((JsonNumber)v["unit_body_radius"]).Value;
                if (scale == 1.0 && v.ContainsKey("hurt_radius_scale")) scale = ((JsonNumber)v["hurt_radius_scale"]).Value;
                var ext = ((JsonString)current["extends"]).Value;
                current = ext.StartsWith("feel.preset.hit_geometry", StringComparison.Ordinal)
                    ? FeelRules.Row(Path.Combine(GeometryDataDir, "feel", "feel.preset.json"), ext)
                    : null;
            }

            var shortName = presetId.Substring("feel.preset.".Length);
            var calibration = FeelRules.Row(Path.Combine(GeometryDataDir, "feel", "feel.calibration.json"), "feel.calibration.lab_" + shortName);
            return body * ((JsonNumber)calibration["reference_height"]).Value * scale;
        }

        private static HashSet<string> HitSet(FeelFp fp)
        {
            var hit = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in fp.Items("spatialhit.hit_sets"))
            {
                hit.Add(item.Substring(item.IndexOf('=') + 1));
            }

            return hit;
        }

        [Theory]
        [InlineData("2d_action", "feel.preset.hit_geometry")]
        [InlineData("2d_action", "feel.preset.hit_geometry_wide")]
        [InlineData("2d_action", "feel.preset.hit_geometry_point")]
        [InlineData("2d_targeted", "feel.preset.hit_geometry")]
        [InlineData("3d_action", "feel.preset.hit_geometry_wide")]
        public void HurtRadius_DecidesWhichStakesTheSectorHits_AccordingToTheirDistanceToTheShape(string cell, string presetId)
        {
            var (radius, angle) = ArcShape();
            var hurt = HurtRadius(presetId);
            var hit = HitSet(FeelFp.Of(GeometryScript, cell, new LabRunVariant { PresetId = presetId }));

            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in DummyPositions())
            {
                // 不变量：目标被命中 ⇔ 目标中心到命中形状的最短距离不超过受击半径（半径为 0 时退化为"中心在形状内"）。
                var distance = DistanceToSector(pair.Value.X, pair.Value.Y, radius, angle);
                if (distance <= hurt + 1e-9)
                {
                    expected.Add(pair.Key);
                }
            }

            Assert.Equal(expected, hit);
        }

        [Fact]
        public void HurtRadius_Scenario_IsNonVacuous_TheNearStakeIsOutsideTheShapeButWithinTheHurtRadius()
        {
            // 场景前提（复现）：近处木桩中心在形状外、与形状的距离小于缺省受击半径；远处木桩距离大于缺省与放大后的受击半径。
            var (radius, angle) = ArcShape();
            var pos = DummyPositions();
            var near = DistanceToSector(pos["edge_near"].X, pos["edge_near"].Y, radius, angle);
            var far = DistanceToSector(pos["edge_far"].X, pos["edge_far"].Y, radius, angle);
            Assert.True(near > 0.0, "近处木桩中心应在形状之外");
            Assert.True(near < HurtRadius("feel.preset.hit_geometry"));
            Assert.True(far > HurtRadius("feel.preset.hit_geometry_wide"));
            Assert.Equal(new[] { "edge_near" }, HitSet(FeelFp.Of(GeometryScript, "2d_action")));
            Assert.Empty(HitSet(FeelFp.Of(GeometryScript, "2d_action", new LabRunVariant { PresetId = "feel.preset.hit_geometry_point" })));
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2d_targeted")]
        public void HurtRadiusOff_KeepsThePointBehaviour_EvenWhenThePresetDeclaresABodyRadius(string cell)
        {
            // 装配选项 HitRadiusFromFeel 缺省关：同一份数据、同一个预设，不开选项时命中把目标当点——与受击半径为 0 时逐位一致。
            var script = LabTestSupport.Script(GeometryScript);
            Assert.True(script.Meta.HitRadiusFromFeel);
            var off = InputScript.Parse(script.ToJson());
            off.Meta.HitRadiusFromFeel = false;
            var offHit = HitSet(new FeelFp(LabTestSupport.Runner.Run(off, cell)));
            Assert.Empty(offHit);

            var point = FeelFp.Of(GeometryScript, cell, new LabRunVariant { PresetId = "feel.preset.hit_geometry_point" });
            var offFp = new FeelFp(LabTestSupport.Runner.Run(off, cell, new LabRunVariant { PresetId = "feel.preset.hit_geometry_point" }));
            Assert.Equal(point.Text("spatialhit.hit_sets"), offFp.Text("spatialhit.hit_sets"));
            Assert.Equal(point.Num("spatialhit.hit_confirmed"), offFp.Num("spatialhit.hit_confirmed"));
        }

        [Fact]
        public void HurtRadiusFlag_IsWrittenOnlyWhenUsed_SoExistingScriptsKeepTheirSerializedText()
        {
            var script = LabTestSupport.Script(GeometryScript);
            Assert.Contains("\"hitRadiusFromFeel\": true", script.ToJson());
            var melee = LabTestSupport.Script("feel_melee");
            Assert.DoesNotContain("hitRadiusFromFeel", melee.ToJson());
            Assert.False(melee.Meta.HitRadiusFromFeel);
        }

        // ---------- 蓄力手感缩放与分段手感覆盖 ----------

        private static double ScaleAt(string field, double ratio)
        {
            var skill = FeelRules.Row(Path.Combine(ScaleDataDir, "skill", "skill.def.json"), "skill.lab_hs_charge");
            var charge = (JsonObject)((JsonObject)skill["timeline"])["charge"];
            var range = (JsonObject)((JsonObject)charge["feel_scale"])[field];
            var min = ((JsonNumber)range["min"]).Value;
            var max = ((JsonNumber)range["max"]).Value;
            return min + ((max - min) * ratio);
        }

        private static double OverrideMs(string field) =>
            ((JsonNumber)FindWrite(FeelRules.Row(Path.Combine(ScaleDataDir, "feel", "feel.action.json"), "feel.action.lab_hs_finisher"), field)["value"]).Value;

        private static JsonObject FindWrite(JsonObject row, string field)
        {
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return o;
                }
            }

            throw new InvalidOperationException($"覆盖行没有写字段 {field}");
        }

        [Fact]
        public void ChargeFeelScale_ScalesTheHitstopOfEverySegment_AndTheSegmentOverrideReplacesTheBaseValue()
        {
            var fp = FeelFp.Of(ChargeScript, "2d_action");
            var preset = FeelRules.Preset("feel.preset.arpg_responsive");
            var ratios = fp.Nums("actiontl.charge_ratios");
            var confirmTicks = new List<int>();
            foreach (var item in fp.Items("spatialhit.confirm_ticks"))
            {
                confirmTicks.Add(FeelFp.TickOf(item));
            }

            var started = new Dictionary<(int Tick, string Unit), int>();
            foreach (var item in fp.Items("hitstop.started"))
            {
                var parts = item.Split(':');
                started[(int.Parse(parts[0], CultureInfo.InvariantCulture), parts[1])] = int.Parse(parts[2], CultureInfo.InvariantCulture);
            }

            // 三次出手 × 两段 = 六次命中确认，顺序为（比例0 段0、段1）（比例中间 段0、段1）（比例1 段0、段1）。
            Assert.Equal(ratios.Count * 2, confirmTicks.Count);
            var overrideAttacker = OverrideMs("attacker_hitstop_ms");
            var overrideTarget = OverrideMs("target_hitstop_ms");
            Assert.NotEqual(preset.N("attacker_hitstop_ms"), overrideAttacker);
            var previous = new[] { 0, 0 };
            for (var i = 0; i < ratios.Count; i++)
            {
                for (var segment = 0; segment < 2; segment++)
                {
                    var baseAttacker = segment == 1 ? overrideAttacker : preset.N("attacker_hitstop_ms");
                    var baseTarget = segment == 1 ? overrideTarget : preset.N("target_hitstop_ms");
                    var expectedAttacker = Math.Min(
                        FeelRules.T(baseAttacker * ScaleAt("attacker_hitstop_ms", ratios[i])), FeelRules.T(preset.N("attacker_hitstop_cap_ms")));
                    var expectedTarget = Math.Min(
                        FeelRules.T(baseTarget * ScaleAt("target_hitstop_ms", ratios[i])), FeelRules.T(preset.N("hitstop_cap_ms")));
                    var tick = confirmTicks[(i * 2) + segment];
                    Assert.Equal(expectedAttacker, started[(tick, "player")]);
                    Assert.Equal(expectedTarget, started[(tick, "stake")]);

                    // 不变量：同一段的顿帧随蓄力比例单调不减。
                    Assert.True(started[(tick, "player")] >= previous[segment], $"比例 {ratios[i]} 段 {segment} 的攻击方顿帧回落");
                    previous[segment] = started[(tick, "player")];
                }
            }

            // 复现：比例最高那次的缩放确实改变了结果（不是缩放倍率恒为 1 的空转）。
            Assert.True(ScaleAt("attacker_hitstop_ms", ratios[ratios.Count - 1]) > 1.0);
        }

        [Fact]
        public void ChargeFeelScale_WithoutTheDeclaration_IsIdentity_TheSameSkillWithoutFeelScaleGivesTheBaseHitstop()
        {
            // 对照：既有蓄力脚本 feel_charge（技能没有声明 feel_scale）三次出手的第 0 段顿帧与蓄力比例无关。
            var fp = FeelFp.Of("feel_charge", "2d_action");
            var preset = FeelRules.Preset("feel.preset.arpg_responsive");
            var expectedAttacker = FeelRules.T(preset.N("attacker_hitstop_ms"));
            var attackerTicks = new HashSet<int>();
            foreach (var item in fp.Items("hitstop.started"))
            {
                var parts = item.Split(':');
                if (parts[1] == "player") attackerTicks.Add(int.Parse(parts[2], CultureInfo.InvariantCulture));
            }

            Assert.Equal(new HashSet<int> { expectedAttacker }, attackerTicks);
        }
    }
}

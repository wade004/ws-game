using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 竖直轴能力包补完新增的三个可选手感字段（ADR-0130 追加决定）：<c>air_hit_reaction</c>（受击方，腾空受击反应）、
    /// <c>launch_stack</c>/<c>launch_stack_cap</c>（攻击方，击飞叠加）。不变量：全部是判定型受击组的可选字段（框架预设不需要补值，
    /// 缺省即 1.95.0 行为）；枚举取值与文档一致；叠加上限是体高倍数（经标定成世界单位）。
    /// </summary>
    public class AirAndLaunchStackFieldTests
    {
        [Fact]
        public void NewFields_AreRegistered_AsOptionalJudgingReactionFields()
        {
            foreach (var name in new[] { FeelFieldNames.AirHitReaction, FeelFieldNames.LaunchStack, FeelFieldNames.LaunchStackCap })
            {
                Assert.True(FeelFields.Default.TryGet(name, out var def), name);
                Assert.True(def.Optional, name);
                Assert.Equal(FeelHalf.Judging, def.Half);
                Assert.Equal(FeelGroup.Reaction, def.Meta.Group);
            }
        }

        [Fact]
        public void EnumValues_MatchTheDocumentedVocabulary()
        {
            FeelFields.Default.TryGet(FeelFieldNames.AirHitReaction, out var air);
            Assert.Equal(new[] { "same", "none", "flinch", "stagger_light", "stagger", "knockback", "knockdown" }, air.EnumValues!.ToArray());
            FeelFields.Default.TryGet(FeelFieldNames.LaunchStack, out var stack);
            Assert.Equal(new[] { "restart", "add" }, stack.EnumValues!.ToArray());
        }

        [Fact]
        public void LaunchStackCap_IsABodyHeightMultiple_WithinTheDocumentedRange()
        {
            FeelFields.Default.TryGet(FeelFieldNames.LaunchStackCap, out var cap);
            Assert.Equal(FeelUnit.BodyHeights, cap.Unit);
            Assert.Equal(0.0, cap.Min);
            Assert.Equal(20.0, cap.Max);
        }
    }
}

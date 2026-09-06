using System;
using Core.Foundation.Common;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary><see cref="ModelCharacterRig"/> 占位实现用例：与外形类型无关的能力（AnimState/
    /// ProceduralAnim）可用，sprite 专属能力抛 <see cref="NotSupportedException"/>（见任务书"model 型
    /// rig 只留接口与 NotSupported 说明"）。</summary>
    public class ModelCharacterRigTests
    {
        [Fact]
        public void SetAnimState_And_ProceduralAnim_Work_LikeSpriteRig()
        {
            var rig = new ModelCharacterRig(new Id("unit.hero_1"));

            rig.SetAnimState(AnimState.Cast);
            Assert.Equal(AnimState.Cast, rig.CurrentAnimState);

            double? sampled = null;
            rig.ProceduralAnim.Scale(new ScaleParams(1.5, 1.0), onSample: v => sampled = v);
            rig.Update(0.5);

            Assert.Equal(1.5, sampled!.Value, 6);
        }

        [Fact]
        public void ResolveAnchorLocalOffset_Throws_NotSupported()
        {
            var rig = new ModelCharacterRig(new Id("unit.hero_1"));

            Assert.Throws<NotSupportedException>(() =>
                rig.ResolveAnchorLocalOffset(new Id("anchor.hand_main"), Direction.Continuous(0.0)));
        }

        [Fact]
        public void ComposeAndApplyLayers_Throws_NotSupported()
        {
            var rig = new ModelCharacterRig(new Id("unit.hero_1"));

            Assert.Throws<NotSupportedException>(() =>
                rig.ComposeAndApplyLayers(Array.Empty<string>(), Direction.Continuous(0.0), _ => new Id("layer.x")));
        }

        [Fact]
        public void ApplyLayers_Throws_NotSupported()
        {
            var rig = new ModelCharacterRig(new Id("unit.hero_1"));

            Assert.Throws<NotSupportedException>(() => rig.ApplyLayers(Array.Empty<Id>()));
        }

        [Fact]
        public void PlayClip_NoFrameAnimPlayerInjected_DoesNotThrow()
        {
            var rig = new ModelCharacterRig(new Id("unit.hero_1"));

            var ex = Record.Exception(() => rig.PlayClip(new Id("anim.sample")));
            Assert.Null(ex);
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.Presentation.Render
{
    /// <summary>
    /// PJ140-01 兼容层用例（<c>architecture/落地计划/audit-c86bfa9-20260908/</c> 第七方审核）：本文件
    /// 刻意用"继续按 1.3 接口形状编写"的写法调用 <see cref="ICharacterRig.HitFrameReached"/>（不是先转
    /// <see cref="IHitFrameEmitter"/> 再订阅）与 <see cref="ViewKind.GameObject"/>（不是 <see cref="ViewKind.Gobj"/>），
    /// 模拟一段没有随本仓库同步升级的外部 1.3 消费方源码——本文件本身能够编译通过、且断言均成立，就是
    /// 对"1.4 恢复了 1.3 源码兼容"这条判断记录的验收证据（对照组：改动前 <c>consumer14_build_final.log</c>
    /// 记录同样写法编译失败 CS0117/CS1061）。
    /// <para>
    /// 两个兼容成员都标了 <see cref="ObsoleteAttribute"/>，本文件在触碰它们的那几行局部禁用 CS0618——
    /// 这正是"1.3 风格消费代码"应有的样子：老代码继续编译，只是会看到过时警告提示改用新成员，不会因为
    /// 本仓库 <c>TreatWarningsAsErrors</c> 而被这几行拖累整个测试工程编译失败。
    /// </para>
    /// </summary>
    public class Pj140_01LegacyConsumerCompatTests
    {
        /// <summary>与 <c>CharacterRigHitFrameSourceTests.FakeRig</c> 同一惯例的最小 <see cref="ICharacterRig"/>
        /// 假实现，额外实现 <see cref="IHitFrameEmitter"/>（框架自带的 <see cref="SpriteCharacterRig"/>/
        /// <see cref="ModelCharacterRig"/> 均同时实现两者，见 <see cref="ICharacterRig"/> 类型注释判断
        /// 记录），只用于验证兼容事件转发，不被复用到生产代码。</summary>
        private sealed class FakeRig : ICharacterRig, IHitFrameEmitter
        {
            public event Action<Id>? HitFrameReached;

            public Id EntityId { get; } = new Id("unit.pj140_01_fake");

            public AnimState CurrentAnimState => AnimState.Idle;

            public IProceduralAnim ProceduralAnim => throw new NotSupportedException();

            public void SetAnimState(AnimState state)
            {
            }

            public void PlayClip(Id clipId, bool loop = false, double speed = 1.0)
            {
            }

            public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing) => null;

            public void ComposeAndApplyLayers(IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId)
            {
            }

            public void ApplyLayers(IReadOnlyList<Id> resourceIds)
            {
            }

            public void Update(double dt)
            {
            }

            public void FireHitFrame() => HitFrameReached?.Invoke(EntityId);
        }

        /// <summary>1.3 风格：只声明 <see cref="ICharacterRig"/> 类型的变量，直接对它订阅
        /// <c>HitFrameReached</c>（不先做 <c>rig is IHitFrameEmitter</c> 探测）——1.3.0 该事件本就是
        /// <see cref="ICharacterRig"/> 的强制成员，消费方没有理由写探测代码。</summary>
        [Fact]
        public void LegacyStyle_ICharacterRigTypedVariable_SubscribesHitFrameReachedDirectly_ForwardsToRealEmitter()
        {
            ICharacterRig rig = new FakeRig();
            Id? raisedFor = null;

#pragma warning disable CS0618 // PJ140-01 兼容层：刻意验证过时成员仍可用。
            rig.HitFrameReached += id => raisedFor = id;
#pragma warning restore CS0618

            ((FakeRig)rig).FireHitFrame();

            Assert.Equal(new Id("unit.pj140_01_fake"), raisedFor);
        }

        /// <summary>退订路径同样必须原样可编译、原样生效——add/remove 都要转发，不能只实现一半。</summary>
        [Fact]
        public void LegacyStyle_ICharacterRigTypedVariable_UnsubscribesHitFrameReached_StopsForwarding()
        {
            ICharacterRig rig = new FakeRig();
            var raised = false;
            Action<Id> handler = _ => raised = true;

#pragma warning disable CS0618
            rig.HitFrameReached += handler;
            rig.HitFrameReached -= handler;
#pragma warning restore CS0618

            ((FakeRig)rig).FireHitFrame();

            Assert.False(raised);
        }

        /// <summary>1.3 风格：<c>ViewKind.GameObject</c> 直接作为字面量使用（不是 <see cref="ViewKind.Gobj"/>），
        /// 与新名字数值相同，可以互相比较/切换而不需要消费方改任何一行分支逻辑。</summary>
        [Fact]
        public void LegacyStyle_ViewKindGameObject_EqualsGobj_CompilesAndComparesEqual()
        {
#pragma warning disable CS0618 // PJ140-01 兼容层：刻意验证过时成员仍可用。
            var legacyKind = ViewKind.GameObject;
#pragma warning restore CS0618

            Assert.Equal(ViewKind.Gobj, legacyKind);
            Assert.Equal((int)ViewKind.Gobj, (int)legacyKind);
        }
    }
}

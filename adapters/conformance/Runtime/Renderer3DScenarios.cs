#nullable enable
// Renderer3DScenarios：IRenderer3D 契约一致性场景（见 02_引擎适配层.md 第 1.12 节）。
// 条件必需接口——桩实现与 Unity 实现（W6-B 收口后）均按正常路径工作。场景仍然保留经
// ConformanceContext.SupportsRenderer3D 在"声明降级"与"真实实现"两条路径间切换断言的能力（不是
// assert.Skip，见任务书"场景允许实现声明降级并跳过"——本组场景选择"声明降级时断言抛出
// NotSupportedException"而不是彻底跳过，因为"全部方法必须抛同一种异常"本身就是一条可确定性验证
// 的契约条款，比单纯跳过更有把关价值）：仍有引擎适配层选择整体声明降级时（02 第 1.12 节"条件
// 必需"允许），把 ConformanceContext.SupportsRenderer3D 显式设为 false 即可复用同一组场景断言。
// ModelId 现指向 W6-B 占位模型 model.placeholder_biped（见 adapters/unity/Assets/Editor/
// GeneratePlaceholderModelAssets.cs）——真实实现路径下 CreateModelInstance 需要一个确实存在的
// 模型资源；桩实现对任意 Id 都返回可用句柄，不受此约束。
using System;
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class Renderer3DScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IRenderer3D>> All = new[]
        {
            new ConformanceScenario<IRenderer3D>("创建销毁_按降级标志分别验证", CreateThenDestroy_RespectsSupportFlag),
            new ConformanceScenario<IRenderer3D>("放置与动画_按降级标志分别验证", PlacementAndAnim_RespectsSupportFlag),
            new ConformanceScenario<IRenderer3D>("槽位挂点材质阴影_按降级标志分别验证", SlotSocketMaterialShadow_RespectsSupportFlag),
            new ConformanceScenario<IRenderer3D>("OnAnimEvent_按降级标志分别验证", OnAnimEvent_RespectsSupportFlag),
            new ConformanceScenario<IRenderer3D>("非循环剪辑结束发finished事件_循环剪辑不发_按降级标志分别验证", NonLoopClipFinishes_LoopClipDoesNot_RespectsSupportFlag),
        };

        private static readonly Id ModelId = new Id("model.placeholder_biped");

        /// <summary>H5b 根治新增：与 model.placeholder_biped 的 AnimatorController 已声明的两个状态
        /// 对应（见 Editor/GeneratePlaceholderModelAssets.cs：attack 非循环 0.5 秒，idle 循环
        /// 1.0 秒）——BareName(clipId) 取末段即状态名，与 ModelId（"model.placeholder_biped"）不同：
        /// 后者是模型资源 id，不对应任何 Animator 状态名，PlacementAndAnim_RespectsSupportFlag 场景
        /// 把它当 clipId 传只是验证"调用不抛异常"，不真正驱动 Animator 播放；本场景需要真正驱动到
        /// 具体状态，因此另取两个精确对应状态名的 clipId。</summary>
        private static readonly Id AttackClipId = new Id("anim.placeholder.attack");
        private static readonly Id IdleClipId = new Id("anim.placeholder.idle");

        /// <summary>H5b 根治新增（游戏侧复核发现 1）：IRenderer3D 追加的契约义务——非循环剪辑
        /// （<c>loop: false</c>）自然播放完成后必须经 OnAnimEvent 发出一次
        /// "anim_event.finished"（与 Presentation.Render.ModelCharacterRig.AnimFinishedEventId 逐字
        /// 相等），循环剪辑不发。声明降级路径复用前两条场景（PlacementAndAnim/OnAnimEvent）已经断言
        /// 过的 NotSupportedException 契约，不在本场景重复断言同一件事。</summary>
        private static IEnumerator NonLoopClipFinishes_LoopClipDoesNot_RespectsSupportFlag(IRenderer3D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (!ctx.SupportsRenderer3D)
            {
                yield break;
            }

            var handle = renderer.CreateModelInstance(ModelId);
            var received = new List<Id>();
            var subscription = renderer.OnAnimEvent(handle, (h, eventId) => received.Add(eventId));

            // 非循环剪辑：自然播放完成后必须收到一次完成事件。
            renderer.PlayAnim(handle, AttackClipId, loop: false, speed: 1.0, blendSeconds: 0.0);
            if (ctx.CompleteNonLoopAnim != null)
            {
                yield return ctx.CompleteNonLoopAnim(handle);
            }
            assert.True(received.Contains(ModelCharacterRigAnimFinishedEventId), "非循环剪辑自然播放完成后应当收到一次 anim_event.finished");

            received.Clear();

            // 循环剪辑：不应该收到完成事件。
            renderer.PlayAnim(handle, IdleClipId, loop: true, speed: 1.0, blendSeconds: 0.0);
            if (ctx.CompleteNonLoopAnim != null)
            {
                yield return ctx.CompleteNonLoopAnim(handle);
            }
            assert.False(received.Contains(ModelCharacterRigAnimFinishedEventId), "循环剪辑不应该触发 anim_event.finished");

            subscription.Dispose();
            renderer.DestroyModelInstance(handle);
        }

        /// <summary>H5b 根治新增：与 Presentation.Render.ModelCharacterRig.AnimFinishedEventId 逐字
        /// 相等的本地常量——adapters/conformance 不引用 presentation/（同类分层惯例见
        /// adapters/stub/StubRenderer3D.cs 同款判断记录），改本地按同一约定构造一份。</summary>
        private static readonly Id ModelCharacterRigAnimFinishedEventId = new Id("anim_event.finished");

        private static IEnumerator CreateThenDestroy_RespectsSupportFlag(IRenderer3D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (!ctx.SupportsRenderer3D)
            {
                assert.Throws<NotSupportedException>(() => renderer.CreateModelInstance(ModelId), "声明降级的实现，CreateModelInstance 应抛 NotSupportedException");
                yield break;
            }

            var handle = renderer.CreateModelInstance(ModelId);
            renderer.DestroyModelInstance(handle);
            assert.Throws<InvalidOperationException>(() => renderer.DestroyModelInstance(handle), "重复销毁同一个模型句柄应抛 InvalidOperationException");
        }

        private static IEnumerator PlacementAndAnim_RespectsSupportFlag(IRenderer3D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (!ctx.SupportsRenderer3D)
            {
                assert.Throws<NotSupportedException>(
                    () => renderer.SetPlacement(default, Vec2.Zero, 0, 0, 1, 0),
                    "声明降级的实现，SetPlacement 应抛 NotSupportedException");
                assert.Throws<NotSupportedException>(
                    () => renderer.PlayAnim(default, ModelId, false, 1, 0),
                    "声明降级的实现，PlayAnim 应抛 NotSupportedException");
                yield break;
            }

            var handle = renderer.CreateModelInstance(ModelId);
            assert.DoesNotThrow(() => renderer.SetPlacement(handle, new Vec2(1, 2), 5, 0.5, 1, 2), "SetPlacement 对存活句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.PlayAnim(handle, ModelId, true, 1.0, 0.2), "PlayAnim 对存活句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.SetAnimSpeed(handle, 1.5), "SetAnimSpeed 对存活句柄不应抛异常");
            renderer.DestroyModelInstance(handle);
        }

        private static IEnumerator SlotSocketMaterialShadow_RespectsSupportFlag(IRenderer3D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (!ctx.SupportsRenderer3D)
            {
                assert.Throws<NotSupportedException>(() => renderer.SetSlotMesh(default, ModelId, null), "声明降级的实现，SetSlotMesh 应抛 NotSupportedException");
                assert.Throws<NotSupportedException>(() => renderer.SetShadow(default, ShadowMode.Blob), "声明降级的实现，SetShadow 应抛 NotSupportedException");
                yield break;
            }

            var parent = renderer.CreateModelInstance(ModelId);
            var child = renderer.CreateModelInstance(ModelId);

            assert.DoesNotThrow(() => renderer.SetSlotMesh(parent, ModelId, null), "SetSlotMesh 对存活句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.AttachToSocket(parent, ModelId, child), "AttachToSocket 对两个存活句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.Detach(child), "Detach 对已挂接的子句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.SetMaterialParam(parent, "conformance_param", 1.0), "SetMaterialParam 对存活句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.SetShadow(parent, ShadowMode.Projected), "SetShadow 对存活句柄不应抛异常");

            renderer.DestroyModelInstance(parent);
            renderer.DestroyModelInstance(child);
        }

        /// <summary>W3b 审计发现补齐：<see cref="IRenderer3D.OnAnimEvent"/> 此前未被任何场景覆盖
        /// （降级路径尤其未验证——见任务书"降级路径断言"）。同本文件其余三条场景一贯的
        /// "声明降级时断言抛同一种异常，未降级时验证真实注册可用"两条腿。</summary>
        private static IEnumerator OnAnimEvent_RespectsSupportFlag(IRenderer3D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (!ctx.SupportsRenderer3D)
            {
                assert.Throws<NotSupportedException>(
                    () => renderer.OnAnimEvent(default, (handle, eventId) => { }),
                    "声明降级的实现，OnAnimEvent 应抛 NotSupportedException");
                yield break;
            }

            var handle = renderer.CreateModelInstance(ModelId);
            SubscriptionHandle? subscription = null;
            assert.DoesNotThrow(
                () => subscription = renderer.OnAnimEvent(handle, (h, eventId) => { }),
                "OnAnimEvent 对存活句柄不应抛异常");
            assert.NotNull(subscription, "OnAnimEvent 应当返回一个非空的 SubscriptionHandle");
            assert.DoesNotThrow(() => subscription!.Dispose(), "退订返回的 SubscriptionHandle 不应抛异常");

            renderer.DestroyModelInstance(handle);
        }
    }
}

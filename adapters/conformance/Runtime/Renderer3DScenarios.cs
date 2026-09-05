#nullable enable
// Renderer3DScenarios：IRenderer3D 契约一致性场景（见 02_引擎适配层.md 第 1.12 节）。
// 条件必需接口——桩实现按正常路径工作；Unity 本迭代整体声明降级（UnityRenderer3D 全部方法抛
// NotSupportedException，见该类型注释）。场景通过 ConformanceContext.SupportsRenderer3D 在两条
// 路径间切换断言，而不是跳过（任务书："场景允许实现声明降级并跳过"——本组场景选择"声明降级时
// 断言抛出 NotSupportedException"而不是彻底跳过，因为"全部方法必须抛同一种异常"本身就是一条
// 可确定性验证的契约条款，比单纯跳过更有把关价值）。
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
        };

        private static readonly Id ModelId = new Id("model.conformance_placeholder");

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
    }
}

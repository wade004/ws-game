#nullable enable
// UnityRenderer3D：IRenderer3D 的 Unity 引擎实现——声明降级（任务书明确要求）。
//
// 判断记录：本迭代的表现路线固定为 sprite 型外形（见 architecture/13 口味配置清单"外形类型"、
// architecture/14_资产规格书模板.md 第 1 节"本框架默认表现路线是 sprite 型外形"），
// IRenderer3D 属于"条件必需"接口——只有游戏层为至少一类外形选择 model 型时才需要真正实现
// （见 02_引擎适配层.md 第 1.12 节"可选性"）。落地计划阶段 4 验收 1 明确本迭代不做 model 型
// 外形的真实三维渲染，因此本类型的全部方法一律抛出 NotSupportedException，不提供任何部分实现，
// 避免"看起来能用但语义不完整"的假象；调用方（游戏层若选择 model 型外形）在真正需要三维渲染时
// 应参照 02 第 4 节"迁移引擎的步骤清单"评估一套完整实现，或走 12_扩展与变更流程.md 补齐。
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityRenderer3D : IRenderer3D
    {
        private const string NotSupportedMessage =
            "IRenderer3D 在本适配层未实现（声明降级，见 02 §1.12 与落地计划阶段 4 验收 1）";

        public ModelHandle CreateModelInstance(Id modelId) => throw new System.NotSupportedException(NotSupportedMessage);

        public void DestroyModelInstance(ModelHandle handle) => throw new System.NotSupportedException(NotSupportedMessage);

        public void SetPlacement(ModelHandle handle, Vec2 planePos, double height, double facing, double scale, double sortY) =>
            throw new System.NotSupportedException(NotSupportedMessage);

        public void PlayAnim(ModelHandle handle, Id clipId, bool loop, double speed, double blendSeconds) =>
            throw new System.NotSupportedException(NotSupportedMessage);

        public void SetAnimSpeed(ModelHandle handle, double speed) => throw new System.NotSupportedException(NotSupportedMessage);

        public SubscriptionHandle OnAnimEvent(ModelHandle handle, AnimEventCallback callback) =>
            throw new System.NotSupportedException(NotSupportedMessage);

        public void SetSlotMesh(ModelHandle handle, Id slotId, Id? meshId) => throw new System.NotSupportedException(NotSupportedMessage);

        public void AttachToSocket(ModelHandle handle, Id socketId, ModelHandle child) =>
            throw new System.NotSupportedException(NotSupportedMessage);

        public void Detach(ModelHandle child) => throw new System.NotSupportedException(NotSupportedMessage);

        public void SetMaterialParam(ModelHandle handle, string paramName, double value) =>
            throw new System.NotSupportedException(NotSupportedMessage);

        public void SetShadow(ModelHandle handle, ShadowMode mode) => throw new System.NotSupportedException(NotSupportedMessage);
    }
}

using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>三维模型实例的不透明句柄。</summary>
    public readonly struct ModelHandle : System.IEquatable<ModelHandle>
    {
        public int Value { get; }

        public ModelHandle(int value) => Value = value;

        public bool Equals(ModelHandle other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ModelHandle other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(ModelHandle left, ModelHandle right) => left.Equals(right);

        public static bool operator !=(ModelHandle left, ModelHandle right) => !left.Equals(right);
    }

    /// <summary>阴影呈现模式：无 / 贴地简化影子 / 投影阴影。</summary>
    public enum ShadowMode
    {
        None,
        Blob,
        Projected
    }

    /// <summary>
    /// 订阅动画剪辑内标记的关键帧事件（命中、收招、脚步声等，事件清单见
    /// 04_数据与内容管线.md 的 display.anim_set）。
    /// </summary>
    public delegate void AnimEventCallback(ModelHandle handle, Id eventId);

    /// <summary>
    /// 三维模型实例句柄、骨骼动画、槽位换装、挂点、材质参数、阴影模式
    /// （见 02_引擎适配层.md 第 1.12 节）。条件必需接口——仅当游戏层为至少一类外形选择
    /// model 型时必需；只使用 sprite 型外形的游戏可以不实现本接口。
    /// </summary>
    public interface IRenderer3D
    {
        ModelHandle CreateModelInstance(Id modelId);

        void DestroyModelInstance(ModelHandle handle);

        /// <summary>
        /// 用世界平面坐标 planePos 加高度 height、朝向、缩放与 sortY 定位模型
        /// （sortY 与 IRenderer2D 共享同一排序空间）。
        /// </summary>
        void SetPlacement(ModelHandle handle, Vec2 planePos, double height, double facing, double scale, double sortY);

        /// <summary>播放一条动画剪辑，blendSeconds 控制与上一动作的过渡混合时长。</summary>
        void PlayAnim(ModelHandle handle, Id clipId, bool loop, double speed, double blendSeconds);

        void SetAnimSpeed(ModelHandle handle, double speed);

        SubscriptionHandle OnAnimEvent(ModelHandle handle, AnimEventCallback callback);

        /// <summary>槽位换装；meshId 为 null 表示卸下该部位网格。</summary>
        void SetSlotMesh(ModelHandle handle, Id slotId, Id? meshId);

        /// <summary>把一个子模型实例挂接到父模型的命名挂点（武器、特效等）。</summary>
        void AttachToSocket(ModelHandle handle, Id socketId, ModelHandle child);

        void Detach(ModelHandle child);

        /// <summary>参数含义由 DisplayInfo 映射决定，本接口不解释参数语义。</summary>
        void SetMaterialParam(ModelHandle handle, string paramName, double value);

        void SetShadow(ModelHandle handle, ShadowMode mode);
    }
}

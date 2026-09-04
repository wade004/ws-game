using Core.Foundation.Common;

namespace Presentation.Camera
{
    /// <summary>
    /// 镜头契约（见 01 L5 模块表 <c>camera</c> 行"契约接口名：CameraHost"、09 第 3.5、8 节）。
    /// 镜头是表现层对象，不是逻辑对象；本接口只做跟随与在配置范围内的缩放，不支持自由旋转
    /// （见 09 第 3.5 节）。
    /// </summary>
    public interface ICameraHost
    {
        /// <summary>设定俯角/朝向/缩放范围 → <c>ICamera.Configure(...)</c>，并把
        /// <paramref name="profile"/> 登记进本实例的 profile 表（供 <see cref="SwitchProfile"/>/
        /// <see cref="Shake"/> 按 id 查找）。</summary>
        void Configure(CameraProfile profile);

        /// <summary>登记一个 profile 但不立即切换（供 <see cref="SwitchProfile"/> 后续按 id 查找）。</summary>
        void RegisterProfile(CameraProfile profile);

        /// <summary>设定跟随目标；实际跟随位置在下一次 <see cref="Update"/> 时生效。</summary>
        void Follow(Id entityId);

        /// <summary>每帧调用：用当前跟随目标的位置（经 <see cref="ICameraFollowTarget"/>，可选按
        /// <see cref="CameraProfile.Bounds"/> 夹取）驱动 <c>ICamera.Follow</c>。</summary>
        void Update(double alpha);

        /// <summary>在当前 profile 的 <c>[ZoomMin, ZoomMax]</c> 范围内设置缩放。</summary>
        void SetZoom(double zoom);

        /// <summary>按当前 profile 的 <c>camera_profile.shake_presets</c> 震屏档位触发震屏。</summary>
        void Shake(Id shakePresetId);

        /// <summary>切换到另一个已登记的 profile（过场镜头，见 09 第 8 节）。</summary>
        void SwitchProfile(Id profileId);
    }
}

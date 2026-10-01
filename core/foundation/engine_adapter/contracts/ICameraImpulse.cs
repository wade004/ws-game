using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 镜头冲击可选能力（手感设计/07 第 2 节、05 第 7 节 <c>supportsCameraImpulse</c>）：<see cref="ICamera"/> 的实现可以
    /// 同时实现本接口，声明自己支持"沿某方向的一次性镜头推移并按衰减时长回落"。不实现本接口、或
    /// <see cref="SupportsCameraImpulse"/> 为假的适配层，由表现层（<c>Presentation.Camera.CameraHost.Impulse</c>）
    /// 退化为 <see cref="ICamera.Shake"/> 并写一条诊断（05 第 7 节"可选能力缺失按预设声明的降级项退化"）。
    /// <para>
    /// 判断记录：独立成一个新接口而不是给 <see cref="ICamera"/> 加成员——<see cref="ICamera"/> 是必需接口，
    /// 既有实现（桩、引擎实现、消费方自写）不需要为一个可选能力改一行代码；探测写法同 <c>IHitFrameEmitter</c>
    /// （<c>camera is ICameraImpulse impulse &amp;&amp; impulse.SupportsCameraImpulse</c>）。幅度以"画面高度比例"计
    /// （00 第 6 节），由实现按当前缩放换算为实际位移，不跨投影直接复用世界距离。
    /// </para>
    /// </summary>
    public interface ICameraImpulse
    {
        /// <summary>适配层是否真正支持镜头冲击（实现类型可以静态实现本接口而按运行环境动态声明不支持）。</summary>
        bool SupportsCameraImpulse { get; }

        /// <summary>
        /// 触发一次镜头冲击：<paramref name="direction"/> 是单位方向（世界平面，镜头沿它被推开后回落；零向量表示无方向，
        /// 实现自行取各向同性处理），<paramref name="magnitude"/> 是峰值幅度（画面高度比例，非负），
        /// <paramref name="decayMs"/> 是从峰值衰减回零的时长（毫秒，正数）。
        /// </summary>
        void Impulse(Vec2 direction, double magnitude, double decayMs);
    }
}

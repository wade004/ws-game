using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 镜头地面可视范围可选能力（ADR-0181）：<see cref="ICamera"/> 的实现可以同时实现本接口，回答"以当前姿态（缩放、偏航、俯仰、投影），
    /// 画面四角在地面（世界平面）上落在哪里"。表现层的镜头宿主据此把跟随点收在"可视范围不越出地图"的区间内
    /// （<c>camera_profile.view_limit</c>，缺省关闭）。不实现本接口、或 <see cref="SupportsCameraGroundFootprint"/> 为假的适配层，
    /// 该约束不生效（镜头行为与没声明时一致，宿主写一条诊断）。
    /// <para>
    /// 判断记录：独立成一个新接口而不是给 <see cref="ICamera"/> 加成员——同 <see cref="ICameraImpulse"/>，必需接口不为一个可选能力改动。
    /// 偏移量取"相对于跟随点（<see cref="ICamera.Follow"/> 收敛到的世界平面位置）"而不是绝对坐标：宿主要回答的问题是"跟随点放哪儿画面才不出界"，
    /// 与镜头当前实际落在哪儿（跟随平滑中、震屏中）无关；正交姿态下偏移量只由缩放、画面宽高比和偏航决定，
    /// 透视俯角下是画面四角射线与地面交点的外接矩形（取当前姿态，近似视为随跟随点平移不变）。
    /// </para>
    /// </summary>
    public interface ICameraGroundFootprint
    {
        /// <summary>适配层是否真正能算出地面可视范围（实现类型可以静态实现本接口而按运行环境动态声明不支持）。</summary>
        bool SupportsCameraGroundFootprint { get; }

        /// <summary>
        /// 当前姿态下画面可视的地面区域，用轴对齐外接矩形表示，坐标是相对于跟随点的偏移：<paramref name="minOffset"/> 是外接矩形左下角，
        /// <paramref name="maxOffset"/> 是右上角。画面四角中任何一角的射线不与地面相交（指向天空、镜头在地面以下）时返回 false，调用方不做约束。
        /// </summary>
        bool TryGetGroundFootprint(out Vec2 minOffset, out Vec2 maxOffset);
    }
}

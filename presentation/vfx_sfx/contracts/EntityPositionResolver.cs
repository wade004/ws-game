using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 按实体 id 取其当前只读世界平面坐标的委托（呼应表现层铁律 P1"只读逻辑状态"）。
    /// <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 在 <see cref="VfxAttachMode.Anchor"/> 且
    /// <see cref="AnchorResolver"/> 查不到具体锚点时，按 09_表现层.md 第 5.3 节判断记录"缺省用实体
    /// 位置"退化到实体本体坐标；两者都查不到时记诊断并跳过播放。返回 null 表示实体不存在/不可见。
    /// </summary>
    public delegate Vec2? EntityPositionResolver(Id entityId);
}

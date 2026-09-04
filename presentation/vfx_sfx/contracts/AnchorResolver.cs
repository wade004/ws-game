using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 按 <c>(entityId, anchorId)</c> 取锚点当前世界坐标的委托，供 <see cref="Presentation.VfxSfx.Core.VfxPlayer"/>
    /// 把 <see cref="VfxAttachMode.Anchor"/> 换算为 <c>IRenderer2D.EmitParticle</c> 需要的世界坐标
    /// （见 09_表现层.md 第 3.3.1 节锚点表；具体锚点查询由 <c>view_binding</c>/<c>render</c> 模块的
    /// <c>CharacterRig</c> 提供，本模块不持有精灵/纸娃娃层结构，只声明这个窄契约注入点，见
    /// vfx_sfx/README.md"契约缺口"）。返回 null 表示查不到该锚点（实体不存在/锚点未登记），
    /// 调用方按"缺省用实体位置"兜底（09 第 3.2 节 direction 档位缺省同一惯例的语义类比）。
    /// </summary>
    public delegate Vec2? AnchorResolver(Id entityId, Id anchorId);
}

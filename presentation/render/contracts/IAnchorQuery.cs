using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 缺口 6：锚点查询窄契约（见 09_表现层.md 第 3.3.1 节锚点表、
    /// <c>presentation/vfx_sfx/contracts/AnchorResolver.cs</c>"具体锚点查询由 view_binding/render
    /// 模块……提供"）。按 <c>(entityId, anchorId)</c> 取该锚点当前的世界坐标，供
    /// <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 的 <c>attach_mode: anchor</c> 挂接模式使用。
    /// <para>
    /// 判断记录（谁实现本接口）：09 第 3.3.1 节锚点表挂在 <c>display.map.anchor_points</c>，需要
    /// "实体当前方向档位"（决定按 <c>display.map.mirror_pairs</c> 是否镜像 x 分量，见
    /// <c>ResolveDirectionSlot</c>）与"实体当前世界坐标"两项运行期状态；<c>RenderConventionHost</c>
    /// 是无状态纯函数集合（见其类型注释"无内部状态，可安全作为单例复用"），本身不持有任何实体状态，
    /// 而 <see cref="Presentation.ViewBinding.ViewBinder"/> 已经持有"实体 id → DisplayId"绑定表与
    /// <see cref="Presentation.Common.ISimSnapshot"/>（位置/朝向的唯一只读来源）——故本接口由
    /// <c>ViewBinder</c> 实现，内部复用 <see cref="IRenderConventionHost.ResolveDirectionSlot"/> 判定
    /// 镜像，而不是另起一份镜像判定逻辑。
    /// </para>
    /// <para>
    /// 镜像下锚点偏移的换算判断记录：<c>anchor_points</c> 只登记"原创绘制"侧（<c>front</c>/<c>_r</c>
    /// 系列档位）的一组偏移，14 第 2.1 节"<c>_l</c> 档位由 <c>_r</c> 档位水平翻转得到"同一惯例延伸到
    /// 锚点——档位解析为镜像（<c>FlipX == true</c>）时，锚点偏移的水平分量（相对于精灵局部竖直中心轴，
    /// 与 <c>IRenderer2D</c> 精灵翻转同一枢轴）取反，纵向分量不变；未镜像时原样使用。
    /// </para>
    /// </summary>
    public interface IAnchorQuery
    {
        /// <summary>
        /// 查询 <paramref name="entityId"/> 身上 <paramref name="anchorId"/> 锚点的当前世界坐标。
        /// 以下情形返回 null 并记一条诊断（不抛异常，见 <c>IPresentationDiagnostics</c> 惯例）：
        /// 实体未绑定 View；该实体的 <c>DisplayInfo</c> 查不到；<c>DisplayInfo.Kind == Model</c>
        /// （model 型外形没有 sprite 锚点，走 09 第 3.3.2 节挂点查询，不归本接口管）；
        /// <c>anchor_points</c> 未登记该 <paramref name="anchorId"/>。
        /// <para>
        /// 判断记录（<paramref name="anchorId"/> 格式）：<see cref="Id"/> 要求"至少一个点分段"（00
        /// 第 4.1 节），而 <c>display.map.anchor_points</c> 的键名是不带点的裸名字（如
        /// <c>"hand_main"</c>）；调用惯例同既有 <c>VfxAttach.Anchor</c>（见
        /// <c>presentation/vfx_sfx/tests/VfxPlayerTests.cs</c>）用 <c>"anchor.hand_main"</c> 这类带
        /// <c>"anchor."</c> 域前缀的 <see cref="Id"/>，实现内部取最后一个点分段还原成裸键名去查
        /// <c>anchor_points</c>（同 <c>DirectionSlots.StripPrefix</c> 一类前缀还原惯例）。
        /// </para>
        /// </summary>
        Vec2? GetAnchorWorldPosition(Id entityId, Id anchorId);
    }
}

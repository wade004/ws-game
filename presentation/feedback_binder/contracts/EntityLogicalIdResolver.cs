using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 按运行期实体 id 取其"逻辑 id"（技能/光环/物品/生物模板 id，即可喂给
    /// <c>IDisplayInfoRegistry.Lookup</c> 的那种 id，见 09 第 5.6 节）的委托，供
    /// <c>from_display: source/target</c> 使用（见 <see cref="FromDisplaySource"/> 类型注释"契约
    /// 缺口"）。本模块契约清单没有提供"实体 id → 模板 id"这层映射（<c>IUnitAccess</c> 只有
    /// faction/level/tags 等查询，没有模板 id 访问器），因此设计为可选注入：不注入时
    /// <c>from_display: source/target</c> 记一条诊断并跳过该动作，不崩溃、不影响其余动作。
    /// 返回 null 表示查不到（实体不存在/无对应模板）。
    /// </summary>
    public delegate Id? EntityLogicalIdResolver(Id entityId);
}

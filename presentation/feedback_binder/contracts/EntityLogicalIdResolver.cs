using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 按运行期实体 id 取其"逻辑 id"（技能/光环/物品/生物模板 id，即可喂给
    /// <c>IDisplayInfoRegistry.Lookup</c> 的那种 id，见 09 第 5.6 节）的委托，供
    /// <c>from_display: source/target</c> 使用（见 <see cref="FromDisplaySource"/> 类型注释）。
    /// <para>
    /// 判断记录（P4-2 更新）：本类型最初记录"本模块契约清单没有提供实体 id → 模板 id 映射
    /// （<c>IUnitAccess</c> 只有 faction/level/tags 等查询，没有模板 id 访问器）"这一契约缺口；
    /// <c>Core.Rules.Common.IUnitAccess</c> 契约本身已经补上 <c>GetTemplateId(Id unitId): Id?</c>
    /// （05 第 1.1 节 <c>Entity.templateId</c>），该缺口已不存在。本委托类型继续保留，作为
    /// <c>Presentation.FeedbackBinder.Core.FeedbackBinder.ResolveEntityLogicalId</c> 的可选覆盖点
    /// （优先于默认的 <c>IUnitAccess.GetTemplateId</c> 路径），供需要"模板 id 之外的另一套映射规则"
    /// （例如按运行期外形覆盖而非出生模板）的具体游戏接入时使用；两者都未注入时
    /// <c>from_display: source/target</c> 记一条诊断并跳过该动作，不崩溃、不影响其余动作。返回 null
    /// 表示查不到（实体不存在/无对应模板）。
    /// </para>
    /// </summary>
    public delegate Id? EntityLogicalIdResolver(Id entityId);
}

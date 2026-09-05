namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// <c>play_vfx</c>/<c>play_sfx</c> 动作的 <c>from_display</c> 来源（见 09_表现层.md 第 6.1 节
    /// <c>from_display: source|target|skill</c>）：经 DisplayInfo 动态解析 vfx_id/sfx_id，而不是
    /// 直接写死。<see cref="Source"/>/<see cref="Target"/> 的解析需要"实体 id → 逻辑 id"（09 §5.6
    /// 的"逻辑 id"是技能/光环/物品/生物模板 id，不是运行期实体 id）；<see cref="Skill"/> 经事件的
    /// <c>skillId</c>/<c>auraDefId</c> 字段直接可得，<see cref="Source"/>/<see cref="Target"/> 默认
    /// 经 <c>Core.Rules.Common.IUnitAccess.GetTemplateId</c> 取模板 id（P4-2 起，见
    /// feedback_binder/README.md 判断记录 5），也可用可选注入的 <c>EntityLogicalIdResolver</c> 覆盖。
    /// </summary>
    public enum FromDisplaySource
    {
        Source,
        Target,
        Skill,
    }
}

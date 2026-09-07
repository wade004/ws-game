namespace Presentation.Common
{
    /// <summary>
    /// View 的种类（见 09_表现层.md 第 2 节"（建议）ViewKind 的枚举值建议至少覆盖：unit、
    /// gobj、projectile、areaTrigger、droppedLoot，与 05 中的逻辑对象分类一一对应"）。
    /// </summary>
    /// <remarks>
    /// 判断记录（第八方深度审核自检，`architecture/落地计划/audit-5c444f1-20260908/`）：本枚举成员
    /// 原名 <c>GameObject</c>，与具体引擎的核心类型同名，在技术无关的架构文档/接口正文里构成模糊的
    /// 技术名误报；改名为 <see cref="Gobj"/>，与 <c>Core.Foundation.SimLoop.EntityKinds.Gobj</c>
    /// （常量值 <c>"gobj"</c>，<c>core/carriers/gobj</c> 模块既有的中立缩写）保持一致，语义不变
    /// （仍是"泛化交互物件/世界对象"这一逻辑分类，不特指任何引擎的具体类型）。
    /// </remarks>
    public enum ViewKind
    {
        Unit,
        Gobj,
        Projectile,
        AreaTrigger,
        DroppedLoot
    }
}

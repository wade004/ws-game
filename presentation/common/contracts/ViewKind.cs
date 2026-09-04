namespace Presentation.Common
{
    /// <summary>
    /// View 的种类（见 09_表现层.md 第 2 节"（建议）ViewKind 的枚举值建议至少覆盖：unit、
    /// gameObject、projectile、areaTrigger、droppedLoot，与 05 中的逻辑对象分类一一对应"）。
    /// </summary>
    public enum ViewKind
    {
        Unit,
        GameObject,
        Projectile,
        AreaTrigger,
        DroppedLoot
    }
}

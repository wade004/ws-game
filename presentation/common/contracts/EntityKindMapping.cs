namespace Presentation.Common
{
    /// <summary>
    /// <c>Entity.Kind</c>（自由字符串，由各 L3/L4 模块自行给出，见 <c>Core.Foundation.SimLoop.Entity.Kind</c>
    /// 注释"实体类型（如 unit、gobj），由具体子类给出"）到 <see cref="ViewKind"/> 的映射。
    /// <para>
    /// 契约缺口（见任务汇报"契约缺口"一节）：架构文档（09 第 2 节）只"建议"<see cref="ViewKind"/>
    /// 应覆盖 unit/gameObject/projectile/areaTrigger/droppedLoot 五类，但没有规定各 L3/L4 模块
    /// <c>Entity.Kind</c> 具体应该写什么字符串——目前代码库里只有三个已落地的 <c>Entity</c> 子类
    /// 给出了确定值：<c>PlayerUnit.Kind == "player"</c>、<c>CreatureUnit.Kind == "creature"</c>
    /// （见 core/carriers/unit）、<c>DroppedLootEntity.Kind == "loot"</c>（见 core/gameplay/loot）。
    /// <c>core/carriers/gobj</c>（GameObject）、抛射物、区域触发器三个模块目前尚未落地对应的
    /// <c>Entity</c> 子类，本类型按 09 第 2 节枚举命名的显而易见惯例先登记
    /// <c>"gobj"</c>/<c>"projectile"</c>/<c>"area_trigger"</c> 三个占位字符串，留待这些模块落地后
    /// 由设计层核对是否一致（若不一致，只需改本文件一处映射表，不影响其余表现层代码）。
    /// </para>
    /// </summary>
    public static class EntityKindMapping
    {
        /// <summary>按 <c>Entity.Kind</c> 字符串解析出 <see cref="ViewKind"/>；未知字符串返回
        /// false（调用方按"跳过、不创建 View、记诊断"处理，见 <c>ViewBinder</c>）。</summary>
        public static bool TryMap(string entityKind, out ViewKind kind)
        {
            switch (entityKind)
            {
                case "player":
                case "creature":
                    kind = ViewKind.Unit;
                    return true;
                case "gobj":
                    kind = ViewKind.GameObject;
                    return true;
                case "projectile":
                    kind = ViewKind.Projectile;
                    return true;
                case "area_trigger":
                    kind = ViewKind.AreaTrigger;
                    return true;
                case "loot":
                    kind = ViewKind.DroppedLoot;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }
    }
}

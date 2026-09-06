using Core.Foundation.SimLoop;

namespace Presentation.Common
{
    /// <summary>
    /// <c>Entity.Kind</c>（自由字符串，由各 L3/L4 模块自行给出，见 <c>Core.Foundation.SimLoop.Entity.Kind</c>
    /// 注释"实体类型（如 unit、gobj），由具体子类给出"）到 <see cref="ViewKind"/> 的映射。
    /// <para>
    /// 判断记录（G1 遗留恢复，取代此前"占位字符串"写法）：<see cref="Core.Foundation.SimLoop.EntityKinds"/>
    /// （G1 新增，见其类型注释）已把代码库里确有落地 <c>Entity</c> 子类在用的取值收敛成词汇表——
    /// <see cref="EntityKinds.Player"/>/<see cref="EntityKinds.Creature"/> 均映射
    /// <see cref="ViewKind.Unit"/>、<see cref="EntityKinds.Gobj"/> 映射
    /// <see cref="ViewKind.GameObject"/>、<see cref="EntityKinds.Loot"/> 映射
    /// <see cref="ViewKind.DroppedLoot"/>、<see cref="EntityKinds.Projectile"/>（收边任务补齐：
    /// <c>core/carriers/projectile</c> 落地 <c>ProjectileHost</c> 时一并登记，见其类型注释）映射
    /// <see cref="ViewKind.Projectile"/>、<see cref="EntityKinds.AreaTrigger"/>（加固任务补齐：
    /// <c>core/gameplay/area_trigger</c> 落地 <c>AreaTriggerEntity</c> 时一并登记，见其类型注释）
    /// 映射 <see cref="ViewKind.AreaTrigger"/>，本类型改用这六个常量，不再手写裸字符串字面量（此前
    /// <c>"area_trigger"</c> 是 <see cref="EntityKinds"/> 尚未登记对应常量前的占位字符串，现已随
    /// <see cref="EntityKinds.AreaTrigger"/> 落地换成常量引用）。
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
                case EntityKinds.Player:
                case EntityKinds.Creature:
                    kind = ViewKind.Unit;
                    return true;
                case EntityKinds.Gobj:
                    kind = ViewKind.GameObject;
                    return true;
                case EntityKinds.Projectile:
                    kind = ViewKind.Projectile;
                    return true;
                case EntityKinds.AreaTrigger:
                    kind = ViewKind.AreaTrigger;
                    return true;
                case EntityKinds.Loot:
                    kind = ViewKind.DroppedLoot;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }
    }
}

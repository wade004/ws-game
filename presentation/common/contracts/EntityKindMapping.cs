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
    /// <see cref="ViewKind.DroppedLoot"/>，本类型改用这四个常量，不再手写裸字符串字面量。
    /// <c>"projectile"</c>/<c>"area_trigger"</c> 两个字符串 <see cref="EntityKinds"/> 未登记（09 第 2
    /// 节建议 <see cref="ViewKind"/> 覆盖五类，但 <c>core/</c> 内抛射物/区域触发器两个模块尚未落地对应
    /// 的 <c>Entity</c> 子类，<see cref="EntityKinds"/> 按"未使用的不发明"原则暂不登记，见其类型注释
    /// 判断记录）——本类型继续按 09 第 2 节命名惯例保留这两个占位字符串，留待对应模块落地
    /// <c>Entity</c> 子类、<see cref="EntityKinds"/> 补上常量后再一并换掉。
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
                case "projectile":
                    kind = ViewKind.Projectile;
                    return true;
                case "area_trigger":
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

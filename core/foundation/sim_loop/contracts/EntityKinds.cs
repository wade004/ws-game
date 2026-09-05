namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="Entity.Kind"/> 已知取值的词汇表（见该属性注释"如 unit、gobj，由具体子类给出"）。
    /// 本类只登记代码库里已经落地、确有具体 <see cref="Entity"/> 子类或
    /// <see cref="IWorldSim.AllocateEntityId(string)"/> 调用点在用的字符串常量，不发明尚未落地的
    /// 取值（见任务书"以代码中实际使用的字面量为准核对，缺的补上，未使用的不发明"）：
    /// <list type="bullet">
    /// <item><see cref="Player"/>：<c>Core.Carriers.Unit.PlayerUnit.Kind</c>。</item>
    /// <item><see cref="Creature"/>：<c>Core.Carriers.Unit.CreatureUnit.Kind</c>，同时是
    /// <c>spawn.table.content_ref</c> 的合法领域值之一（见 <c>Core.Gameplay.Spawn.SpawnHost</c>
    /// 按该领域路由到 <c>CreatureFactory</c>）。</item>
    /// <item><see cref="Gobj"/>：<c>Core.Carriers.Gobj.GameObjectEntity.Kind</c>，同时是
    /// <c>spawn.table.content_ref</c> 的合法领域值之一（路由到 <c>GameObjectFactory</c>）。</item>
    /// <item><see cref="Loot"/>：<c>Core.Gameplay.Loot.DroppedLootEntity.Kind</c>。</item>
    /// </list>
    /// <para>
    /// 判断记录（<c>summon</c>/<c>projectile</c>/<c>area_trigger</c> 未登记）：09 第 2 节建议
    /// <c>ViewKind</c> 覆盖 unit/gameObject/projectile/areaTrigger/droppedLoot 五类，
    /// <c>presentation/common/contracts/EntityKindMapping.cs</c> 因此先登记了
    /// <c>"projectile"</c>/<c>"area_trigger"</c> 两个占位字符串，但 <c>core/</c> 内目前没有任何
    /// <see cref="Entity"/> 子类把 <see cref="Entity.Kind"/> 取这两个值（抛射物/区域触发器尚未落地
    /// 对应实体），<c>summon</c> 同理（<c>core/carriers/summon</c> 尚未落地 <see cref="Entity"/>
    /// 子类）——按"未使用的不发明"原则本类暂不登记这三个，留待对应模块落地 <see cref="Entity"/>
    /// 子类时再补（<c>presentation/common</c> 不在本次改动范围，见任务分工，届时其占位字符串也
    /// 应同步换成本类常量）。
    /// </para>
    /// </summary>
    public static class EntityKinds
    {
        public const string Player = "player";

        public const string Creature = "creature";

        public const string Gobj = "gobj";

        public const string Loot = "loot";
    }
}

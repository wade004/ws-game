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
    /// <item><see cref="Projectile"/>：<c>Core.Carriers.Projectile.ProjectileEntity.Kind</c>
    /// （收边任务落地，见 05 第 1.4 节 <c>Projectile</c>），同时是
    /// <c>presentation/common/contracts/EntityKindMapping.cs</c> 此前占位字符串
    /// <c>"projectile"</c> 对应的正式常量。</item>
    /// <item><see cref="AreaTrigger"/>：<c>Core.Gameplay.AreaTrigger.AreaTriggerEntity.Kind</c>
    /// （加固任务落地，见 05 第 1.5 节 <c>AreaTrigger</c>；<c>AreaTriggerHost.RegisterTrap</c>
    /// 动态登记的陷阱触发体同样落这个 Kind，见该实体类型判断记录），同时是
    /// <c>presentation/common/contracts/EntityKindMapping.cs</c> 此前占位字符串
    /// <c>"area_trigger"</c> 对应的正式常量。</item>
    /// </list>
    /// <para>
    /// 判断记录（<c>summon</c> 仍未登记）：09 第 2 节建议 <c>ViewKind</c> 覆盖
    /// unit/gameObject/projectile/areaTrigger/droppedLoot 五类；<c>projectile</c>/<c>area_trigger</c>
    /// 均已随各自收边/加固任务落地对应 <see cref="Entity"/> 子类（见上），<c>summon</c> 尚未落地
    /// （<c>core/carriers/summon</c> 尚未落地 <see cref="Entity"/> 子类）——按"未使用的不发明"原则
    /// 本类暂不登记这一个，留待该模块落地 <see cref="Entity"/> 子类时再补。
    /// </para>
    /// </summary>
    public static class EntityKinds
    {
        public const string Player = "player";

        public const string Creature = "creature";

        public const string Gobj = "gobj";

        public const string Loot = "loot";

        public const string Projectile = "projectile";

        public const string AreaTrigger = "area_trigger";
    }
}

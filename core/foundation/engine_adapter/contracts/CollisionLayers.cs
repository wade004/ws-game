namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 05_对象模型与世界.md 第 3.6 节"碰撞层规划"三层的标签常量词汇表（加固任务落地：该节此前只有
    /// 文档层面的三行表格，全仓库无对应代码常量，任何空间查询都只能用调用方自己拼的字符串标签，见
    /// architecture/落地计划/文档代码一致性审计_2026-09-06.md"碰撞层三常量"条目）。三层的语义与承载
    /// 位置（05 §3.6 结论"碰撞层是 ISpatialQuery 查询时的过滤维度，不是独立的对象类型"未变，本类只
    /// 是把这三个字符串标签固定下来，供各处按同一约定打标签/过滤，不改变 <see cref="ISpatialQuery"/>
    /// 契约本身——<see cref="QueryFilter"/> 仍然只认识中立的字符串标签，不解释任何标签含义）：
    /// <list type="bullet">
    /// <item><see cref="TerrainBlock"/>：地形阻挡，由 <see cref="INavigation2D"/>（`IsWalkable`/
    /// `FindPath`/`Raycast`）承载；地形对象本身不登记进 <see cref="ISpatialQuery"/> 空间索引，本常量
    /// 只用于标签约定与文档对齐，代码里目前没有任何登记点会真正打这个标签（地形不是 `Entity`）。</item>
    /// <item><see cref="UnitBlock"/>：单位登记进空间索引时携带的标签（见
    /// <c>Core.Carriers.Assembly.CarriersAssembly.DefaultSpatialSyncKinds</c> 给 `creature`/`player`
    /// 打此标签）；是否据此阻挡移动是移动策略的口味开关（见
    /// <c>Core.Carriers.Unit.MovementOptions.UnitBlocking</c>，默认 false，允许单位重叠），不是
    /// 空间索引本身的固定行为。</item>
    /// <item><see cref="TriggerOnly"/>：区域触发实体（<c>Core.Gameplay.AreaTrigger.AreaTriggerEntity</c>）
    /// 登记进空间索引时携带的标签；命中判定/视线感知/移动阻挡一类查询必须排除携带此标签的对象——见
    /// 各查询点判断记录（`RequiredTags` 含 `"unit"` 的查询天然排除；不带 `RequiredTags` 的查询需要
    /// 显式 `ExcludedTags`）。</item>
    /// </list>
    /// </summary>
    public static class CollisionLayers
    {
        /// <summary>地形阻挡：影响导航与视线，由 <see cref="INavigation2D"/> 承载，空间索引不登记
        /// 地形对象（见类型注释）。</summary>
        public const string TerrainBlock = "terrain_block";

        /// <summary>单位间是否互相阻挡移动，策略配置项（默认关闭，见
        /// <c>Core.Carriers.Unit.MovementOptions.UnitBlocking</c>）。</summary>
        public const string UnitBlock = "unit_block";

        /// <summary>只参与 AreaTrigger/感知一类查询，不参与移动阻挡与视线；命中/视线/移动查询必须
        /// 排除携带此标签的对象。</summary>
        public const string TriggerOnly = "trigger_only";
    }
}

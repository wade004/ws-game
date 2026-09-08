using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// 非活体、可交互的世界对象运行期实体（见 05 第 1.3 节 <c>GameObject</c> 字段表）。
    /// <para>
    /// 判断记录——本类型不持有可变运行期状态：05 第 1.3 节 <c>GameObject.state</c>
    /// （<c>Map&lt;String, Value&gt;</c>）"落地为 <c>WorldState</c> 的具名标志……对象本身不单独持久化"，
    /// 本类型因此不声明任何 <c>state</c> 字段/属性——全部可变状态经 <see cref="GameObjectHost"/> 通过
    /// <see cref="Core.Carriers.Common.IWorldFlags"/> 读写（key 见 <see cref="GobjStateKeys"/>），本类型
    /// 只保留 <see cref="Core.Foundation.SimLoop.Entity.TemplateId"/>（必填）与 <see cref="LockId"/>
    /// 两个"标识/引用"性质的字段，呼应 <c>core/carriers/unit</c> 的 <c>Unit</c> 同款做法。
    /// </para>
    /// </summary>
    public sealed class GameObjectEntity : Entity
    {
        public override string Kind => EntityKinds.Gobj;

        /// <summary>当前锁（可空，见 05 第 1.3 节 <c>GameObject.lockId</c>）：初始通常取
        /// <see cref="GameObjectTemplate.LockId"/>，但允许手工放置对象在生成时覆盖，或运行期被脚本
        /// 改变（如"打碎了钥匙孔"一类剧情事件），故声明为可写属性而非构造期只读字段。</summary>
        public Id? LockId { get; set; }

        /// <summary>
        /// CR150-02 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：一个"跨越本次运行期实体
        /// 生命周期仍然稳定"的身份标记，供 <see cref="GameObjectHost"/> 的待补发掉落余量台账（宝箱
        /// <c>Partial</c> 剩余、<c>gather_node</c> 未交付部分）按"这是哪一个刷新点/摆放位置"记账，
        /// 而不是按 <see cref="Core.Foundation.SimLoop.Entity.EntityId"/>——同一张地图 <c>ClearAll</c>
        /// 后重新生成（无论经 <c>SpawnHost</c> 按刷新点重放，还是直接调用
        /// <see cref="GameObjectFactory.Spawn"/>）的实体会拿到一个全新的运行期 id，旧台账按旧 id
        /// 记的账会永久失联（外部审计复现：<c>oldId=gobj.inst_1</c> 的余量仍在，新实体
        /// <c>gobj.inst_2</c> 无法关联上）；本字段则由 <see cref="GameObjectFactory.Spawn"/> 在生成时
        /// 填充为一个"只要地图/位置/模板三者不变就必然相同"的稳定键（见该方法判断记录），同一刷新点
        /// 的历次重新生成都会拿到同一个 <see cref="OriginKey"/>，台账因此能跨实体重建正确关联。
        /// <para>
        /// 可空且默认 <c>null</c>：只有经 <see cref="GameObjectFactory.Spawn"/> 生成的实体才会被自动
        /// 填充；绕过工厂直接 <c>new GameObjectEntity(...)</c> 构造的实体（本模块以外的测试替身，见
        /// <c>core/carriers/assembly</c> 的既有测试）保持 <c>null</c>——<see cref="GameObjectHost"/>
        /// 读取时对 <c>null</c> 退化为使用 <see cref="Core.Foundation.SimLoop.Entity.EntityId"/>（与
        /// 本修复之前完全一致的行为），不因为这个新字段而对既有直接构造用法产生任何行为变化。
        /// </para>
        /// </summary>
        public Id? OriginKey { get; set; }

        /// <summary>
        /// <paramref name="templateId"/> 在本类型是必填的（见 05 第 1.3 节 <c>GameObject.templateId</c>
        /// 字段类型为 <c>Id</c>，非 <c>Optional&lt;Id&gt;</c>——不同于基类 <c>Entity.TemplateId</c>
        /// "手工放置对象可为空"的宽松语义，任何 <see cref="GameObjectEntity"/> 都必然来自某个
        /// <c>gobj.template</c>）。复用基类 <see cref="Entity.TemplateId"/> 作存储位置，只在构造函数
        /// 层面把它变成必填参数（惯例同 <c>core/carriers/unit</c> 的 <c>CreatureUnit</c>）。
        /// </summary>
        public GameObjectEntity(Id entityId, Id mapId, Id templateId, Id? lockId = null)
            : base(entityId, mapId)
        {
            TemplateId = templateId;
            LockId = lockId;
        }
    }
}

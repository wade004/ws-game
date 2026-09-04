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
        public override string Kind => "gobj";

        /// <summary>当前锁（可空，见 05 第 1.3 节 <c>GameObject.lockId</c>）：初始通常取
        /// <see cref="GameObjectTemplate.LockId"/>，但允许手工放置对象在生成时覆盖，或运行期被脚本
        /// 改变（如"打碎了钥匙孔"一类剧情事件），故声明为可写属性而非构造期只读字段。</summary>
        public Id? LockId { get; set; }

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

using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 一个运行期物品实例（见 10 第 2.2 节"<c>ItemInstance</c> 是一条独立结构（实例 id、模板 id、
    /// 堆叠数、实例级词缀/耐久等可选字段）"、07 第 1.3 节 <c>InventoryHost.listItems</c>）。
    /// <see cref="Extra"/> 是 07 第 1.6 节"可选扩展点"（耐久 <c>durability</c>、词缀
    /// <c>socket_ids</c>/随机属性等）的落地位置：这些扩展点本版不展开强类型字段，统一收进一个自由
    /// 形状的 <see cref="JsonObject"/>，默认空对象，避免未来加入某个扩展点时改动本类型签名。
    /// </summary>
    public readonly struct ItemInstance
    {
        private static readonly JsonObject EmptyExtra = new JsonObjectBuilder().Build();

        public Id InstanceId { get; }

        public Id TemplateId { get; }

        public int Count { get; }

        /// <summary>预留扩展字段（耐久/词缀等，见类型注释）；未提供时为一个空 <see cref="JsonObject"/>
        /// （不是 null）。</summary>
        public JsonObject Extra { get; }

        public ItemInstance(Id instanceId, Id templateId, int count, JsonObject? extra = null)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
            Count = count;
            Extra = extra ?? EmptyExtra;
        }

        /// <summary>该实例的不透明引用句柄（见 <see cref="ItemInstanceRef"/>）。</summary>
        public ItemInstanceRef ToRef() => new ItemInstanceRef(InstanceId);
    }
}

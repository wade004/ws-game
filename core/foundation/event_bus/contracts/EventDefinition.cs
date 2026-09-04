using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 单条事件定义：对应数据表 <c>found.event_catalog</c> 的一行
    /// （见 04_数据与内容管线.md 第 1.1 节、schema/found.event_catalog.md）。
    /// </summary>
    public sealed class EventDefinition
    {
        /// <summary>事件 key（表中字段 <c>key</c>）。</summary>
        public Id Key { get; }

        /// <summary>事件所属 domain（表中字段 <c>domain</c>）；对 06 第 8 节已给出 domain 的
        /// 事件照抄该文档的值，不保证一定等于 <see cref="Key"/> 的第一段
        /// （例如 <c>proc.triggered</c> 的 domain 是 <c>skill</c>）。</summary>
        public string Domain { get; }

        /// <summary>事件携带的字段名列表（表中字段 <c>fields</c>），只登记名字，不登记类型。</summary>
        public IReadOnlyList<string> Fields { get; }

        /// <summary>触发时机说明（表中字段 <c>description</c>），可选。</summary>
        public string? Description { get; }

        public EventDefinition(Id key, string domain, IReadOnlyList<string> fields, string? description = null)
        {
            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException("domain 不能为空", nameof(domain));
            }

            Key = key;
            Domain = domain;
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            Description = description;
        }
    }
}

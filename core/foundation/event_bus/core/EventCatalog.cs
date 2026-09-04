using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// <see cref="IEventCatalog"/> 的内存实现：由一组 <see cref="EventDefinition"/> 构造，
    /// 重复 key 在构造期直接抛异常（登记表本身不允许两条记录争用同一个事件 key）。
    /// </summary>
    public sealed class EventCatalog : IEventCatalog
    {
        private readonly Dictionary<Id, EventDefinition> _byKey;
        private readonly List<EventDefinition> _all;

        private EventCatalog(Dictionary<Id, EventDefinition> byKey, List<EventDefinition> all)
        {
            _byKey = byKey;
            _all = all;
        }

        /// <summary>
        /// 从一组事件定义构造登记表。<paramref name="definitions"/> 通常来自数据注册表
        /// 读取 <c>found.event_catalog</c> 后转换出的结果（见 T1-4），本模块只提供这个内存
        /// 构造入口，不做 JSON 读取。
        /// </summary>
        /// <exception cref="InvalidOperationException">存在重复 key。</exception>
        public static EventCatalog FromDefinitions(IEnumerable<EventDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var byKey = new Dictionary<Id, EventDefinition>();
            var all = new List<EventDefinition>();

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("事件定义列表不能包含 null 元素", nameof(definitions));
                }

                if (byKey.ContainsKey(definition.Key))
                {
                    throw new InvalidOperationException($"事件 key 重复登记：\"{definition.Key}\"");
                }

                byKey.Add(definition.Key, definition);
                all.Add(definition);
            }

            return new EventCatalog(byKey, all);
        }

        public bool IsRegistered(Id key) => _byKey.ContainsKey(key);

        public EventDefinition? Get(Id key) => _byKey.TryGetValue(key, out var definition) ? definition : null;

        public IReadOnlyList<EventDefinition> All => _all;
    }
}

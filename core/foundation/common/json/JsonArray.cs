using System;
using System.Collections;
using System.Collections.Generic;

namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// JSON 数组：保持插入（解析出现）顺序的 <see cref="JsonValue"/> 列表。不可变——
    /// 构造之后不提供任何修改方法，与 <see cref="JsonValue"/> 树的"不可变树"约定一致。
    /// </summary>
    public sealed class JsonArray : JsonValue, IReadOnlyList<JsonValue>
    {
        private readonly List<JsonValue> _items;

        public JsonArray()
        {
            _items = new List<JsonValue>();
        }

        public JsonArray(IEnumerable<JsonValue> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new List<JsonValue>(items);
        }

        public override JsonKind Kind => JsonKind.Array;

        public int Count => _items.Count;

        public JsonValue this[int index] => _items[index];

        public List<JsonValue>.Enumerator GetEnumerator() => _items.GetEnumerator();

        IEnumerator<JsonValue> IEnumerable<JsonValue>.GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}

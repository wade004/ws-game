using System;
using System.Collections;
using System.Collections.Generic;

namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// JSON 对象：保持键的插入（解析出现）顺序，用 <c>List&lt;KeyValuePair&lt;string, JsonValue&gt;&gt;</c>
    /// 存主体、外加一个 <c>Dictionary&lt;string, int&gt;</c> 索引加速按键查找。不可变——构造之后
    /// （<see cref="JsonObjectBuilder"/> 完成 <see cref="Build"/> 之后）不提供任何修改方法。
    /// 重复键（同一层出现两次同名字段）在构建期直接报错，不做"后者覆盖前者"的静默处理
    /// （避免数据表里的拼写失误被悄悄接受）。
    /// </summary>
    public sealed class JsonObject : JsonValue, IReadOnlyList<KeyValuePair<string, JsonValue>>
    {
        private readonly List<KeyValuePair<string, JsonValue>> _entries;
        private readonly Dictionary<string, int> _index;

        internal JsonObject(List<KeyValuePair<string, JsonValue>> entries, Dictionary<string, int> index)
        {
            _entries = entries;
            _index = index;
        }

        public override JsonKind Kind => JsonKind.Object;

        public int Count => _entries.Count;

        public KeyValuePair<string, JsonValue> this[int index] => _entries[index];

        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var entry in _entries) yield return entry.Key;
            }
        }

        public bool ContainsKey(string key) => _index.ContainsKey(key);

        public bool TryGetValue(string key, out JsonValue value)
        {
            if (_index.TryGetValue(key, out var i))
            {
                value = _entries[i].Value;
                return true;
            }

            value = JsonNull.Instance;
            return false;
        }

        public JsonValue this[string key]
        {
            get
            {
                if (!TryGetValue(key, out var value))
                {
                    throw new KeyNotFoundException($"JsonObject 不含键 \"{key}\"");
                }
                return value;
            }
        }

        public List<KeyValuePair<string, JsonValue>>.Enumerator GetEnumerator() => _entries.GetEnumerator();

        IEnumerator<KeyValuePair<string, JsonValue>> IEnumerable<KeyValuePair<string, JsonValue>>.GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
    }

    /// <summary>
    /// <see cref="JsonObject"/> 的构建期可变构造器：<see cref="JsonReader"/> 边解析边
    /// <see cref="Add"/>，重复键立即抛异常；<see cref="Build"/> 产出不可变的 <see cref="JsonObject"/>。
    /// 供程序化构造 <see cref="JsonObject"/>（而非从文本解析）时复用同一套重复键检查逻辑。
    /// </summary>
    public sealed class JsonObjectBuilder
    {
        private readonly List<KeyValuePair<string, JsonValue>> _entries = new List<KeyValuePair<string, JsonValue>>();
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        public JsonObjectBuilder Add(string key, JsonValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));

            if (_index.ContainsKey(key))
            {
                throw new ArgumentException($"对象中键 \"{key}\" 重复", nameof(key));
            }

            _index.Add(key, _entries.Count);
            _entries.Add(new KeyValuePair<string, JsonValue>(key, value));
            return this;
        }

        public JsonObject Build() => new JsonObject(_entries, _index);
    }
}

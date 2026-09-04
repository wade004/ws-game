using System;
using System.Collections.Generic;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 挂载点回调收到的参数包：对一个只读 <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// 的类型安全包装（见 03_运行时骨架.md 第 9 节 <c>HookRegistry.invoke(hookId, args:
    /// Map&lt;String, Any&gt;)</c>）。本类型不做任何格式/命名校验——具体参数名与类型由各
    /// 挂载点 <see cref="HookPointDefinition.Signature"/> 的文本说明约定，本类型只负责
    /// 安全地按名字、按类型取值。
    /// </summary>
    public sealed class HookArgs
    {
        /// <summary>不携带任何参数的共享实例，供未声明任何参数的挂载点调用复用。</summary>
        public static readonly HookArgs Empty = new HookArgs(new Dictionary<string, object?>());

        private readonly IReadOnlyDictionary<string, object?> _values;

        public HookArgs(IReadOnlyDictionary<string, object?> values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>
        /// 按名字取参数并要求类型为 <typeparamref name="T"/>；不存在或类型不匹配抛
        /// <see cref="KeyNotFoundException"/>/<see cref="InvalidOperationException"/>。
        /// </summary>
        public T Get<T>(string name)
        {
            if (!_values.TryGetValue(name, out var raw))
            {
                throw new KeyNotFoundException($"HookArgs 中不存在参数 \"{name}\"");
            }

            if (raw is T typed)
            {
                return typed;
            }

            if (raw is null && !typeof(T).IsValueType)
            {
                return default!;
            }

            throw new InvalidOperationException(
                $"HookArgs 参数 \"{name}\" 的实际类型是 \"{raw?.GetType().Name ?? "null"}\"，" +
                $"与期望类型 \"{typeof(T).Name}\" 不匹配");
        }

        /// <summary>按名字尝试取参数；不存在或类型不匹配返回 false，不抛异常。</summary>
        public bool TryGet<T>(string name, out T value)
        {
            if (_values.TryGetValue(name, out var raw))
            {
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }

                if (raw is null && !typeof(T).IsValueType)
                {
                    value = default!;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        /// <summary>参数名是否存在（不检查类型）。</summary>
        public bool ContainsKey(string name) => _values.ContainsKey(name);
    }
}

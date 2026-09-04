using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// rng.stream_states 段的 <see cref="IPersistable"/> 实现（10_存档与持久化.md 第 2.4 节）：
    /// 保存全部已创建流的 <see cref="RngStreamState"/> 文本形式，加载时逐条
    /// <see cref="IRngHost.SetStreamState"/> 恢复——目标流不存在时按 <c>IRngHost</c> 的懒创建
    /// 语义自动创建（见 rng/README.md"懒创建与派生"一节，<c>SetStreamState</c> 对不存在的流
    /// 直接以给定状态创建，不依赖先前的派生初始状态）。
    /// </summary>
    public sealed class RngStreamsPersistable : IPersistable
    {
        private readonly IRngHost _rng;

        public RngStreamsPersistable(IRngHost rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public string SectionKey => SaveSections.RngStreamStates;

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            foreach (var stream in _rng.Streams)
            {
                var state = _rng.GetStreamState(stream);
                builder.Add(stream.Value, new JsonString(state.ToString()));
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonObject sections))
            {
                throw new FormatException(
                    $"rng.stream_states 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            foreach (var entry in sections)
            {
                if (!Id.TryParse(entry.Key, out var streamId))
                {
                    throw new FormatException($"rng.stream_states 段中流标识 \"{entry.Key}\" 不是合法的 Id");
                }

                if (!(entry.Value is JsonString text) || !RngStreamState.TryParse(text.Value, out var state))
                {
                    throw new FormatException($"rng.stream_states 段中流 \"{entry.Key}\" 的状态文本非法");
                }

                _rng.SetStreamState(streamId, state);
            }
        }
    }
}

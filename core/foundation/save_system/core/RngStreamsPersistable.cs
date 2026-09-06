using System;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// rng.stream_states 段的 <see cref="IPersistable"/> 实现（10_存档与持久化.md 第 2.4 节）：
    /// 保存全部已创建流的 <see cref="RngStreamState"/> 文本形式与当前主种子
    /// （<see cref="IRngHost.MasterSeed"/>），加载时先按存档的主种子 <see cref="IRngHost.Reset"/>
    /// （清空全部残留流、换回存档时的主种子），再逐条 <see cref="IRngHost.SetStreamState"/> 恢复
    /// 已保存的流。
    /// <para>
    /// P1-04 收口（此前只逐条 <c>SetStreamState</c>，没有 <c>Reset</c>，也不保存主种子）：这会
    /// 留下两类确定性缺口——(a) 读档前该 <see cref="IRngHost"/> 实例上任何"残留"流（例如同一进程
    /// 里读档前已经被别的逻辑访问过、但存档里没有记录——通常发生在"没有先重建一个全新 Host 就地
    /// 读档"的调用方式）不会被清空，继续携带读档前的状态；(b) 存档里完全没有出现过的流（"未来
    /// 才第一次被访问的流"）在懒创建时会派生自调用方另行构造 <see cref="IRngHost"/> 时给的主种子，
    /// 而不是存档时的主种子，读档后与存档前的后续序列不一致。现在 <see cref="Load"/> 先
    /// <c>Reset(savedMasterSeed)</c> 再恢复流，两个问题一并解决。
    /// </para>
    /// <para>
    /// 向后兼容：<see cref="Save"/> 恒写出 <c>master_seed</c>；旧格式存档（没有该字段）
    /// <see cref="Load"/> 时退化为旧行为（不 <c>Reset</c>，只逐条 <c>SetStreamState</c>），因为
    /// 旧存档从未记录过主种子、无法确定应该 Reset 到哪个值——这是已知的旧存档兼容边界，不是
    /// 新写入的存档会遇到的情况。
    /// </para>
    /// </summary>
    public sealed class RngStreamsPersistable : IPersistable
    {
        private const string MasterSeedKey = "master_seed";

        private readonly IRngHost _rng;

        public RngStreamsPersistable(IRngHost rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public string SectionKey => SaveSections.RngStreamStates;

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            // master_seed 用十六进制文本（同 RngStreamState 的段文本惯例）而不是 JsonNumber：
            // JsonNumber 内部用 double 存储（见 Core.Foundation.Common.Json.JsonNumber），
            // ulong 全值域（最大 2^64-1）经 double 往返会丢精度，文本形式则精确无损。
            builder.Add(MasterSeedKey, new JsonString(_rng.MasterSeed.ToString("x16", CultureInfo.InvariantCulture)));

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

            if (sections.TryGetValue(MasterSeedKey, out var masterSeedRaw))
            {
                if (!(masterSeedRaw is JsonString masterSeedText) ||
                    !ulong.TryParse(masterSeedText.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var masterSeed))
                {
                    throw new FormatException($"rng.stream_states 段中 \"{MasterSeedKey}\" 的文本非法");
                }

                // 先清空全部残留流并换回存档时的主种子，再逐条恢复已保存的流状态（见类型注释
                // P1-04 判断记录）；恢复顺序无关紧要——Reset 之后 SetStreamState 对每条流都是
                // 独立覆盖，不依赖彼此的懒创建初始状态。
                _rng.Reset(masterSeed);
            }

            foreach (var entry in sections)
            {
                if (entry.Key == MasterSeedKey)
                {
                    continue;
                }

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

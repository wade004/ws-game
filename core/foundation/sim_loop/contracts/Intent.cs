using System;
using System.Text.RegularExpressions;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 意图原语（见 03_运行时骨架.md 第 4.2 节步骤 1"输入意图收集"："玩家动作与 AI 意图
    /// 使用同一种意图数据结构"；第 3.2 节第 6 条"回放 = 意图序列"）。玩家经 <c>InputMap</c>
    /// 转译出的动作意图与 L2 AI 模块产生的意图都装进同一个 <see cref="Intent"/> 结构，
    /// 本模块（<c>sim_loop</c>）不解释 <see cref="Kind"/>/<see cref="Args"/> 的具体业务含义，
    /// 只搬运（呼应 <c>save_system</c> 的 <c>ReplayInputRecord</c> 对 <c>Args</c> 的同一约定）。
    /// <para>
    /// 与 <c>Core.Foundation.SaveSystem.ReplayInputRecord</c>"可互转"：两者字段一一对应
    /// （<see cref="ActorId"/> ↔ <c>ActorId</c>、<see cref="Kind"/> ↔ <c>IntentKind</c>、
    /// <see cref="Args"/> ↔ <c>Args</c>，外加 <c>ReplayInputRecord</c> 独有的 <c>Tick</c>），
    /// 本类型不反向依赖 <c>save_system</c>（避免两个 L0 模块相互依赖），转换由持有双方类型
    /// 的调用方（如 <c>save_system.ReplayPlayer</c>/<c>ReplayRecorder</c>）按字段直接构造。
    /// </para>
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        private static readonly Regex KindFormatRegex = new Regex(
            "^[a-z][a-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonObject EmptyArgs = new JsonObjectBuilder().Build();

        /// <summary>发出该意图的行动者。</summary>
        public Id ActorId { get; }

        /// <summary>意图种类：全小写、下划线分隔（如 <c>"move"</c>、<c>"cast"</c>），
        /// 具体词汇表由规则层/玩法层约定，本类型只校验格式。</summary>
        public string Kind { get; }

        /// <summary>意图携带的参数；未提供时为一个空对象（不是 null），
        /// 便于调用方无条件按 <see cref="JsonObject"/> API 读取。</summary>
        public JsonObject Args { get; }

        public Intent(Id actorId, string kind, JsonObject? args = null)
        {
            if (string.IsNullOrEmpty(kind) || !KindFormatRegex.IsMatch(kind))
            {
                throw new ArgumentException(
                    $"意图 Kind 格式非法：\"{kind ?? "<null>"}\"，应满足 ^[a-z][a-z0-9_]*$（全小写、下划线分隔）",
                    nameof(kind));
            }

            ActorId = actorId;
            Kind = kind;
            Args = args ?? EmptyArgs;
        }

        /// <summary>序列化为 <see cref="JsonObject"/>：<c>{ "actorId", "kind", "args" }</c>。</summary>
        public JsonObject ToJson()
        {
            return new JsonObjectBuilder()
                .Add("actorId", new JsonString(ActorId.Value))
                .Add("kind", new JsonString(Kind))
                .Add("args", Args)
                .Build();
        }

        /// <summary>按 <see cref="ToJson"/> 的形状反序列化；<c>args</c> 字段缺失时视为空对象。</summary>
        public static Intent FromJson(JsonObject json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var actorId = new Id(((JsonString)json["actorId"]).Value);
            var kind = ((JsonString)json["kind"]).Value;
            var args = json.TryGetValue("args", out var argsValue) && argsValue is JsonObject argsObject
                ? argsObject
                : EmptyArgs;

            return new Intent(actorId, kind, args);
        }

        public bool Equals(Intent other) =>
            ActorId.Equals(other.ActorId)
            && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
            && ReferenceEquals(Args, other.Args);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ActorId.GetHashCode();
                hash = (hash * 397) ^ (Kind != null ? StringComparer.Ordinal.GetHashCode(Kind) : 0);
                return hash;
            }
        }

        public override string ToString() => $"Intent(actorId={ActorId}, kind={Kind})";

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }
}

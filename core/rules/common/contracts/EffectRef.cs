using Core.Foundation.Common.Json;

namespace Core.Rules.Common
{
    /// <summary>
    /// <c>skill.def.effects</c> 列表中的一项（见 06 第 3.1 节 <c>effects: List&lt;EffectRef&gt;</c>
    /// "效果列表，每项引用一个效果原语与其参数"）。<see cref="Params"/> 保持数据表原始 JSON 结构不做
    /// 强类型展开——每种 <see cref="EffectKind"/> 的参数形状不同（见 06 第 3.2 节"参数要点"列），
    /// 由各原语的执行逻辑（skill 模块职责范围，不属于本共享契约）自行按 <see cref="Kind"/> 解释
    /// <see cref="Params"/> 的字段。
    /// </summary>
    public readonly struct EffectRef
    {
        private static readonly JsonObject Empty = new JsonObjectBuilder().Build();

        public EffectKind Kind { get; }

        private readonly JsonObject? _params;

        /// <summary>该效果项的参数；未提供时为一个空 <see cref="JsonObject"/>（不是 null）。</summary>
        public JsonObject Params => _params ?? Empty;

        public EffectRef(EffectKind kind, JsonObject? @params = null)
        {
            Kind = kind;
            _params = @params ?? Empty;
        }
    }
}

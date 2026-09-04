using System;
using Core.Foundation.Expr;

namespace Core.Rules.ExprHost
{
    /// <summary>
    /// 通用 <see cref="IExprSchema"/> 合并帮助类型：按传入顺序依次尝试每一份 schema，第一个命中
    /// 的签名生效。集成任务改动（阶段 3 整理）：从 <c>core/gameplay/world_state/contracts</c> 迁移
    /// 到本目录，作为 <see cref="RulesExprSchema.Compose"/> 的实现细节——迁移前该类型与
    /// <c>RulesExprSchema</c>"已知分组一律放行未登记 key"的旧行为组合时，任何排在
    /// <c>RulesExprSchema</c> 之后的精确 <c>world</c>/<c>quest</c>/<c>player</c> 登记表都可能被
    /// <c>RulesExprSchema</c> 提前截获（见迁移前 <c>WorldExprSchemaEntries</c> 顶部判断记录"契约
    /// 缺口"）；<c>RulesExprSchema</c> 改为严格模式（ADR-0015：未登记的 <c>group.key</c> 一律不是
    /// 合法引用，落回 Id 字面量）后，这个已知不兼容不再存在——本类型不需要再叮嘱调用方注意登记表
    /// 顺序，按 <see cref="RulesExprSchema.Compose"/> 的默认顺序（extras 优先于
    /// <see cref="RulesExprSchema.Base"/>）传入即可。
    /// </summary>
    public sealed class CompositeExprSchema : IExprSchema
    {
        private readonly IExprSchema[] _schemas;

        public CompositeExprSchema(params IExprSchema[] schemas)
        {
            if (schemas == null || schemas.Length == 0)
            {
                throw new ArgumentException("至少需要一份 IExprSchema", nameof(schemas));
            }
            _schemas = schemas;
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            for (var i = 0; i < _schemas.Length; i++)
            {
                if (_schemas[i].TryGetSignature(group, key, out signature))
                {
                    return true;
                }
            }
            signature = default;
            return false;
        }
    }
}

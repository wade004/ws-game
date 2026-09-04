using System;
using Core.Foundation.Expr;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// 通用 <see cref="IExprSchema"/> 合并帮助类型：按传入顺序依次尝试每一份 schema，第一个命中
    /// 的签名生效（任务书"若 RulesExprSchema 不可扩展，提供一个 ExprSchema 合并帮助函数"的落地）。
    /// 用于把 <see cref="WorldExprSchemaEntries.RegisterInto"/> 产出的精确登记与
    /// <c>core/rules/expr_host.RulesExprSchema.Instance</c>（覆盖 <c>self</c>/<c>target</c>/
    /// <c>combat</c>/<c>enemies</c>/<c>time</c> 等其它八个分组）合并成组装层实际使用的一份
    /// <see cref="IExprSchema"/>。
    /// <para>
    /// 判断记录：把精确的 <c>world</c> 登记表排在前面并不能完全消除
    /// <see cref="WorldExprSchemaEntries"/> 顶部记录的解析歧义——只要 <c>RulesExprSchema</c> 出现在
    /// 链条里，<c>world.&lt;路径&gt;</c> 这类未被本类型任何一份精确 schema 命中的 key 最终仍会落到
    /// <c>RulesExprSchema</c> 的"已知分组放行"分支。本类型仍然有用（用于合并 <c>self</c>/<c>target</c>
    /// 等分组、以及为 <c>world.get</c>/<c>has</c>/<c>get_int</c> 提供比"已知分组放行"更精确的参数个数
    /// 校验），但不是那处歧义的完整解法——完整解法需要一份对 <c>world</c> 分组不放行未登记 key 的
    /// schema（见 <see cref="WorldExprSchemaEntries.BuildStandalone"/>），不把 <c>RulesExprSchema</c>
    /// 放进同一条查找链的 <c>world</c> 部分。
    /// </para>
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

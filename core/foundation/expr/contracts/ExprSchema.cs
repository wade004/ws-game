using System;
using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// <see cref="IExprSchema"/> 的可编程实现：调用方用 <see cref="Register"/> 逐条登记
    /// <c>group.key</c> 的签名，供内容校验期使用。
    /// </summary>
    public sealed class ExprSchema : IExprSchema
    {
        private readonly Dictionary<string, ExprSignature> _signatures = new Dictionary<string, ExprSignature>(StringComparer.Ordinal);

        public ExprSchema Register(string group, string key, ExprValueKind returnKind, params ExprValueKind[] argKinds)
        {
            if (string.IsNullOrEmpty(group)) throw new ArgumentException("group 不能为空", nameof(group));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 不能为空", nameof(key));

            _signatures[MakeKey(group, key)] = new ExprSignature(returnKind, argKinds ?? Array.Empty<ExprValueKind>());
            return this;
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            return _signatures.TryGetValue(MakeKey(group, key), out signature);
        }

        // group/key 合法字符集仅含小写字母、数字、下划线、点；用 '#' 分隔，避免
        // group="a", key="bc" 与 group="ab", key="c" 撞键。
        private static string MakeKey(string group, string key) => group + "#" + key;
    }
}

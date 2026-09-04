using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// 某个 <c>group.key</c> 引用的静态签名：返回类型与各参数的期望类型，供
    /// <see cref="IExprSchema"/> 登记、<c>ExprValidator</c> 做静态校验用。
    /// </summary>
    public readonly struct ExprSignature
    {
        public ExprValueKind ReturnKind { get; }

        public IReadOnlyList<ExprValueKind> ArgKinds { get; }

        public ExprSignature(ExprValueKind returnKind, IReadOnlyList<ExprValueKind> argKinds)
        {
            ReturnKind = returnKind;
            ArgKinds = argKinds;
        }
    }
}

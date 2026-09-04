namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 类型集合（见 00_架构总则.md 4.1 节、04_数据与内容管线.md 6.3 节）：
    /// Bool、Int、Number、String、Id 五种，Expr 语言本身没有其它类型。
    /// </summary>
    public enum ExprValueKind
    {
        Bool,
        Int,
        Number,
        String,
        Id,
    }
}

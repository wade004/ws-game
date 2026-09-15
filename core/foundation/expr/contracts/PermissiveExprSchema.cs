using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// 分阶段落地计划 T-N5-2：只读遍历场景专用的兜底 <see cref="IExprSchema"/>——
    /// <see cref="TryGetSignature"/> 对任意 <c>group.key</c> 组合恒返回 <c>true</c>，签名内容本身
    /// （<see cref="ExprSignature.ReturnKind"/>/<see cref="ExprSignature.ArgKinds"/>）没有实际意义。
    /// <para>
    /// <b>用途</b>：配合 <see cref="ExprReferenceCollector"/> 只需要把 <c>group.key</c> 或
    /// <c>group.key(args)</c> 形态的点分标识符一致地解析成 <see cref="ExprReferenceNode"/>（而不是
    /// 因为某个分组/key 未登记而在词法正确的表达式上让
    /// <see cref="ExprParser.Parse(string, IExprSchema)"/> 抛 <see cref="ExprParseException"/>——见
    /// <c>ExprParser.ParseIdentTerm</c> 判断记录"未登记的引用不能带参数列表"）的场景：调用方只想
    /// 枚举表达式里出现过的全部引用节点，不关心某个具体 <c>group.key</c> 是否真的在生产环境的
    /// 宿主/校验期 <see cref="IExprSchema"/> 里注册过、也不关心参数个数或类型是否匹配——那些判断
    /// 留给内容真正加载时使用的生产 schema 与既有 <c>expr_parsable</c>/<see cref="ExprValidator"/>
    /// 校验通道负责（本类型不重复、也不替代那条校验路径）。
    /// </para>
    /// <para>
    /// <b>不适用场景</b>：不用于内容加载（<c>DataRegistryOptions.ExprSchema</c>）或运行期求值
    /// （<c>IExprHostFactory</c> 一类运行期装配）——那两处必须使用能反映真实宿主能力的 schema，本类型
    /// "什么都认识"的宽松特性会让原本该报"未知引用"的错误内容悄悄通过。也不用于
    /// <see cref="ExprValidator.Validate"/>：本类型登记的 <see cref="ExprSignature"/> 不代表任何
    /// 真实类型约束，跑静态类型校验没有意义。
    /// </para>
    /// <para>
    /// 判断记录（为什么不复用某个模块已有的"完整组合" schema，如
    /// <c>Core.Rules.ExprHost.RulesExprSchema.Base</c>/<c>Core.Gameplay.Assembly.GameplaySchemaCatalog.FullExprSchema</c>）：
    /// 本类型所在的 <c>core/foundation/expr</c> 是 L0 基础层，不能反向依赖 L2 <c>core/rules</c>/
    /// L4 <c>core/gameplay</c>（01 分层与依赖）；同一个只读遍历场景的调用方（如 L1
    /// <c>core/numbers/stat_block</c> 的 <c>StatDefinitionConsumerValidationRule</c>，见该类型
    /// 判断记录）也不能反向依赖 L2/L4 具体登记表，且真实内容里出现的具体分组/key 集合会随游戏层
    /// 内容不断增长，任何一份"完整组合" schema 都不可能穷举——本类型用"全部放行"从根本上避免
    /// 这个穷举问题，与 ADR-0020"不允许派生出第二套（词法）规则"同一取舍方向：不新造一份需要
    /// 跟哪个模块的登记表同步的平行清单。
    /// </para>
    /// </summary>
    public sealed class PermissiveExprSchema : IExprSchema
    {
        public static readonly PermissiveExprSchema Instance = new PermissiveExprSchema();

        private static readonly ExprSignature AnySignature =
            new ExprSignature(ExprValueKind.Bool, System.Array.Empty<ExprValueKind>());

        private PermissiveExprSchema()
        {
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            signature = AnySignature;
            return true;
        }

        /// <summary>门禁要求：带默认实现的接口成员必须显式转发/重写（见
        /// <c>presentation/assembly/tests/InterfaceDefaultMemberForwardingTests.cs</c>）。本类型
        /// "什么 group.key 都认识"，没有一份真正的已登记 key 清单可枚举——显式转发为与
        /// <see cref="IExprSchema.KnownKeys"/> 默认实现相同的空集合（纯粹满足门禁要求，不改变
        /// 行为；调用方不应该、也没有必要对本类型做 <c>KnownKeys</c>/<see cref="KnownGroups"/>
        /// 意义上的自动补全/字段清单展示——那是给真正的生产 schema 用的能力，见类型顶部判断
        /// 记录"不适用场景"）。</summary>
        public IReadOnlyCollection<string> KnownKeys(string group) => System.Array.Empty<string>();

        /// <summary>同上——显式转发为空集合。</summary>
        public IReadOnlyCollection<string> KnownGroups => System.Array.Empty<string>();
    }
}

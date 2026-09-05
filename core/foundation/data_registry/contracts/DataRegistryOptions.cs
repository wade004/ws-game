using Core.Foundation.Expr;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>校验严格级别（见 01_分层与依赖.md L0 模块表 <c>data_registry</c> 行"策略配置项：
    /// 校验严格级别（警告/阻断）"）。</summary>
    public enum DataRegistryStrictness
    {
        /// <summary>只有 Warning、没有 Error 时仍可正常读取（<see cref="ValidationReport.IsBlocking"/> 为 false）。</summary>
        WarningsAllowed,

        /// <summary>存在 Warning 也阻断读取，与存在 Error 视同（内容提交门槛更严格的项目可选用）。</summary>
        WarningsBlock,
    }

    /// <summary>
    /// <see cref="DataRegistry"/> 的构造期策略配置（见 04 第 4～7 节涉及的可配置项：严格级别、
    /// 默认语言、Expr 校验用 schema、未知表处理策略）。
    /// </summary>
    public sealed class DataRegistryOptions
    {
        public DataRegistryStrictness Strictness { get; set; } = DataRegistryStrictness.WarningsAllowed;

        /// <summary>默认语言（见 04 第 7.2 节"语言表 l10n.locale 定义支持的语言 id 与回退链"），
        /// 用于 <c>text_key_exists</c> 校验项：文本键必须能在 <c>l10n.text</c> 按此语言查到。
        /// 默认 <c>l10n.locale.zh_cn</c>。</summary>
        public CommonId DefaultLocale { get; set; } = new CommonId("l10n.locale.zh_cn");

        /// <summary>Expr 字段（<see cref="FieldKind.Expr"/>）解析/校验用的引用登记表；为 null 时
        /// <c>expr_parsable</c> 校验项跳过实际解析，只记一条 Warning（见任务书"ExprSchema 为空 →
        /// 警告并跳过"）。</summary>
        public IExprSchema? ExprSchema { get; set; }

        /// <summary>数据源里出现未登记 <see cref="TableSchema"/> 的表时：true（默认）报
        /// <c>envelope</c> 错误；false 则以"无 schema 表"加载并只做信封检查（见
        /// <see cref="TableSchema.Unschematized"/>）。</summary>
        public bool FailOnUnknownTable { get; set; } = true;

        /// <summary>框架数据行覆盖语义（数据行覆盖语义任务新增，见 <c>DataRegistry</c> 类型级判断
        /// 记录"覆盖语义"）：多根合并时，是否允许后层行用行级字段 <c>"override": true</c> 整行替换
        /// 前层同主键行（前层行声明 <c>"final": true</c> 时仍拒绝被覆盖，见该判断记录）。默认
        /// <c>true</c>；设为 <c>false</c> 时 <c>override</c>/<c>final</c> 两个字段完全不生效，
        /// 跨根同主键重复一律按原规则（改动前行为）判定为阻断错误——供需要禁用覆盖机制、
        /// 强制"同名必须显式改名"的项目/测试选用。</summary>
        public bool AllowOverride { get; set; } = true;
    }
}

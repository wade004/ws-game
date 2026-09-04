using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest.def</c> 专属内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验规则的
    /// 扩展点"、任务书"校验：type 合法且 target_ref 域名与类型匹配……prerequisite Expr 可解析"）。
    /// 校验方式：对每条记录调用 <see cref="QuestDefinition.FromRecord"/>——该方法本身已经把
    /// "objectives[].type 合法"、"target_ref 域名匹配"、"explore/escort/talk 的 count==1"、
    /// "prerequisite/eventFilter 可解析"全部作为解析期的强制检查（格式错误/域名不匹配抛
    /// <see cref="System.FormatException"/>/<see cref="System.ArgumentException"/>，Expr 语法错误抛
    /// <see cref="ExprParseException"/>），本规则只负责捕获这些异常并转换成
    /// <see cref="ValidationIssue"/>，不重复实现一遍同样的检查逻辑。
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new QuestContentValidationRule())</c>（惯例同
    /// <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class QuestContentValidationRule : IValidationRule
    {
        private const string Check = "quest_content";

        private readonly IExprSchema _exprSchema;

        public QuestContentValidationRule(IExprSchema? exprSchema = null)
        {
            _exprSchema = exprSchema ?? QuestExprSchemaEntries.BuildParsingSchema();
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(QuestSchemas.Def.Name))
            {
                // 判断记录：C# 语言规则不允许在 catch 子句体内 yield return（CS1631），
                // 因此把"解析是否失败"的结果先落到局部变量，出了 try/catch 再统一 yield。
                string? errorMessage = null;
                try
                {
                    QuestDefinition.FromRecord(record, _exprSchema);
                }
                catch (System.Exception ex)
                {
                    errorMessage = ex.Message;
                }

                if (errorMessage != null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, QuestSchemas.Def.Name, Check,
                        $"quest.def 解析失败：{errorMessage}", recordKey: record.Key);
                }

                // exclusive_group 引用完整性、prerequisite 引用的其它任务 id 是否存在等跨记录检查
                // 留给通用的 reference_integrity 校验项（04 第 5 节）；本规则只做解析期能发现的问题。
            }
        }
    }
}

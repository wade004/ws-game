using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <see cref="ICreatureTemplateQuery"/> 的一个通用只读实现：直接从已加载的
    /// <see cref="IDataRegistryView"/> 现读现解析 <c>creature.template</c> 记录（不像
    /// <see cref="CreatureFactory"/> 那样在构造期把全表一次性解析进内存索引）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判断记录（用途；不写成 <c>&lt;see cref&gt;</c> 是因为本文件所在的 <c>Core.Carriers</c> 程序集
    /// 按分层不引用 L4 <c>Core.Gameplay</c>/最外层 <c>Presentation.Assembly</c>，写 cref 会因目标
    /// 类型不可解析产生 CS1574 警告，本仓库 <c>TreatWarningsAsErrors</c> 下即编译失败）：供校验装配
    /// 入口（<c>Presentation.Assembly.ContentValidationAssembly.CreateRegistryCore</c>）默认接线
    /// <c>Core.Gameplay.Spawn.SpawnSummonOnlyCreatureRule</c> 使用——<see cref="IValidationRule.Validate"/>
    /// 的调用时机晚于 <see cref="IDataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>
    /// 的数据加载阶段，此时 <see cref="IDataRegistryView"/> 已可读；不需要像
    /// <see cref="CreatureFactory"/> 那样为了在构造期就能提供强类型索引而要求"registry 必须已经
    /// 加载完成"这个更强的前置条件——本类型的构造函数只接收 view 引用，不要求其已加载（真正读取
    /// 延迟到 <see cref="Get"/>/<see cref="HasFlag"/> 调用时）。
    /// </para>
    /// <para>
    /// 判断记录（异常收敛，不重复报告字段级问题）：<see cref="Get"/> 对未登记的模板 id 抛
    /// <see cref="ArgumentException"/>（同 <see cref="ICreatureTemplateQuery"/> 契约、同
    /// <see cref="CreatureFactory.Get"/> 惯例）；记录存在但字段非法时，<see cref="CreatureTemplate.FromRecord"/>
    /// 抛 <see cref="DataFieldException"/>，本类型把它包成 <see cref="ArgumentException"/>（内含
    /// inner 异常）再抛——模板字段非法已经由字段级校验（<c>required_field</c>/<c>field_type</c> 等
    /// 检查项）单独报出，<c>SpawnSummonOnlyCreatureRule</c> 不需要（也不应该）重复报告，更不能让
    /// 本查询的异常把整次 <c>ContentValidationAssembly.Run</c> 校验从"报告若干诊断"变成进程级未
    /// 处理异常——阻断态从"<c>Report.IsBlocking == true</c>"变成"根本拿不到 Report"，对调用方是
    /// 两种完全不同的失败模式，后者不可接受。
    /// </para>
    /// <para>
    /// 判断记录（不做跨次缓存）：每次 <see cref="Get"/>/<see cref="HasFlag"/> 调用都从
    /// <see cref="IDataRegistryView"/> 现读现解析，不缓存解析结果——<see cref="IDataRegistry.Reload(string)"/>
    /// 支持单表重载，若本类型缓存了解析结果，调用方在两次校验之间 <c>Reload</c> 了
    /// <c>creature.template</c> 后，缓存会读到过期数据；本类型的典型生命周期是"校验期一次性使用"
    /// （每次 <c>ContentValidationAssembly.Run</c>/<c>CreateRegistry</c> 调用都新建一个实例），不
    /// 缓存不构成性能问题。
    /// </para>
    /// <para>
    /// 判断记录（阻断态边界，实测确认）：<c>DataRegistry</c> 在整次 <c>LoadAll</c>/校验期间把内部
    /// "是否阻断"标记固定为 <c>false</c>（见 <c>RunValidationAndBuildReport</c> 判断记录"校验期间
    /// 允许读取"），只在全部 <see cref="IValidationRule"/>（含用本类型接线的
    /// <c>SpawnSummonOnlyCreatureRule</c>）都跑完之后才按最终报告回填真实阻断状态——因此本类型在
    /// <c>Validate()</c> 调用期间读取 <paramref name="view"/>（构造函数参数）永远不会因为"数据集别处
    /// 已经有 Error"而被拒绝读取，只会遇到上面判断记录说的 <see cref="DataFieldException"/> 分支。
    /// 但若调用方在 <c>LoadAll</c> 完全结束、报告确已阻断之后，才另外持有一个
    /// <see cref="RegistryCreatureTemplateQuery"/> 去读同一个已阻断的 <see cref="DataRegistry"/>，
    /// <see cref="IDataRegistryView.Get(string, Id)"/> 会直接抛 <see cref="InvalidOperationException"/>
    /// （"数据校验未通过，禁止读取"，与本类型无关，是 <c>DataRegistry.EnsureReadable</c> 的既有契约）
    /// ——本类型不吞掉这种异常，也不应该吞：那是调用方自己在阻断态下发起了一次不该有的读取，与"模板
    /// 字段本身合不合法"是两个不同层面的问题。
    /// </para>
    /// </remarks>
    public sealed class RegistryCreatureTemplateQuery : ICreatureTemplateQuery
    {
        private readonly IDataRegistryView _view;

        public RegistryCreatureTemplateQuery(IDataRegistryView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public CreatureTemplate Get(Id templateId)
        {
            var record = _view.Get(CreatureSchemas.Template.Name, templateId)
                ?? throw new ArgumentException($"未登记的生物模板：\"{templateId}\"", nameof(templateId));

            try
            {
                return CreatureTemplate.FromRecord(record);
            }
            catch (DataFieldException ex)
            {
                // 判断记录见类型注释"异常收敛"：字段非法已由字段级校验单独报出，本查询不重复报告，
                // 但也不能让 DataFieldException 原样冒泡把整次校验从"报告诊断"变成进程级异常——
                // 包成 ArgumentException（符合 ICreatureTemplateQuery.Get 的既有契约：未登记/无法
                // 提供强类型模板时统一抛 ArgumentException），inner 保留原始异常供排查。
                throw new ArgumentException(
                    $"生物模板 \"{templateId}\" 字段非法，无法解析为强类型模板（该问题已由字段级校验单独报出）",
                    nameof(templateId), ex);
            }
        }

        public bool HasFlag(Id templateId, NpcFlag flag) => Get(templateId).NpcFlags.Contains(flag);
    }
}

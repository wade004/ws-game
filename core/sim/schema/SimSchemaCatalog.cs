using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-2a：<c>sim.anchor</c>/<c>sim.scenario</c> 两表的统一注册入口（惯例同
    /// <c>Core.Gameplay.Assembly.GameplaySchemaCatalog</c>）。
    /// <para>
    /// 判断记录（为何不并入 <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll"/>）：
    /// 04 第 1.1 节表清单原文——"仅无头仿真与内容工具读取，运行期宿主不读"（ADR-0035）。
    /// <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll"/> 是运行期宿主
    /// （<see cref="Core.Gameplay.Assembly.GameplayAssembly"/>）与内容校验共用的注册清单，把
    /// <c>sim.*</c> 两表塞进去会让每一个运行期宿主（游戏进程本身）都背上两张自己永远不读的表；
    /// 拆成独立的 <see cref="SimSchemaCatalog"/>，由需要它们的两类调用方（
    /// <see cref="HeadlessWorldBuilder"/>——无头仿真；<c>toolchain/validator</c>——内容校验，经
    /// <c>Presentation.Assembly.ContentValidationOptions.ExtraSchemaRegistration</c> 钩子接入）
    /// 各自显式调用一次，运行期宿主则完全不知道这两张表的存在。
    /// </para>
    /// </summary>
    public static class SimSchemaCatalog
    {
        /// <summary>注册 <see cref="SimSchemas.Anchor"/>/<see cref="SimSchemas.Scenario"/> 两张
        /// <see cref="TableSchema"/> 与各自的 <see cref="IValidationRule"/>（<see cref="SimAnchorValidationRule"/>/
        /// <see cref="SimScenarioValidationRule"/>/<see cref="SimGrowthOpponentAmbiguityValidationRule"/>——
        /// 反馈第 53 条新增，见该类型判断记录；本条不注册新表，只对已加载的 <c>creature.template</c>/
        /// <c>fac.*</c> 做只读交叉校验，放在本入口一起注册纯粹是"仿真相关的内容校验规则集中一处登记"
        /// 的既有惯例，不代表它依赖 <c>sim.anchor</c>/<c>sim.scenario</c> 两张表本身）。不调用
        /// <see cref="IDataRegistry.LoadAll()"/>——加载时机由调用方决定，同
        /// <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll"/> 既有惯例。</summary>
        public static void RegisterAll(IDataRegistry registry)
        {
            registry.RegisterSchema(SimSchemas.Anchor);
            registry.RegisterSchema(SimSchemas.Scenario);
            registry.RegisterValidationRule(new SimAnchorValidationRule());
            registry.RegisterValidationRule(new SimScenarioValidationRule());
            registry.RegisterValidationRule(new SimGrowthOpponentAmbiguityValidationRule());
        }
    }
}

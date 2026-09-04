using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 资源池契约（见 06_规则层_属性技能战斗AI.md 第 2.2 节）。规则层只消费本契约，不知道
    /// 具体实现（见 01_分层与依赖.md L1 模块表 <c>power_set</c> 行"契约接口名：PowerHost"）。
    /// 06 原文只给出 <c>getPower</c>/<c>getPowerMax</c>/<c>modifyPower</c> 三个方法；本接口
    /// 按任务书拍板补充单位注册、进出战斗切换、时间推进、上限重算等运行期状态管理方法
    /// （06 §2.1 提到的回复/衰减/脱战回满等行为都需要这些方法才能落地，06 原文未逐一给出
    /// 签名，属于契约的必要展开而非新增原语）。
    /// </summary>
    public interface IPowerHost
    {
        /// <summary>
        /// 为某单位注册一组资源类型（见 06 第 2.1 节"资源类型可配置数量"，本方法允许每个单位
        /// 注册任意数量、任意种类的资源类型组合，禁止把资源池数量硬编码为固定值——见落地方案
        /// T2-2 行禁止事项）。<paramref name="powerTypes"/> 必须全部是构造 <see cref="PowerHost"/>
        /// 时已登记的资源类型 id，否则抛 <see cref="System.InvalidOperationException"/>；重复注册
        /// 同一单位同样抛异常。按 <paramref name="powerTypes"/> 的顺序确定性地初始化每种资源的
        /// 当前值（<c>start_full</c> 为 true 时为上限，否则为下限）与上限（<c>max_source.kind</c>
        /// 为 <c>stat</c> 时经构造期注入的 <see cref="StatLookup"/> 查询，为 null 时抛异常）。
        /// </summary>
        void RegisterUnit(Id unitId, IReadOnlyList<Id> powerTypes);

        /// <summary>移除某单位的全部资源池状态；单位未注册时抛 <see cref="System.InvalidOperationException"/>。</summary>
        void UnregisterUnit(Id unitId);

        /// <summary>该单位是否已注册且持有该资源类型；不存在时返回 false，不抛异常（纯查询）。</summary>
        bool HasPower(Id unitId, Id powerType);

        /// <summary>取当前值；单位未注册或未持有该资源类型时抛 <see cref="System.InvalidOperationException"/>。</summary>
        double GetPower(Id unitId, Id powerType);

        /// <summary>取当前上限；单位未注册或未持有该资源类型时抛 <see cref="System.InvalidOperationException"/>。</summary>
        double GetPowerMax(Id unitId, Id powerType);

        /// <summary>
        /// 修改资源值：结果夹取到 <c>[min, max]</c>，除非该资源类型 <c>allow_overflow</c> 为
        /// true（此时正向超出 max 不再夹取，负向仍夹取到 min）。变化时发 <c>power.changed</c>；
        /// 从大于 min 变为等于 min 的那一次额外发 <c>power.depleted</c>（连续多次扣到 min 只发
        /// 一次，见 06 第 2.2 节"事件"）。<paramref name="sourceId"/> 供调用方标记本次修改的
        /// 来源（如触发本次消耗的技能 id），事件本身按 06 原文只携带
        /// <c>{unitId, powerType, oldValue, newValue}</c>，不下发 sourceId。
        /// </summary>
        void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId);

        /// <summary>
        /// 切换某单位的进出战斗状态（见 06 第 4.5 节"脱离战斗...触发...资源回复规则切换（如脱
        /// 战自动回满）"）：从 true 切到 false 的那一次，对 <c>refill_on_leave_combat</c> 为 true
        /// 的资源类型立即回满并发 <c>power.changed</c>。单位未注册时抛异常。
        /// </summary>
        void SetInCombat(Id unitId, bool inCombat);

        /// <summary>
        /// 按 <paramref name="timeUnits"/>（以数据集声明的时间单位计，见 04 第 3.1 节）推进单个
        /// 单位全部已注册资源的回复/衰减：按注册顺序遍历资源类型，每种资源先应用回复（战斗内/
        /// 脱战速率二选一）再应用衰减（只在脱战时生效），保证确定性。<paramref name="timeUnits"/>
        /// 为负或单位未注册均抛异常。
        /// </summary>
        void Advance(Id unitId, double timeUnits);

        /// <summary>按注册顺序对全部已注册单位调用 <see cref="Advance"/>（确定性遍历，见方法注释）。</summary>
        void AdvanceAll(double timeUnits);

        /// <summary>
        /// 重新计算某单位全部 <c>max_source.kind == "stat"</c> 资源类型的上限（经
        /// <see cref="StatLookup"/> 重新查询，供属性变化后手动触发同步，见 06 第 2.1 节"上限来源
        /// ...引用属性"）；上限下降导致当前值超出新上限时，当前值随之夹取（可能触发
        /// <c>power.changed</c>/<c>power.depleted</c>）。<c>fixed</c> 来源的资源类型不受影响。
        /// 单位未注册时抛异常。
        /// </summary>
        void RecomputeMax(Id unitId);
    }
}

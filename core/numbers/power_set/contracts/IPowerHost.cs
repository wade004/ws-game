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

        /// <summary>
        /// 消费方反馈第 33 条（示例技能消耗 <c>arch.power.mana</c> 时 <see cref="GetPower"/> 直接
        /// 抛异常，调用方常常只是想"这个单位有没有这种资源、有的话是多少"这类容错查询，不想每次都
        /// 包一层 try/catch 或先调 <see cref="HasPower"/> 再调 <see cref="GetPower"/> 两次查找）：
        /// 尝试获取当前值，单位未注册或未持有该资源类型时返回 <c>false</c>（<paramref name="value"/>
        /// 置 0）而不抛异常；成功时返回 <c>true</c>。默认实现按"try <see cref="GetPower"/>，只捕获
        /// <see cref="System.InvalidOperationException"/>（本接口该方法明确抛出的异常类型）"给出——
        /// 不用 <c>catch (Exception)</c> 掩盖其它意外异常，呼应 11 第 4 节"运行时不做静默降级"、
        /// 与 <see cref="Core.Foundation.DataRegistry.IDataRegistryView.TryGetRecordCount"/> 同一套
        /// 既有惯例（见该成员判断记录）。带默认实现的接口成员新增不构成"公开 API 表面"意义上的
        /// 破坏性变更。<see cref="Core.Numbers.PowerSet.PowerHost"/> 显式覆盖为永不抛出的直接实现
        /// （先 <see cref="HasPower"/> 判断，命中才取值，不经过这里的 try/catch），见该类型同名成员
        /// 判断记录。
        /// </summary>
        bool TryGetPower(Id unitId, Id powerType, out double value)
        {
            try
            {
                value = GetPower(unitId, powerType);
                return true;
            }
            catch (System.InvalidOperationException)
            {
                value = 0;
                return false;
            }
        }

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
        /// T-N4-5（ADR-0033 决策 7；06 第 2.5 节"升级回满：<c>progression.level_up</c> 触发生命与
        /// 资源回满，由资源池订阅实现"）：把该单位当前已注册的、<see cref="PowerTypeDefinition.StartFull"/>
        /// 为 true 的"回复型"资源类型（如生命、法力）全部回满到各自当前上限；按注册顺序遍历，值
        /// 确实发生变化的每种资源类型各发一次 <c>power.changed</c>（经既有"落值 + 发事件"入口，
        /// 与 <see cref="ModifyPower"/>/<see cref="SetInCombat"/> 脱战回满/<see cref="RecomputeMax"/>
        /// 上限夹取共用同一处，不绕过事件，见 <see cref="PowerHost"/> 判断记录"<c>SetCurrentClamped</c>
        /// 唯一入口"）。<paramref name="sourceId"/> 供调用方标记本次回满的来源（同
        /// <see cref="ModifyPower"/>，事件本身不下发该值）。单位未注册时抛
        /// <see cref="System.InvalidOperationException"/>。
        /// <para>
        /// 契约疑点上报（积累型资源是否回满）：ADR-0033 决策 7/06 第 2.5 节原文只说"生命与资源
        /// 回满"，未区分资源类型是否"积累型"（<see cref="PowerTypeDefinition.StartFull"/> 为
        /// false——单位注册时初始为 <see cref="PowerTypeDefinition.Min"/>，典型如连击点一类"起始空、
        /// 只能靠战斗行为累积"的资源）。本方法取"只回满 <c>StartFull=true</c> 的回复型资源，积累型
        /// 资源不动"这一保守判断——理由：积累型资源的既定设计意图是"通过战斗行为获得"，升级把它们
        /// 直接填满与这条既定玩法语义相冲突；且本仓库已有先例 <see cref="SetInCombat"/> 脱战回满
        /// 同样只对显式登记 <see cref="PowerTypeDefinition.RefillOnLeaveCombat"/> 为 true 的类型生效，
        /// 不是无条件回满全部已注册资源类型。若设计层认定积累型资源也应在升级时回满，需要在
        /// <c>arch.power_type</c> 补一个显式策略字段（同 <c>refill_on_leave_combat</c> 的显式开关
        /// 写法），本方法当前实现留待那时调整，不擅自扩大契约未声明的行为范围。
        /// </para>
        /// <para>
        /// 判断记录（默认实现，C#8 默认接口方法，ABI 门禁 G3"公开 API 只能新增"，惯例同
        /// <see cref="Core.Numbers.Progression.IProgressionHost.ApplyGrowthToCurrentLevel"/>）：
        /// 默认体是空操作，本接口目前只有 <see cref="PowerHost"/> 一个生产实现（显式覆盖本方法）；
        /// 其它实现方（测试假实现、未来第三方实现，若存在）不因新增本成员而编译失败，只是调用本
        /// 方法时静默无效果——它们此前也不需要支持"升级回满"这项能力。
        /// </para>
        /// </summary>
        void RefillAll(Id unitId, Id sourceId) { }

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

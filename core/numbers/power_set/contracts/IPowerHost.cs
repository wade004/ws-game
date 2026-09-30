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
        /// <paramref name="delta"/> 为 NaN 或 ±Infinity 时抛 <see cref="System.ArgumentOutOfRangeException"/>，
        /// 不发事件、状态不变（ADR-0125 D17；<see cref="Advance"/>/<see cref="AdvanceAll"/> 的时长、
        /// <see cref="SetMinOverride"/> 的覆盖值同理）。
        /// </summary>
        void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId);

        /// <summary>
        /// 切换某单位的进出战斗状态（见 06 第 4.5 节"脱离战斗...触发...资源回复规则切换（如脱
        /// 战自动回满）"）：从 true 切到 false 的那一次，对 <c>refill_on_leave_combat</c> 为 true
        /// 的资源类型立即回满并发 <c>power.changed</c>。单位未注册时抛异常。
        /// </summary>
        void SetInCombat(Id unitId, bool inCombat);

        /// <summary>
        /// 消费方反馈-2026-09-17（读档触发脱战回满）根治新增：存档/回滚恢复专用的"纯赋值"入口——
        /// 只设置进出战布尔状态，不触发 <see cref="SetInCombat"/> 那条"true→false 时对
        /// <c>refill_on_leave_combat</c> 为真的资源类型立即回满"业务副作用，也不发任何事件。
        /// <see cref="Core.Rules.Combat.CombatHost.RestoreCombatState"/>（存档读档/回滚恢复战斗态的
        /// 唯一入口）改调用本方法而不是 <see cref="SetInCombat"/>，避免读档/回滚把"运行期状态恰好
        /// 是 true、存档快照是 false"误判为一次真实脱战，覆盖掉刚从存档恢复的资源当前值（真实探针
        /// 复现：存档 health=37，读档后被回满成 100，见 <see cref="Core.Numbers.PowerSet.PowerHost"/>
        /// 同名成员判断记录）。
        /// <para>
        /// 判断记录（默认实现，C#8 默认接口方法，ABI 门禁 G3"公开 API 只能新增"，惯例同 <see
        /// cref="RefillAll"/>/<see cref="TryGetPower"/>）：默认体回落为调用 <see cref="SetInCombat"/>
        /// ——本接口除 <see cref="Core.Numbers.PowerSet.PowerHost"/>（显式覆盖为真正的纯赋值实现）外
        /// 目前没有其它生产实现，其它实现方（测试假实现）新增本成员不构成编译破坏；对它们而言调用
        /// 本方法与调用 <see cref="SetInCombat"/> 效果一致（不比修复前更差），它们本就不是本次缺陷
        /// 涉及的存档恢复路径的真实消费方。<b>不能</b>把默认体设计成真正的纯赋值——接口层面没有可
        /// 直接读写的进出战字段，唯一现成手段就是 <see cref="SetInCombat"/> 本身，因此默认体无法在
        /// 不重新引入本次要修复的副作用的前提下做到"纯赋值"，这正是本方法必须要求
        /// <see cref="Core.Numbers.PowerSet.PowerHost"/> 显式覆盖的原因。
        /// </para>
        /// </summary>
        void RestoreInCombat(Id unitId, bool inCombat) => SetInCombat(unitId, inCombat);

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

        /// <summary>
        /// ADR-0106（消费方反馈第五十五批"单一模板受伤但不死"）新增：为 <paramref name="unitId"/> 的
        /// <paramref name="powerType"/> 设置单位级下限覆盖，<paramref name="min"/> 为 <c>null</c> 时
        /// 清除覆盖（回落到 <see cref="PowerTypeDefinition.Min"/>）。<see cref="PowerHost"/> 全部夹取
        /// 出口（<c>RegisterUnit</c> 初始值、<c>ApplyDelta</c> 下界、上限下降夹取、<c>SetCurrentClamped</c>
        /// 的"跌到下限"判定）此后统一读"覆盖 ?? 定义 Min"，不再单读 <see cref="PowerTypeDefinition.Min"/>
        /// ——这是唯一改动点，<c>combat.damage_dealt</c> 等事件携带的数值不受影响（仍是结算管线算出的
        /// 夹取前原始量，见 <c>Core.Rules.Combat.Resolver</c> 判断记录"选覆盖下限而非死亡判定特判"）。
        /// <para>
        /// 前置条件：<paramref name="unitId"/> 必须已注册且持有 <paramref name="powerType"/>（否则抛
        /// <see cref="System.InvalidOperationException"/>，与 <see cref="GetPower"/> 同一约定）；
        /// <paramref name="min"/> 非 <c>null</c> 时必须 <c>&gt;=</c> <see cref="PowerTypeDefinition.Min"/>
        /// 且 <c>&lt;=</c> 该单位该资源类型的当前上限，否则抛 <see cref="System.ArgumentException"/>
        /// ——覆盖只能收紧"这个单位不应该被打到 0/低于下限以下"这一意图本身的下界，不能突破资源类型
        /// 定义的全局下限，也不能超过自己的上限（下限 &gt; 上限没有意义）。设置后若当前值低于新下限，
        /// 立即经既有"落值 + 发事件"唯一出口（<c>SetCurrentClamped</c>）夹上去，与
        /// <see cref="ModifyPower"/>/<see cref="SetInCombat"/> 脱战回满/<see cref="RecomputeMax"/> 上限
        /// 夹取共用同一处，可能触发 <c>power.changed</c>（不会触发 <c>power.depleted</c>——夹的方向是
        /// 从低于下限升到下限，不是从高于下限跌到下限）。<see cref="UnregisterUnit"/> 连带清除覆盖
        /// （覆盖存于单位内部状态，随单位一起移除，不需要调用方额外清理）。
        /// </para>
        /// <para>
        /// 判断记录（默认实现，C#8 默认接口方法，ABI 门禁 G3"公开 API 只能新增"，惯例同
        /// <see cref="RefillAll"/>/<see cref="TryGetPower"/>）：默认体抛
        /// <see cref="System.NotSupportedException"/>，不是空操作——本成员会真正改变夹取下限这一数值
        /// 语义，若默认体悄悄什么都不做，调用方会以为覆盖已生效、实际单位仍按原下限（含 0）被打死，
        /// 这正是 11 第 4 节"运行时不做静默降级"要拦截的那种"看似成功、实则无效"的降级；比照
        /// <see cref="RecomputeMax"/> 一类"改变既有数值状态"的成员本就是必须实现的抽象成员，本成员
        /// 因为要保持"公开 API 只新增"才退而求其次用带默认实现的接口成员，但默认体选择显式失败而非
        /// 悄悄放行。本接口目前只有 <see cref="PowerHost"/> 一个生产实现（显式覆盖，见其同名成员判断
        /// 记录）；其余实现方（<c>core/rules/expr_host/tests/ExprHostTestSupport.FakePowerHost</c> 等
        /// 测试替身）新增本成员不构成编译破坏，它们本就不是本次缺陷涉及的生成/结算路径的真实消费方，
        /// 调用本方法会显式抛出而不是产生一个看似正确的空操作。
        /// </para>
        /// </summary>
        void SetMinOverride(Id unitId, Id powerType, double? min) =>
            throw new System.NotSupportedException(
                $"{GetType().Name} 未实现 {nameof(SetMinOverride)}（ADR-0106 单位级资源下限覆盖，" +
                $"默认接口成员故意不做静默空操作，见 IPowerHost.{nameof(SetMinOverride)} 判断记录）");

        /// <summary>
        /// ADR-0108（消费方反馈第五十七批"静息回复"）新增：为 <paramref name="unitId"/> 的
        /// <paramref name="powerType"/> 登记/替换一条运行期回复速率修饰器（<see cref="RegenModifier"/>），
        /// 供"某个运行期状态下这个单位的这种资源回复更快/更慢"这类内容（典型如"坐下回复加速"）落地——
        /// 06 原文的 <c>regen_out_of_combat</c>/<c>regen_in_combat</c> 是资源类型级全局固定数字，没有
        /// 任何按单位的运行期倍率/加成钩子，本方法是补齐的必要展开（同 <see cref="SetMinOverride"/>
        /// 判断记录同一处理时机——运行期状态管理方法按需展开进契约）。
        /// <para>
        /// <paramref name="key"/> 标识"是谁登记的这条修饰器"（典型取调用方自己的光环实例 id），同一
        /// <paramref name="key"/> 重复调用是替换（覆盖旧值），不是叠加——不同 <paramref name="key"/>
        /// 的多条修饰器才会一起参与组合（见 <see cref="Core.Numbers.PowerSet.PowerHost"/>"有效速率"
        /// 公式判断记录）。前置条件：<paramref name="unitId"/> 必须已注册且持有
        /// <paramref name="powerType"/>，否则抛 <see cref="System.InvalidOperationException"/>（与
        /// <see cref="ModifyPower"/>/<see cref="SetMinOverride"/> 同一惯例——本方法在写入与某个具体
        /// 资源池绑定的运行期状态，目标资源池必须先存在）。
        /// </para>
        /// <para>
        /// 判断记录（默认实现，C#8 默认接口方法，ABI 门禁 G3"公开 API 只能新增"，惯例同
        /// <see cref="SetMinOverride"/>）：默认体抛 <see cref="System.NotSupportedException"/>，理由
        /// 同 <see cref="SetMinOverride"/> 判断记录——本成员会真正改变有效回复速率这一数值语义，默认体
        /// 悄悄空操作会让调用方误以为修饰器已生效。本接口目前只有
        /// <see cref="Core.Numbers.PowerSet.PowerHost"/> 一个生产实现（显式覆盖）；其余测试替身新增
        /// 本成员不构成编译破坏，调用会显式抛出而不是产生看似正确的空操作。
        /// </para>
        /// </summary>
        void AddRegenModifier(Id unitId, Id powerType, Id key, RegenModifier modifier) =>
            throw new System.NotSupportedException(
                $"{GetType().Name} 未实现 {nameof(AddRegenModifier)}（ADR-0108 单位级回复速率修饰器，" +
                $"默认接口成员故意不做静默空操作，见 IPowerHost.{nameof(AddRegenModifier)} 判断记录）");

        /// <summary>
        /// ADR-0108 新增：移除 <paramref name="unitId"/> 的 <paramref name="powerType"/> 下、由
        /// <paramref name="key"/> 登记的回复速率修饰器；<paramref name="unitId"/>/<paramref name="powerType"/>
        /// 未注册、或该 <paramref name="key"/> 从未登记过，均静默忽略（不抛异常）——本方法主要供光环
        /// 到期/移除时的清理路径调用，清理路径不应因为"目标资源池状态已经先一步发生变化"而崩溃（同
        /// <see cref="TryGetPower"/> 一类"容错查询/清理"方法的既有惯例，与写入新状态的
        /// <see cref="AddRegenModifier"/> 要求前置条件成立不同——两者一写一清，容错尺度分别对齐各自
        /// 的既有惯例）。
        /// <para>
        /// 判断记录（默认实现，写法同 <see cref="AddRegenModifier"/>）：默认体同样抛
        /// <see cref="System.NotSupportedException"/>，不是空操作——虽然本方法运行时对"目标不存在"
        /// 静默忽略，但对"未实现移除能力的宿主"仍应显式失败，避免调用方以为移除已生效、实际修饰器
        /// 仍在起作用（同 <see cref="AddRegenModifier"/> 判断记录"看似成功、实则无效"的降级顾虑）。
        /// </para>
        /// </summary>
        void RemoveRegenModifier(Id unitId, Id powerType, Id key) =>
            throw new System.NotSupportedException(
                $"{GetType().Name} 未实现 {nameof(RemoveRegenModifier)}（ADR-0108 单位级回复速率修饰器，" +
                $"默认接口成员故意不做静默空操作，见 IPowerHost.{nameof(RemoveRegenModifier)} 判断记录）");
    }
}

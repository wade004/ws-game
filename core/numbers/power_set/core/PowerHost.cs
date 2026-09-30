using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// <see cref="IPowerHost"/> 的默认实现（见 06 第 2.2 节、01_分层与依赖.md L1 模块表
    /// <c>power_set</c> 行）。构造期接收全部已知资源类型定义（<see cref="PowerTypeDefinition"/>，
    /// 数量不限——禁止硬编码资源池数量，见落地方案 T2-2 行禁止事项）与一个可选的
    /// <see cref="StatLookup"/>（<c>max_source.kind == "stat"</c> 的资源类型必须提供，否则在
    /// 首次需要用到时抛异常）。全部状态保存在内存，不接触任何文件/引擎符号。
    /// </summary>
    public sealed class PowerHost : IPowerHost
    {
        private sealed class PowerState
        {
            public double Current;
            public double Max;

            /// <summary>ADR-0106：单位级下限覆盖，<c>null</c> 表示未覆盖（回落到
            /// <see cref="PowerTypeDefinition.Min"/>），见 <see cref="SetMinOverride"/>/
            /// <see cref="EffectiveMin"/>。</summary>
            public double? MinOverride;

            /// <summary>ADR-0108：运行期回复速率修饰器表，键为调用方登记时传入的 <c>key</c>（典型是
            /// 光环实例 id）；<c>null</c> 表示尚未登记过任何修饰器（惰性分配，绝大多数资源池永远
            /// 用不到，不为每个 PowerState 都分配一个空字典）。用 <see cref="SortedDictionary{TKey,TValue}"/>
            /// 而不是 <see cref="Dictionary{TKey,TValue}"/>——见 <see cref="ComputeEffectiveRegenRate"/>
            /// 判断记录"确定性遍历"，惯例同 <c>Core.Rules.Combat.ThreatTable</c> 内层表。</summary>
            public SortedDictionary<Id, RegenModifier>? RegenModifiers;
        }

        private sealed class UnitState
        {
            public readonly List<Id> PowerTypeOrder = new List<Id>();
            public readonly Dictionary<Id, PowerState> Powers = new Dictionary<Id, PowerState>();
            public bool InCombat;
        }

        private readonly Dictionary<Id, PowerTypeDefinition> _definitions = new Dictionary<Id, PowerTypeDefinition>();
        private readonly Dictionary<Id, UnitState> _units = new Dictionary<Id, UnitState>();
        private readonly List<Id> _unitOrder = new List<Id>();

        private readonly IEventBus _bus;
        private readonly StatLookup? _statLookup;

        public PowerHost(IEnumerable<PowerTypeDefinition> powerTypes, IEventBus bus, StatLookup? statLookup = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _statLookup = statLookup;

            LoadDefinitions(powerTypes);
        }

        /// <summary>
        /// P2-05 同类缓存收口（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：<see
        /// cref="_definitions"/> 此前只在构造期从注入的 <see cref="IEnumerable{T}"/> 一次性建索引、
        /// 没有任何刷新入口——与 <c>QuestHost</c>/<c>DialogHost</c> 同一类"构造签名只接收预解析
        /// 集合、不持有 registry 本身"的模式（本类型是 L1，刻意不依赖 <c>Core.Foundation.
        /// DataRegistry</c>，见类型顶部"全部状态保存在内存，不接触任何文件/引擎符号"），因此采用
        /// 与 Quest/Dialog 相同的处理方式：新增公开 <see cref="Reload"/>，由持有 registry 的组装根
        /// （<c>core/rules/assembly.RulesAssembly</c>）订阅 <c>DataLoadCompletedEvent</c> 后重新
        /// 解析 <c>arch.power_type</c> 表并调用。<see cref="_units"/>（每单位当前值/上限，运行期
        /// 状态）不受影响——reload 只替换资源类型定义表本身，不重算/不清空已注册单位的资源值，
        /// 与本类型既有的"上限变化经 <see cref="RecomputeMax"/> 显式重算，不隐式在定义变化时
        /// 联动"惯例一致（调用方若需要新定义的上限立即生效，仍应显式对受影响单位调用
        /// <see cref="RecomputeMax"/>，本方法不越权自动遍历全部已注册单位）。
        /// </summary>
        public void Reload(IEnumerable<PowerTypeDefinition> powerTypes)
        {
            _definitions.Clear();
            LoadDefinitions(powerTypes);
        }

        private void LoadDefinitions(IEnumerable<PowerTypeDefinition> powerTypes)
        {
            if (powerTypes == null)
            {
                throw new ArgumentNullException(nameof(powerTypes));
            }

            foreach (var definition in powerTypes)
            {
                if (definition == null)
                {
                    throw new ArgumentException("资源类型定义列表不能包含 null 元素", nameof(powerTypes));
                }

                if (_definitions.ContainsKey(definition.Id))
                {
                    throw new ArgumentException($"资源类型 \"{definition.Id}\" 重复登记", nameof(powerTypes));
                }

                _definitions.Add(definition.Id, definition);
            }
        }

        // -----------------------------------------------------------------
        // 注册
        // -----------------------------------------------------------------

        public void RegisterUnit(Id unitId, IReadOnlyList<Id> powerTypes)
        {
            if (powerTypes == null)
            {
                throw new ArgumentNullException(nameof(powerTypes));
            }

            if (powerTypes.Count == 0)
            {
                throw new ArgumentException("powerTypes 不能为空", nameof(powerTypes));
            }

            if (_units.ContainsKey(unitId))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 已注册，不能重复 RegisterUnit");
            }

            var state = new UnitState();
            foreach (var powerType in powerTypes)
            {
                if (state.Powers.ContainsKey(powerType))
                {
                    throw new ArgumentException($"资源类型 \"{powerType}\" 在同一次 RegisterUnit 调用中重复出现", nameof(powerTypes));
                }

                var definition = RequireDefinition(powerType);
                var max = ComputeMax(unitId, definition);
                var current = definition.StartFull ? max : definition.Min;

                state.PowerTypeOrder.Add(powerType);
                state.Powers.Add(powerType, new PowerState { Current = current, Max = max });
            }

            _units.Add(unitId, state);
            _unitOrder.Add(unitId);
        }

        public void UnregisterUnit(Id unitId)
        {
            if (!_units.Remove(unitId))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 未注册，无法 UnregisterUnit");
            }

            _unitOrder.Remove(unitId);
        }

        // -----------------------------------------------------------------
        // 查询
        // -----------------------------------------------------------------

        /// <summary>RC-06 收边补齐：单位是否已注册（不区分具体资源类型）——供
        /// <c>RulesAssembly</c> 订阅 <c>stat.changed</c> 转调 <see cref="RecomputeMax"/> 前判断
        /// "这个改变了属性的单位是否也在本 <see cref="PowerHost"/> 里注册"，避免对未注册单位调用
        /// <see cref="RecomputeMax"/> 抛异常（<see cref="StatHost"/>/<see cref="PowerHost"/> 的注册
        /// 单位集合彼此独立，没有强制同步保证，见该组装根判断记录）。</summary>
        public bool IsRegistered(Id unitId) => _units.ContainsKey(unitId);

        /// <summary>
        /// CORE-111-01 根治（architecture/落地计划/audit-6739f50-20260909，P2，主审已确认）：按注册
        /// 顺序返回该单位当前持有的全部资源类型 id——供 <c>player.vitals</c> 段把"当前值持久化"从
        /// 只覆盖 <see cref="Core.Rules.Common.WellKnownPowers.Health"/> 泛化为覆盖全部已注册资源池
        /// （见 <see cref="Core.Gameplay.Assembly.PlayerVitalsPersistable"/> 类型判断记录），此前
        /// <see cref="IPowerHost"/> 没有任何"枚举某单位已注册资源类型"的查询，调用方只能对单个已知
        /// 类型逐一 <see cref="HasPower"/> 试探。单位未注册时抛 <see cref="InvalidOperationException"/>
        /// （与 <see cref="GetPower"/> 同一约定，调用方应先用 <see cref="IsRegistered"/> 判断）。
        /// <para>
        /// 判断记录（新增到 <see cref="PowerHost"/> 具体类型，不进 <see cref="IPowerHost"/> 契约）：
        /// 06 原文只给出 <c>getPower</c>/<c>getPowerMax</c>/<c>modifyPower</c> 三个方法，本仓库既有
        /// 惯例（<see cref="IsRegistered"/> 的 RC-06 判断记录）是"运行期状态管理方法"按需展开进契约；
        /// 本次不这样做的理由是 <see cref="RulesAssembly.Powers"/> 对外公开的字段类型本就是具体
        /// <see cref="PowerHost"/>（不是 <see cref="IPowerHost"/>），唯一的生产消费方
        /// <see cref="Core.Gameplay.Assembly.PlayerVitalsPersistable"/> 已经改为直接持有具体类型（见
        /// 该类型构造函数判断记录），加进契约只会强迫本仓库另外三处与 <c>player.vitals</c> 完全无关的
        /// 独立测试假实现（<c>presentation/ui/tests/TestSupport.cs</c> 等）同步补一个它们永远用不到
        /// 的方法，不新增契约方法更贴合"必要展开"的既有尺度。
        /// </para>
        /// </summary>
        public IReadOnlyList<Id> GetRegisteredPowerTypes(Id unitId) => RequireUnit(unitId).PowerTypeOrder;

        /// <summary>
        /// CORE-111-01 根治：查询该单位当前的进出战斗状态——<see cref="SetInCombat"/> 此前只有写
        /// 没有对应的读方法，<c>player.vitals</c> 段要把"读档前运行期快照"存下来（见
        /// <see cref="Core.Gameplay.Assembly.PlayerVitalsPersistable"/> 类型判断记录）必须能先读到
        /// 这个值。单位未注册时抛 <see cref="InvalidOperationException"/>（与 <see cref="GetPower"/>
        /// 同一约定）。不进 <see cref="IPowerHost"/> 契约的理由同 <see cref="GetRegisteredPowerTypes"/>
        /// 判断记录。
        /// </summary>
        public bool IsInCombat(Id unitId) => RequireUnit(unitId).InCombat;

        public bool HasPower(Id unitId, Id powerType) =>
            _units.TryGetValue(unitId, out var state) && state.Powers.ContainsKey(powerType);

        public double GetPower(Id unitId, Id powerType) => RequirePower(unitId, powerType).Current;

        /// <summary>消费方反馈第 33 条：显式实现，不经过 <see cref="IPowerHost.TryGetPower"/> 默认
        /// 实现的 try/catch——先 <see cref="HasPower"/> 判断是否存在，命中才取值，未命中直接返回
        /// <c>false</c>，不依赖抛异常再捕获这条路径（同 <see cref="Core.Foundation.DataRegistry.DataRegistry"/>
        /// 对 <c>TryGetRecordCount</c>/<c>TryGetAll</c> 的既有显式覆盖惯例，见该类型同名成员判断
        /// 记录）。</summary>
        public bool TryGetPower(Id unitId, Id powerType, out double value)
        {
            if (HasPower(unitId, powerType))
            {
                value = GetPower(unitId, powerType);
                return true;
            }
            value = 0;
            return false;
        }

        public double GetPowerMax(Id unitId, Id powerType) => RequirePower(unitId, powerType).Max;

        // -----------------------------------------------------------------
        // 修改
        // -----------------------------------------------------------------

        public void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId)
        {
            // ADR-0125 D17：夹取对 NaN 不生效（比较恒假），非有限 delta 会把当前值写成 NaN，前置拒绝。
            NumericGuard.RequireFinite(delta, nameof(delta));
            var definition = RequireDefinition(powerType);
            var power = RequirePower(unitId, powerType);
            ApplyDelta(unitId, powerType, definition, power, delta);
        }

        public void SetInCombat(Id unitId, bool inCombat)
        {
            var state = RequireUnit(unitId);
            var wasInCombat = state.InCombat;
            state.InCombat = inCombat;

            if (!wasInCombat || inCombat)
            {
                return;
            }

            // 从 true 切到 false：脱战瞬间，对 refill_on_leave_combat 的资源类型立即回满。
            foreach (var powerType in state.PowerTypeOrder)
            {
                var definition = _definitions[powerType];
                if (!definition.RefillOnLeaveCombat)
                {
                    continue;
                }

                var power = state.Powers[powerType];
                SetCurrentClamped(unitId, powerType, definition, power, power.Max);
            }
        }

        /// <summary>
        /// 消费方反馈-2026-09-17（读档触发脱战回满）根治新增：存档/回滚恢复专用的"纯赋值"入口——
        /// 与 <see cref="SetInCombat"/> 彻底分离业务语义。<see cref="SetInCombat"/> 从诞生起就同时
        /// 承担两件事："设置进出战布尔状态" 与 "true→false 时对 <see
        /// cref="PowerTypeDefinition.RefillOnLeaveCombat"/> 为真的资源类型立即回满"（06 第 4.5 节
        /// 玩法语义：这是给"真实战斗结束"这一刻设计的奖励/惩罚副作用）。<see
        /// cref="Core.Rules.Combat.CombatHost.RestoreCombatState"/>（C11-RELOAD 根治，2026-09-11 引入）
        /// 为了让 <c>CombatHost.IsInCombat</c> 与本类型 <see cref="IsInCombat"/> 在读档后保持一致，把
        /// "存档恢复"场景也路由到了 <see cref="SetInCombat"/> 上，但存档恢复不是一次真实的"脱战"——
        /// 若读档/回滚发生时运行期状态恰好是 <c>true</c>、而存档快照写的是 <c>false</c>，就会被误判为
        /// 一次真实脱战，对 <c>refill_on_leave_combat=true</c> 的资源类型（框架默认 <c>health</c> 即是,
        /// ADR-0031）触发回满，覆盖掉刚从存档恢复的当前值（真实探针复现：存档 health=37、读档前运行期
        /// in_combat=true、存档 in_combat=false，读档后 health 被回满成 100）。本方法只做一行赋值，
        /// 不遍历资源类型、不调用 <see cref="SetCurrentClamped"/>、不触发任何回满，语义与
        /// <see cref="Core.Gameplay.Economy.EconomyHost.SetBalance"/>（"读档等以快照为准场景，整体
        /// 替换，不触发 <c>Add</c> 那条业务事件路径"，见该方法判断记录）同一惯例：本仓库已有先例是
        /// "恢复"与"业务操作"即便改的是同一份底层状态，也应该是两个不共享副作用逻辑的独立入口。
        /// <para>
        /// 不发布事件：本方法不涉及任何资源值变化，<see cref="PowerChangedEvent"/>/<see
        /// cref="PowerDepletedEvent"/> 均与资源当前值相关，纯粹的进出战标志位赋值没有对应事件可发
        /// （同 <see cref="SetInCombat"/> 本身"标志位赋值不发事件，只有连带的回满才经
        /// <see cref="SetCurrentClamped"/> 发事件"这一既有行为一致）。
        /// </para>
        /// <para>
        /// 进 <see cref="IPowerHost"/> 契约（与 <see cref="IsInCombat"/>/<see
        /// cref="GetRegisteredPowerTypes"/> 不进契约的先例不同）：唯一生产消费方 <see
        /// cref="Core.Rules.Combat.CombatHost"/> 持有的字段类型是接口 <see cref="IPowerHost"/>（不是
        /// 具体 <see cref="PowerHost"/>），必须经接口才能调用。以 C#8 默认接口方法新增（ABI 门禁
        /// G3"公开 API 只能新增"，惯例同 <see cref="IPowerHost.RefillAll"/>/<see
        /// cref="IPowerHost.TryGetPower"/>），本类型显式覆盖默认体（默认体回落为调用
        /// <see cref="SetInCombat"/>，见 <see cref="IPowerHost.RestoreInCombat"/> 判断记录），其它
        /// 实现方（测试假类型）不覆盖时行为与本次改动前一致，不构成编译或行为破坏。
        /// </para>
        /// </summary>
        public void RestoreInCombat(Id unitId, bool inCombat)
        {
            RequireUnit(unitId).InCombat = inCombat;
        }

        public void Advance(Id unitId, double timeUnits)
        {
            NumericGuard.RequireFinite(timeUnits, nameof(timeUnits)); // ADR-0125 D17
            if (timeUnits < 0)
            {
                throw new ArgumentException("timeUnits 不能为负数", nameof(timeUnits));
            }

            var state = RequireUnit(unitId);
            AdvanceUnit(unitId, state, timeUnits);
        }

        public void AdvanceAll(double timeUnits)
        {
            NumericGuard.RequireFinite(timeUnits, nameof(timeUnits)); // ADR-0125 D17
            if (timeUnits < 0)
            {
                throw new ArgumentException("timeUnits 不能为负数", nameof(timeUnits));
            }

            // 按注册顺序遍历单位与资源，保证确定性（见 IPowerHost.AdvanceAll 注释）。
            foreach (var unitId in _unitOrder)
            {
                AdvanceUnit(unitId, _units[unitId], timeUnits);
            }
        }

        /// <summary>T-N4-5：显式实现，只对 <see cref="PowerTypeDefinition.StartFull"/> 为 true 的
        /// 回复型资源回满，积累型资源（<c>StartFull=false</c>）原样不动——见
        /// <see cref="IPowerHost.RefillAll"/> 判断记录"契约疑点上报（积累型资源是否回满）"。经
        /// <see cref="SetCurrentClamped"/>（与 <see cref="ApplyDelta"/>/<see cref="SetInCombat"/>
        /// 脱战回满/<see cref="RecomputeMax"/> 上限夹取共用的唯一"落值 + 发事件"入口）落值，不绕过
        /// <c>power.changed</c> 事件。<paramref name="sourceId"/> 同 <see cref="ModifyPower"/>，仅
        /// 供调用方标记来源，不写入事件字段。</summary>
        public void RefillAll(Id unitId, Id sourceId)
        {
            var state = RequireUnit(unitId);
            foreach (var powerType in state.PowerTypeOrder)
            {
                var definition = _definitions[powerType];
                if (!definition.StartFull)
                {
                    continue;
                }

                var power = state.Powers[powerType];
                SetCurrentClamped(unitId, powerType, definition, power, power.Max);
            }
        }

        public void RecomputeMax(Id unitId)
        {
            var state = RequireUnit(unitId);
            foreach (var powerType in state.PowerTypeOrder)
            {
                var definition = _definitions[powerType];
                if (definition.MaxSourceKind != PowerMaxSourceKind.Stat)
                {
                    continue;
                }

                var power = state.Powers[powerType];
                var newMax = ComputeMax(unitId, definition);
                power.Max = newMax;

                if (power.Current > newMax)
                {
                    var effectiveMin = EffectiveMin(power, definition);
                    var clampedTarget = newMax < effectiveMin ? effectiveMin : newMax;
                    SetCurrentClamped(unitId, powerType, definition, power, clampedTarget);
                }
            }
        }

        /// <summary>
        /// ADR-0106（消费方反馈第五十五批"单一模板受伤但不死"）新增：见
        /// <see cref="IPowerHost.SetMinOverride"/> 契约注释。前置校验顺序：先确认单位已注册且持有该
        /// 资源类型（<see cref="RequirePower"/>，与既有 <see cref="ModifyPower"/> 同一惯例），再校验
        /// <paramref name="min"/> 落在 <c>[definition.Min, power.Max]</c> 区间——校验顺序决定报告的
        /// 异常类型："单位/资源类型未注册"报 <see cref="InvalidOperationException"/>（与本类型其余查询
        /// 方法一致），"覆盖值越界"报 <see cref="ArgumentException"/>（与本类型其余入参校验一致，如
        /// <see cref="RegisterUnit"/> 的 <c>powerTypes</c> 为空）。
        /// </summary>
        public void SetMinOverride(Id unitId, Id powerType, double? min)
        {
            var definition = RequireDefinition(powerType);
            var power = RequirePower(unitId, powerType);

            if (min.HasValue)
            {
                // ADR-0125 D17：NaN 会穿过下面两个区间比较（比较恒假）被写成下限覆盖，先拒绝非有限数。
                NumericGuard.RequireFinite(min.Value, nameof(min));
                if (min.Value < definition.Min)
                {
                    throw new ArgumentException(
                        $"单位 \"{unitId}\" 资源类型 \"{powerType}\" 的下限覆盖 {min.Value} 低于该资源类型定义的下限 {definition.Min}",
                        nameof(min));
                }

                if (min.Value > power.Max)
                {
                    throw new ArgumentException(
                        $"单位 \"{unitId}\" 资源类型 \"{powerType}\" 的下限覆盖 {min.Value} 高于该单位当前上限 {power.Max}",
                        nameof(min));
                }
            }

            power.MinOverride = min;

            var effectiveMin = EffectiveMin(power, definition);
            if (power.Current < effectiveMin)
            {
                SetCurrentClamped(unitId, powerType, definition, power, effectiveMin);
            }
        }

        /// <summary>ADR-0108：见 <see cref="IPowerHost.AddRegenModifier"/> 契约注释。前置校验同
        /// <see cref="SetMinOverride"/>——先经 <see cref="RequirePower"/> 确认单位已注册且持有该资源
        /// 类型（未满足抛 <see cref="InvalidOperationException"/>），再写入/替换 <c>key</c> 对应的
        /// 修饰器（惰性分配 <see cref="PowerState.RegenModifiers"/>）。写入不重算/不立即触发任何
        /// <c>power.changed</c>——有效速率只在下一次 <see cref="Advance"/>/<see cref="AdvanceAll"/>
        /// 推进时读取（同 <c>arch.power_type</c> 定义速率本身"只影响后续 Advance，不倒推已经流逝的
        /// 时间"这一既有语义一致，不新增特例）。</summary>
        public void AddRegenModifier(Id unitId, Id powerType, Id key, RegenModifier modifier)
        {
            var power = RequirePower(unitId, powerType);
            (power.RegenModifiers ??= new SortedDictionary<Id, RegenModifier>())[key] = modifier;
        }

        /// <summary>ADR-0108：见 <see cref="IPowerHost.RemoveRegenModifier"/> 契约注释——单位未注册、
        /// 未持有该资源类型、或该 <c>key</c> 从未登记过，均静默返回，不抛异常（清理路径的既定容错
        /// 尺度，见该接口成员判断记录）。</summary>
        public void RemoveRegenModifier(Id unitId, Id powerType, Id key)
        {
            if (!_units.TryGetValue(unitId, out var state))
            {
                return;
            }

            if (!state.Powers.TryGetValue(powerType, out var power))
            {
                return;
            }

            power.RegenModifiers?.Remove(key);
        }

        // -----------------------------------------------------------------
        // 内部
        // -----------------------------------------------------------------

        private void AdvanceUnit(Id unitId, UnitState state, double timeUnits)
        {
            if (timeUnits == 0)
            {
                return;
            }

            foreach (var powerType in state.PowerTypeOrder)
            {
                var definition = _definitions[powerType];
                var power = state.Powers[powerType];

                // 先 regen 后 decay（见 IPowerHost.Advance 注释）。ADR-0108：regenRate 改经
                // ComputeEffectiveRegenRate 读取（叠加运行期修饰器），定义速率本身的读取点不变。
                var regenRate = ComputeEffectiveRegenRate(definition, power, state.InCombat);
                if (regenRate != 0)
                {
                    ApplyDelta(unitId, powerType, definition, power, regenRate * timeUnits);
                }

                if (!state.InCombat && definition.DecayOutOfCombat != 0)
                {
                    ApplyDelta(unitId, powerType, definition, power, -definition.DecayOutOfCombat * timeUnits);
                }
            }
        }

        /// <summary>
        /// ADR-0108：单一取值出口——把 <c>arch.power_type</c> 定义速率（按 <paramref name="inCombat"/>
        /// 二选一，与本方法之前的既有读取逐位一致）与该资源池当前登记的全部 <see cref="RegenModifier"/>
        /// 组合成"这一次 Advance 实际使用的回复速率"。公式（任务书拍板，与
        /// <see cref="IPowerHost.AddRegenModifier"/> 判断记录同一处描述）：
        /// <c>(定义速率 + Σ 匹配当前战斗状态的 Add) × Π 匹配当前战斗状态的 Multiplier</c>，结果
        /// <c>&lt; 0</c> 夹到 0（负回复速率没有意义——本方法只处理"回复"这一读取点，脱战衰减
        /// <see cref="PowerTypeDefinition.DecayOutOfCombat"/> 走独立字段，不经本方法，不受影响）。
        /// 没有任何登记过的修饰器（<see cref="PowerState.RegenModifiers"/> 为 <c>null</c> 或空）时
        /// 直接返回定义速率本身，不做任何浮点运算——与本方法引入之前逐位一致（回归）。
        /// <para>
        /// 判断记录（遍历顺序确定性）：<see cref="PowerState.RegenModifiers"/> 是
        /// <see cref="SortedDictionary{TKey,TValue}"/>（按 <c>key</c> 的 <c>Id</c> 序数排序），不是
        /// 普通 <see cref="Dictionary{TKey,TValue}"/>——加法/乘法虽然数学上满足交换律，但浮点运算不
        /// 满足结合律，不同遍历顺序可能得到位级不同的结果；本仓库对"不依赖字典枚举顺序"有硬性规则
        /// （11 第 3 节），既有先例 <c>Core.Rules.Combat.ThreatTable</c> 内层表同样用
        /// <see cref="SortedDictionary{TKey,TValue}"/> 保证确定性，此处沿用同一惯例。
        /// </para>
        /// </summary>
        private static double ComputeEffectiveRegenRate(PowerTypeDefinition definition, PowerState power, bool inCombat)
        {
            var baseRate = inCombat ? definition.RegenInCombat : definition.RegenOutOfCombat;
            if (power.RegenModifiers == null || power.RegenModifiers.Count == 0)
            {
                return baseRate;
            }

            double addSum = 0;
            double multProduct = 1;
            foreach (var modifier in power.RegenModifiers.Values)
            {
                if (!MatchesScope(modifier.Scope, inCombat))
                {
                    continue;
                }

                addSum += modifier.Add;
                multProduct *= modifier.Multiplier;
            }

            var effective = (baseRate + addSum) * multProduct;
            return effective < 0 ? 0 : effective;
        }

        private static bool MatchesScope(RegenScope scope, bool inCombat)
        {
            switch (scope)
            {
                case RegenScope.Both:
                    return true;
                case RegenScope.InCombat:
                    return inCombat;
                case RegenScope.OutOfCombat:
                    return !inCombat;
                default:
                    return false;
            }
        }

        private double ComputeMax(Id unitId, PowerTypeDefinition definition)
        {
            if (definition.MaxSourceKind == PowerMaxSourceKind.Fixed)
            {
                return definition.MaxFixedValue;
            }

            if (_statLookup == null)
            {
                throw new InvalidOperationException(
                    $"资源类型 \"{definition.Id}\" 的上限来源是属性引用（{definition.MaxStat}），但构造 PowerHost 时未注入 StatLookup");
            }

            return _statLookup(unitId, definition.MaxStat!.Value);
        }

        /// <summary>
        /// 修改当前值的唯一入口：夹取到 <c>[min, max]</c>（<c>allow_overflow</c> 时上限不夹取），
        /// 值变化时发 <c>power.changed</c>，从大于 min 变为等于 min 时额外发一次 <c>power.depleted</c>。
        /// </summary>
        private void ApplyDelta(Id unitId, Id powerType, PowerTypeDefinition definition, PowerState power, double delta)
        {
            var raw = power.Current + delta;
            var target = ClampTarget(definition, power, raw);
            SetCurrentClamped(unitId, powerType, definition, power, target);
        }

        private static double ClampTarget(PowerTypeDefinition definition, PowerState power, double raw)
        {
            var lower = EffectiveMin(power, definition);
            var upper = definition.AllowOverflow ? double.PositiveInfinity : power.Max;

            if (raw < lower)
            {
                return lower;
            }

            if (raw > upper)
            {
                return upper;
            }

            return raw;
        }

        /// <summary>ADR-0106：本类型全部夹取/"跌到下限"判定的唯一取值出口——覆盖存在时用覆盖值，
        /// 否则回落到资源类型定义的 <see cref="PowerTypeDefinition.Min"/>。不复制这段判断逻辑，
        /// <see cref="RegisterUnit"/> 初始值、<see cref="ClampTarget"/>、<see cref="RecomputeMax"/>
        /// 上限下降夹取、<see cref="SetCurrentClamped"/> 的 depleted 判定均经本方法读取。</summary>
        private static double EffectiveMin(PowerState power, PowerTypeDefinition definition) =>
            power.MinOverride ?? definition.Min;

        /// <summary>把当前值直接设为 <paramref name="target"/>（调用方已完成夹取计算），
        /// 值变化时发事件；供 <see cref="ApplyDelta"/>、脱战回满、上限下降夹取三处复用。</summary>
        private void SetCurrentClamped(Id unitId, Id powerType, PowerTypeDefinition definition, PowerState power, double target)
        {
            var old = power.Current;
            if (target.Equals(old))
            {
                return;
            }

            power.Current = target;
            _bus.Enqueue(new PowerChangedEvent(unitId, powerType, old, target));

            // ADR-0106：depleted 判定改读 EffectiveMin——覆盖存在时，"跌到下限"指跌到覆盖值，不是
            // 资源类型定义的全局 Min（否则覆盖了下限却仍在覆盖值那一刻误发 depleted，与"不死"这一
            // 设计意图矛盾：调用方若订阅 power.depleted 做死亡结算的旁路判断，会看到与预期不符的信号）。
            var effectiveMin = EffectiveMin(power, definition);
            if (old > effectiveMin && target <= effectiveMin)
            {
                _bus.Enqueue(new PowerDepletedEvent(unitId, powerType));
            }
        }

        private UnitState RequireUnit(Id unitId)
        {
            if (!_units.TryGetValue(unitId, out var state))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 未注册");
            }

            return state;
        }

        private PowerTypeDefinition RequireDefinition(Id powerType)
        {
            if (!_definitions.TryGetValue(powerType, out var definition))
            {
                throw new InvalidOperationException($"资源类型 \"{powerType}\" 未在构造 PowerHost 时登记");
            }

            return definition;
        }

        private PowerState RequirePower(Id unitId, Id powerType)
        {
            var state = RequireUnit(unitId);
            if (!state.Powers.TryGetValue(powerType, out var power))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 未持有资源类型 \"{powerType}\"");
            }

            return power;
        }
    }
}

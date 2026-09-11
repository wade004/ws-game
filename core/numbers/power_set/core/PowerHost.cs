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

        public void Advance(Id unitId, double timeUnits)
        {
            if (timeUnits < 0)
            {
                throw new ArgumentException("timeUnits 不能为负数", nameof(timeUnits));
            }

            var state = RequireUnit(unitId);
            AdvanceUnit(unitId, state, timeUnits);
        }

        public void AdvanceAll(double timeUnits)
        {
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
                    var clampedTarget = newMax < definition.Min ? definition.Min : newMax;
                    SetCurrentClamped(unitId, powerType, definition, power, clampedTarget);
                }
            }
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

                // 先 regen 后 decay（见 IPowerHost.Advance 注释）。
                var regenRate = state.InCombat ? definition.RegenInCombat : definition.RegenOutOfCombat;
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
            var lower = definition.Min;
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

            if (old > definition.Min && target <= definition.Min)
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

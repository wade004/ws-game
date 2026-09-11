using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 每单位/每技能/每冷却分类的剩余冷却、公共冷却剩余、充能数与恢复进度（见 06 第 3.6 节步骤
    /// 3/4、第 3.1 节 <c>charges</c>、<see cref="SkillOptions.GcdEnabled"/>/
    /// <see cref="SkillOptions.GcdDuration"/>）。全部计时以"当前生效的时间模式"单位计（见 04 第
    /// 3.1 节），本类倒计时推进（<see cref="Update"/>/<see cref="AdvanceCharges"/>）本身仍不关心
    /// 具体是秒还是回合。
    /// <para>
    /// 判断记录（"相邻缺口"根治，第五轮外部审核 audit-5e779c6-20260907 WA 报告"需要说明的取舍"
    /// 第 1 条；与 R05 同批语义，晚于 R05 一轮收口）：R05 只解决"切换那一刻，已经存在的倒计时状态
    /// 跟着换算"这一层——<see cref="StartCooldown"/> 施放当下从 <c>SkillDef</c> 读到的原始
    /// <c>cooldown_duration</c>/<c>charges.recharge_time</c> 数值本身，此前直接原样喂给倒计时状态，
    /// 不管施放当下究竟处于连续还是离散模式；若数据按"连续模式秒数"authoring、但技能恰好是在离散
    /// 战斗<b>进行中</b>才第一次被释放（不是"连续模式下已有冷却、切换时刻被换算"这条路径），这次
    /// 冷却会直接把"秒数"的数值当成"轮数"喂给按轮推进的 <see cref="Update"/>，同一份数据在两种
    /// 模式下产生完全不同倍数的实际冷却时长。本类型现持有 <see cref="_currentFactor"/>——"当前模式
    /// 1 个计时单位相当于连续模式（数据 authoring 的规范单位）多少秒"的运行期累乘系数，随
    /// <see cref="RescaleAll"/> 每次切换累乘更新（初始 1.0，即"当前是连续模式"）——<see cref="StartCooldown"/>
    /// 在把原始 <c>cooldown_duration</c>/<c>recharge_time</c> 写入倒计时状态前先乘以该系数，使
    /// 施放当下不论处于哪种模式，都能把 authoring 时的规范秒数正确折算成当前模式的计时单位，与
    /// R05 已经解决的"切换时刻换算"衔接成完整链路（两者共用同一个系数字段，互不冲突）。
    /// </para>
    /// <para>
    /// 判断记录（第五轮外部审核相邻缺口根治，architecture/落地计划/audit-5e779c6-20260907，续接
    /// R05/上一条判断记录的悬而未决问题）：<see cref="ModifyCooldown"/> 的 <c>delta</c> 现同样纳入
    /// 换算——<c>modify_cooldown</c> 效果原语的内容作者按"缩短/延长几秒冷却"authoring 这个数值，
    /// 与同一份 <c>SkillDef</c> 数据集里 <c>cooldown_duration</c>/<c>charges.recharge_time</c> 的
    /// authoring 惯例同一口径（06 未对 <c>delta</c> 的时间单位另行约定，最合理的默认解释是与所属
    /// 数据集其它时间字段统一，同 04 第 3.1 节"全部时间字段一律以数据集声明的时间单位计"）；而
    /// <see cref="_skillCooldowns"/>/<see cref="_categoryCooldowns"/> 的既有存量已经是"当前模式计时
    /// 单位"（与 <see cref="StartCooldown"/> 写入时的口径一致），二者相加前必须先把 <c>delta</c>
    /// 换算到同一单位，否则会出现"当前是离散模式，效果想缩短 3 秒却被当成缩短 3 轮"的错位。
    /// <see cref="AddCharge"/> 的 <c>amount</c> 不纳入本次换算——它是"增加几次充能"的离散计数
    /// （<c>int</c>，见该方法签名），不是时间量，与 <c>delta</c>/<c>cooldown_duration</c> 不是同一
    /// 量纲，乘时间换算系数没有意义（会把"加 1 次充能"在 factor≠1 时变成非整数次充能，需要额外
    /// 舍入规则且无对应的数据语义可循）；06 未把 <c>amount</c> 登记为时间字段，维持原样。
    /// </para>
    /// </summary>
    public sealed class CooldownTracker
    {
        /// <summary>见类型判断记录：当前模式 1 个计时单位相当于连续模式多少秒；初始 1.0（游戏总是
        /// 从连续模式起步，见 <c>Core.Gameplay.Assembly.TimeModelSwitch.CurrentMode</c> 默认值），
        /// 随 <see cref="RescaleAll"/> 每次模式切换累乘更新。</summary>
        private double _currentFactor = 1.0;

        private sealed class ChargeState
        {
            public int Current;
            public double RechargeRemaining;
        }

        private readonly Dictionary<(Id Unit, Id Skill), double> _skillCooldowns = new Dictionary<(Id, Id), double>();
        private readonly Dictionary<(Id Unit, Id Category), double> _categoryCooldowns = new Dictionary<(Id, Id), double>();
        private readonly Dictionary<(Id Unit, Id Skill), ChargeState> _charges = new Dictionary<(Id, Id), ChargeState>();
        private readonly Dictionary<Id, double> _gcdRemaining = new Dictionary<Id, double>();

        /// <summary>
        /// W1 收边补齐（06 第 3.5 节 <c>SpellModDimension.Charges</c>）：延迟注入的 SpellMod 解析器，
        /// 供 <see cref="EffectiveChargesMax"/>/<see cref="EffectiveRechargeTime"/> 按当前生效的
        /// <c>charges</c> 维度 SpellMod 修正充能上限与单次恢复时间。延迟为可写属性（而非构造参数）
        /// 是因为 <c>SkillHost</c> 构造顺序里 <see cref="CooldownTracker"/> 先于
        /// <see cref="SpellModResolver"/> 构造（后者依赖 <c>SkillDefCache</c>/<c>AuraHost</c>），与
        /// <c>AuraHost.ProcHost</c>/<c>AuraHost.EffectSink</c> 同一种"先构造、后回填"处理循环依赖的
        /// 既有写法。为 null（未接线）时按 <c>def.ChargesMax</c>/<c>def.ChargesRechargeTime</c>
        /// 原始值计算，行为与本次改动之前完全一致。
        /// <para>
        /// 判断记录：06 原文只给出 <c>charges</c> 这一个维度同时对应"充能次数与单次恢复时间"两个
        /// 数值（第 3.1 节 <c>charges: Optional&lt;{max, recharge_time}&gt;</c>），未规定 SpellMod
        /// 具体修正二者中的哪一个；本模块拍板两者都受同一维度的 flat/pct 修正各自独立解析（各自以
        /// 原始值为 <c>baseValue</c> 调 <see cref="SpellModResolver.Apply"/>），呼应"充能"天赋常见的
        /// 两种口味（多给一次充能 / 缩短恢复时间）都能表达，不强行二选一。
        /// </para>
        /// </summary>
        public SpellModResolver? SpellMods { get; set; }

        /// <summary>
        /// 消费方反馈（2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"，见
        /// architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md）：<see cref="_currentFactor"/>
        /// 的只读公开出口——供 <see cref="SkillHost.GetSkillReadiness"/> 把 <see cref="SpellModResolver"/>
        /// 修饰后、仍是 authoring 规范单位的 <c>cooldown_duration</c> 换算成与 <see cref="GetCooldown"/>
        /// 同一口径的"当前模式计时单位"（换算方式与 <see cref="StartCooldown"/> 写入倒计时状态前的
        /// 折算完全相同：修饰后原始值 × 本系数）。只读，不修改任何状态。
        /// </summary>
        public double CurrentTimeFactor => _currentFactor;

        /// <summary>该技能是否就绪（不含公共冷却，公共冷却由 <see cref="IsGcdReady"/> 单独判断，
        /// 见 06 第 3.6 节步骤 3/4 是两个独立步骤）：有充能配置时看充能数 &gt; 0，否则看技能自身
        /// 与所属分类冷却是否均已归零。</summary>
        public bool IsSkillReady(Id unitId, SkillDef def)
        {
            if (def.HasCharges)
            {
                return GetCharges(unitId, def) > 0;
            }

            if (GetSkillCooldownRemaining(unitId, def.Id) > 0)
            {
                return false;
            }

            if (def.CooldownCategory.HasValue && GetCategoryCooldownRemaining(unitId, def.CooldownCategory.Value) > 0)
            {
                return false;
            }

            return true;
        }

        public double GetSkillCooldownRemaining(Id unitId, Id skillId) =>
            _skillCooldowns.TryGetValue((unitId, skillId), out var v) ? Math.Max(0, v) : 0;

        public double GetCategoryCooldownRemaining(Id unitId, Id category) =>
            _categoryCooldowns.TryGetValue((unitId, category), out var v) ? Math.Max(0, v) : 0;

        /// <summary>合并技能自身与所属分类冷却的剩余时间（<see cref="ISkillHost.GetCooldown"/> 语义，
        /// 见 06 第 7 节"某技能……距下次可用的剩余时间；就绪返回 0"）：有充能配置时按"充能耗尽时的
        /// 恢复进度"折算。</summary>
        public double GetCooldown(Id unitId, SkillDef def)
        {
            if (def.HasCharges)
            {
                var current = GetCharges(unitId, def);
                if (current > 0)
                {
                    return 0;
                }

                return _charges.TryGetValue((unitId, def.Id), out var state) ? Math.Max(0, state.RechargeRemaining) : 0;
            }

            var skillRemaining = GetSkillCooldownRemaining(unitId, def.Id);
            var categoryRemaining = def.CooldownCategory.HasValue
                ? GetCategoryCooldownRemaining(unitId, def.CooldownCategory.Value)
                : 0;
            return Math.Max(skillRemaining, categoryRemaining);
        }

        /// <summary>当前已产生充能状态的 (unit, skill) 组合，供 <see cref="SkillHost.Update"/>
        /// 逐个调用 <see cref="AdvanceCharges"/> 推进恢复（本类不持有 <see cref="SkillDef"/>，
        /// 无法自行推进，见 <see cref="Update"/> 注释）。</summary>
        public IReadOnlyList<(Id Unit, Id Skill)> TrackedChargeKeys => new List<(Id, Id)>(_charges.Keys);

        /// <summary>不知道 <see cref="SkillDef"/> 时的查询：从未产生过充能状态（技能从未施放/从未
        /// 被 <see cref="AdvanceCharges"/> 触达）时返回 0——注意这与"满充能"不同，调用方已知
        /// <see cref="SkillDef"/> 时应改用 <see cref="GetCharges(Id, SkillDef)"/>（后者对未初始化
        /// 的状态正确返回 <c>ChargesMax</c>，见该方法注释）。</summary>
        public int GetCharges(Id unitId, Id skillId) =>
            _charges.TryGetValue((unitId, skillId), out var state) ? state.Current : 0;

        /// <summary>当前充能数；从未产生过充能状态（技能从未施放过，也未被 <see cref="AdvanceCharges"/>
        /// 触达过）时视为"满充能"（<c>def.ChargesMax</c>），而不是 0——一个刚学会、从未使用过的
        /// 技能理应是满充能可用状态。惰性创建状态（见 <see cref="GetOrCreateChargeState"/>）。</summary>
        public int GetCharges(Id unitId, SkillDef def) => GetOrCreateChargeState(unitId, def).Current;

        /// <summary>
        /// 消费方反馈（2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"，见
        /// architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md）：距下一次充能恢复完成
        /// 的剩余时间——供 <see cref="SkillHost.GetSkillReadiness"/> 呈现"部分充能恢复中"这一状态
        /// （区分"当前充能数"与"下次恢复还差多久"，<see cref="GetCharges(Id, SkillDef)"/> 只呈现前者）。
        /// 与 <see cref="GetCharges(Id, SkillDef)"/> 同一惯例：从未产生过充能状态时惰性创建（满充能、
        /// <c>RechargeRemaining=0</c>），不是"未知"；满充能（含从未消耗过）时恒为 0——没有正在进行
        /// 的恢复窗口，与 <see cref="AddCharge"/>/<see cref="StartCooldown"/> 补满时清零
        /// <c>RechargeRemaining</c> 的既有惯例一致。只读，不推进时间、不修改任何状态（惰性创建的
        /// 默认状态与"从未调用本方法"时 <see cref="GetCharges(Id, SkillDef)"/> 会创建的状态完全
        /// 相同，不产生可观测差异）。
        /// </summary>
        public double GetChargeRechargeRemaining(Id unitId, SkillDef def) =>
            Math.Max(0, GetOrCreateChargeState(unitId, def).RechargeRemaining);

        /// <summary>
        /// 消费方反馈（同上）：<c>charges</c> 维度 SpellMod 修正后的有效充能上限——原为私有
        /// <see cref="EffectiveChargesMax"/> 的只读公开出口，供 <see cref="SkillHost.GetSkillReadiness"/>
        /// 呈现"修饰后有效上限"。只读，不修改任何状态。
        /// </summary>
        public int GetEffectiveChargesMax(Id unitId, SkillDef def) => EffectiveChargesMax(unitId, def);

        /// <summary>
        /// 消费方反馈（同上）：<c>charges</c> 维度 SpellMod 修正后的单次充能恢复时间，已按
        /// <see cref="CurrentTimeFactor"/> 折算为与 <see cref="GetCooldown(Id, SkillDef)"/> 同一口径
        /// 的当前模式计时单位（折算方式同 <see cref="StartCooldown"/>/<see cref="AdvanceCharges"/>
        /// 写入 <c>RechargeRemaining</c> 前的折算）——供 <see cref="SkillReadiness.EffectiveCooldownDuration"/>
        /// 呈现有充能配置的技能"修饰后完整恢复周期"。只读，不修改任何状态。
        /// </summary>
        public double GetEffectiveRechargeTimeScaled(Id unitId, SkillDef def) =>
            EffectiveRechargeTime(unitId, def) * _currentFactor;

        /// <summary>技能施放成功、进入步骤 9 时调用：扣减一次充能或进入标准冷却（见 06 第 3.6 节
        /// 步骤 9）。<paramref name="cooldownDurationOverride"/> 供 <see cref="SpellModResolver"/>
        /// 修正过的冷却时长覆盖 <c>def.CooldownDuration</c>（见 06 第 3.5 节 <c>cooldown</c> 维度）；
        /// 为 null 时使用 <c>def.CooldownDuration</c> 原值。
        /// <para>
        /// R06 收边补齐（外部审计 5e779c6，P2）：<c>recharge_time &lt;= 0</c>（<c>skill.def.charges.
        /// recharge_time</c> 显式登记为 0，或——见 <see cref="EffectiveRechargeTime"/>——被
        /// <c>charges</c> 维度 SpellMod 动态修正到 0/负数）统一按<b>"即时恢复"</b>处理：本次消耗的
        /// 那一点充能立即原地补满，不产生"进入一个恢复窗口"的中间状态。判断记录（为什么不是"不
        /// 恢复"）：修复前的实现把 <c>RechargeRemaining &lt;= 0</c> 同时用作"当前没有正在进行的恢复"
        /// 与"恢复已经完成、只差被 <see cref="AdvanceCharges"/> 发现"两种含义的哨兵值——recharge_time
        /// 为 0 时这里把它设成 0（没有变化），<see cref="AdvanceCharges"/> 一看到
        /// <c>RechargeRemaining &lt;= 0</c> 就直接判定"没有需要推进的恢复"提前返回，永远不会把
        /// <c>Current</c> 加回去；充能一旦耗尽（<c>Current</c> 降到 0）就再也没有任何路径能让它变回
        /// 正数——技能永久不可用（外部审计复现）。"0 秒即可恢复"字面意思就是"立刻就好"，选择"即时
        /// 恢复"是对这个数值最直接的解读，也是唯一不会引入死状态的选择；显式登记 0 是否真的是数据
        /// 作者的本意（也可能是想表达"用一次就永久没了"）无法在引擎层判断，交给
        /// <c>SkillValidationRules</c> 的告警提醒数据作者复核（见该类型判断记录、06 勘误）。
        /// </para>
        /// </summary>
        public void StartCooldown(Id unitId, SkillDef def, double? cooldownDurationOverride = null)
        {
            if (def.HasCharges)
            {
                var state = GetOrCreateChargeState(unitId, def);
                if (state.Current > 0)
                {
                    state.Current--;
                }

                var max = EffectiveChargesMax(unitId, def);
                if (state.Current < max)
                {
                    // 类型判断记录"相邻缺口根治"：EffectiveRechargeTime 返回的是 SkillDef/SpellMod
                    // 视角的原始（authoring）恢复时长，乘 _currentFactor 折算成当前模式的计时单位，
                    // 与 R05 换算既有充能状态衔接。_currentFactor 恒为正数，乘法不改变 <= 0 判定结果，
                    // 这里先折算再判断与判断后再折算等价，不产生行为分歧。
                    var recharge = EffectiveRechargeTime(unitId, def) * _currentFactor;
                    if (recharge <= 0)
                    {
                        state.Current = max;
                        state.RechargeRemaining = 0;
                    }
                    else if (state.RechargeRemaining <= 0)
                    {
                        state.RechargeRemaining = recharge;
                    }
                }

                return;
            }

            // 类型判断记录"相邻缺口根治"：原始 cooldown_duration（或 SpellMod 修正后的 override，
            // 同样是在原始规范单位上做相对调整，未改变其单位含义）乘 _currentFactor 折算成当前模式
            // 的计时单位。
            var duration = (cooldownDurationOverride ?? def.CooldownDuration) * _currentFactor;
            _skillCooldowns[(unitId, def.Id)] = duration;
            if (def.CooldownCategory.HasValue)
            {
                _categoryCooldowns[(unitId, def.CooldownCategory.Value)] = duration;
            }
        }

        public void StartGcd(Id unitId, double duration)
        {
            _gcdRemaining[unitId] = duration;
        }

        public bool IsGcdReady(Id unitId) => GetGcdRemaining(unitId) <= 0;

        public double GetGcdRemaining(Id unitId) => _gcdRemaining.TryGetValue(unitId, out var v) ? Math.Max(0, v) : 0;

        /// <summary><c>modify_cooldown</c> 效果原语落地（见 06 第 3.2 节）：<paramref name="delta"/>
        /// 为负表示缩短/重置冷却，正表示延长；结果夹取到不小于 0。
        /// <para>
        /// 第五轮外部审核相邻缺口根治：见类型顶部判断记录——<paramref name="delta"/> 与
        /// <c>cooldown_duration</c> 同属 authoring 规范时间量，乘 <see cref="_currentFactor"/>
        /// 折算成当前模式计时单位后再与既有存量（已经是当前模式单位）相加。
        /// </para>
        /// </summary>
        public void ModifyCooldown(Id unitId, Id skillOrCategoryId, double delta, bool isCategory)
        {
            var scaledDelta = delta * _currentFactor;
            if (isCategory)
            {
                var key = (unitId, skillOrCategoryId);
                var current = _categoryCooldowns.TryGetValue(key, out var v) ? v : 0;
                _categoryCooldowns[key] = Math.Max(0, current + scaledDelta);
            }
            else
            {
                var key = (unitId, skillOrCategoryId);
                var current = _skillCooldowns.TryGetValue(key, out var v) ? v : 0;
                _skillCooldowns[key] = Math.Max(0, current + scaledDelta);
            }
        }

        /// <summary><c>add_charge</c> 效果原语落地（见 06 第 3.2 节）：增加充能数，夹取到
        /// <c>def.ChargesMax</c>；技能未配置充能时无效果。<paramref name="amount"/> 不按
        /// <see cref="_currentFactor"/> 折算——见类型顶部判断记录：它是离散的充能次数计数，不是
        /// 时间量。</summary>
        public void AddCharge(Id unitId, SkillDef def, int amount)
        {
            if (!def.HasCharges)
            {
                return;
            }

            var state = GetOrCreateChargeState(unitId, def);
            var max = EffectiveChargesMax(unitId, def);
            state.Current = Math.Min(max, state.Current + amount);
            if (state.Current >= max)
            {
                state.RechargeRemaining = 0;
            }
        }

        /// <summary>按 <paramref name="dt"/> 推进技能自身/分类冷却与公共冷却计时（见 06 第 3.6 节
        /// 施法管线；调用时机见 <see cref="SkillTickHandler"/>）。充能恢复需要知道每个技能的
        /// <c>ChargesMax</c>/<c>recharge_time</c>，本类不持有 <see cref="SkillDef"/> 集合，由调用方
        /// 对每个已知技能定义调用 <see cref="AdvanceCharges"/> 单独推进（见该方法注释）。</summary>
        public void Update(double dt)
        {
            AdvanceMap(_skillCooldowns, dt);
            AdvanceMap(_categoryCooldowns, dt);

            var gcdKeys = new List<Id>(_gcdRemaining.Keys);
            foreach (var key in gcdKeys)
            {
                _gcdRemaining[key] = Math.Max(0, _gcdRemaining[key] - dt);
            }
        }

        /// <summary>充能恢复的"归零即 +1 并重置进度"语义需要知道 <c>ChargesMax</c>，本类
        /// <see cref="Update"/> 不持有 <see cref="SkillDef"/>，改由调用方在拿到 def 时调用本方法
        /// 精确推进单个技能的充能恢复（<see cref="SkillHost"/> 在 <c>Update</c> 中对每个已知
        /// 技能定义调用）。</summary>
        public void AdvanceCharges(Id unitId, SkillDef def, double dt)
        {
            if (!def.HasCharges)
            {
                return;
            }

            if (!_charges.TryGetValue((unitId, def.Id), out var state) || state.RechargeRemaining <= 0)
            {
                return;
            }

            var max = EffectiveChargesMax(unitId, def);
            state.RechargeRemaining -= dt;
            while (state.RechargeRemaining <= 0 && state.Current < max)
            {
                state.Current++;
                if (state.Current >= max)
                {
                    state.RechargeRemaining = 0;
                    break;
                }

                // CR130-03 根治（外部审计 audit-5c444f1-20260908）：StartCooldown 写入下一个恢复窗口
                // 时先乘 _currentFactor（见该方法判断记录），这里"这一次充能刚恢复完、紧接着开始下
                // 一个恢复窗口"必须用同一口径，否则模式切换后只有"第一颗充能消耗时启动的那个窗口"被
                // 正确折算，后续窗口全部退回未折算的原始 authoring 秒数（外部审计复现：factor=0.2、
                // recharge_time=10，期望折算为 2，实际残留 10）。
                state.RechargeRemaining += EffectiveRechargeTime(unitId, def) * _currentFactor;
            }
        }

        private ChargeState GetOrCreateChargeState(Id unitId, SkillDef def)
        {
            var key = (unitId, def.Id);
            if (!_charges.TryGetValue(key, out var state))
            {
                state = new ChargeState { Current = EffectiveChargesMax(unitId, def), RechargeRemaining = 0 };
                _charges[key] = state;
            }

            return state;
        }

        /// <summary>见 <see cref="SpellMods"/> 判断记录：<c>charges</c> 维度 SpellMod 修正后的充能
        /// 上限，四舍五入取整并夹取到至少 1（不允许修正后变成 0 或负数——那会让"有充能配置"的技能
        /// 永久不可用，属于内容/数值配平层面才应做出的决定，本层只兜底不允许运行时崩溃）。</summary>
        private int EffectiveChargesMax(Id unitId, SkillDef def)
        {
            var max = def.ChargesMax!.Value;
            if (SpellMods == null)
            {
                return max;
            }

            var modified = SpellMods.Apply(unitId, SpellModDimension.Charges, def.Id, def.School, def.Tags, max);
            return Math.Max(1, (int)Math.Round(modified, MidpointRounding.AwayFromZero));
        }

        /// <summary>见 <see cref="SpellMods"/> 判断记录：<c>charges</c> 维度 SpellMod 修正后的单次
        /// 恢复时间，夹取到不小于 0。</summary>
        private double EffectiveRechargeTime(Id unitId, SkillDef def)
        {
            if (SpellMods == null)
            {
                return def.ChargesRechargeTime;
            }

            return Math.Max(0, SpellMods.Apply(unitId, SpellModDimension.Charges, def.Id, def.School, def.Tags, def.ChargesRechargeTime));
        }

        private static void AdvanceMap<TKey>(Dictionary<TKey, double> map, double dt) where TKey : notnull
        {
            var keys = new List<TKey>(map.Keys);
            foreach (var key in keys)
            {
                map[key] = Math.Max(0, map[key] - dt);
            }
        }

        /// <summary>
        /// R05 收边补齐（外部审计 5e779c6，P2；见 <see cref="Core.Rules.Common.TimeModelRescaledEvent"/>
        /// 类型判断记录）：连续/离散模式切换时把全部倒计时状态（技能冷却、分类冷却、公共冷却、
        /// 充能恢复进度）按同一系数换算——本类型注释"全部计时以数据集声明的时间单位计"，切换模式
        /// 相当于换了"1 个时间单位"的物理含义（秒↔轮），已经存在的剩余数值必须跟着换算，否则会被
        /// 新模式的 <see cref="Update"/> 用错误的单位重新解读（外部审计 R05 描述的"计时错位"）。
        /// <paramref name="factor"/> 语义同 <see cref="Core.Foundation.SimLoop.SimTimers.RescaleAll"/>：
        /// 新单位下 1 个单位对应旧单位下 <paramref name="factor"/> 个单位。
        /// </summary>
        public void RescaleAll(double factor)
        {
            if (factor <= 0)
            {
                throw new ArgumentException("factor 必须为正数", nameof(factor));
            }

            // 类型判断记录"相邻缺口根治"：累乘更新，供本次切换之后（<see cref="StartCooldown"/>）
            // 施放的新技能按当前模式正确折算原始 authoring 数值，与本方法下方对既有状态的换算是
            // 两件互补的事——既有状态换算过去已经计入的时长，_currentFactor 换算此后新产生的时长。
            _currentFactor *= factor;

            RescaleMap(_skillCooldowns, factor);
            RescaleMap(_categoryCooldowns, factor);

            var gcdKeys = new List<Id>(_gcdRemaining.Keys);
            foreach (var key in gcdKeys)
            {
                _gcdRemaining[key] = _gcdRemaining[key] * factor;
            }

            var chargeKeys = new List<(Id, Id)>(_charges.Keys);
            foreach (var key in chargeKeys)
            {
                var state = _charges[key];
                if (state.RechargeRemaining > 0)
                {
                    state.RechargeRemaining *= factor;
                }
            }
        }

        private static void RescaleMap<TKey>(Dictionary<TKey, double> map, double factor) where TKey : notnull
        {
            var keys = new List<TKey>(map.Keys);
            foreach (var key in keys)
            {
                map[key] = map[key] * factor;
            }
        }
    }
}

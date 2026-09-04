using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 每单位/每技能/每冷却分类的剩余冷却、公共冷却剩余、充能数与恢复进度（见 06 第 3.6 节步骤
    /// 3/4、第 3.1 节 <c>charges</c>、<see cref="SkillOptions.GcdEnabled"/>/
    /// <see cref="SkillOptions.GcdDuration"/>）。全部计时以数据集声明的时间单位计（见 04 第 3.1
    /// 节），本类不关心具体是秒还是回合，只做倒计时。
    /// </summary>
    public sealed class CooldownTracker
    {
        private sealed class ChargeState
        {
            public int Current;
            public double RechargeRemaining;
        }

        private readonly Dictionary<(Id Unit, Id Skill), double> _skillCooldowns = new Dictionary<(Id, Id), double>();
        private readonly Dictionary<(Id Unit, Id Category), double> _categoryCooldowns = new Dictionary<(Id, Id), double>();
        private readonly Dictionary<(Id Unit, Id Skill), ChargeState> _charges = new Dictionary<(Id, Id), ChargeState>();
        private readonly Dictionary<Id, double> _gcdRemaining = new Dictionary<Id, double>();

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

        /// <summary>技能施放成功、进入步骤 9 时调用：扣减一次充能或进入标准冷却（见 06 第 3.6 节
        /// 步骤 9）。<paramref name="cooldownDurationOverride"/> 供 <see cref="SpellModResolver"/>
        /// 修正过的冷却时长覆盖 <c>def.CooldownDuration</c>（见 06 第 3.5 节 <c>cooldown</c> 维度）；
        /// 为 null 时使用 <c>def.CooldownDuration</c> 原值。</summary>
        public void StartCooldown(Id unitId, SkillDef def, double? cooldownDurationOverride = null)
        {
            if (def.HasCharges)
            {
                var state = GetOrCreateChargeState(unitId, def);
                if (state.Current > 0)
                {
                    state.Current--;
                }

                if (state.RechargeRemaining <= 0 && state.Current < def.ChargesMax!.Value)
                {
                    state.RechargeRemaining = def.ChargesRechargeTime;
                }

                return;
            }

            var duration = cooldownDurationOverride ?? def.CooldownDuration;
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
        /// 为负表示缩短/重置冷却，正表示延长；结果夹取到不小于 0。</summary>
        public void ModifyCooldown(Id unitId, Id skillOrCategoryId, double delta, bool isCategory)
        {
            if (isCategory)
            {
                var key = (unitId, skillOrCategoryId);
                var current = _categoryCooldowns.TryGetValue(key, out var v) ? v : 0;
                _categoryCooldowns[key] = Math.Max(0, current + delta);
            }
            else
            {
                var key = (unitId, skillOrCategoryId);
                var current = _skillCooldowns.TryGetValue(key, out var v) ? v : 0;
                _skillCooldowns[key] = Math.Max(0, current + delta);
            }
        }

        /// <summary><c>add_charge</c> 效果原语落地（见 06 第 3.2 节）：增加充能数，夹取到
        /// <c>def.ChargesMax</c>；技能未配置充能时无效果。</summary>
        public void AddCharge(Id unitId, SkillDef def, int amount)
        {
            if (!def.HasCharges)
            {
                return;
            }

            var state = GetOrCreateChargeState(unitId, def);
            state.Current = Math.Min(def.ChargesMax!.Value, state.Current + amount);
            if (state.Current >= def.ChargesMax.Value)
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

            state.RechargeRemaining -= dt;
            while (state.RechargeRemaining <= 0 && state.Current < def.ChargesMax!.Value)
            {
                state.Current++;
                if (state.Current >= def.ChargesMax.Value)
                {
                    state.RechargeRemaining = 0;
                    break;
                }

                state.RechargeRemaining += def.ChargesRechargeTime;
            }
        }

        private ChargeState GetOrCreateChargeState(Id unitId, SkillDef def)
        {
            var key = (unitId, def.Id);
            if (!_charges.TryGetValue(key, out var state))
            {
                state = new ChargeState { Current = def.ChargesMax!.Value, RechargeRemaining = 0 };
                _charges[key] = state;
            }

            return state;
        }

        private static void AdvanceMap<TKey>(Dictionary<TKey, double> map, double dt) where TKey : notnull
        {
            var keys = new List<TKey>(map.Keys);
            foreach (var key in keys)
            {
                map[key] = Math.Max(0, map[key] - dt);
            }
        }
    }
}

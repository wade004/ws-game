using System;
using System.Collections.Generic;
using Core.Foundation.Feel;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 运动模式规则（手感设计/02 第 3.1 节；数据表 <c>feel.motion_mode_rules</c> 的消费结果）：每个模式三个判断——
    /// 是否接受输入位移、是否允许转向、退出时是否保留动量。游戏可改表（例如允许硬直中缓慢转向）。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <para>
    /// 1) <see cref="Default"/> 与框架数据 <c>data/_feel/feel/feel.motion_mode_rules.json</c> 的七行逐项一致（测试锁定），
    /// 没有装配规则表的游戏得到同一组缺省；表里缺行的模式回退到缺省行（<see cref="FromRules"/>）。
    /// </para>
    /// <para>
    /// 2) <c>by_profile</c> 只对 <c>action</c> 有定义（接受输入 = <c>action_move_speed_ratio &gt; 0</c>、允许转向 =
    /// <c>!action_turn_lock</c>、保留动量 = <c>keep_momentum_on_action_end</c>）；别的模式写 <c>by_profile</c> 是数据错误，
    /// 构造时抛 <see cref="ArgumentException"/>，不静默按某个值处理。<c>restore_previous_mode</c> 只对 <c>frozen</c> 有定义，
    /// 语义是"叠加态，速度保留、解冻后恢复底层模式"，视为保留动量。
    /// </para>
    /// </summary>
    public sealed class MotionModeRuleSet
    {
        private const string Yes = "yes";
        private const string No = "no";
        private const string ByProfile = "by_profile";
        private const string None = "none";
        private const string Restore = "restore_previous_mode";

        private readonly string[] _accepts;
        private readonly string[] _turn;
        private readonly string[] _keeps;

        private MotionModeRuleSet(string[] accepts, string[] turn, string[] keeps)
        {
            _accepts = accepts;
            _turn = turn;
            _keeps = keeps;
        }

        /// <summary>设计文档第 3.1 节表的缺省规则（与框架数据一致）。</summary>
        public static MotionModeRuleSet Default { get; } = BuildDefault();

        private static MotionModeRuleSet BuildDefault()
        {
            // 下标 = (int)MotionMode：Grounded, Action, Forced, Staggered, Rooted, Frozen, Dead
            var accepts = new[] { Yes, ByProfile, No, No, No, No, No };
            var turn = new[] { Yes, ByProfile, No, No, Yes, No, No };
            var keeps = new[] { None, ByProfile, No, No, No, Restore, None };
            return new MotionModeRuleSet(accepts, turn, keeps);
        }

        /// <summary>从 <c>feel.motion_mode_rules</c> 的行构造；缺行的模式用缺省，未知模式名或非法取值抛 <see cref="ArgumentException"/>。</summary>
        public static MotionModeRuleSet FromRules(IEnumerable<FeelMotionModeRule>? rules)
        {
            var accepts = (string[])Default._accepts.Clone();
            var turn = (string[])Default._turn.Clone();
            var keeps = (string[])Default._keeps.Clone();
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    var mode = ParseMode(rule.Mode);
                    var i = (int)mode;
                    Validate(mode, "accepts_input_displacement", rule.AcceptsInputDisplacement, Yes, No, ByProfile);
                    Validate(mode, "allows_turn", rule.AllowsTurn, Yes, No, ByProfile);
                    Validate(mode, "keeps_momentum_on_exit", rule.KeepsMomentumOnExit, Yes, No, ByProfile, None, Restore);
                    accepts[i] = rule.AcceptsInputDisplacement;
                    turn[i] = rule.AllowsTurn;
                    keeps[i] = rule.KeepsMomentumOnExit;
                }
            }

            return new MotionModeRuleSet(accepts, turn, keeps);
        }

        /// <summary>从档案集合的规则表构造。</summary>
        public static MotionModeRuleSet FromProfiles(FeelProfileSet profiles)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            return FromRules(profiles.MotionModeRules);
        }

        private static MotionMode ParseMode(string name)
        {
            for (var m = MotionMode.Grounded; m <= MotionMode.Dead; m++)
            {
                if (MotionModeNames.ToName(m) == name) return m;
            }

            throw new ArgumentException($"feel.motion_mode_rules 的 mode \"{name}\" 不是 grounded|action|forced|staggered|rooted|frozen|dead");
        }

        private static void Validate(MotionMode mode, string column, string value, params string[] allowed)
        {
            if (Array.IndexOf(allowed, value) < 0)
            {
                throw new ArgumentException($"feel.motion_mode_rules[{MotionModeNames.ToName(mode)}].{column} 取值 \"{value}\" 非法");
            }

            if (value == ByProfile && mode != MotionMode.Action)
            {
                throw new ArgumentException(
                    $"feel.motion_mode_rules[{MotionModeNames.ToName(mode)}].{column} 写了 by_profile，但 by_profile 只对 action 模式有定义");
            }

            if (value == Restore && mode != MotionMode.Frozen)
            {
                throw new ArgumentException(
                    $"feel.motion_mode_rules[{MotionModeNames.ToName(mode)}].{column} 写了 restore_previous_mode，但它只对 frozen 模式有定义");
            }
        }

        /// <summary>该模式是否接受输入位移（<c>by_profile</c> 时 <c>action_move_speed_ratio &gt; 0</c>）。</summary>
        public bool AcceptsInput(MotionMode mode, in MotionProfile profile)
        {
            var v = _accepts[(int)mode];
            return v == ByProfile ? profile.ActionMoveSpeedRatio > 0 : v == Yes;
        }

        /// <summary>该模式是否允许转向（<c>by_profile</c> 时 <c>!action_turn_lock</c>）。</summary>
        public bool AllowsTurn(MotionMode mode, in MotionProfile profile)
        {
            var v = _turn[(int)mode];
            return v == ByProfile ? !profile.ActionTurnLock : v == Yes;
        }

        /// <summary>
        /// 退出该模式时是否<b>清零</b>动量：规则明确写 <c>no</c>（或 <c>by_profile</c> 且档案不保留）才清零；<c>none</c>（不适用，如
        /// grounded 与 dead）、<c>yes</c>、<c>restore_previous_mode</c> 都不清零。运动积分用它而不是
        /// <see cref="KeepsMomentumOnExit"/> 判定模式切换时的速度，免得从 grounded 进入 action 时误清速度。
        /// </summary>
        public bool ZeroesMomentumOnExit(MotionMode mode, in MotionProfile profile)
        {
            var v = _keeps[(int)mode];
            return v == ByProfile ? !profile.KeepMomentumOnActionEnd : v == No;
        }

        /// <summary>退出该模式时是否保留动量（<c>by_profile</c> 时 <c>keep_momentum_on_action_end</c>）。</summary>
        public bool KeepsMomentumOnExit(MotionMode mode, in MotionProfile profile)
        {
            var v = _keeps[(int)mode];
            return v == ByProfile ? profile.KeepMomentumOnActionEnd : (v == Yes || v == Restore);
        }
    }
}

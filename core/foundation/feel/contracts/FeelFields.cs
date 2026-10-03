using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 框架手感字段名常量（<see cref="FeelFields.Default"/> 登记的全部字段；手感设计/05 第 3.1 节七个分组、
    /// 01 第 4 节输入/动作组、02 第 2/7 节移动组、03 第 3/4 节受击组、07 第 2/3 节镜头与音频组、05 第 3.1 节特效组、
    /// 08 第 4 节武器资产字段）。消费方按名读取，不手写字符串字面量。
    /// </summary>
    public static class FeelFieldNames
    {
        // 输入（判定型）
        public const string BufferMs = "buffer_ms";
        public const string BufferSlots = "buffer_slots";
        public const string GraceMs = "grace_ms";
        public const string HoldThresholdMs = "hold_threshold_ms";

        // ADR-0143：轴处理三字段已从手感档案登记表移除（归设备/玩家设置，改在 found.input_action 声明，InputMapHost 消费）。
        // 常量按 ABI 只新增的约束保留并标过时，不再登记、不再有任何消费方。
        [Obsolete("ADR-0143：轴死区归 found.input_action.dead_zone 与玩家设置，不再是手感档案字段")]
        public const string DeadZone = "dead_zone";
        [Obsolete("ADR-0143：轴响应曲线归 found.input_action.response_curve 与玩家设置，不再是手感档案字段")]
        public const string ResponseCurve = "response_curve";
        [Obsolete("ADR-0143：轴平滑归 found.input_action.smoothing_ms 与玩家设置，不再是手感档案字段")]
        public const string SmoothingMs = "smoothing_ms";

        // 移动（判定型）
        public const string AccelMs = "accel_ms";
        public const string DecelMs = "decel_ms";
        public const string AccelCurve = "accel_curve";
        public const string BrakeCurve = "brake_curve";
        public const string ReversePolicy = "reverse_policy";
        public const string TurnRateDegS = "turn_rate_deg_s";
        public const string WalkSpeedRatio = "walk_speed_ratio";
        public const string SprintSpeedRatio = "sprint_speed_ratio";
        public const string ActionMoveSpeedRatio = "action_move_speed_ratio";
        public const string ActionTurnLock = "action_turn_lock";
        public const string KeepMomentumOnActionEnd = "keep_momentum_on_action_end";
        public const string KeepMomentumOnMotionEnd = "keep_momentum_on_motion_end";
        public const string ArrivalDecel = "arrival_decel";
        public const string WallSlide = "wall_slide";
        public const string ApplyToPathFollowing = "apply_to_path_following";
        public const string KnockbackResistanceStat = "knockback_resistance_stat";
        public const string UnitBodyRadius = "unit_body_radius";
        public const string DodgeThroughUnits = "dodge_through_units";
        public const string UnitSeparationSpeedRatio = "unit_separation_speed_ratio";
        public const string PathAvoidUnits = "path_avoid_units";
        public const string ForcedPushUnits = "forced_push_units";
        public const string ForcedPushRatio = "forced_push_ratio";
        public const string PassThroughMotionKinds = "pass_through_motion_kinds";

        // 移动（呈现型：步态阈值、步幅、启停混合、倾斜）
        public const string IdleMaxRatio = "idle_max_ratio";
        public const string WalkMaxRatio = "walk_max_ratio";
        public const string SprintMinRatio = "sprint_min_ratio";
        public const string GaitHysteresisRatio = "gait_hysteresis_ratio";
        public const string StrideScale = "stride_scale";
        public const string StartBlendMs = "start_blend_ms";
        public const string LandHoldMs = "land_hold_ms";
        public const string StopBlendMs = "stop_blend_ms";
        public const string LeanDegPerAccel = "lean_deg_per_accel";

        // 动作（判定型）
        public const string PhaseScaleStartup = "phase_scale.startup";
        public const string PhaseScaleActive = "phase_scale.active";
        public const string PhaseScaleRecovery = "phase_scale.recovery";
        public const string CancelWindowScale = "cancel_window_scale";
        public const string ComboWindowScale = "combo_window_scale";
        public const string ComboResetMs = "combo_reset_ms";
        public const string TurnAssistDeg = "turn_assist_deg";
        public const string MinActionMs = "min_action_ms";
        public const string StopDistance = "stop_distance";

        // 受击（判定型）
        public const string ImpactClass = "impact_class";
        public const string AttackerHitstopMs = "attacker_hitstop_ms";
        public const string TargetHitstopMs = "target_hitstop_ms";
        public const string HitstopCapMs = "hitstop_cap_ms";
        public const string AttackerHitstopCapMs = "attacker_hitstop_cap_ms";
        public const string HitStunMs = "hit_stun_ms";
        public const string StaggerPower = "stagger_power";
        public const string PoiseDamage = "poise_damage";
        public const string PoiseRecoverPerS = "poise_recover_per_s";
        public const string PoiseRecoverDelayMs = "poise_recover_delay_ms";
        public const string PoiseRecoverMode = "poise_recover_mode";
        public const string PoiseBreakResetMs = "poise_break_reset_ms";
        public const string KnockbackDistance = "knockback_distance";
        public const string LaunchHeight = "launch_height";
        public const string DownedMs = "downed_ms";
        public const string ReactionCap = "reaction_cap";
        public const string AirHitReaction = "air_hit_reaction";
        public const string LaunchStack = "launch_stack";
        public const string LaunchStackCap = "launch_stack_cap";
        public const string LaunchHeightCap = "launch_height_cap";
        public const string LaunchBodyScale = "launch_body_scale";

        /// <summary>
        /// 受击半径缩放（手感落地 M5-S2a，受击方档案，体型原型层可写）：目标命中半径 = <c>unit_body_radius</c>（标定后世界单位）× 本字段；
        /// 缺省（无值）= 1。只在装配启用 <c>HitRadiusFromFeel</c> 时被命中几何读取，见手感设计/03 第 2.2 节。
        /// </summary>
        public const string HurtRadiusScale = "hurt_radius_scale";
        public const string AirReactionCap = "air_reaction_cap";
        public const string AirStunUntilLand = "air_stun_until_land";
        public const string KillHitstopScale = "kill_hitstop_scale";

        // 镜头（呈现型；07 第 2 节，加 camera_ 前缀与输入组的 dead_zone 区分）
        public const string CameraFollowLagMs = "camera_follow_lag_ms";
        public const string CameraLookAhead = "camera_look_ahead";
        public const string CameraLookAheadLagMs = "camera_look_ahead_lag_ms";
        public const string CameraDeadZoneWidth = "camera_dead_zone_width";
        public const string CameraDeadZoneHeight = "camera_dead_zone_height";
        public const string CameraDampingXMs = "camera_damping_x_ms";
        public const string CameraDampingYMs = "camera_damping_y_ms";
        public const string CameraCombatZoomDelta = "camera_combat_zoom_delta";
        public const string CameraCombatZoomBlendMs = "camera_combat_zoom_blend_ms";
        public const string CameraImpulseGain = "camera_impulse_gain";
        public const string CameraImpulseMinIntervalMs = "camera_impulse_min_interval_ms";
        public const string CameraShakeCap = "camera_shake_cap";
        public const string CameraDistanceAttenuation = "camera_distance_attenuation";
        public const string CameraUserIntensitySetting = "camera_user_intensity_setting";

        // 特效（呈现型）
        public const string ImpactProfileRef = "impact_profile_ref";
        public const string TrailEnabled = "trail_enabled";
        public const string AfterimageEnabled = "afterimage_enabled";
        public const string TrailRef = "trail_ref";
        public const string ImpactVfxScale = "impact_vfx_scale";

        // 音频（呈现型）
        public const string SfxSwingTier = "sfx_swing_tier";
        public const string SfxWhiffTier = "sfx_whiff_tier";
        public const string SfxImpactTier = "sfx_impact_tier";
        public const string SfxSweetenerTier = "sfx_sweetener_tier";
        public const string SfxFootstepTier = "sfx_footstep_tier";
        public const string SfxMaterial = "sfx_material";
        public const string SfxMaxConcurrent = "sfx_max_concurrent";
    }

    /// <summary>
    /// 框架默认手感字段登记（<see cref="Default"/>）。字段集合取自手感设计 01～03、05、07、08 各文档的字段表；
    /// 范围是限幅与校验依据，不是推荐值（推荐值在 <c>feel.preset</c> 行里）。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <para>
    /// 1) 镜头组字段全部加 <c>camera_</c> 前缀——07 第 2 节 <c>camera_profile</c> 的 <c>dead_zone</c>（呈现型，镜头）曾与
    /// 输入组的 <c>dead_zone</c>（判定型，手柄轴死区）同名，而同一张登记表里字段名必须全局唯一（一个字段只属于一边），因此镜头一侧改名。
    /// 输入组的轴处理三字段后来按 ADR-0143 移出登记表（归设备/玩家设置），前缀沿用不改（改名是无谓的破坏性变更）。
    /// </para>
    /// <para>
    /// 2) 合成来源：05 第 3.4 节点名的字段按点名归类（加减速、转向、步幅、<c>hit_stun_ms</c>、脚步层 → 角色为主；
    /// 分相倍率、取消窗口、<c>impact_class</c>、顿帧、击退、命中/挥空层 → 武器为主；<c>action_move_speed_ratio</c>、
    /// <c>action_turn_lock</c> → 攻击期间武器临时覆盖）；点名之外的字段：输入组、<c>combo_reset_ms</c>、
    /// <c>turn_assist_deg</c>、<c>min_action_ms</c>、两个顿帧上限、<c>downed_ms</c>、<c>reaction_cap</c>、镜头组（除冲击增益）
    /// 描述的是行动者自身的操控/承受属性，归角色为主；<c>stagger_power</c>、<c>kill_hitstop_scale</c>、
    /// <c>stop_distance</c>、<c>camera_impulse_gain</c>、特效与命中/挥空/增味音效描述的是"这把武器打出去的东西"，归武器为主。
    /// </para>
    /// <para>
    /// 3) <c>impact_vfx_scale</c> 是本登记补的数值型特效字段：05 第 3.1 节特效组只列了反馈包引用与拖尾/残影开关
    /// （布尔与引用只允许 <c>set</c>），而 05 第 3.4 节要求副手武器以 add/multiply 叠加"音效增味、特效"，没有数值型特效字段
    /// 就无法表达副手的特效叠加，故补一个特效强度倍率，标 <c>offhand_stackable</c>。
    /// </para>
    /// </summary>
    public static class FeelFields
    {
        private const FeelOpSet SetMulAdd = FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add;
        private const FeelOpSet SetOnly = FeelOpSet.Set;

        private static readonly IReadOnlyList<string> ImpactClassValues = new[] { "light", "medium", "heavy", "massive" };
        private static readonly IReadOnlyList<string> AirHitReactionValues = new[] { "same", "none", "flinch", "stagger_light", "stagger", "knockback", "knockdown" };
        private static readonly IReadOnlyList<string> LaunchStackValues = new[] { "restart", "add" };
        private static readonly IReadOnlyList<string> PoiseRecoverModeValues = new[] { "delay", "out_of_combat" };
        private static readonly IReadOnlyList<string> ReactionCapValues = new[] { "none", "flinch", "stagger_light", "stagger", "knockback", "knockdown" };
        private static readonly IReadOnlyList<string> ReversePolicyValues = new[] { "instant", "through_zero" };

        /// <summary>
        /// 已登记但尚无消费方的字段（落地状态 <see cref="FeelFieldStatus.Planned"/>，手感设计/05 第 4 节、ADR-0146）。
        /// 某个字段有了生产消费方之后，把它从下表删除即回到 active；测试 <c>FeelFieldStatusTests</c> 守住"active ⇔ 有消费方引用"。
        /// </summary>
        private static readonly (string Name, string Note)[] PlannedFields =
        {
            (FeelFieldNames.SprintSpeedRatio, "没有冲刺移动模式，目标速度不读该字段"),
            (FeelFieldNames.StrideScale, "表现层尚未按实际地面速度匹配动画播放速率"),
            (FeelFieldNames.StartBlendMs, "表现层尚未实现起步过渡"),
            (FeelFieldNames.StopBlendMs, "表现层尚未实现急停过渡"),
            (FeelFieldNames.LeanDegPerAccel, "表现层尚未实现按加速度的身体倾斜"),
            (FeelFieldNames.TrailEnabled, "表现层缺省 sink 不渲染拖尾"),
            (FeelFieldNames.AfterimageEnabled, "表现层缺省 sink 不渲染残影"),
            (FeelFieldNames.TrailRef, "表现层缺省 sink 不渲染拖尾，没有读取拖尾定义的消费方"),
        };

        /// <summary>框架默认登记（不可变，登记顺序即遍历顺序）。</summary>
        public static FeelFieldSet Default { get; } = Build();

        /// <summary>游戏自有手感字段的名字前缀（手感设计/05 第 4 节）。</summary>
        public const string GameFieldPrefix = "game.";

        /// <summary>
        /// 在框架默认登记之上追加游戏自有字段（手感设计/05 第 4 节"游戏自有字段"，ADR-0146）：游戏自己的字段（如招架窗口、冲刺次数）参与八层解析、
        /// 溯源与调参面板，但框架的判定与表现消费方不读取它们（消费方是游戏自己的代码，经解析结果按名读取）。
        /// 约束：名字必须以 <see cref="GameFieldPrefix"/> 开头（与框架字段永不重名）、不得与已登记字段重名、状态恒为 active（消费方在游戏里）；
        /// 建议声明为可选字段（<c>optional: true</c>），否则框架预设与每个游戏预设都得给它取值。
        /// 要让数据里能写这些字段，把返回的登记同时交给 <c>FeelSchemas.RegisterAll</c>（数据校验）与装配选项（<c>FeelAssemblyOptions.Fields</c> 或
        /// <c>CarriersFeelOptions.Fields</c>）。
        /// </summary>
        public static FeelFieldSet Extend(IEnumerable<FeelFieldDef> gameFields)
        {
            if (gameFields == null) throw new System.ArgumentNullException(nameof(gameFields));
            var all = new List<FeelFieldDef>(Default.Fields);
            foreach (var field in gameFields)
            {
                if (field == null) throw new System.ArgumentException("游戏字段登记不能为 null", nameof(gameFields));
                if (!field.Name.StartsWith(GameFieldPrefix, System.StringComparison.Ordinal))
                {
                    throw new System.ArgumentException($"游戏自有字段必须以 \"{GameFieldPrefix}\" 开头：{field.Name}", nameof(gameFields));
                }
                if (field.Status != FeelFieldStatus.Active)
                {
                    throw new System.ArgumentException($"游戏自有字段不能标 planned：{field.Name}", nameof(gameFields));
                }
                all.Add(field);
            }

            return new FeelFieldSet(all);
        }

        private static FeelFieldDef Num(
            string name, FeelGroup group, FeelHalf half, FeelUnit unit, FeelComposition comp,
            double min, double max, string desc, FeelOpSet ops = SetMulAdd, bool optional = false, bool offhand = false,
            string? attr = null)
            => new FeelFieldDef(name, FeelFieldKind.Number, new FeelFieldMeta(half, group, ops, comp, unit, offhand),
                desc, min, max, optional: optional, attributeBackedReason: attr);

        private static FeelFieldDef IntF(
            string name, FeelGroup group, FeelHalf half, FeelUnit unit, FeelComposition comp,
            double min, double max, string desc, FeelOpSet ops = SetMulAdd, bool optional = false, bool offhand = false)
            => new FeelFieldDef(name, FeelFieldKind.Int, new FeelFieldMeta(half, group, ops, comp, unit, offhand),
                desc, min, max, optional: optional);

        private static FeelFieldDef Bool(
            string name, FeelGroup group, FeelHalf half, FeelComposition comp, string desc, bool optional = false)
            => new FeelFieldDef(name, FeelFieldKind.Bool, new FeelFieldMeta(half, group, SetOnly, comp), desc, optional: optional);

        private static FeelFieldDef Enum(
            string name, FeelGroup group, FeelHalf half, FeelComposition comp, IReadOnlyList<string> values, string desc, bool optional = false)
            => new FeelFieldDef(name, FeelFieldKind.Enum, new FeelFieldMeta(half, group, SetOnly, comp), desc, enumValues: values, optional: optional);

        private static FeelFieldDef Text(
            string name, FeelGroup group, FeelHalf half, FeelComposition comp, string desc, bool optional = false)
            => new FeelFieldDef(name, FeelFieldKind.Text, new FeelFieldMeta(half, group, SetOnly, comp), desc, optional: optional);

        private static FeelFieldDef IdF(
            string name, FeelGroup group, FeelHalf half, FeelComposition comp, string desc, string? softRef, bool optional = true)
            => new FeelFieldDef(name, FeelFieldKind.Id, new FeelFieldMeta(half, group, SetOnly, comp), desc,
                optional: optional, softReferenceTable: softRef);

        private const string SpeedAttr = "移动速度已由属性系统承载（基础移速属性），光环改速度走属性，不经手感修饰";
        private const string HasteAttr = "动作速率由急速属性承载，光环改动作快慢走属性，不经手感修饰";

        private static FeelFieldSet Build()
        {
            const FeelHalf J = FeelHalf.Judging;
            const FeelHalf P = FeelHalf.Presenting;
            const FeelComposition C = FeelComposition.CharacterPrimary;
            const FeelComposition W = FeelComposition.WeaponPrimary;
            const FeelComposition A = FeelComposition.AttackOverride;
            const FeelUnit Ms = FeelUnit.Milliseconds;
            const FeelUnit BodyH = FeelUnit.BodyHeights;
            const FeelUnit Ratio = FeelUnit.Ratio;
            const FeelUnit SpeedRatio = FeelUnit.BaseSpeedRatio;
            const FeelUnit Tier = FeelUnit.IntensityTier;
            const FeelGroup In = FeelGroup.Input;
            const FeelGroup Mv = FeelGroup.Movement;
            const FeelGroup Ac = FeelGroup.Action;
            const FeelGroup Re = FeelGroup.Reaction;
            const FeelGroup Cam = FeelGroup.Camera;
            const FeelGroup Fx = FeelGroup.Effects;
            const FeelGroup Au = FeelGroup.Audio;

            var fields = new List<FeelFieldDef>
            {
                // ---------- 输入（01 第 4 节，判定型）----------
                Num(FeelFieldNames.BufferMs, In, J, Ms, C, 0, 1000, "动作类输入的缓冲窗口；0 表示不缓冲（只在按下当 tick 有效）"),
                IntF(FeelFieldNames.BufferSlots, In, J, FeelUnit.Count, C, 1, 8, "缓冲槽位数"),
                Num(FeelFieldNames.GraceMs, In, J, Ms, C, 0, 500, "宽限窗口：前置条件刚失效后仍视为满足的时长"),
                Num(FeelFieldNames.HoldThresholdMs, In, J, Ms, C, 0, 5000, "缺省按住阈值；缺省（无值）表示动作不区分点按与按住", optional: true),

                // ---------- 移动（02 第 2 节，判定型）----------
                Num(FeelFieldNames.AccelMs, Mv, J, Ms, C, 0, 3000, "从静止达到目标速度的时间；0 即瞬时达速"),
                Num(FeelFieldNames.DecelMs, Mv, J, Ms, C, 0, 3000, "从目标速度到停止的时间；0 即瞬时停止"),
                Text(FeelFieldNames.AccelCurve, Mv, J, C, "加速曲线引用：linear 或曲线形态登记的曲线 id"),
                Text(FeelFieldNames.BrakeCurve, Mv, J, C, "制动曲线引用：linear 或曲线形态登记的曲线 id"),
                Enum(FeelFieldNames.ReversePolicy, Mv, J, C, ReversePolicyValues, "反向输入：instant 立即反向（保留速率），through_zero 先减速到零再加速"),
                Num(FeelFieldNames.TurnRateDegS, Mv, J, FeelUnit.DegreesPerSecond, C, 0, 7200, "朝向转向速率（度/秒）；0 即瞬时转向"),
                Num(FeelFieldNames.WalkSpeedRatio, Mv, J, SpeedRatio, C, 0.05, 2, "walk 模式的目标速度相对基础移速的倍数", attr: SpeedAttr),
                Num(FeelFieldNames.SprintSpeedRatio, Mv, J, SpeedRatio, C, 1, 4, "冲刺（可选步态）目标速度相对基础移速的倍数；缺省（无值）表示无冲刺", optional: true, attr: SpeedAttr),
                Num(FeelFieldNames.ActionMoveSpeedRatio, Mv, J, SpeedRatio, A, 0, 2, "动作进行中允许的移动速度倍率（0 即定身）；攻击期间武器临时覆盖，动作结束自动撤回"),
                Bool(FeelFieldNames.ActionTurnLock, Mv, J, A, "动作进行中锁朝向；攻击期间武器临时覆盖，动作结束自动撤回"),
                Bool(FeelFieldNames.KeepMomentumOnActionEnd, Mv, J, C, "动作结束时保留残余速度（真）还是清零（假）"),
                Bool(FeelFieldNames.KeepMomentumOnMotionEnd, Mv, J, C,
                    "动作位移窗口（motion_end）结束时保留末速度并按 decel_ms 滑行（真）还是立即清零、位移距离等于声明值（假）；缺省（无值）视为假", optional: true),
                Bool(FeelFieldNames.ArrivalDecel, Mv, J, C, "路径跟随到达终点前按 decel_ms 减速"),
                Bool(FeelFieldNames.WallSlide, Mv, J, C, "位移被阻挡截断时沿墙滑动（真），而不是整体停下（假）"),
                Bool(FeelFieldNames.ApplyToPathFollowing, Mv, J, C, "运动档案是否也作用于目标类移动（AI/点击移动）；假则目标类移动保持瞬时达速"),
                IdF(FeelFieldNames.KnockbackResistanceStat, Mv, J, C, "读哪个属性作为击退抗性（0～1）；缺省（无值）视为 0", "stat.definition"),
                Num(FeelFieldNames.UnitBodyRadius, Mv, J, BodyH, C, 0, 2,
                    "单位体积半径（身高倍数）：声明（大于 0）即参与单位间体积阻挡，两个单位中心距不小于半径之和；缺省（无值）= 无体积，不阻挡也不被阻挡",
                    ops: SetOnly, optional: true),
                Bool(FeelFieldNames.DodgeThroughUnits, Mv, J, C,
                    "动作位移是否穿过其他单位的体积的总开关（真；地形仍阻挡）；具体哪些位移种类穿过由 pass_through_motion_kinds 决定（缺省 dash、step_back）；缺省（无值）视为假，即动作位移也被体积阻挡；只在声明了 unit_body_radius 时有意义",
                    optional: true),
                Text(FeelFieldNames.PassThroughMotionKinds, Mv, J, C,
                    "dodge_through_units 为真时穿过体积的动作位移种类，逗号分隔，取值 lunge|dash|step_back|charge；缺省（无值）= dash,step_back（与此前写死的行为一致）",
                    optional: true),
                Num(FeelFieldNames.UnitSeparationSpeedRatio, Mv, J, SpeedRatio, C, 0, 4,
                    "重叠分离速率（基础移速倍数）：本单位与别的有体积单位重叠时每 tick 被推开的速率上限 = 倍数 × 移动速度属性，0 表示本单位不被推开；缺省（无值）取 0.5；只在声明了 unit_body_radius 时有意义",
                    ops: SetOnly, optional: true),
                Bool(FeelFieldNames.PathAvoidUnits, Mv, J, C,
                    "路径跟随与追击遇到别的单位的体积时局部绕行（真）还是撞到即停（假，开 wall_slide 时沿切向滑一段）；缺省（无值）视为真；只在声明了 unit_body_radius 时有意义",
                    optional: true),
                Bool(FeelFieldNames.ForcedPushUnits, Mv, J, C,
                    "受控位移（击退）被别的单位体积挡住时，把剩余位移按 forced_push_ratio 转移给被撞单位（真）；缺省（无值）视为假：被挡即停、不推人；只在声明了 unit_body_radius 时有意义",
                    optional: true),
                Num(FeelFieldNames.ForcedPushRatio, Mv, J, Ratio, C, 0, 1,
                    "forced_push_units 为真时的转移比例：被撞单位获得的位移 = 撞停时剩余位移 × 比例 ×（1 − 被撞单位的击退抗性）；缺省（无值）取 0.5",
                    ops: SetOnly, optional: true),

                // ---------- 移动（02 第 7 节，呈现型）----------
                Num(FeelFieldNames.IdleMaxRatio, Mv, P, SpeedRatio, C, 0, 0.5, "步态 idle 上界：速度/基础移速低于它为 idle"),
                Num(FeelFieldNames.WalkMaxRatio, Mv, P, SpeedRatio, C, 0.1, 2, "步态 walk 上界：低于它为 walk，其余为 run"),
                Num(FeelFieldNames.SprintMinRatio, Mv, P, SpeedRatio, C, 1, 4, "步态 sprint 下界（声明了冲刺时）；缺省（无值）表示无 sprint 步态", optional: true),
                Num(FeelFieldNames.GaitHysteresisRatio, Mv, P, SpeedRatio, C, 0, 0.5, "步态阈值滞回，避免在阈值附近抖动"),
                Num(FeelFieldNames.StrideScale, Mv, P, Ratio, C, 0.1, 4, "步幅缩放：动画播放速率匹配实际地面速度时的倍率"),
                Num(FeelFieldNames.LandHoldMs, Mv, P, Ms, C, 0, 2000,
                    "落地姿势保持时长（手感落地 M4-W1b）：单位落地后空中阶段的落地相（jump.land）保持多久；缺省（无值）取呈现层默认 8 个 tick，0 = 不播落地姿势", optional: true),
                Num(FeelFieldNames.StartBlendMs, Mv, P, Ms, C, 0, 1000, "起步混合时长"),
                Num(FeelFieldNames.StopBlendMs, Mv, P, Ms, C, 0, 1000, "急停混合时长"),
                Num(FeelFieldNames.LeanDegPerAccel, Mv, P, FeelUnit.Degrees, C, 0, 45, "身体倾斜：每单位加速度对应的倾斜角度上限（度）"),

                // ---------- 动作（01 第 4 节，判定型）----------
                Num(FeelFieldNames.PhaseScaleStartup, Ac, J, Ratio, W, 0.1, 10, "时间线前摇倍率", attr: HasteAttr),
                Num(FeelFieldNames.PhaseScaleActive, Ac, J, Ratio, W, 0.1, 10, "时间线判定相倍率", attr: HasteAttr),
                Num(FeelFieldNames.PhaseScaleRecovery, Ac, J, Ratio, W, 0.1, 10, "时间线后摇倍率", attr: HasteAttr),
                Num(FeelFieldNames.CancelWindowScale, Ac, J, Ratio, W, 0, 10, "取消窗口长度倍率"),
                Num(FeelFieldNames.ComboWindowScale, Ac, J, Ratio, W, 0, 10, "连招窗口倍率"),
                Num(FeelFieldNames.ComboResetMs, Ac, J, Ms, C, 0, 10000, "连招链重置：进入待机超过该时长后重置"),
                Num(FeelFieldNames.TurnAssistDeg, Ac, J, FeelUnit.Degrees, C, 0, 180, "接受动作时朝向对齐的最大转角（度）"),
                Num(FeelFieldNames.MinActionMs, Ac, J, Ms, C, 0, 2000, "速率重映射的动作时长下限"),
                Num(FeelFieldNames.StopDistance, Ac, J, BodyH, W, 0, 5, "冲向目标类位移到达目标身前的停止距离"),

                // ---------- 受击（03 第 3/4 节，判定型）----------
                Enum(FeelFieldNames.ImpactClass, Re, J, W, ImpactClassValues, "冲击等级，反馈包与受击裁决的公共输入"),
                Num(FeelFieldNames.AttackerHitstopMs, Re, J, Ms, W, 0, 500, "攻击方顿帧时长"),
                Num(FeelFieldNames.TargetHitstopMs, Re, J, Ms, W, 0, 500, "受击方顿帧时长"),
                Num(FeelFieldNames.HitstopCapMs, Re, J, Ms, C, 0, 1000, "受击方顿帧上限（嵌套取大后限幅）"),
                Num(FeelFieldNames.AttackerHitstopCapMs, Re, J, Ms, C, 0, 1000, "攻击方顿帧上限（群体命中取最大后限幅）"),
                Num(FeelFieldNames.HitStunMs, Re, J, Ms, C, 0, 3000, "硬直时长（受击方体型为主）"),
                Num(FeelFieldNames.StaggerPower, Re, J, FeelUnit.None, W, 0, 1000, "硬直强度：与目标韧性比较，不高于韧性时只播受击动画不打断"),
                Num(FeelFieldNames.PoiseDamage, Re, J, FeelUnit.None, W, 0, 1000,
                    "韧性伤害（手感落地 M4-L，动态韧性）：命中从目标当前韧性池里扣掉的量；缺省（无值）表示该攻击不走动态韧性，沿用静态规则" +
                    "（stagger_power 与目标韧性属性比较）", optional: true),
                Num(FeelFieldNames.PoiseRecoverPerS, Re, J, FeelUnit.None, C, 0, 1000,
                    "韧性回复速率（每秒，目标侧）：被动态韧性命中过的目标在 poise_recover_delay_ms 内没再受动态韧性伤害后，每秒回复这么多韧性直到满；缺省（无值）表示不回复", optional: true),
                Num(FeelFieldNames.PoiseRecoverDelayMs, Re, J, Ms, C, 0, 10000,
                    "韧性回复延迟（目标侧）：最近一次动态韧性伤害之后等待多久才开始回复；缺省（无值）按 0", optional: true),
                Enum(FeelFieldNames.PoiseRecoverMode, Re, J, C, PoiseRecoverModeValues,
                    "韧性回复模式（手感落地 M4-W3，目标侧）：delay（缺省）= 最近一次动态韧性伤害后等 poise_recover_delay_ms 再回复；" +
                    "out_of_combat = 目标处于战斗中时回复与延迟计时都暂停，脱战后才开始延迟计时并回复（脱战判定取战斗宿主的进出战状态）；缺省（无值）= delay",
                    optional: true),
                Num(FeelFieldNames.PoiseBreakResetMs, Re, J, Ms, C, 0, 60000,
                    "破韧后自动回满延迟（手感落地 M4-W3，目标侧）：韧性被打到 0（破韧）之后过这么久把韧性池一次回满，不受回复速率、回复模式与期间再受击影响；缺省（无值）= 不自动回满", optional: true),
                Num(FeelFieldNames.KnockbackDistance, Re, J, BodyH, W, 0, 5, "击退距离"),
                Num(FeelFieldNames.LaunchHeight, Re, J, BodyH, W, 0, 10,
                    "击飞高度：knockback/knockdown 反应把目标抛起的顶点高度（目标脚下再升高多少）；只在世界有竖直轴（体积空间 / 横版二维能力包）时生效，" +
                    "平面世界忽略；缺省（无值）表示不击飞", optional: true),
                Num(FeelFieldNames.LaunchStackCap, Re, J, BodyH, W, 0, 20,
                    "击飞叠加上限：launch_stack=add 时叠加后的向上初速不超过升到该顶点高度所需的初速（目标脚下再升高多少）；缺省（无值）表示不设上限", optional: true),
                Enum(FeelFieldNames.LaunchStack, Re, J, C, LaunchStackValues,
                    "击飞叠加方式（攻击方档案）：restart 重新抛起（缺省，不叠加）；add 在已腾空的目标上把本次初速叠加到当前竖直速度；只在世界有竖直轴时生效",
                    optional: true),
                Enum(FeelFieldNames.AirHitReaction, Re, J, C, AirHitReactionValues,
                    "腾空受击反应（受击方档案）：目标在空中被命中时，把（韧性与冲击等级映射得出的）反应替换为该值，之后仍受 reaction_cap 限制；" +
                    "same 或缺省（无值）= 与地面受击一致；死亡与霸体不受影响；只在世界有竖直轴时生效", optional: true),
                Num(FeelFieldNames.LaunchHeightCap, Re, J, BodyH, C, 0, 20,
                    "击飞绝对高度上限（手感落地 M4-W1b，攻击方与受击方档案都可声明，取较小者）：击飞（含叠加）之后脚下高度的最高点不超过该值（身高倍数，标定后是世界单位；" +
                    "与 launch_stack_cap 的区别：后者封叠加后的初速，本字段封世界高度，二者可同时声明）；缺省（无值）表示不设绝对上限", optional: true),
                Num(FeelFieldNames.LaunchBodyScale, Re, J, Ratio, C, 0, 10,
                    "击飞体型缩放（手感落地 M4-W1b，受击方档案，体型原型层可写）：击飞顶点 × 本字段；缺省（无值）= 1 即不缩放，0 = 不可被击飞", optional: true),
                Num(FeelFieldNames.HurtRadiusScale, Re, J, Ratio, C, 0, 4,
                    "受击半径缩放（手感落地 M5-S2a，受击方档案，体型原型层可写）：目标命中半径 = unit_body_radius（标定后世界单位）× 本字段，命中形状与该圆相交即算命中；缺省（无值）= 1；" +
                    "没有声明 unit_body_radius 的单位半径恒为 0（按点判定）；只在装配启用 HitRadiusFromFeel 时生效", ops: SetOnly, optional: true),
                Enum(FeelFieldNames.AirReactionCap, Re, J, C, ReactionCapValues,
                    "空中受击反应上限（手感落地 M4-W1b，受击方档案）：目标在空中被命中时，反应在 reaction_cap 之外再受它限制（取两者较低）；缺省（无值）表示空中不另设上限",
                    optional: true),
                Bool(FeelFieldNames.AirStunUntilLand, Re, J, C,
                    "空中硬直持续到落地（手感落地 M4-W1b，受击方档案）：真时，硬直类反应的时长到点后若目标仍在空中，则硬直保持到落地那一刻才结束；缺省（无值）视为假（硬直按时长结束，与是否在空中无关）",
                    optional: true),
                Num(FeelFieldNames.DownedMs, Re, J, Ms, C, 0, 10000, "倒地时长"),
                Enum(FeelFieldNames.ReactionCap, Re, J, C, ReactionCapValues, "受击反应上限（none 最低、knockdown 即不封顶）"),
                Num(FeelFieldNames.KillHitstopScale, Re, J, Ratio, W, 1, 5, "击杀时顿帧放大倍数"),

                // ---------- 镜头（07 第 2 节，呈现型）----------
                Num(FeelFieldNames.CameraFollowLagMs, Cam, P, Ms, C, 0, 2000, "镜头跟随滞后"),
                Num(FeelFieldNames.CameraLookAhead, Cam, P, BodyH, C, 0, 10, "沿速度方向的前瞻距离"),
                Num(FeelFieldNames.CameraLookAheadLagMs, Cam, P, Ms, C, 0, 2000, "前瞻点自身的滞后，避免反转时甩动"),
                Num(FeelFieldNames.CameraDeadZoneWidth, Cam, P, BodyH, C, 0, 10, "死区宽：目标在死区内镜头不动"),
                Num(FeelFieldNames.CameraDeadZoneHeight, Cam, P, BodyH, C, 0, 10, "死区高"),
                Num(FeelFieldNames.CameraDampingXMs, Cam, P, Ms, C, 0, 2000, "水平轴阻尼"),
                Num(FeelFieldNames.CameraDampingYMs, Cam, P, Ms, C, 0, 2000, "垂直轴阻尼"),
                Num(FeelFieldNames.CameraCombatZoomDelta, Cam, P, Ratio, C, 0.25, 4, "进入战斗的缩放变化倍率"),
                Num(FeelFieldNames.CameraCombatZoomBlendMs, Cam, P, Ms, C, 0, 3000, "进出战斗缩放的过渡时长"),
                Num(FeelFieldNames.CameraImpulseGain, Cam, P, FeelUnit.ScreenHeightRatio, W, 0, 0.2, "镜头冲击基准幅度（画面高度比例）"),
                Num(FeelFieldNames.CameraImpulseMinIntervalMs, Cam, P, Ms, C, 0, 2000, "镜头冲击合并的最小间隔"),
                Num(FeelFieldNames.CameraShakeCap, Cam, P, FeelUnit.ScreenHeightRatio, C, 0, 0.5, "震屏与冲击叠加后的上限（画面高度比例）"),
                Text(FeelFieldNames.CameraDistanceAttenuation, Cam, P, C, "命中点到跟随目标的距离衰减曲线引用：linear 或曲线 id"),
                Text(FeelFieldNames.CameraUserIntensitySetting, Cam, P, C, "玩家可调整体强度开关的设置项引用", optional: true),

                // ---------- 特效（05 第 3.1 节、08 第 4 节，呈现型）----------
                IdF(FeelFieldNames.ImpactProfileRef, Fx, P, W, "打击反馈包引用（feedback.impact_profile），再按 impact_class 与命中结局选变体；缺省（无值）表示无反馈包", "feedback.impact_profile"),
                Bool(FeelFieldNames.TrailEnabled, Fx, P, W, "拖尾开关"),
                Bool(FeelFieldNames.AfterimageEnabled, Fx, P, W, "残影开关"),
                IdF(FeelFieldNames.TrailRef, Fx, P, W, "武器拖尾定义的 id（可选；目标表由表现层登记，此处不做存在性检查）", softRef: null),
                Num(FeelFieldNames.ImpactVfxScale, Fx, P, Ratio, W, 0, 4, "命中特效强度倍率；副手武器可叠加", offhand: true),

                // ---------- 音频（07 第 3 节，呈现型）----------
                IntF(FeelFieldNames.SfxSwingTier, Au, P, Tier, W, 0, 5, "挥动层强度档"),
                IntF(FeelFieldNames.SfxWhiffTier, Au, P, Tier, W, 0, 5, "挥空层强度档"),
                IntF(FeelFieldNames.SfxImpactTier, Au, P, Tier, W, 0, 5, "命中层强度档"),
                IntF(FeelFieldNames.SfxSweetenerTier, Au, P, Tier, W, 0, 5, "增味层强度档；副手武器可叠加", offhand: true),
                IntF(FeelFieldNames.SfxFootstepTier, Au, P, Tier, C, 0, 5, "脚步层强度档（材质由地图区域标签决定）"),
                Text(FeelFieldNames.SfxMaterial, Au, P, W, "武器音效材质标签（swing/impact 层映射到 sfx 表行），缺省 generic"),
                IntF(FeelFieldNames.SfxMaxConcurrent, Au, P, FeelUnit.Count, C, 1, 32, "同时发声上限"),
            };

            ApplyPlannedStatus(fields);
            return new FeelFieldSet(fields);
        }

        private static void ApplyPlannedStatus(List<FeelFieldDef> fields)
        {
            for (var p = 0; p < PlannedFields.Length; p++)
            {
                var index = fields.FindIndex(f => f.Name == PlannedFields[p].Name);
                if (index < 0) throw new System.InvalidOperationException("planned 字段未登记：" + PlannedFields[p].Name);
                fields[index] = fields[index].WithStatus(FeelFieldStatus.Planned, PlannedFields[p].Note);
            }
        }
    }
}

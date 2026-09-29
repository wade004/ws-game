#nullable enable
// AnimClipResolver：动画状态 -> 具体剪辑 id 的查表落地（W3b 收边，拍板 6"09 §4 动画层……程序动画
// 原语……以及命中帧同步 sprite 侧"配套的"状态机驱动到具体剪辑"这一半，见
// presentation/render/contracts/ICharacterRig.cs 类型注释"判断记录（动画状态机驱动职责在本接口的
// 落地形状）"——AnimStateMachine 只回答"当前该处于哪个 AnimState"，"该状态该播哪个具体剪辑"这一
// 查表逻辑显式留给"持有 WeaponStyleResolver 的组装层代码"，本类型就是这一层组装代码的落地。
//
// W6-B 收口（ADR-0017 决策 c/e）：
//   1) 不再自造 AnimSetRecordParser 解析 display.anim_set.clips——改为直接消费
//      Core.Foundation.DisplayInfo.AnimSetDef.FromRecord（W6-A 新增的统一解析结果类型，同时携带
//      resource_ref 与 events，取代本文件此前"显式跳过 events"的临时简化）。UnityViewFactory 现在
//      直接调用该静态方法解析，不再经由本文件的 AnimSetRecordParser 中转，本文件因此删除该类型。
//   2) 武器风格来源改用 Presentation.VfxSfx.Contracts.IWeaponStyleSource（实体 -> weaponStyleRef，
//      见该接口类型注释），取代此前的 weaponStyleRefForEntity 委托参数——语义完全等价，只是改成
//      正式契约类型，便于与 EquipmentWeaponStyleSource 直接对接，不需要装配代码额外包一层委托。
//   3) 订阅从 AnimStateMachine.StateChanged 改为 AnimStateMachine.StateChangedWithSkill（W6-A 新增，
//      携带触发这次切换的技能 id）：Cast 状态现在可以按 WeaponStyleDef.CastAnimOverride 做按技能 id
//      覆盖（见该字段类型注释），根治此前文件顶部"判断记录（cast 状态不做按技能覆盖）"记录的已知
//      简化——AutoAttackAnim（Attack 状态）不受影响，逻辑不变。
//
// ADR-0111（消费方反馈第六十一批）：战斗姿态变体。AnimStateMachine 为每个实体记一个与 AnimState 正交的
// "是否处于战斗姿态"布尔值（AnimStateMachine.IsInCombatStance），本类型解析剪辑时按它选剪辑键变体：
// 战斗姿态下状态 <key> 的默认剪辑先查 combat_<key>，没有就回落 <key>（前缀见
// Core.Foundation.DisplayInfo.AnimSetDef.CombatClipKeyPrefix）；优先级 武器风格/技能覆盖剪辑 > 变体键 >
// 普通键。姿态变化（AnimStateMachine.CombatStanceChanged）时对运动态（Idle/Move）重新解析，与"上一次
// 实际播放的剪辑"不同才切换、从头播；相同（该状态没有变体键）什么都不做，不重播。瞬态状态
// （Attack/Cast/Hit/Jump）期间姿态变化不打断，回落到运动态时按当时姿态解析；Death 终态不受影响。
//
// 已知限制（ADR-0111）：
//   1) 变体剪辑内容永远不会到位（声明了 combat_<key> 却没有对应美术）时，一直维持当前显示，没有超时回落、
//      没有诊断——与"缺表现资源不阻断游戏"的既有宽容口径一致，美术缺失由数据/资源校验负责发现。
//   2) 武器风格/技能覆盖剪辑（AutoAttackAnim/CastAnimOverride）优先级高于变体键，原样播放，不感知战斗
//      姿态；要战斗/非战斗两套请拆成两个武器风格或不用覆盖、改用 combat_attack/combat_cast 变体键。
//   3) jump 不在默认登记的六个状态键里（既有行为，非本次引入），combat_jump 只有在外形声明了它且外部
//      登记了对应剪辑时才可能被解析到；默认装配下 Jump 状态没有默认剪辑表项，本类型对它什么都不播。
//   4) 战斗中移动只查 combat_move，没有就回落普通 move（不会借用 combat_idle）；姿态变化不重播正在播的
//      同一剪辑，因此没有任何 combat_* 键的外形进出战与 1.89.0 逐帧一致。
//   5) model 型（没有序列帧播放器）没有异步内容登记这一步，变体剪辑恒视为就绪，切换即时生效；
//      由 rig 播放器按剪辑名现查现用，找不到该名字的剪辑时的表现由 rig 决定，本类型不介入。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;

namespace Adapter.Unity.Presentation
{
    /// <summary>见文件顶部类型注释。订阅 <see cref="AnimStateMachine.StateChangedWithSkill"/>，按
    /// 状态名查默认剪辑表，Attack 状态额外尝试武器风格覆盖（<see cref="WeaponStyleDef.AutoAttackAnim"/>），
    /// Cast 状态额外按触发技能 id 尝试武器风格覆盖（<see cref="WeaponStyleDef.CastAnimOverride"/>），
    /// 解出 clipId 后调用注入的 <paramref name="playClip"/> 委托——不直接依赖
    /// <see cref="ICharacterRig.PlayClip"/>（见 <c>UnitySpriteView.PlayClip</c> 判断记录"rig.PlayClip
    /// 结构性失效"），调用方通常传入 <c>UnitySpriteView.PlayClip</c>、<c>UnityFrameAnimPlayer.Play</c>
    /// 或（model 型）<c>IRenderer3D.PlayAnim</c> 的包装委托。</summary>
    public sealed class AnimClipResolver : IDisposable
    {
        /// <summary>Idle/Move 循环播放，其余（Attack/Cast/Hit/Death/Jump）播放一次。</summary>
        public static bool DefaultLoop(AnimState state) => state == AnimState.Idle || state == AnimState.Move;

        /// <summary>ADR-0111：<paramref name="state"/> 的战斗姿态变体剪辑键（<c>combat_&lt;StateKey&gt;</c>），
        /// 前缀取自 <see cref="AnimSetDef.CombatClipKeyPrefix"/>（全仓库唯一定义处）。</summary>
        public static string CombatStateKey(AnimState state) => AnimSetDef.CombatClipKey(StateKey(state));

        public static string StateKey(AnimState state) => state switch
        {
            AnimState.Idle => "idle",
            AnimState.Move => "move",
            AnimState.Attack => "attack",
            AnimState.Cast => "cast",
            AnimState.Hit => "hit",
            AnimState.Death => "death",
            AnimState.Jump => "jump",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知 AnimState"),
        };

        private readonly AnimStateMachine _stateMachine;
        private readonly Func<Id, IReadOnlyDictionary<string, Id>?> _defaultClipsForEntity;
        private readonly Action<Id, Id, bool, double> _playClip;
        private readonly IReadOnlyDictionary<Id, WeaponStyleDef>? _weaponStyles;
        private readonly IWeaponStyleSource? _weaponStyleSource;
        private readonly Func<Id, Id, bool>? _isClipReady;

        // ADR-0111：每个实体最近一次经本类型实际播放的剪辑 id——姿态变化时用它判断"新姿态解析出的剪辑与
        // 正在播的是否相同"（相同则不重播）。实体销毁/重生时由 Forget 清理。
        private readonly Dictionary<Id, Id> _lastPlayedClip = new Dictionary<Id, Id>();

        /// <param name="stateMachine">状态来源。</param>
        /// <param name="defaultClipsForEntity">实体 id -> 该实体的"剪辑名 -> clipId"表（sprite 型通常
        /// 来自 <c>UnityViewFactory.RegisterDefaultClips</c>，model 型来自
        /// <c>UnityViewFactory.RegisterDefaultModelClips</c>，两者都基于
        /// <see cref="Core.Foundation.DisplayInfo.AnimSetDef"/>）；查不到该实体或表内查不到当前
        /// 状态名时静默跳过本次切换（不播放任何剪辑），不抛异常——同 09 第 1 节表现层"缺表现资源不
        /// 阻断游戏"的一贯宽容策略。</param>
        /// <param name="playClip">解出 clipId 后的落地委托：(entityId, clipId, loop, speed)。</param>
        /// <param name="weaponStyleSource">可选：实体 -> weaponStyleRef 查询（W6-B 新增，取代此前的
        /// <c>weaponStyleRefForEntity</c> 委托参数）；与 <paramref name="weaponStyles"/> 都非空时，
        /// Attack 状态优先尝试 <see cref="WeaponStyleDef.AutoAttackAnim"/>、Cast 状态优先按触发技能 id
        /// 尝试 <see cref="WeaponStyleDef.CastAnimOverride"/> 覆盖默认剪辑表。</param>
        /// <param name="weaponStyles">可选：武器风格目录（<c>display.weapon_style</c>）。</param>
        public AnimClipResolver(
            AnimStateMachine stateMachine,
            Func<Id, IReadOnlyDictionary<string, Id>?> defaultClipsForEntity,
            Action<Id, Id, bool, double> playClip,
            IWeaponStyleSource? weaponStyleSource = null,
            IReadOnlyDictionary<Id, WeaponStyleDef>? weaponStyles = null)
            : this(stateMachine, defaultClipsForEntity, playClip, weaponStyleSource, weaponStyles, null)
        {
        }

        /// <summary>
        /// ADR-0111：带"剪辑就绪探针"的构造重载（旧构造保持原签名转调本重载，<paramref name="isClipReady"/>
        /// 为 null）。<paramref name="isClipReady"/>(entityId, clipId) 回答"该剪辑的内容此刻是否已经可以
        /// 真正播放"——只对<b>战斗姿态变体剪辑</b>咨询：变体剪辑内容还没加载完（冷加载）时本类型不切过去，
        /// 维持当前显示（状态切换时退回普通键剪辑；姿态切换时什么都不做），内容就绪后由调用方调用
        /// <see cref="Refresh"/> 补切——冷加载与热路径走同一出口、同一规则。为 null 时视为一律就绪。
        /// </summary>
        public AnimClipResolver(
            AnimStateMachine stateMachine,
            Func<Id, IReadOnlyDictionary<string, Id>?> defaultClipsForEntity,
            Action<Id, Id, bool, double> playClip,
            IWeaponStyleSource? weaponStyleSource,
            IReadOnlyDictionary<Id, WeaponStyleDef>? weaponStyles,
            Func<Id, Id, bool>? isClipReady)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _defaultClipsForEntity = defaultClipsForEntity ?? throw new ArgumentNullException(nameof(defaultClipsForEntity));
            _playClip = playClip ?? throw new ArgumentNullException(nameof(playClip));
            _weaponStyleSource = weaponStyleSource;
            _weaponStyles = weaponStyles;
            _isClipReady = isClipReady;

            _stateMachine.StateChangedWithSkill += OnStateChangedWithSkill;
            // H5b 根治（游戏侧复核发现 2）：AnimStateMachine.StateRetriggered（同状态重入，见其类型
            // 判断记录）与 StateChangedWithSkill（状态真的切换了）需要同一套"解析剪辑并播放"逻辑
            // ——两者唯一的区别只是"这次调用是不是状态数值真的变了"，播放器本身对"再调用一次
            // playClip"的语义就是"从头重播"，本类型因此把两个事件处理方法收敛到同一个
            // PlayResolvedClip，不重复实现一遍查表逻辑。
            _stateMachine.StateRetriggered += OnStateRetriggered;
            _stateMachine.CombatStanceChanged += OnCombatStanceChanged;
        }

        private void OnStateChangedWithSkill(Id entityId, AnimState from, AnimState to, Id? triggerSkillId) =>
            PlayResolvedClip(entityId, to, triggerSkillId);

        private void OnStateRetriggered(Id entityId, AnimState state, Id? triggerSkillId) =>
            PlayResolvedClip(entityId, state, triggerSkillId);

        private void OnCombatStanceChanged(Id entityId, bool inCombat) => Refresh(entityId);

        /// <summary>
        /// ADR-0111：按实体<b>当前</b>状态与战斗姿态重新解析运动态（Idle/Move）剪辑，与该实体最近一次
        /// 实际播放的剪辑不同才切换（从头播）；相同什么都不做（不重播、不打断时间轴）。实体正处于瞬态
        /// 状态（Attack/Cast/Hit/Jump）或 Death 终态时什么都不做——瞬态不被打断，其回落时的
        /// <c>StateChanged</c> 会按当时姿态解析。姿态变化事件、以及变体剪辑内容加载完成后（冷加载补切）
        /// 都走本方法，同一出口。本类型从未为该实体播放过任何剪辑时，基线取"不考虑变体的普通键剪辑"
        /// （即视图当前静态显示对应的那一条），因此没有任何变体键的外形永远不会被本方法额外播放。
        /// </summary>
        public void Refresh(Id entityId)
        {
            var state = _stateMachine.GetState(entityId);
            if (state != AnimState.Idle && state != AnimState.Move)
            {
                return;
            }

            var table = _defaultClipsForEntity(entityId);
            if (table == null)
            {
                return;
            }

            var desired = ResolveDefaultClip(entityId, state, table, allowVariant: true);
            if (!desired.HasValue)
            {
                return;
            }

            Id baseline;
            if (_lastPlayedClip.TryGetValue(entityId, out var lastPlayed))
            {
                baseline = lastPlayed;
            }
            else
            {
                var baseClip = ResolveDefaultClip(entityId, state, table, allowVariant: false);
                if (!baseClip.HasValue)
                {
                    return;
                }
                baseline = baseClip.Value;
            }

            if (desired.Value.Equals(baseline))
            {
                return;
            }

            PlayAndRecord(entityId, desired.Value, DefaultLoop(state));
        }

        /// <summary>ADR-0111：释放对该实体的"最近播放剪辑"记账（实体销毁/重生时由调用方清理，同
        /// <see cref="AnimStateMachine.Forget"/> 配套）。</summary>
        public void Forget(Id entityId) => _lastPlayedClip.Remove(entityId);

        private void PlayResolvedClip(Id entityId, AnimState to, Id? triggerSkillId)
        {
            Id? clipId = null;

            if (_weaponStyles != null && _weaponStyleSource != null)
            {
                var weaponStyleRef = _weaponStyleSource.GetWeaponStyleRef(entityId);
                if (weaponStyleRef.HasValue && _weaponStyles.TryGetValue(weaponStyleRef.Value, out var def))
                {
                    if (to == AnimState.Attack)
                    {
                        clipId = def.AutoAttackAnim;
                    }
                    else if (to == AnimState.Cast && triggerSkillId.HasValue
                        && def.CastAnimOverride.TryGetValue(triggerSkillId.Value, out var castOverride))
                    {
                        clipId = castOverride;
                    }
                }
            }

            if (clipId == null)
            {
                var table = _defaultClipsForEntity(entityId);
                if (table != null)
                {
                    clipId = ResolveDefaultClip(entityId, to, table, allowVariant: true);
                }
            }

            if (clipId.HasValue)
            {
                PlayAndRecord(entityId, clipId.Value, DefaultLoop(to));
            }
        }

        /// <summary>ADR-0111：默认剪辑表解析——战斗姿态下先查变体键（且变体剪辑内容已就绪，见构造重载
        /// 参数注释），查不到/未就绪回落普通键。武器风格/技能覆盖不经本方法（优先级更高，见
        /// <see cref="PlayResolvedClip"/>）。</summary>
        private Id? ResolveDefaultClip(Id entityId, AnimState state, IReadOnlyDictionary<string, Id> table, bool allowVariant)
        {
            if (allowVariant && _stateMachine.IsInCombatStance(entityId)
                && table.TryGetValue(CombatStateKey(state), out var variant)
                && (_isClipReady == null
                    // 已经在播这条变体：重探测（换向/换装）期间就绪探针可能暂时为 false，不能因此把正在
                    // 播的变体踢回普通键（会闪一下 idle）——已经在播就视为就绪。
                    || (_lastPlayedClip.TryGetValue(entityId, out var playing) && playing.Equals(variant))
                    || _isClipReady(entityId, variant)))
            {
                return variant;
            }

            return table.TryGetValue(StateKey(state), out var fallback) ? fallback : (Id?)null;
        }

        private void PlayAndRecord(Id entityId, Id clipId, bool loop)
        {
            _lastPlayedClip[entityId] = clipId;
            _playClip(entityId, clipId, loop, 1.0);
        }

        public void Dispose()
        {
            _stateMachine.StateChangedWithSkill -= OnStateChangedWithSkill;
            _stateMachine.StateRetriggered -= OnStateRetriggered;
            _stateMachine.CombatStanceChanged -= OnCombatStanceChanged;
        }
    }
}

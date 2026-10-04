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
// 边界与设计决定（ADR-0111）：
//   1) 变体剪辑内容永远不会到位（声明了 combat_<key> 却没有对应美术）时，一直维持当前显示，没有超时回落、没有诊断
//      （设计决定）：与"缺表现资源不阻断游戏"的既有宽容口径一致——变体加载失败同样结束探测、不会无限等待，
//      美术缺失由数据/资源校验负责发现，不在运行期每次进出战刷诊断。
//   2) 武器风格/技能覆盖剪辑（AutoAttackAnim/CastAnimOverride）优先级高于变体键，原样播放，不感知战斗姿态
//      （设计决定）：覆盖剪辑是作者显式指定的"这一下就播这个"，姿态变体不应反过来改写它；要战斗/非战斗两套请拆成
//      两个武器风格或不用覆盖、改用 combat_attack/combat_cast 变体键。
//   3) 无后缀的 jump 键：外形声明了它就进默认键表（NF2 补齐，见 UnityViewFactory.AnimStateKeysFor）；combat_jump
//      只有外形声明了才登记。没声明 jump 的外形 Jump 状态没有默认剪辑，本类型对它什么都不播。
//   4) 战斗中移动只查 combat_move，没有就回落普通 move，不会借用 combat_idle（设计决定）：待机剪辑表达"站着"，借给
//      移动会让角色在移动时原地踏空；姿态变化不重播正在播的同一剪辑，因此没有任何 combat_* 键的外形进出战
//      与 1.89.0 逐帧一致。
//   5) model 型（没有序列帧播放器）没有异步内容登记这一步，变体剪辑恒视为就绪，切换即时生效（设计决定）：
//      由 rig 播放器按剪辑名现查现用，找不到该名字的剪辑时的表现由 rig 决定，本类型不介入。
//
// 手感设计/04（ADR-0119）：姿势维度。默认剪辑的解析从"战斗变体键 -> 普通键"两级扩成姿势回落链
// （Core.Foundation.DisplayInfo.PoseResolver：去变体 -> 去武器族 -> 去姿态 -> 去步态 -> 基础键），旧键 combat_<state>
// 与 <state>.combat 等价（PoseKeys）。步态/武器族/变体来自可选的 IPoseContextSource（构造重载第 7 个参数）；没有来源时
// 请求只有"状态 + 战斗姿态"两维，解析出的剪辑与改动前逐位一致（回落链此时恰好是 [变体键, 普通键]，就绪探针仍只对
// 非基础键咨询，已在播的那一条视为就绪）。来源的 ContextChanged 与姿态变化走同一个 Refresh 出口：只对运动态重新解析，
// 与最近播放的剪辑不同才切换，瞬态不被打断。武器风格/技能覆盖剪辑（AutoAttackAnim/CastAnimOverride）优先级仍最高，
// 不经姿势解析；武器族维度补齐的是待机/移动/受击等没有覆盖剪辑的状态。
//
// ADR-0147（手感落地 M5-S4）：
//   1) 受击子键：状态机处于反应驱动模式（AnimStateMachine.IsReactionDriven）时，Hit 状态按裁决结果带子键解析
//      （hit.light/heavy/knockback/knockdown/getup/block，经姿势回落链回落到 hit）；空中受击（hit.air）只对 light/heavy/knockback 生效。
//      反应=none 不进 Hit 状态，所以什么也不播。没有子键（未反应驱动）时与此前逐位一致。
//   2) 播放速率：可选的 ClipPlaybackRates 给出当前剪辑的播放速率（动作分相重映射的速率比、移动剪辑的步幅速率）。切换剪辑时随 playClip 的
//      speed 参数下发；播放中速率变化（相位切换、速度变化）经 setClipSpeed 委托下发，只在速率真的变了时调用（取整步长由速率来源保证）。
//      没传速率来源时恒为 1，与此前一致。
//   3) 起步/急停混合：可选的 LocomotionBlends 给出 Idle 与 Move 之间状态切换的混合时长，经 hintBlend 委托在下一次 playClip 之前下发
//      （一次性提示，只 model 型接线；sprite 型没有交叉淡入）。
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
        private readonly IPoseContextSource? _poseContext;
        private readonly ClipPlaybackRates? _playbackRates;
        private readonly Action<Id, double>? _setClipSpeed;
        private readonly ILocomotionBlendSource? _blends;
        private readonly Action<Id, double>? _hintBlend;

        // ADR-0147：每个实体最近一次下发给播放器的速率（playClip 的 speed 或 setClipSpeed），用于只在变化时下发。
        private readonly Dictionary<Id, double> _lastRate = new Dictionary<Id, double>();

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
            : this(stateMachine, defaultClipsForEntity, playClip, weaponStyleSource, weaponStyles, isClipReady, null)
        {
        }

        /// <summary>
        /// 手感设计/04：带姿势上下文来源的构造重载（旧构造保持原签名转调本重载，<paramref name="poseContext"/> 为 null）。
        /// 非空时默认剪辑按 <c>状态 + 步态 + 战斗姿态 + 武器族 + 变体</c> 走姿势回落链解析，来源的
        /// <see cref="IPoseContextSource.ContextChanged"/> 触发运动态的 <see cref="Refresh"/>。
        /// </summary>
        public AnimClipResolver(
            AnimStateMachine stateMachine,
            Func<Id, IReadOnlyDictionary<string, Id>?> defaultClipsForEntity,
            Action<Id, Id, bool, double> playClip,
            IWeaponStyleSource? weaponStyleSource,
            IReadOnlyDictionary<Id, WeaponStyleDef>? weaponStyles,
            Func<Id, Id, bool>? isClipReady,
            IPoseContextSource? poseContext)
            : this(stateMachine, defaultClipsForEntity, playClip, weaponStyleSource, weaponStyles, isClipReady, poseContext, null, null, null, null)
        {
        }

        /// <summary>
        /// ADR-0147：带播放速率/起停混合的构造重载（旧构造保持原签名转调本重载，新参数全为 null）。
        /// <paramref name="playbackRates"/>：剪辑播放速率来源；<paramref name="setClipSpeed"/>：播放中改速率的落地委托 (entityId, speed)；
        /// <paramref name="blends"/>：起步/急停混合时长来源；<paramref name="hintBlend"/>：混合时长提示落地委托 (entityId, seconds)，
        /// 在对应的一次 <c>playClip</c> 之前调用（model 型接线；sprite 型传 null）。
        /// </summary>
        public AnimClipResolver(
            AnimStateMachine stateMachine,
            Func<Id, IReadOnlyDictionary<string, Id>?> defaultClipsForEntity,
            Action<Id, Id, bool, double> playClip,
            IWeaponStyleSource? weaponStyleSource,
            IReadOnlyDictionary<Id, WeaponStyleDef>? weaponStyles,
            Func<Id, Id, bool>? isClipReady,
            IPoseContextSource? poseContext,
            ClipPlaybackRates? playbackRates,
            Action<Id, double>? setClipSpeed,
            ILocomotionBlendSource? blends,
            Action<Id, double>? hintBlend)
        {
            _playbackRates = playbackRates;
            _setClipSpeed = setClipSpeed;
            _blends = blends;
            _hintBlend = hintBlend;
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _defaultClipsForEntity = defaultClipsForEntity ?? throw new ArgumentNullException(nameof(defaultClipsForEntity));
            _playClip = playClip ?? throw new ArgumentNullException(nameof(playClip));
            _weaponStyleSource = weaponStyleSource;
            _weaponStyles = weaponStyles;
            _isClipReady = isClipReady;
            _poseContext = poseContext;

            _stateMachine.StateChangedWithSkill += OnStateChangedWithSkill;
            // H5b 根治（游戏侧复核发现 2）：AnimStateMachine.StateRetriggered（同状态重入，见其类型
            // 判断记录）与 StateChangedWithSkill（状态真的切换了）需要同一套"解析剪辑并播放"逻辑
            // ——两者唯一的区别只是"这次调用是不是状态数值真的变了"，播放器本身对"再调用一次
            // playClip"的语义就是"从头重播"，本类型因此把两个事件处理方法收敛到同一个
            // PlayResolvedClip，不重复实现一遍查表逻辑。
            _stateMachine.StateRetriggered += OnStateRetriggered;
            _stateMachine.CombatStanceChanged += OnCombatStanceChanged;
            if (_poseContext != null)
            {
                _poseContext.ContextChanged += OnPoseContextChanged;
            }
            if (_playbackRates != null)
            {
                _playbackRates.RateChanged += OnRateChanged;
            }
        }

        /// <summary>ADR-0147：速率变化（动作相位切换、移动速度变化、动作结束）后，对该实体正在播的剪辑改速率；已经是这个速率则不下发。</summary>
        private void OnRateChanged(Id entityId)
        {
            if (_setClipSpeed == null || _playbackRates == null || !_lastPlayedClip.ContainsKey(entityId))
            {
                return;
            }

            var rate = _playbackRates.GetRate(entityId);
            if (_lastRate.TryGetValue(entityId, out var last) && last == rate)
            {
                return;
            }
            if (!_lastRate.ContainsKey(entityId) && rate == 1.0)
            {
                return;
            }

            _lastRate[entityId] = rate;
            _setClipSpeed(entityId, rate);
        }

        private void OnPoseContextChanged(Id entityId)
        {
            // 空中阶段：先让状态机进出 Jump（状态机没挂空中阶段来源时无操作），再按当前状态重新解析剪辑。
            _stateMachine.ApplyAirPhase(entityId);
            Refresh(entityId);
        }

        private void OnStateChangedWithSkill(Id entityId, AnimState from, AnimState to, Id? triggerSkillId) =>
            PlayResolvedClip(entityId, to, triggerSkillId, from);

        private void OnStateRetriggered(Id entityId, AnimState state, Id? triggerSkillId) =>
            PlayResolvedClip(entityId, state, triggerSkillId, null);

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
            // ADR-0130 追加决定（空中姿势）：Jump 状态下空中阶段（rise/fall/land）变化时也重新解析——每个阶段是不同剪辑。
            var airJump = state == AnimState.Jump && _poseContext != null && _poseContext.GetContext(entityId).Air != AirPhase.None;
            if (state != AnimState.Idle && state != AnimState.Move && !airJump)
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

        /// <summary>
        /// 显示复位（复活）：按实体<b>当前</b>状态与战斗姿态解析运动态（Idle/Move）剪辑并<b>无条件播放一次</b>
        /// （从头播），不与任何基线比较，随后写入"最近播放剪辑"记账。<see cref="Refresh"/> 的"无记录时基线取普通键
        /// 剪辑"假设视图当前静态显示的就是普通待机，复活那一刻这个假设不成立——视图停在死亡剪辑末帧，
        /// 已脱战或外形没有任何变体键时解析结果恰好等于基线，<see cref="Refresh"/> 会什么都不播，活人继续显示
        /// 倒地末帧。复活路径改调本方法（<c>UnityViewFactory.OnUnitRespawnedForAnim</c>）；<see cref="Refresh"/>
        /// 的语义与其它调用点不变。姿态对应的变体剪辑尚未就绪（冷加载）时按 <see cref="ResolveDefaultClip"/> 的
        /// 既有规则先播普通键剪辑（记账记实际播放的那条），变体就绪后由 <see cref="Refresh"/> 补切——绝不停在
        /// 死亡末帧等加载。走与状态切换相同的播放出口（<c>playClip</c> 委托），逐层帧、装备层、model 型 rig 随之复位。
        /// 状态不是 Idle/Move（复活前已 <see cref="AnimStateMachine.Forget"/> 并 <see cref="AnimStateMachine.Track"/>，
        /// 正常恒为 Idle）或表里没有该状态的剪辑时什么都不播。
        /// <para>语义边界（设计决定）：①无条件播放——对从未死亡的实体误调也会把当前待机从头重播一次，唯一调用点是复活事件
        /// 处理，不做基线比较正是本方法存在的理由；②只复位运动态剪辑，不处理复活瞬间的武器风格覆盖/瞬态（复活时状态已清空，
        /// 没有进行中的瞬态）；③变体永远不就绪则一直播普通待机（同 <see cref="Refresh"/> 的边界 1）。</para>
        /// </summary>
        internal void ResetToLocomotionClip(Id entityId)
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

            var clip = ResolveDefaultClip(entityId, state, table, allowVariant: true);
            if (clip.HasValue)
            {
                PlayAndRecord(entityId, clip.Value, DefaultLoop(state));
            }
        }

        /// <summary>ADR-0111：释放对该实体的"最近播放剪辑"记账（实体销毁/重生时由调用方清理，同
        /// <see cref="AnimStateMachine.Forget"/> 配套）。</summary>
        public void Forget(Id entityId)
        {
            _lastPlayedClip.Remove(entityId);
            _lastRate.Remove(entityId);
        }

        private void PlayResolvedClip(Id entityId, AnimState to, Id? triggerSkillId, AnimState? from)
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
                // ADR-0147：起步/急停（Idle 与 Move 之间）的混合时长提示，一次性，随紧接着的这一次 playClip 生效。
                if (from.HasValue && _blends != null && _hintBlend != null
                    && _blends.TryGetBlendSeconds(entityId, from.Value, to, out var blendSeconds))
                {
                    _hintBlend(entityId, blendSeconds);
                }
                PlayAndRecord(entityId, clipId.Value, DefaultLoop(to));
            }
        }

        /// <summary>ADR-0111：默认剪辑表解析——战斗姿态下先查变体键（且变体剪辑内容已就绪，见构造重载
        /// 参数注释），查不到/未就绪回落普通键。武器风格/技能覆盖不经本方法（优先级更高，见
        /// <see cref="PlayResolvedClip"/>）。</summary>
        private Id? ResolveDefaultClip(Id entityId, AnimState state, IReadOnlyDictionary<string, Id> table, bool allowVariant)
        {
            var stateKey = StateKey(state);
            PoseRequest request;

            // ADR-0130 追加决定（空中姿势）：有空中阶段时 jump/hit/attack 走空中回落链
            // （jump.rise|fall|land -> jump -> idle；hit.air -> hit.launch -> hit；attack.air[.族] -> attack.air -> attack[.族] -> attack），
            // M4-W1b 起空中键带姿态/武器族/变体维度（jump.rise.combat、hit.air.wounded …，先去变体、再去武器族、再去姿态）。
            // 没有空中阶段（没接 AirPoseFeeder 或在地面）时不进本分支，解析与改动前逐位一致。
            if (allowVariant && _poseContext != null
                && _poseContext.GetContext(entityId).TryGetAirRequest(stateKey, _stateMachine.IsInCombatStance(entityId), out var airRequest))
            {
                Func<string, bool>? airUsable = null;
                if (_isClipReady != null)
                {
                    airUsable = tableKey =>
                    {
                        var clip = table[tableKey];
                        return (_lastPlayedClip.TryGetValue(entityId, out var playing) && playing.Equals(clip))
                            || _isClipReady(entityId, clip);
                    };
                }

                return PoseResolver.TryResolve(airRequest, table, out var airResolved, out _, airUsable) ? airResolved : (Id?)null;
            }

            // ADR-0147：反应驱动的受击子键（hit.light/heavy/knockback/knockdown/getup/block）。空中受击只对 light/heavy/knockback 改走 hit.air。
            var hitSub = allowVariant && state == AnimState.Hit ? _stateMachine.GetHitPoseSub(entityId) : null;
            if (hitSub != null && _poseContext != null
                && _poseContext.GetContext(entityId).TryGetAirHitRequest(_stateMachine.IsInCombatStance(entityId), hitSub, out var airHit))
            {
                Func<string, bool>? airHitUsable = null;
                if (_isClipReady != null)
                {
                    airHitUsable = tableKey =>
                    {
                        var clip = table[tableKey];
                        return (_lastPlayedClip.TryGetValue(entityId, out var playing) && playing.Equals(clip))
                            || _isClipReady(entityId, clip);
                    };
                }
                return PoseResolver.TryResolve(airHit, table, out var airHitResolved, out _, airHitUsable) ? airHitResolved : (Id?)null;
            }

            if (!allowVariant)
            {
                request = PoseRequest.Base(stateKey);
            }
            else if (hitSub != null)
            {
                var combatHit = _stateMachine.IsInCombatStance(entityId);
                request = _poseContext != null
                    ? _poseContext.GetContext(entityId).ToRequest(stateKey, combatHit, hitSub)
                    : new PoseRequest(stateKey, null, combatHit ? PoseKeys.StanceCombat : null, null, null, hitSub);
            }
            else
            {
                var combat = _stateMachine.IsInCombatStance(entityId);
                request = _poseContext != null
                    ? _poseContext.GetContext(entityId).ToRequest(stateKey, combat)
                    : new PoseRequest(stateKey, stance: combat ? PoseKeys.StanceCombat : null);
            }

            // 就绪探针只对非基础键咨询（PoseResolver 保证）。已经在播这条剪辑：重探测（换向/换装）期间就绪探针可能
            // 暂时为 false，不能因此把正在播的剪辑踢回更低一级（会闪一下 idle）——已经在播就视为就绪。
            Func<string, bool>? usable = null;
            if (_isClipReady != null)
            {
                usable = tableKey =>
                {
                    var clip = table[tableKey];
                    return (_lastPlayedClip.TryGetValue(entityId, out var playing) && playing.Equals(clip))
                        || _isClipReady(entityId, clip);
                };
            }

            return PoseResolver.TryResolve(request, table, out var resolved, out _, usable) ? resolved : (Id?)null;
        }

        private void PlayAndRecord(Id entityId, Id clipId, bool loop)
        {
            _lastPlayedClip[entityId] = clipId;
            var rate = _playbackRates?.GetRate(entityId) ?? 1.0;
            _lastRate[entityId] = rate;
            _playClip(entityId, clipId, loop, rate);
        }

        public void Dispose()
        {
            _stateMachine.StateChangedWithSkill -= OnStateChangedWithSkill;
            _stateMachine.StateRetriggered -= OnStateRetriggered;
            _stateMachine.CombatStanceChanged -= OnCombatStanceChanged;
            if (_poseContext != null)
            {
                _poseContext.ContextChanged -= OnPoseContextChanged;
            }
            if (_playbackRates != null)
            {
                _playbackRates.RateChanged -= OnRateChanged;
            }
        }
    }
}

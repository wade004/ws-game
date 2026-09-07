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
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
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
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _defaultClipsForEntity = defaultClipsForEntity ?? throw new ArgumentNullException(nameof(defaultClipsForEntity));
            _playClip = playClip ?? throw new ArgumentNullException(nameof(playClip));
            _weaponStyleSource = weaponStyleSource;
            _weaponStyles = weaponStyles;

            _stateMachine.StateChangedWithSkill += OnStateChangedWithSkill;
        }

        private void OnStateChangedWithSkill(Id entityId, AnimState from, AnimState to, Id? triggerSkillId)
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
                if (table != null && table.TryGetValue(StateKey(to), out var fallback))
                {
                    clipId = fallback;
                }
            }

            if (clipId.HasValue)
            {
                _playClip(entityId, clipId.Value, DefaultLoop(to), 1.0);
            }
        }

        public void Dispose() => _stateMachine.StateChangedWithSkill -= OnStateChangedWithSkill;
    }
}

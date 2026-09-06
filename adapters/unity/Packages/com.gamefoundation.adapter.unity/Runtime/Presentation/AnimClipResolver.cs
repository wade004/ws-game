#nullable enable
// AnimClipResolver：动画状态 -> 具体剪辑 id 的查表落地（W3b 收边，拍板 6"09 §4 动画层……程序动画
// 原语……以及命中帧同步 sprite 侧"配套的"状态机驱动到具体剪辑"这一半，见
// presentation/render/contracts/ICharacterRig.cs 类型注释"判断记录（动画状态机驱动职责在本接口的
// 落地形状）"——AnimStateMachine 只回答"当前该处于哪个 AnimState"，"该状态该播哪个具体剪辑"这一
// 查表逻辑显式留给"持有 WeaponStyleResolver 的组装层代码"，本类型就是这一层组装代码的落地。
//
// 判断记录（消费 display.anim_set 而不是给 sprite 型 DisplayInfo 加字段）：04 第 7.1 节
// display.map 的 anim_set_ref 字段严格是 model 型专属——core/foundation/display_info.
// DisplayKindFieldGroupRule 把 anim_set_ref 列在 ModelOnlyFields，kind=sprite 的记录若填了这个
// 字段会被数据校验直接判为错误（本任务不得改 core/**，这条硬约束不可绕过）。本类型因此不经
// DisplayInfo 读取 anim_set_ref，改为构造期直接注入"实体 id -> 剪辑查表"的解析委托（通常由具体
// 游戏的引导代码按约定好的 display.anim_set 记录 id 直接构造，不依赖 display.map 挂上这个引用）——
// display.anim_set 本身作为独立数据表仍然是拍板 6 要求的"消费 display.anim_set"这一半，只是不
// 经由 display.map 的 anim_set_ref 间接查找，这是本模块在 model-only 字段约束下能给出的最小可用
// 形状，记录在此供 W5 判断是否需要在 09/04 补一句"sprite 型如需复用 display.anim_set 的剪辑目录，
// 由引擎适配层直接按约定 id 查表，不经 anim_set_ref"的勘误。
//
// 判断记录（cast 状态不做按技能覆盖）：WeaponStyleDef.CastAnimOverride 是"技能 id -> 剪辑 id"的
// 映射（见 09 第 4.4 节），但 AnimStateMachine.StateChanged 事件只携带 (entityId, from, to) 三元组，
// 不携带触发这次切换的技能 id（该状态机本身也不持有，见其类型注释"只回答该处于哪个状态"）。本类型
// 因此对 Cast 状态只应用 display.anim_set 的默认 "cast" 剪辑，不做技能级覆盖——这是已知简化，
// AutoAttackAnim（Attack 状态，不需要技能 id）不受影响，正常生效。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;

namespace Adapter.Unity.Presentation
{
    /// <summary>把一条 <c>display.anim_set</c> 记录的 <c>clips</c> 字段解析成"剪辑名 -> resource_ref"
    /// 的只读表（09/04 均未给出运行期强类型 DTO，见文件顶部判断记录，本类型只解析
    /// <see cref="AnimClipResolver"/> 需要的最小字段——<c>resource_ref</c>，不解析 <c>events</c>
    /// 关键帧列表：sprite 型命中帧改走 <see cref="FrameAnimClip.Keyframes"/>，见
    /// <c>UnityFrameAnimPlayer.RegisterClip</c>，不经本表的 <c>events</c>）。</summary>
    public static class AnimSetRecordParser
    {
        public static IReadOnlyDictionary<string, Id> ParseClips(DataRecord record)
        {
            var result = new Dictionary<string, Id>(StringComparer.Ordinal);
            if (!record.TryGetObject("clips", out var clips))
            {
                return result;
            }

            foreach (var kv in clips)
            {
                if (kv.Value is JsonObject clipObj
                    && clipObj.TryGetValue("resource_ref", out var refVal)
                    && refVal is JsonString refStr
                    && Id.TryParse(refStr.Value, out var clipId))
                {
                    result[kv.Key] = clipId;
                }
            }

            return result;
        }
    }

    /// <summary>见文件顶部类型注释。订阅 <see cref="AnimStateMachine.StateChanged"/>，按状态名查
    /// 默认剪辑表，Attack 状态额外尝试武器风格覆盖（<see cref="WeaponStyleDef.AutoAttackAnim"/>），
    /// 解出 clipId 后调用注入的 <paramref name="playClip"/> 委托——不直接依赖
    /// <see cref="ICharacterRig.PlayClip"/>（见 <c>UnitySpriteView.PlayClip</c> 判断记录"rig.PlayClip
    /// 结构性失效"），调用方通常传入 <c>UnitySpriteView.PlayClip</c> 或
    /// <c>UnityFrameAnimPlayer.Play</c> 的包装委托。</summary>
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
        private readonly Func<Id, Id?>? _weaponStyleRefForEntity;

        /// <param name="stateMachine">状态来源。</param>
        /// <param name="defaultClipsForEntity">实体 id -> 该实体的"剪辑名 -> clipId"表（通常来自
        /// <see cref="AnimSetRecordParser.ParseClips"/>）；查不到该实体或表内查不到当前状态名时
        /// 静默跳过本次切换（不播放任何剪辑），不抛异常——同 09 第 1 节表现层"缺表现资源不阻断游戏"
        /// 的一贯宽容策略。</param>
        /// <param name="playClip">解出 clipId 后的落地委托：(entityId, clipId, loop, speed)。</param>
        /// <param name="weaponStyles">可选：武器风格目录（<c>display.weapon_style</c>），非空且
        /// <paramref name="weaponStyleRefForEntity"/> 也非空时，Attack 状态优先尝试
        /// <see cref="WeaponStyleDef.AutoAttackAnim"/> 覆盖默认剪辑表。</param>
        public AnimClipResolver(
            AnimStateMachine stateMachine,
            Func<Id, IReadOnlyDictionary<string, Id>?> defaultClipsForEntity,
            Action<Id, Id, bool, double> playClip,
            IReadOnlyDictionary<Id, WeaponStyleDef>? weaponStyles = null,
            Func<Id, Id?>? weaponStyleRefForEntity = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _defaultClipsForEntity = defaultClipsForEntity ?? throw new ArgumentNullException(nameof(defaultClipsForEntity));
            _playClip = playClip ?? throw new ArgumentNullException(nameof(playClip));
            _weaponStyles = weaponStyles;
            _weaponStyleRefForEntity = weaponStyleRefForEntity;

            _stateMachine.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(Id entityId, AnimState from, AnimState to)
        {
            Id? clipId = null;

            if (to == AnimState.Attack && _weaponStyles != null && _weaponStyleRefForEntity != null)
            {
                var weaponStyleRef = _weaponStyleRefForEntity(entityId);
                if (weaponStyleRef.HasValue && _weaponStyles.TryGetValue(weaponStyleRef.Value, out var def))
                {
                    clipId = def.AutoAttackAnim;
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

        public void Dispose() => _stateMachine.StateChanged -= OnStateChanged;
    }
}

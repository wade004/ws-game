#nullable enable
// UnityModelView：model 型外形的 Unity 侧具体 View（W6-B 新增，ADR-0017 决策 b 收口——presentation/
// render/core 目前只有 sprite 型的 SpriteViewBase 骨架，model 型没有对应的引擎无关基类可继承（W6-A
// 判断记录"model 型 View/ViewFactory 装配代码那一层决定资源加载/创建时机，不属于 CharacterRig
// 本身职责"，那一层正是本类型），因此本类型直接实现 Presentation.Common.IView 三件套
// （Bind/OnEvent/SyncPose/Destroy）+ IHasCharacterRig + IModelHandleProvider，持有一个
// Presentation.Render.ModelCharacterRig，与 UnitySpriteView 持有 SpriteCharacterRig 同一职责分工，
// 只是没有共同基类可抽取（sprite/model 两条路线目前唯一的共同点只有 IView 契约本身）。
//
// 判断记录（entityId 在构造期而不是 Bind 时确定）：Presentation.Render.ModelCharacterRig 的构造函数
// 要求 entityId 参数（用于其内部 HitFrameReached 事件携带的实体 id），不像 SpriteCharacterRig 那样
// 支持"先构造、后 Bind"两阶段初始化；Adapter.Unity.Presentation.UnityViewFactory.CreateView(kind,
// displayId, entityId) 恰好在创建 View 之前就已经拿到 entityId（该参数目前只用于 sprite 路线的默认
// 动画接线，见其 AttachDefaultAnimation 调用点），本类型据此直接在构造函数里把 entityId 转交
// ModelCharacterRig，<see cref="Bind"/> 只做"确认已绑定"的记账（校验传入的 entityId 与构造期一致，
// 不一致时抛异常提示装配错误——这是比静默忽略更早暴露问题的选择）。
using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Presentation.Common;
using Presentation.Render;

namespace Adapter.Unity.Presentation
{
    public sealed class UnityModelView : IView, IHasCharacterRig, IModelHandleProvider
    {
        private readonly IRenderer3D _renderer;
        private readonly IRenderConventionHost _conventions;
        private readonly ModelHandle _handle;
        private readonly ModelCharacterRig _rig;
        private readonly Core.Foundation.DisplayInfo.DisplayInfo _displayInfo;

        /// <summary>见 <see cref="Presentation.Render.SpriteViewBase"/> 缺口 10 同款判断记录：物品
        /// 实例 id -> <see cref="EquipVisualDef"/> 查询表，未注入时 <see cref="OnEvent"/> 对装备变化
        /// 事件保持默认空处理。</summary>
        private readonly IReadOnlyDictionary<Id, EquipVisualDef>? _equipVisuals;

        /// <summary>PR130-07 根治新增：本 View 生命周期内实际应用过的 装备实例 id -&gt; 生效时使用的
        /// <see cref="EquipVisualDef"/> 可逆索引——取代此前卸装路径直接把事件携带的逻辑 <c>slot</c>
        /// id 同时当 slot_mesh 槽位 id 与 socket_attach 挂点 id 使用的做法（装备按
        /// <see cref="EquipVisualDef.SocketId"/> 挂接，二者不是同一个 id 域，<c>ClearSocket(unequipped.Slot)</c>
        /// 几乎总是查不到对应挂接，子模型残留不清理）。<see cref="OnEvent"/> 处理
        /// <c>item.equipped</c> 时把这次实际应用的 <see cref="EquipVisualDef"/> 记进本索引；处理
        /// <c>item.unequipped</c> 时按 <see cref="ItemUnequippedEvent.ItemInstanceId"/>（该事件确实
        /// 携带该字段，见 <c>core/carriers/common/contracts/Events.cs</c>）反查，按其
        /// <see cref="EquipVisualDef.Mode"/> 精确调用 <see cref="ModelCharacterRig.ClearSlot"/> 或
        /// <see cref="ModelCharacterRig.ClearSocket"/>，不再两个都盲试。索引独立于外部注入的
        /// <see cref="_equipVisuals"/> 表（不依赖它在卸装时是否仍持有同一条目——那张表的生命周期由
        /// 装配层管理，见 <c>UnityViewFactory</c> 判断记录），保证"卸下的正是刚才实际穿上的那一件"。
        /// 反查不到（存档恢复后的初始装备状态、本索引尚未来得及记账等场景）时退回按逻辑 slot 尝试
        /// slot_mesh 清理一次——<see cref="ModelCharacterRig.ClearSlot"/> 对未换过装的槽位调用是幂等
        /// 的（见 <see cref="IRenderer3D.SetSlotMesh"/> 契约语义），不会因此产生错误副作用。</summary>
        private readonly Dictionary<Id, EquipVisualDef> _appliedEquipVisualsByItemInstanceId = new Dictionary<Id, EquipVisualDef>();

        private bool _destroyed;

        public Id EntityId { get; private set; }

        public bool IsAlive { get; private set; }

        public ICharacterRig Rig => _rig;

        public UnityModelView(
            Id entityId,
            IRenderer3D renderer,
            IRenderConventionHost conventions,
            Core.Foundation.DisplayInfo.DisplayInfo displayInfo,
            RenderOptions? options = null,
            IReadOnlyDictionary<Id, EquipVisualDef>? equipVisualByItemInstanceId = null)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));

            if (_displayInfo.Kind != DisplayKind.Model || _displayInfo.Model == null)
            {
                throw new ArgumentException(
                    $"UnityModelView 只支持 kind=model 的 DisplayInfo，实际 kind={_displayInfo.Kind}", nameof(displayInfo));
            }

            EntityId = entityId;
            _equipVisuals = equipVisualByItemInstanceId;

            _handle = _renderer.CreateModelInstance(_displayInfo.Model.ModelRef);
            _rig = new ModelCharacterRig(entityId, _renderer, _handle, _displayInfo, options);

            foreach (var kv in _displayInfo.Model.DefaultSlotMeshes)
            {
                _renderer.SetSlotMesh(_handle, kv.Key, kv.Value);
            }
            foreach (var kv in _displayInfo.Model.MaterialParams)
            {
                _renderer.SetMaterialParam(_handle, kv.Key, kv.Value);
            }

            _renderer.SetShadow(_handle, ShadowSpec.ToEngineShadowMode(_displayInfo.Shadow));
        }

        /// <summary>本 View 挂接的引擎侧模型句柄，供 <c>UnityViewFactory</c> 一类同属引擎适配层的
        /// 协作代码取用（如挂接默认动画/命中帧登记），惯例同 <c>UnitySpriteView.EngineHandle</c>。</summary>
        public ModelHandle EngineHandle => _handle;

        public ModelHandle? TryGetModelHandle() => IsAlive ? _handle : (ModelHandle?)null;

        public void Bind(Id entityId)
        {
            // 见类型顶部判断记录：entityId 已在构造期确定，这里只校验一致性并置存活标记。
            if (!EntityId.Equals(entityId))
            {
                throw new InvalidOperationException(
                    $"UnityModelView 构造期绑定的 entityId（{EntityId}）与 Bind 调用传入的 entityId（{entityId}）不一致，" +
                    "这是装配层的用法错误——UnityViewFactory.CreateView 的 entityId 参数应与随后 ViewBinder.Bind 使用的" +
                    "实体 id 保持一致。");
            }
            IsAlive = true;
        }

        /// <summary>见 <see cref="SpriteViewBase.OnEvent"/> 缺口 10 同款判断记录：默认处理
        /// <c>item.equipped</c>/<c>item.unequipped</c>，按 <see cref="EquipVisualDef"/> 调用
        /// <see cref="ModelCharacterRig.ApplyEquipVisual"/>/<see cref="ModelCharacterRig.ClearSlot"/>/
        /// <see cref="ModelCharacterRig.ClearSocket"/>；<see cref="_equipVisuals"/> 未注入或查不到对应
        /// 行时保持不变，不抛异常。PR130-07 根治：卸装路径改按 <see cref="_appliedEquipVisualsByItemInstanceId"/>
        /// 反查实际应用过的 <see cref="EquipVisualDef"/>，见该字段判断记录。</summary>
        public void OnEvent(IEvent evt)
        {
            if (_equipVisuals == null)
            {
                return;
            }

            switch (evt)
            {
                case ItemEquippedEvent equipped when equipped.UnitId.Equals(EntityId):
                    if (_equipVisuals.TryGetValue(equipped.ItemInstanceId, out var def))
                    {
                        _rig.ApplyEquipVisual(def);
                        _appliedEquipVisualsByItemInstanceId[equipped.ItemInstanceId] = def;
                    }
                    break;

                case ItemUnequippedEvent unequipped when unequipped.UnitId.Equals(EntityId):
                    if (_appliedEquipVisualsByItemInstanceId.TryGetValue(unequipped.ItemInstanceId, out var appliedDef))
                    {
                        switch (appliedDef.Mode)
                        {
                            case EquipVisualMode.SlotMesh:
                                if (appliedDef.SlotId.HasValue)
                                {
                                    _rig.ClearSlot(appliedDef.SlotId.Value);
                                }
                                break;

                            case EquipVisualMode.SocketAttach:
                                if (appliedDef.SocketId.HasValue)
                                {
                                    _rig.ClearSocket(appliedDef.SocketId.Value);
                                }
                                break;
                        }
                        _appliedEquipVisualsByItemInstanceId.Remove(unequipped.ItemInstanceId);
                    }
                    else
                    {
                        // 见 _appliedEquipVisualsByItemInstanceId 判断记录：反查不到时（本 View 生命
                        // 周期内未记录过对应的装备事件，如存档恢复后的初始装备状态）退回按逻辑 slot
                        // 尝试 slot_mesh 清理一次——ClearSlot 幂等，不会因此产生错误副作用；不盲试
                        // ClearSocket（socket id 与逻辑 slot id 不是同一个域，见类型顶部判断记录，
                        // 盲试没有意义）。
                        _rig.ClearSlot(unequipped.Slot);
                    }
                    break;
            }
        }

        public void SyncPose(Vec2 pos, Direction facing, double height)
        {
            EnsureAlive();
            var sortY = _conventions.ComputeSortY(pos, _displayInfo.SortOffset);
            _rig.SyncPlacement(pos, height, facing.RawRadians, _displayInfo.Scale, sortY);
        }

        public void Destroy()
        {
            if (_destroyed)
            {
                return;
            }

            _rig.Dispose();
            _renderer.DestroyModelInstance(_handle);
            _destroyed = true;
            IsAlive = false;
        }

        private void EnsureAlive()
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("View 尚未 Bind 或已 Destroy，不能调用本方法");
            }
        }
    }
}

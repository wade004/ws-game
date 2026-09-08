using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Common
{
    /// <summary>
    /// 只读快照门面（铁律 P1"表现层只能读取 WorldSim 暴露的只读快照/查询接口"）：把
    /// <c>IWorldSim.GetEntity</c> 收窄成表现层实际需要的几个只读字段，View 绑定、镜头跟随等
    /// 表现层组件经本接口读取逻辑状态，不直接持有 <c>IWorldSim</c>/<c>Entity</c> 引用。
    /// <para>
    /// 判断记录（PRES-180 版本判据根治，2026-09-09）：<see cref="GetAllEntityIds"/>/
    /// <see cref="GetRawKind"/> 是 C#8 默认接口方法，不是强制成员——此前把它们定义为普通接口方法是
    /// 源码级破坏性变更（任何自定义 <see cref="ISimSnapshot"/> 实现须新增代码才能继续编译，按
    /// architecture/11 版本判据需要 MAJOR），与本框架当期按 MINOR 发布的既定节奏冲突。改为默认实现后，
    /// 未覆盖这两个成员的自定义实现方仍可编译通过，只是在 <c>ViewBinder.OnSaveLoaded</c> 的
    /// <c>save.loaded</c> 全量对账里安全退化——见 <c>ViewBinder.OnSaveLoaded</c> 类型/方法注释"安全
    /// 退化"判断记录，唯一生产实现 <see cref="WorldSimSnapshot"/> 已同步覆盖为真实实现，不受影响。
    /// </para>
    /// </summary>
    public interface ISimSnapshot
    {
        /// <summary>实体的世界平面坐标；实体不存在时抛 <see cref="System.InvalidOperationException"/>
        /// （调用前应先 <see cref="Exists"/>）。</summary>
        Vec2 GetPosition(Id entityId);

        /// <summary>实体的朝向角度（弧度，见 05 第 3 节）；实体不存在时抛
        /// <see cref="System.InvalidOperationException"/>。</summary>
        double GetFacing(Id entityId);

        /// <summary>实体的高度偏移（见 05 第 3.3 节，仅 <c>Unit</c> 子类有意义，非 <c>Unit</c>
        /// 实体固定返回 0）；实体不存在时抛 <see cref="System.InvalidOperationException"/>。</summary>
        double GetHeight(Id entityId);

        /// <summary>实体当前是否存在（未销毁）。</summary>
        bool Exists(Id entityId);

        /// <summary>用于查询外形的逻辑 id（<c>TemplateId ?? EntityId</c>，见 03 第 5 节）；
        /// 实体不存在返回 null。</summary>
        Id? GetDisplayId(Id entityId);

        /// <summary>实体对应的 <see cref="ViewKind"/>；无法从实体的 <c>Kind</c> 字符串映射出已知
        /// 分类，或实体不存在时返回 null（见 <see cref="EntityKindMapping"/> 契约缺口说明）。</summary>
        ViewKind? GetKind(Id entityId);

        /// <summary>实体的原始 <c>Entity.Kind</c> 字符串（映射前，供 <see cref="ViewKind"/> 之外的
        /// 调用方——如 <c>ViewBinder</c> 的 <c>OnEntityCreated</c>——复用与"经 <c>entity.created</c>
        /// 事件正常创建"完全一致的映射/跳过/诊断逻辑）；实体不存在返回 null。见 PRES-180 存档读档
        /// 对账判断记录（<c>ViewBinder</c> 类型注释）。
        /// <para>
        /// 默认接口方法（见本接口类型注释"版本判据根治"判断记录）：默认恒定返回 <c>null</c>——
        /// 未覆盖本成员的自定义实现在 <c>ViewBinder.OnSaveLoaded</c> 里等价于"这个实体没有可供复用
        /// 的原始 Kind"，该方法只在"按 <see cref="GetAllEntityIds"/> 发现的存活实体补建 View"这一
        /// 分支被调用；<see cref="GetAllEntityIds"/> 默认也返回空集合，两者的默认值组合起来使得
        /// "补建"分支天然不会执行到本方法，不会出现"拿到 null 却继续往下按 null 处理"的路径。
        /// </para>
        /// </summary>
        string? GetRawKind(Id entityId) => null;

        /// <summary>当前存活（未销毁）的全部实体 id，按 <c>EntityId</c> 序数排序（惯例同
        /// <c>IWorldSim.QueryEntities</c>）。供 <c>ViewBinder</c> 在 <c>save.loaded</c> 后按
        /// "WorldSim 当前实体"与已绑定 View 表做一次全量对账（见 PRES-180 判断记录：
        /// <c>SuppressDispatch</c> 作用域丢弃读档期间的 <c>entity.created</c>/<c>entity.destroyed</c>，
        /// 本方法是唯一的补扫入口，其余场景不应依赖它）。
        /// <para>
        /// 默认接口方法（见本接口类型注释"版本判据根治"判断记录）：默认返回空集合——未覆盖本成员的
        /// 自定义 <see cref="ISimSnapshot"/> 实现在 <c>ViewBinder.OnSaveLoaded</c> 对账时，"按存活
        /// 实体补建 View"这一半对账天然变成零次迭代的空操作（不抛异常、不误判、不需要额外分支判断
        /// 是否为默认实现），只保留"销毁已不存在实体的陈旧 View"这一半——那一半只依赖
        /// <see cref="Exists"/>（本接口自始就是的强制成员，任何实现都必须提供），不受本方法是否被
        /// 覆盖影响，继续正确工作。见 <c>ViewBinder.OnSaveLoaded</c> 方法注释"安全退化"判断记录。
        /// </para>
        /// </summary>
        IReadOnlyList<Id> GetAllEntityIds() => Array.Empty<Id>();
    }
}

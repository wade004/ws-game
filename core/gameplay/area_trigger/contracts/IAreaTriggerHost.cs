using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// 区域触发契约（见 05_对象模型与世界.md 第 7.1 节 <c>AreaTriggerHost</c>）：只负责"检测进入/
    /// 离开 + 满足条件 + 发事件或调用钩子"，不内含具体业务逻辑（见 05 第 7 节）。
    /// <see cref="LoadForMap"/>/<see cref="UnloadMap"/>/<see cref="RegisterTrap"/> 是 05 原文接口之外
    /// 任务书拍板补充的成员（见本模块 README"契约缺口"一节）。
    /// </summary>
    public interface IAreaTriggerHost
    {
        /// <summary>登记一个触发定义并返回其 id（见 05 第 7.1 节 <c>register</c>）。</summary>
        Id Register(AreaTriggerDef def);

        /// <summary>移除登记（见 05 第 7.1 节 <c>unregister</c>）；未登记的 id 视为空操作。ADR-0090：
        /// 若该触发体当前有单位仍处于"已进入"状态，会先为每个这样的单位补发一条
        /// <c>reason=unloaded</c> 的 <see cref="AreaTriggerLeftEvent"/>，再清运行期账本，见
        /// <c>AreaTriggerHost</c> 判断记录。</summary>
        void Unregister(Id triggerId);

        /// <summary>
        /// 检测 <paramref name="unitId"/> 与全部已登记触发范围（含 <see cref="RegisterTrap"/> 登记的
        /// 陷阱）在 <paramref name="position"/> 下的进入/离开关系并按需发出事件（见 05 第 7.1 节
        /// <c>evaluate</c>，"由移动系统在单位位置变化后调用"——本任务改由
        /// <see cref="AreaTriggerTickHandler"/> 按位置变化驱动，见该类型注释）。
        /// </summary>
        void Evaluate(Id unitId, Vec2 position);

        /// <summary>从 <paramref name="data"/> 的 <c>area.trigger_def</c> 表登记
        /// <paramref name="mapId"/> 全部触发定义（供场景加载时调用）。</summary>
        void LoadForMap(Id mapId, IDataRegistryView data);

        /// <summary>移除 <paramref name="mapId"/> 下全部已登记触发（含陷阱），供场景卸载时调用；
        /// 逐个转调 <see cref="Unregister"/>，因此同样按 ADR-0090 为仍在区域内的单位补发
        /// <c>reason=unloaded</c> 的离开事件。</summary>
        void UnloadMap(Id mapId);

        /// <summary>
        /// 登记一个 <c>trap</c> 陷阱触发体（见 07 第 3.1 节 <c>trap</c> 类型
        /// "踩踏/触碰触发效果"）：进入时调用注入的
        /// <see cref="AreaTriggerOptions.TrapTrigger"/> 委托，不产生 <c>one_shot</c>/<c>condition</c>
        /// 语义（陷阱的启用/禁用由 <c>gobj.lock</c>/<c>gobj.template.state</c> 等 L3 机制控制，不属于
        /// 本方法职责）。返回内部生成的触发体 id，供后续 <see cref="Unregister"/>。
        /// </summary>
        Id RegisterTrap(Id gobjInstanceId, Shape shape, Id mapId);

        // -----------------------------------------------------------------
        // 消费方反馈（游戏接入方第九批，阻塞，ADR-0066《区域触发宿主契约纳入当前所在区域查询》）：
        // "谁在哪个触发范围内"此前只存在 core/AreaTriggerHost 私有字段 _inside 里，契约不暴露——
        // 接入方要在 HUD 常驻显示当前区域名，只能自己订阅 area.trigger_entered/left 事件、自建一份
        // 平行状态账本。本成员把这份权威状态提升为只读查询出口。
        // -----------------------------------------------------------------

        /// <summary>
        /// 补充（ADR-0066）：<paramref name="unitId"/> 当前所在的全部触发区域 id（含
        /// <see cref="RegisterTrap"/> 登记的陷阱——本成员只回答"是否检测到处于范围内"，不附加"是否
        /// 配置了显示名"这层过滤，那是表现层"当前区域名"派生定义的职责，见 09 表现层文档/
        /// <c>Presentation.Ui.PlayerPathProvider</c> 判断记录），按<b>进入先后排序</b>（最早进入的在
        /// 最前，最近进入的在最后——与列表末尾元素永远是"最新触发"这一直觉一致，供表现层"取最近的
        /// 一个"时不需要反向遍历整份权威状态就近似正确，同时仍能整份转发让调用方自行按需过滤/回溯）。
        /// 不在任何区域时返回空集合，不返回 <c>null</c>（惯例同 <c>Core.Rules.Common.ISkillHost.
        /// GetKnownSkills</c>"查询类默认实现允许显式降级"，返回空集合而不是 null，调用方不需要额外
        /// 判空）。
        /// <para>
        /// C# 8 默认接口成员：本默认实现恒返回空集合——这是只读查询，返回一个明确的保守默认值，语义是
        /// "该宿主实现不提供当前所在区域查询"，供未实现本能力的 <see cref="IAreaTriggerHost"/>（旧版本
        /// 编译产物、未升级的自定义实现）源码/二进制兼容，新增接口成员不破坏既有实现类的编译。生产实现
        /// <see cref="AreaTriggerHost"/> 用显式接口实现转发到内部账本（不用隐式实现是刻意的——见
        /// <c>Core.Rules.Common.ISkillHost</c> 同类新增成员的既有判断记录"ISkillHost 显式接口实现"：
        /// 隐式实现会把落地方法的物理 IL 属性从普通实例方法改写成 <c>virtual sealed</c>，被
        /// <c>toolchain/abi_surface</c> 静态签名比对误判为破坏）。任何组合/包装
        /// <see cref="IAreaTriggerHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个降级默认
        /// 值——同 <c>Core.Rules.Common.ISkillHost.GetSkillReadiness</c> 判断记录"框架内
        /// InterfaceDefaultMemberForwardingTests 门禁"。
        /// </para>
        /// </summary>
        IReadOnlyList<Id> GetActiveTriggerIds(Id unitId) => System.Array.Empty<Id>();
    }
}

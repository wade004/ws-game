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

        /// <summary>移除登记（见 05 第 7.1 节 <c>unregister</c>）；未登记的 id 视为空操作。</summary>
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

        /// <summary>移除 <paramref name="mapId"/> 下全部已登记触发（含陷阱），供场景卸载时调用。</summary>
        void UnloadMap(Id mapId);

        /// <summary>
        /// 登记一个 <c>trap</c> 陷阱触发体（见 07 第 3.1 节 <c>trap</c> 类型
        /// "踩踏/触碰触发效果"）：进入时调用注入的
        /// <see cref="AreaTriggerOptions.TrapTrigger"/> 委托，不产生 <c>one_shot</c>/<c>condition</c>
        /// 语义（陷阱的启用/禁用由 <c>gobj.lock</c>/<c>gobj.template.state</c> 等 L3 机制控制，不属于
        /// 本方法职责）。返回内部生成的触发体 id，供后续 <see cref="Unregister"/>。
        /// </summary>
        Id RegisterTrap(Id gobjInstanceId, Shape shape, Id mapId);
    }
}

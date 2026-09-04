using Core.Foundation.Common;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// 契约缺口：架构未给"打开商店界面"定义契约接口（08 第 3.1 节 <c>vendor</c> 行"逻辑侧只发出
    /// 打开商店请求"，具体经表现层 UI 完成，本层不应直接依赖 UI/Economy 模块），
    /// <see cref="DialogHost"/> 按任务书拍板改用具名委托绕过（惯例同
    /// <see cref="Core.Gameplay.Common.CurrencyGranter"/>）。</summary>
    public delegate void VendorOpenRequestedCallback(Id unitId, Id npcId);

    /// <summary>契约缺口：传送目标的实际执行（移动单位位置/切场景）属于 05/03 文档的场景路由与
    /// 单位位置写入职责，本模块不直接依赖那些模块，改用回调把"传送请求"转发给调用方。</summary>
    public delegate void TeleportRequestedCallback(Id unitId, Id targetRef);

    /// <summary>契约缺口：存档系统 <c>ISaveSystem</c> 的具体存档流程（收集全部 <c>IPersistable</c>
    /// 段、写盘）不应该由 Dialog 反向感知，本模块只发出"请求存档"的信号。</summary>
    public delegate void SaveRequestedCallback(Id unitId);

    /// <summary>契约缺口：遭遇系统 <c>core/gameplay/encounter</c>（本次任务由另一 agent 并行建设）
    /// 未对本模块暴露契约接口，改用回调转发"启动遭遇"请求。</summary>
    public delegate void EncounterStartRequestedCallback(Id encounterRef);
}

using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 任务系统对外契约（见 08 第 9 节汇总表 Quest 行 <c>QuestHost.accept/turnIn/updateProgress(...)</c>，
    /// 本接口按任务书拍板把该签名展开为完整的状态查询/转移方法集）。由 <see cref="QuestHost"/> 实现。
    /// </summary>
    public interface IQuestHost
    {
        /// <summary>
        /// 当前状态。<see cref="QuestState.Unavailable"/>/<see cref="QuestState.Available"/> 两态
        /// 不持久化，每次调用按 <c>prerequisite</c> 实时求值（见 08 第 2.2 节
        /// "unavailable → available：prerequisite 满足"）；其余四态（Active/ObjectivesComplete/
        /// Failed/TurnedIn）来自持久化的运行期记录。
        /// </summary>
        QuestState GetState(Id unitId, Id questId);

        /// <summary>
        /// 接取任务（08 第 2.2 节"available → active：按 start_method 被接取"）。要求当前状态为
        /// <see cref="QuestState.Available"/>，且 <c>exclusive_group</c>（若声明）内没有其它已经
        /// Active/ObjectivesComplete 的任务，否则返回 false、不改变任何状态。成功发
        /// <c>quest.accepted</c>。
        /// </summary>
        bool Accept(Id unitId, Id questId);

        /// <summary>
        /// 手动推进某条目标的进度（主要供 <c>escort</c> 类目标——本类型没有自动事件驱动，见 08
        /// 第 2.1 节 <c>escort</c> 行；其余类型的进度通常由 <see cref="QuestHost"/> 内部的事件订阅
        /// 自动推进，但调用方也可以用本方法手动推进，例如脚本/关卡逻辑显式驱动）。要求任务当前为
        /// <see cref="QuestState.Active"/>，<paramref name="objectiveIndex"/> 越界返回 false。
        /// 计数被夹在 <c>[0, objective.Count]</c> 之间；发 <c>quest.objective_progress</c>；
        /// 全部目标达标时状态转移为 <see cref="QuestState.ObjectivesComplete"/> 并额外发
        /// <c>quest.completed</c>。
        /// </summary>
        bool UpdateProgress(Id unitId, Id questId, int objectiveIndex, int delta);

        /// <summary>
        /// 交付任务（08 第 2.2 节"objectives_complete → turned_in：按 turn_in_method 交付，结算
        /// rewards"）。等价于 <c>TurnIn(unitId, questId, out _)</c>，不关心失败原因。
        /// </summary>
        bool TurnIn(Id unitId, Id questId);

        /// <summary>
        /// 交付任务，同上，额外通过 <paramref name="failure"/> 报告失败原因（N02/N11 根治，见
        /// <see cref="QuestTurnInFailure"/>）。要求当前状态为 <see
        /// cref="QuestState.ObjectivesComplete"/>；交付时先按实际库存核验 <c>collect</c> 且
        /// <c>consume_on_progress=false</c> 的目标能否扣除完整数量（不足则整体失败，见 08 第 2.1 节
        /// 该行 param 要点），确认可扣除后才实际移除，随后经 <see
        /// cref="Core.Gameplay.Common.IRewardDispatcher"/> 结算 <c>rewards</c>——奖励发放原子：
        /// 物品奖励因背包已满无法完整发放时，整批奖励不生效，已扣除的 collect 物品回滚放回背包，
        /// 交付整体失败、任务保持 <see cref="QuestState.ObjectivesComplete"/>（不丢奖励、不误置
        /// <see cref="QuestState.TurnedIn"/>）。交付成功时按 <c>repeatable</c> 决定交付后的状态
        /// （<c>none</c> 终止于 TurnedIn；<c>daily</c> 回落 Available 但记录完成日、同日不可再接；
        /// <c>unlimited</c> 立即回落 Available），并发 <c>quest.turned_in</c>。
        /// </summary>
        bool TurnIn(Id unitId, Id questId, out QuestTurnInFailure failure);

        /// <summary>
        /// 判定任务失败（08 第 2.2 节"escort/event 类可能失败，策略配置是否允许失败"）。要求
        /// <see cref="QuestOptions.AllowFail"/> 开启，且当前状态为 Active 或 ObjectivesComplete，
        /// 否则返回 false。成功发 <c>quest.failed</c>（附 <paramref name="reason"/>）。
        /// </summary>
        bool Fail(Id unitId, Id questId, string reason);

        /// <summary>
        /// 玩家主动放弃任务（ADR-0092，见该决策"Fail 与 Abandon 的语义差异"一节）。只允许对当前状态为
        /// <see cref="QuestState.Active"/>/<see cref="QuestState.ObjectivesComplete"/>（已接取、未
        /// 交付，含已达标但尚未交付）的任务放弃。成功时把该任务的运行期进度记录整体移除——效果等价于
        /// "这个单位从未接取过这条任务"：<see cref="GetState"/> 落回按 <c>prerequisite</c> 实时求值的
        /// Available/Unavailable；<see cref="GetLog"/> 不再包含它；目标计数归零；不写入任何"失败"
        /// 记录（与 <see cref="Fail"/> 刻意区分，见该决策）。成功发 <c>quest.abandoned</c>（字段对齐
        /// <c>quest.failed</c>/<c>quest.turned_in</c> 既有惯例：<c>unitId</c> + <c>questId</c>）。
        /// <para>
        /// 未接取（无进度记录）、已交付（<see cref="QuestState.TurnedIn"/>）、已失败（<see
        /// cref="QuestState.Failed"/>）、或 <paramref name="questId"/> 未登记（含从未存在、或已被
        /// 重新登记定义删除，同 <see cref="GetObjectiveRequiredCounts"/> 判断记录 CORE-118-QUEST）
        /// ——均返回 <c>false</c>，不抛异常、不发事件：这是一个供 UI 一键调用的玩家意图，调用方不
        /// 需要先查询状态再决定是否可以调用。
        /// </para>
        /// <para>
        /// 已知限制（ADR-0092"后果"一节）：整条运行期记录被移除，包含累计完成次数与最近交付日——
        /// 若该任务是可重复任务且此前已经交付过若干次（历史累计完成次数 &gt; 0），玩家重新接取本轮
        /// 又中途放弃，会连带丢失此前全部交付历史（"是否完成过"一类查询会从"曾经完成过"变回"从未
        /// 完成"）。这是"放弃即回到从未接取"这一设计决策的直接推论，不在本次范围内单独处理，见
        /// ADR-0092"备选方案"一节为何不选。
        /// </para>
        /// <para>
        /// C# 8 默认接口成员（ABI 只加法，新增契约成员必须带默认实现）：默认实现恒抛
        /// <see cref="System.NotSupportedException"/>——语义是"该宿主实现不支持玩家主动放弃这一
        /// 能力"，与既有查询类默认成员（返回 null/空集合）不同：放弃是一个会改变运行期状态、会发
        /// 事件的写操作，静默降级为"什么都不做但返回 false"会让调用方（UI）误以为放弃已经生效，
        /// 因此不适用同一套"静默降级"惯例，未实现本能力的宿主应当让调用方明确得知这个操作不可用。
        /// 生产实现 <see cref="QuestHost"/> 提供真正的实现。
        /// </para>
        /// </summary>
        bool Abandon(Id unitId, Id questId) =>
            throw new System.NotSupportedException(
                "该 IQuestHost 实现未提供 Abandon（玩家主动放弃任务）能力，见 ADR-0092");

        /// <summary>该单位全部有持久化记录（曾经 Accept 过，含已完成/已失败）的任务进度快照，
        /// 供存档/UI 展示（见 <see cref="QuestPersistable"/>）。</summary>
        IReadOnlyList<QuestProgress> GetLog(Id unitId);

        /// <summary>
        /// 消费方反馈第 2 条根治：<paramref name="questId"/> 对应任务定义各条目标的需求数量，下标与
        /// <see cref="GetLog"/> 返回的 <see cref="QuestProgress.ObjectiveCounts"/> 对齐——供参考 UI
        /// 任务日志面板（<c>QuestLogPanel</c>）拼"当前/需求"文案用，纯只读查询，不修改任何状态。
        /// 任务定义未登记（含从未存在、或 <see cref="Reload"/> 后已被删除，见 CORE-118 判断记录
        /// "删除定义保留进度"）时返回空列表，不抛异常——调用方（UI 展示层）按"这条目标暂时不知道
        /// 需求数，只展示当前计数"降级，不能因为这类内容态查询失败而让整个面板抛异常。
        /// <para>
        /// 判断记录（ABI 新增默认接口成员，不需要 ADR）：本方法是纯新增的只读查询能力，默认实现
        /// 恒返回空列表——现有 <see cref="IQuestHost"/> 实现（含测试替身）无需改动即可继续编译通过，
        /// 只有 <see cref="QuestHost"/> 覆写为真正从已登记定义读取。参照本仓库对同类"新增只读查询"
        /// 的既有判断（消费方反馈第 4 条"任务指示器"分析：新增只读接口方法向后兼容，不改 schema，
        /// 不属于架构结论变更）。
        /// </para>
        /// </summary>
        IReadOnlyList<int> GetObjectiveRequiredCounts(Id questId) => System.Array.Empty<int>();

        // -----------------------------------------------------------------
        // 任务名/目标描述文本键（消费方反馈第六批，阻塞：任务追踪 HUD 要显示"任务名 + 目标描述 +
        // 进度 + 可交付状态"，前两项此前拿不到——QuestDefinition.TitleKey/QuestObjective.
        // DescriptionKey 两个字段数据侧早已存在，本契约与 QuestLogViewModel 均未转发，逼表现层
        // 自己反查数据表，违反"视图不得自己拼宿主数据"）
        // -----------------------------------------------------------------

        /// <summary>
        /// 按 <paramref name="questId"/> 取该任务定义的标题文本键（<see cref="QuestDefinition.TitleKey"/>），
        /// 供任务追踪 UI 展示任务名。任务未登记（含从未存在、或 <see cref="Reload"/> 后已被删除，同
        /// <see cref="GetObjectiveRequiredCounts"/> 判断记录 CORE-118-QUEST）或该任务未声明
        /// <c>title_key</c> 时返回 <c>null</c>，不抛异常、不编造占位文案——纯只读查询。
        /// <para>
        /// C# 8 默认接口成员：本默认实现恒返回 <c>null</c>——语义是"该宿主实现不提供标题键查询"，
        /// 与 <c>Core.Rules.Common.ISkillHost.GetSkillNameKey</c> 既有的"查询类默认实现允许显式
        /// 降级"惯例一致，供未实现本能力的 <see cref="IQuestHost"/>（旧版本编译产物、未升级的自定义
        /// 实现、测试替身）源码/二进制兼容。生产实现 <see cref="QuestHost"/> 用显式接口实现转发到
        /// 一个同名公开方法（不用隐式实现是刻意的——隐式实现会把该公开方法的物理 IL 属性从普通实例
        /// 方法改写成 <c>virtual sealed</c>，被 <c>toolchain/abi_surface</c> 静态签名比对误判为
        /// 破坏，同 <c>ISkillHost.GetSkillNameKey</c> 判断记录"ISkillHost 显式接口实现"一节）。任何
        /// 组合/包装 <see cref="IQuestHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个降级
        /// 默认值——同 <c>ISkillHost.GetSkillReadiness</c> 判断记录"框架内
        /// InterfaceDefaultMemberForwardingTests 门禁"。
        /// </para>
        /// </summary>
        Id? GetQuestTitleKey(Id questId) => null;

        /// <summary>
        /// 按 <paramref name="questId"/> + <paramref name="objectiveIndex"/> 取该任务某条目标的描述
        /// 文本键（<see cref="QuestObjective.DescriptionKey"/>），下标与 <see cref="GetLog"/> 返回的
        /// <see cref="QuestProgress.ObjectiveCounts"/>/<see cref="GetObjectiveRequiredCounts"/> 对齐。
        /// 任务未登记、<paramref name="objectiveIndex"/> 越界、或该条目标未声明 <c>description_key</c>
        /// 时均返回 <c>null</c>，不抛异常、不编造占位文案——三种情形对调用方（任务追踪 UI）而言都是
        /// "这条目标暂时没有描述文案可显示"，不需要相互区分。
        /// <para>
        /// C# 8 默认接口成员：默认实现与降级/显式接口实现惯例同 <see cref="GetQuestTitleKey"/>。
        /// </para>
        /// </summary>
        Id? GetObjectiveDescriptionKey(Id questId, int objectiveIndex) => null;

        /// <summary>该单位当前全部 Active 任务里尚未达标的目标（questId, objectiveIndex, targetRef）
        /// 三元组列表（见 08 第 2.3 节"每个激活任务的每个未完成目标，可关联一个地图标记……逻辑层只
        /// 暴露当前激活目标的位置查询接口"——本方法只给出 targetRef，具体位置由调用方按 targetRef
        /// 对应实体查询，本模块不涉及空间查询）。</summary>
        IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> GetActiveObjectives(Id unitId);

        /// <summary>
        /// 驱动 <c>start_method=auto</c>/<c>turn_in_method=auto</c> 两类自动转移（见 08 第 2.1 节
        /// 该两字段 <c>auto</c> 取值）：把当前 Available 且 <c>start_method=auto</c> 的任务自动
        /// Accept；把当前 ObjectivesComplete 且 <c>turn_in_method=auto</c> 的任务自动 TurnIn。
        /// 调用方按需（如每次场景加载、每次事件驱动进度更新之后）调用；本方法不会被
        /// <see cref="UpdateProgress"/> 自动触发（避免"进度更新"这一相对高频操作里隐式发生任务
        /// 交付这类有较重副作用（结算奖励）的转移，见判断记录）。
        /// </summary>
        void Update(Id unitId);
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 4 条（2026-09-20，见
    /// architecture/落地计划/消费方反馈-2026-09-20-任务指示器只读查询.md）：任务给予者指示器状态——
    /// 框架此前未规划"某个 NPC 身上是否有可接/进行中/可交付任务"这一聚合概念，纯能力空白（不是数据
    /// 缺口），见 <see cref="QuestGiverIndicatorQuery"/> 判断记录、
    /// [ADR-0045](../../../../architecture/adr/0045-任务给予者指示器只读查询.md)。
    /// <para>
    /// 四态含义（<see cref="QuestState"/> 的聚合视角，见 <see cref="QuestGiverIndicatorQuery.Evaluate"/>）：
    /// <see cref="None"/> 给定任务集合里没有一条处于 Available/Active/ObjectivesComplete；
    /// <see cref="Available"/> 至少一条 <see cref="QuestState.Available"/>（有可接任务）；
    /// <see cref="InProgress"/> 至少一条 <see cref="QuestState.Active"/>（有进行中但未完成的任务）；
    /// <see cref="Completable"/> 至少一条 <see cref="QuestState.ObjectivesComplete"/>（有可交付的已完成
    /// 任务）。同一给予者同时具备多种状态时，按 <see cref="Completable"/> &gt; <see cref="Available"/>
    /// &gt; <see cref="InProgress"/> &gt; <see cref="None"/> 的固定优先级只返回其中一个，理由见
    /// <see cref="QuestGiverIndicatorQuery.Evaluate"/> 判断记录与 ADR-0045"优先级"一节。
    /// </para>
    /// <para>
    /// <b>呈现边界（设计层拍板，写死于此不可推翻）</b>：本枚举只是状态判定结果，具体呈现（头顶图标、
    /// 颜色、动画、贴图切换等）一律归游戏侧决定，框架不内置、不假设任何视觉规则。
    /// </para>
    /// </summary>
    public enum QuestGiverIndicatorState
    {
        /// <summary>给定任务集合里没有一条对该玩家处于 Available/Active/ObjectivesComplete
        /// （可能全部是 Unavailable/Failed/终态 TurnedIn，也可能给定集合本身为空）。</summary>
        None,

        /// <summary>至少一条任务当前 <see cref="QuestState.Available"/>（有可接任务），且没有任何一条
        /// 处于 <see cref="QuestState.ObjectivesComplete"/>（否则按优先级返回 <see cref="Completable"/>）。</summary>
        Available,

        /// <summary>至少一条任务当前 <see cref="QuestState.Active"/>（有进行中但未完成的任务），且没有
        /// 任何一条处于 Available/ObjectivesComplete（否则按优先级分别返回 <see cref="Available"/>/
        /// <see cref="Completable"/>）。</summary>
        InProgress,

        /// <summary>至少一条任务当前 <see cref="QuestState.ObjectivesComplete"/>（有可交付的已完成任务）
        /// ——四态中优先级最高，只要命中就恒返回本值。</summary>
        Completable,
    }

    /// <summary>
    /// 只读聚合查询：某个任务给予者（用现有单位/NPC 身份 <see cref="Id"/> 表示，框架不为"给予者"另建
    /// id 体系，见判断记录"给予者身份如何表示"）对当前玩家单位处于何种 <see
    /// cref="QuestGiverIndicatorState"/>。完全基于既有 <see cref="IQuestHost.GetState"/> 逐条求值
    /// 聚合，不读取/新增任何持久化状态，不改 <c>quest.def</c> schema，不修改 <see cref="IQuestHost"/>
    /// 既有契约（新增独立静态类，不新增接口成员——理由同 <see cref="QuestPrerequisitePreview"/>/
    /// <c>DialogStoryPreview</c> 判断记录"为何不改宿主接口"：本查询与运行期状态机契约正交，且
    /// <c>IQuestHost</c> 在测试与表现层各有独立假实现，直接加接口成员会破坏既有假实现的 ABI 兼容）。
    /// <para>
    /// <b>给予者身份如何表示</b>：框架的 <c>quest.def</c> schema 从未登记"这个任务由哪个 NPC 给出"
    /// ——gossip 菜单的 <c>accept_quest</c>/<c>turn_in_quest</c> 动作（见
    /// <c>core/gameplay/dialog</c> <c>dialog.gossip_menu</c> schema）只携带 <c>quest.def</c> 引用，
    /// NPC → 任务集合的映射完全是内容/游戏侧的编排产物，框架没有可读的"给予者拥有哪些任务"登记表可
    /// 供反查（同消费方反馈第 4 条核实结论）。本查询因此把"给予者对应哪些任务"作为调用方输入
    /// （<paramref name="giverQuestIds"/>，通常是游戏侧按自己的 gossip 菜单/给予者配置静态编排得到），
    /// 不再单独要一个 giverId 参数——这正是"给予者身份用现有单位身份表示，不新造 id 体系"的直接落地：
    /// 调用方在拿到某个 NPC 单位 id 时，自行决定这个 NPC 关联哪些 <c>quest.def</c> id 并传入本方法，
    /// 框架只负责这些任务 id 在当前玩家状态下的聚合与优先级裁决。
    /// </para>
    /// <para>
    /// <b>未覆盖："等级不足预览"等中间态（明确边界，不硬造）</b>：<c>quest.def.prerequisite</c> 是任意
    /// 布尔表达式，可能混合 <c>quest.*</c>/<c>player.*</c>/<c>world.*</c> 等多个分组；框架现有的只读
    /// 前置预演能力 <see cref="QuestPrerequisitePreview"/> 明确只解析 <c>quest.*</c> 分组引用构成的
    /// 任务链结构（"这个任务点名了哪些其它任务"），不对整条 <c>prerequisite</c> 表达式做真正的布尔求值，
    /// 无法据此低成本区分"完全不可接"与"只差某个具体条件（如等级）"这类中间态——要做到这一点需要一套
    /// 新的表达式子集求值/参数反解语义（如识别 <c>player.level(...)</c> 比较子表达式并单独求值），是
    /// 与本次"复用既有状态查询、不新增能力"的任务边界不符的新能力，本次不做，见 ADR-0045"边界"一节。
    /// 因此本查询的 <see cref="QuestGiverIndicatorState.None"/> 不区分"prerequisite 完全不满足"与
    /// "只差一点点（如等级差 1 级）"——两者对调用方而言都是"当前不可接"，如需要更细粒度的预览，调用方
    /// 需自行在游戏侧对 <c>prerequisite</c> 做语义解读，不属于本查询职责。
    /// </para>
    /// </summary>
    public static class QuestGiverIndicatorQuery
    {
        /// <param name="questHost">既有任务状态查询来源，通常是游戏组装根持有的 <see cref="IQuestHost"/>
        /// 实例（生产环境即 <see cref="QuestHost"/>）。</param>
        /// <param name="unitId">玩家（或其它任务持有者）单位身份，同 <see cref="IQuestHost.GetState"/>
        /// 的 <c>unitId</c> 语义。</param>
        /// <param name="giverQuestIds">该给予者关联的任务 id 集合，由调用方按内容编排给出（见类型判断
        /// 记录"给予者身份如何表示"）；允许为空集合（恒返回 <see cref="QuestGiverIndicatorState.None"/>），
        /// 允许包含重复 id（不影响结果，只是被多算一次状态查询）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="questHost"/> 或
        /// <paramref name="giverQuestIds"/> 为 <c>null</c>。</exception>
        public static QuestGiverIndicatorState Evaluate(
            IQuestHost questHost,
            Id unitId,
            IReadOnlyCollection<Id> giverQuestIds)
        {
            if (questHost == null) throw new ArgumentNullException(nameof(questHost));
            if (giverQuestIds == null) throw new ArgumentNullException(nameof(giverQuestIds));

            var hasCompletable = false;
            var hasAvailable = false;
            var hasInProgress = false;

            foreach (var questId in giverQuestIds)
            {
                switch (questHost.GetState(unitId, questId))
                {
                    case QuestState.ObjectivesComplete:
                        hasCompletable = true;
                        break;
                    case QuestState.Available:
                        hasAvailable = true;
                        break;
                    case QuestState.Active:
                        hasInProgress = true;
                        break;
                        // Unavailable/Failed/TurnedIn：对指示器没有贡献，落到 None。
                }
            }

            // 优先级恒定、确定性写死（ADR-0045"优先级"一节）：Completable > Available > InProgress >
            // None。理由：可交付通常意味着"回去交任务"是当前最紧迫的下一步，优先于"还有新任务可接"；
            // 可接优先于进行中，因为进行中任务通常已有其它追踪手段（任务日志、
            // IQuestHost.GetActiveObjectives 给出的当前激活目标），指示器在"进行中"这一态上的边际
            // 提醒价值低于"有新交互可做"。
            if (hasCompletable) return QuestGiverIndicatorState.Completable;
            if (hasAvailable) return QuestGiverIndicatorState.Available;
            if (hasInProgress) return QuestGiverIndicatorState.InProgress;
            return QuestGiverIndicatorState.None;
        }
    }
}

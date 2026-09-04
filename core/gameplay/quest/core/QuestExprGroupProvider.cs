using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest</c> Expr 宿主引用分组的实现（见 04 第 6.2 节示例 <c>quest.is_active(quest.deliver_letter)</c>、
    /// 08 第 3.3 节"显隐条件……宿主分组复用 04 第 6.2 节"）。
    /// <para>
    /// 契约缺口判断记录：<c>core/rules/expr_host.IExprGroupProvider.Query(key, args)</c> 不携带
    /// <c>selfId</c>（见该接口注释——三个分组的查询"整体委托"，不像 <c>self</c>/<c>target</c> 分组
    /// 那样天然绑定到 <c>IExprHostFactory.CreateFor</c> 传入的具体单位），但"任务状态"天然是
    /// "某个单位的任务日志"，必须知道是哪个单位。本架构是单机单人游戏（见 00 架构总则"单机场景"、
    /// 10 第 1 节"不存在账号层"），<c>quest</c>/<c>player</c> 两个分组按 04 第 6.2 节原文语义
    /// （"玩家任务状态"/"玩家角色数据"）本就专指"当前玩家"，因此本类型按任务书拍板构造期注入一个
    /// <see cref="Func{Id}"/> 解析出当前玩家单位 id，而不是要求调用点在 Expr 文本里显式传参——
    /// 这样 <c>quest.is_active(quest.deliver_letter)</c> 的唯一参数仍然只是任务 id，与 04 示例
    /// 文本形态一致。
    /// </para>
    /// </summary>
    public sealed class QuestExprGroupProvider : IExprGroupProvider
    {
        private readonly IQuestHost _questHost;
        private readonly Func<Id> _playerUnitProvider;
        private readonly IExprDiagnostics? _diagnostics;

        public QuestExprGroupProvider(IQuestHost questHost, Func<Id> playerUnitProvider, IExprDiagnostics? diagnostics = null)
        {
            _questHost = questHost ?? throw new ArgumentNullException(nameof(questHost));
            _playerUnitProvider = playerUnitProvider ?? throw new ArgumentNullException(nameof(playerUnitProvider));
            _diagnostics = diagnostics;
        }

        public ExprValue Query(string key, IReadOnlyList<ExprValue> args)
        {
            var player = _playerUnitProvider();

            switch (key)
            {
                case "is_active":
                    return ExprValue.OfBool(_questHost.GetState(player, RequireQuestIdArg(key, args)) == QuestState.Active);

                case "is_available":
                    return ExprValue.OfBool(_questHost.GetState(player, RequireQuestIdArg(key, args)) == QuestState.Available);

                case "is_completed":
                    return ExprValue.OfBool(IsCompleted(player, RequireQuestIdArg(key, args)));

                case "objective_progress":
                    return ExprValue.OfInt(ObjectiveProgress(player, args));

                default:
                    _diagnostics?.Warn($"未知的 quest.{key} 引用，按默认值 Bool(false) 处理");
                    return ExprValue.OfBool(false);
            }
        }

        /// <summary><c>is_completed</c>：任务日志状态机当前是 TurnedIn，或历史累计完成次数 > 0
        /// （见任务书拍板"TurnedIn 或 CompletionCount&gt;0"——可重复任务交付后会回落到 Available/
        /// Unavailable，此时当前状态已不是 TurnedIn，但仍然"完成过"）。</summary>
        private bool IsCompleted(Id player, Id questId)
        {
            if (_questHost.GetState(player, questId) == QuestState.TurnedIn)
            {
                return true;
            }

            foreach (var progress in _questHost.GetLog(player))
            {
                if (progress.QuestId.Equals(questId))
                {
                    return progress.CompletionCount > 0;
                }
            }
            return false;
        }

        private int ObjectiveProgress(Id player, IReadOnlyList<ExprValue> args)
        {
            if (args == null || args.Count < 2 || args[0].Kind != ExprValueKind.Id || !args[1].IsNumeric)
            {
                throw new ArgumentException(
                    "quest.objective_progress 需要两个参数：questId（Id）与 objectiveIndex（Int/Number）",
                    nameof(args));
            }

            var questId = args[0].AsId;
            var index = (int)args[1].ToDouble();

            foreach (var progress in _questHost.GetLog(player))
            {
                if (progress.QuestId.Equals(questId))
                {
                    return index >= 0 && index < progress.ObjectiveCounts.Count ? progress.ObjectiveCounts[index] : 0;
                }
            }
            return 0;
        }

        private static Id RequireQuestIdArg(string key, IReadOnlyList<ExprValue> args)
        {
            if (args == null || args.Count < 1 || args[0].Kind != ExprValueKind.Id)
            {
                throw new ArgumentException($"quest.{key} 需要恰好一个 Id 类型的 questId 参数", nameof(args));
            }
            return args[0].AsId;
        }
    }
}

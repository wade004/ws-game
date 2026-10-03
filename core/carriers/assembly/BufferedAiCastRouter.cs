using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.InputMap;
using Core.Rules.Ai;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// AI 施法经输入缓冲（ADR-0143，<see cref="CarriersFeelOptions.AiIntentsThroughBuffer"/>，缺省关）：把 <see cref="IAiCastRouter"/> 收到的施法决策
    /// 提交为该行动者缓冲里的一条记录（合成动作 <see cref="ActionId"/>，类别为技能），由缓冲按与玩家相同的优先级、过期与取消窗口规则取用；
    /// 接受时 <see cref="BufferedActionIntentSink"/> 把记录携带的技能与 <c>targets</c> 并入生成的 <c>cast</c> 意图。
    /// <para>
    /// 判断记录（一 tick 延迟）：见 <see cref="IAiCastRouter"/>——AI 在步骤 2 决策，缓冲下一个 tick 的步骤 1 取用。
    /// </para>
    /// <para>
    /// 判断记录（不接管的单位）：玩家本地行动者不经本路由（它的施法来自输入）。路由对"没有缓冲"的世界（缓冲未建）返回 false，AI 回落到直接施法。
    /// </para>
    /// </summary>
    public sealed class BufferedAiCastRouter : IAiCastRouter
    {
        /// <summary>合成的 AI 施法输入动作 id（装配在选项开启时自动声明它，游戏不必写数据）。</summary>
        public static readonly Id ActionId = new Id("input.action.ai_cast");

        private readonly InputBufferHost _buffer;

        public BufferedAiCastRouter(InputBufferHost buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        /// <summary>合成动作的定义（技能类；缺省绑定只是满足定义的非空约束，它只进输入缓冲、不进输入映射，不会被任何键触发）。</summary>
        public static ActionDefinition CreateDefinition() => new ActionDefinition(
            ActionId, ActionKind.Button, new[] { "key:ai_cast" }, "default", "AI 施法（经输入缓冲）",
            ActionClass.Skill, bufferMs: null, priority: null, holdThresholdMs: null,
            repeatPolicy: InputRepeatPolicy.Refresh, faceOnAccept: null, graceConditions: null, skillSlot: null);

        public bool TrySubmit(Id unitId, Id skillId, IReadOnlyList<Id> targets)
        {
            if (_buffer.GetDefinition(ActionId) == null) return false;

            JsonObject? extra = null;
            if (targets != null && targets.Count > 0)
            {
                var list = new List<JsonValue>(targets.Count);
                for (var i = 0; i < targets.Count; i++) list.Add(new JsonString(targets[i].Value));
                extra = new JsonObjectBuilder().Add("targets", new JsonArray(list)).Build();
            }

            _buffer.SubmitSkill(unitId, ActionId, skillId, extra);
            return true;
        }
    }
}

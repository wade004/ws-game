using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 一条动作声明：动作 id、类型、默认绑定、重绑分组、说明（见 03_运行时骨架.md 第 7 节、
    /// 第 9 节 <c>InputMapHost.declareActionSet(actionSetId, actions: List&lt;ActionDefinition&gt;)</c>）。
    /// 对应数据表 <c>found.input_action</c> 一行，见 <see cref="InputActionSchema"/>、
    /// <see cref="FromRecord"/>。
    /// </summary>
    public sealed class ActionDefinition
    {
        /// <summary>动作 id，如 <c>input.action.move</c>。</summary>
        public Id ActionId { get; }

        public ActionKind Kind { get; }

        /// <summary>默认绑定字符串列表，语法见 <see cref="BindingParser"/>；同一动作的多条绑定
        /// 之间是"或"关系（任一激活即视为该动作激活，见 <c>IInputMapHost</c> 判断记录）。</summary>
        public IReadOnlyList<string> DefaultBindings { get; }

        /// <summary>重绑分组：同组内的绑定互相独占（见 03 第 7 节"冲突检测"、
        /// <c>IInputMapHost.Rebind</c>）。未声明时默认 <c>"default"</c>。</summary>
        public string RebindGroup { get; }

        public string? Description { get; }

        public ActionDefinition(
            Id actionId,
            ActionKind kind,
            IReadOnlyList<string> defaultBindings,
            string rebindGroup = "default",
            string? description = null)
        {
            if (defaultBindings == null || defaultBindings.Count == 0)
            {
                throw new ArgumentException($"动作 \"{actionId}\" 的 DefaultBindings 不能为空", nameof(defaultBindings));
            }
            if (string.IsNullOrEmpty(rebindGroup))
            {
                throw new ArgumentException($"动作 \"{actionId}\" 的 RebindGroup 不能为空", nameof(rebindGroup));
            }

            ActionId = actionId;
            Kind = kind;
            DefaultBindings = defaultBindings;
            RebindGroup = rebindGroup;
            Description = description;
        }

        /// <summary>
        /// 从 <see cref="DataRecord"/>（<c>found.input_action</c> 表的一行）构造。
        /// <para>
        /// 判断记录：<c>found.input_action</c> 按登记表处理，主键字段名为 <c>key</c>
        /// 而非一般内容表的 <c>id</c>（见本模块 README、<c>data/README.md</c>"记录主键"、
        /// <c>schema/found.input_action.md</c>）——字段里的取值仍是一个合法 <see cref="Id"/>
        /// （如 <c>input.action.move</c>），只是承载它的 JSON 字段名按登记表惯例叫 <c>key</c>，
        /// 与 <c>found.event_catalog</c> 同一惯例。
        /// </para>
        /// </summary>
        public static ActionDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var actionId = record.GetId("key");
            var kindText = record.GetString("kind");
            var kind = ParseKind(record, kindText);

            var bindingsArray = record.GetArray("default_bindings");
            var bindings = new List<string>(bindingsArray.Count);
            for (int i = 0; i < bindingsArray.Count; i++)
            {
                if (!(bindingsArray[i] is JsonString s))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "default_bindings",
                        $"第 {i} 个元素不是字符串");
                }
                bindings.Add(s.Value);
            }

            var rebindGroup = record.TryGetString("rebind_group", out var rg) ? rg : "default";
            var description = record.TryGetString("description", out var d) ? d : null;

            return new ActionDefinition(actionId, kind, bindings, rebindGroup, description);
        }

        private static ActionKind ParseKind(DataRecord record, string kindText)
        {
            switch (kindText)
            {
                case "button": return ActionKind.Button;
                case "axis1d": return ActionKind.Axis1D;
                case "axis2d": return ActionKind.Axis2D;
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "kind",
                        $"取值 \"{kindText}\" 不是合法枚举（button|axis1d|axis2d）");
            }
        }
    }
}

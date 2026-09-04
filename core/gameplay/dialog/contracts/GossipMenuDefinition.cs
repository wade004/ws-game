using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;

namespace Core.Gameplay.Dialog
{
    /// <summary>一条 gossip 菜单动作（见 08 第 3.1 节 <c>GossipAction {kind, ref?, params?}</c>
    /// 结构与十种 kind 的语义表）。<see cref="Ref"/> 依 <see cref="Kind"/> 指向不同目标（任务 id、
    /// 传送点、世界标志键、遭遇 id、技能 id、剧情树 id、脚本钩子 id），<c>save</c> 不需要
    /// <see cref="Ref"/>（见 <see cref="DialogActionKinds.RequiresRef"/>）。<see cref="Params"/> 目前
    /// 只有 <c>set_flag</c> 用到（承载写入的值），其余动作为 null。</summary>
    public sealed class GossipActionDef
    {
        public DialogActionKind Kind { get; }

        public Id? Ref { get; }

        public JsonObject? Params { get; }

        public GossipActionDef(DialogActionKind kind, Id? @ref, JsonObject? @params)
        {
            if (DialogActionKinds.RequiresRef(kind) && !@ref.HasValue)
            {
                throw new ArgumentException($"gossip 动作 {kind} 需要 ref 字段（见 08 第 3.1 节）", nameof(@ref));
            }
            Kind = kind;
            Ref = @ref;
            Params = @params;
        }
    }

    /// <summary>一条 gossip 菜单选项（见 08 第 3.1 节 <c>GossipOption {textKey, visibleIf?, actions}</c>）。</summary>
    public sealed class GossipOption
    {
        public Id TextKey { get; }

        public ExprNode? VisibleIf { get; }

        public IReadOnlyList<GossipActionDef> Actions { get; }

        public GossipOption(Id textKey, ExprNode? visibleIf, IReadOnlyList<GossipActionDef> actions)
        {
            TextKey = textKey;
            VisibleIf = visibleIf;
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }
    }

    /// <summary><c>dialog.gossip_menu</c> 一条记录的内存态表示（见 08 第 3.1 节）。</summary>
    public sealed class GossipMenuDefinition
    {
        public Id Id { get; }

        public IReadOnlyList<GossipOption> Options { get; }

        public GossipMenuDefinition(Id id, IReadOnlyList<GossipOption> options)
        {
            Id = id;
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public static GossipMenuDefinition FromRecord(Core.Foundation.DataRegistry.DataRecord record, IExprSchema exprSchema)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (exprSchema == null) throw new ArgumentNullException(nameof(exprSchema));

            var id = record.GetId("id");
            var optionsArray = record.GetArray("options");
            var options = new List<GossipOption>(optionsArray.Count);
            foreach (var item in optionsArray)
            {
                if (!(item is JsonObject o))
                {
                    throw new FormatException($"dialog.gossip_menu[{id}].options 的元素必须是对象");
                }
                options.Add(ParseOption(id, o, exprSchema));
            }
            return new GossipMenuDefinition(id, options);
        }

        private static GossipOption ParseOption(Id menuId, JsonObject o, IExprSchema exprSchema)
        {
            if (!o.TryGetValue("text_key", out var tk) || !(tk is JsonString tkStr) || !Id.TryParse(tkStr.Value, out var textKey))
            {
                throw new FormatException($"dialog.gossip_menu[{menuId}].options[].text_key 缺失或不是合法 Id");
            }

            ExprNode? visibleIf = null;
            if (o.TryGetValue("visible_if", out var vi) && vi is JsonString viStr && !string.IsNullOrEmpty(viStr.Value))
            {
                visibleIf = ExprParser.Parse(viStr.Value, exprSchema);
            }

            var actions = new List<GossipActionDef>();
            if (o.TryGetValue("actions", out var actionsVal) && actionsVal is JsonArray actionsArr)
            {
                foreach (var a in actionsArr)
                {
                    if (!(a is JsonObject actionObj))
                    {
                        throw new FormatException($"dialog.gossip_menu[{menuId}].options[].actions 的元素必须是对象");
                    }
                    actions.Add(ParseAction(menuId, actionObj));
                }
            }

            return new GossipOption(textKey, visibleIf, actions);
        }

        private static GossipActionDef ParseAction(Id menuId, JsonObject o)
        {
            if (!o.TryGetValue("kind", out var kindVal) || !(kindVal is JsonString kindStr) ||
                !DialogActionKinds.TryParse(kindStr.Value, out var kind))
            {
                throw new FormatException($"dialog.gossip_menu[{menuId}].options[].actions[].kind 缺失或取值非法");
            }

            Id? refId = null;
            if (o.TryGetValue("ref", out var refVal) && refVal is JsonString refStr && Id.TryParse(refStr.Value, out var parsedRef))
            {
                refId = parsedRef;
            }

            JsonObject? paramsObj = o.TryGetValue("params", out var p) && p is JsonObject po ? po : null;

            return new GossipActionDef(kind, refId, paramsObj);
        }
    }
}

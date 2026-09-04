using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// 某个命名空间前缀下标志值应满足的 Expr 标量类型（供 <see cref="WorldStateOptions.EnforceSchema"/>
    /// 开启时的运行期校验使用）。对应 <c>world.flag_schema</c> 登记表某一行的 <c>id</c>（命名空间前缀
    /// 或具体标志键）与 <c>kind</c> 字段（见 <c>schema/WorldStateSchemas.cs</c>），但本类型不直接读取
    /// <c>IDataRegistry</c>——把数据表行转换成本类型列表是游戏组装根的职责（同 <c>core/rules/expr_host</c>
    /// <c>RulesExprHostFactory</c> 的 <c>extraGroups</c> 构造参数由组装根注入的惯例），本模块不对
    /// <c>data_registry</c> 产生编译期依赖。
    /// </summary>
    public readonly struct WorldFlagSchemaEntry
    {
        /// <summary>命名空间前缀或具体标志键，如 <c>world.gobj</c>（覆盖其下全部
        /// <c>world.gobj.&lt;实例id&gt;.*</c>）或 <c>world.bridge.repaired</c>（精确到单个标志）。</summary>
        public Id NamespacePrefix { get; }

        /// <summary>该前缀/标志键下取值必须满足的 Expr 标量类型。</summary>
        public ExprValueKind Kind { get; }

        public WorldFlagSchemaEntry(Id namespacePrefix, ExprValueKind kind)
        {
            NamespacePrefix = namespacePrefix;
            Kind = kind;
        }
    }

    /// <summary>
    /// <see cref="WorldState"/> 的构造期策略配置（见 01 第 L4 模块表 <c>world_state</c> 行"策略配置项：
    /// 命名空间划分方式"——<see cref="EnforceSchema"/>/<see cref="SchemaEntries"/> 是该策略配置项的落地；
    /// <see cref="Mode"/> 是任务书额外拍板的事件派发时机配置）。
    /// </summary>
    public sealed class WorldStateOptions
    {
        /// <summary><c>world.flag_changed</c> 事件的派发时机（任务书拍板）。</summary>
        public enum DispatchMode
        {
            /// <summary><see cref="IWorldState.Set"/>/<see cref="IWorldState.Remove"/> 调用内同步经
            /// <c>IEventBus.PublishImmediate</c> 派发，<see cref="IWorldState.OnChanged"/> 回调同步执行。
            /// 默认值——世界标志的写入多来自对话/任务/脚本等非 tick 上下文，且 07 第 3.4 节要求
            /// <c>gobj.state_changed</c> 与一次 <c>WorldState.set</c> 对应，立即派发保证二者的相对顺序
            /// 不因批处理而错乱（判断记录见 README）。</summary>
            Immediate,

            /// <summary>经 <c>IEventBus.Enqueue</c> 入队，随调用方下一次 <c>IEventBus.DispatchPending</c>
            /// 才实际派发，<see cref="IWorldState.OnChanged"/> 回调同样延后到那一刻才执行——供需要与
            /// tick 内其它批处理事件统一时机的调用方选用。</summary>
            Enqueue,
        }

        /// <summary>默认 <see cref="DispatchMode.Immediate"/>，见该枚举成员注释判断记录。</summary>
        public DispatchMode Mode { get; set; } = DispatchMode.Immediate;

        /// <summary>
        /// 是否在 <see cref="IWorldState.Set"/> 时校验 <see cref="SchemaEntries"/>
        /// （<c>world.flag_schema</c> 登记表内容）：要求 <c>flagKey</c> 命中某条已登记的命名空间前缀，
        /// 且写入值的 <see cref="ExprValueKind"/> 与该条登记的 <see cref="WorldFlagSchemaEntry.Kind"/>
        /// 一致，否则抛 <see cref="ArgumentException"/>。默认 <c>false</c>——04 第 1.1 节表清单原文
        /// 把 <c>world.flag_schema</c> 描述为"非运行态数据，仅作文档化 schema"，本模块因此默认不做强制，
        /// 只把它作为可选的运行期防呆开关（任务书拍板"提供运行期可选校验"）。
        /// </summary>
        public bool EnforceSchema { get; set; }

        /// <summary><see cref="EnforceSchema"/> 为 <c>true</c> 时使用的登记表快照；默认空列表
        /// （此时任何 <see cref="IWorldState.Set"/> 调用都会因"找不到匹配前缀"而抛异常——调用方
        /// 打开 <see cref="EnforceSchema"/> 前必须先提供非空 <see cref="SchemaEntries"/>）。多条前缀
        /// 匹配同一个 <c>flagKey</c> 时取前缀最长（最具体）的一条。</summary>
        public IReadOnlyList<WorldFlagSchemaEntry> SchemaEntries { get; set; } = Array.Empty<WorldFlagSchemaEntry>();
    }
}

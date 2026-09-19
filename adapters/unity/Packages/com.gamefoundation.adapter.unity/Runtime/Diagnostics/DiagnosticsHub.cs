#nullable enable
// 诊断契约统一转发机制（feat/diagnostics-contracts-unification，架构结论见
// architecture/adr/0042-诊断契约统一转发到宿主控制台.md）。
//
// 背景：presentation/vfx_sfx.IPresentationDiagnostics 一套契约（VfxPlayer/SfxPlayer/FeedbackBinder/
// HitFrameSyncPolicy/ViewBinder/CompositeFeedbackSink 六条链路）此前分三批各自手工接入
// PresentationAssemblyDiagnosticsForwarder（同目录 Presentation/PresentationDiagnosticsConsoleForwarding.cs）；
// 全仓普查（见该 ADR 正文引用的普查清单）发现全仓另有 20+ 个同惯例的 I*Diagnostics 契约
// （event_bus/hook_registry/save_system/scene_router/app_lifecycle/input_map/localization 等 L0
// 基础模块，area_trigger/dialog/spawn/death/reward/world_state 等 L4 玩法宿主，power_set/progression
// 等数值模块，combat/skill 等规则模块，presentation/ui 独立契约）同样"只记内存、从不外发到引擎
// 控制台"，且每次都要单独写一个转发器——本文件是这套本该早点抽出来的公共机制。
//
// 判断记录（機制形态：注册制轮询集线器，而不是给每种契约各写一个装饰器）：
// PresentationAssemblyDiagnosticsForwarder 那种做法（装饰器实现同一个接口）要求"这个来源恰好在
// 构造期就能拿到"；但本次普查的多数来源（CombatHost/SkillHost/AreaTriggerHost 等）的诊断参数是
// 构造函数里"要么调用方传，要么内部 new 一个默认实现"的可选参数，默认路径下调用方原本就拿不到
// 引用（本单已给每个这样的 Host 补一个只读 Diagnostics 属性暴露默认实例，ABI 只新增属性，不改
// 构造签名）。改用"注册制"（<see cref="DiagnosticsHub.Register"/> 接收消息列表引用 + 来源名）
// 而不是要求每个来源都实现某个公共接口：这些来源的 Warnings/Errors 集合类型不统一（有的是
// IReadOnlyList&lt;string&gt;，有的是 IReadOnlyList&lt;某个带 Message 字段的 record&gt;，见
// <see cref="ProjectedReadOnlyList{TSource}"/> 判断记录），要求它们都实现同一个新接口意味着要么
// 改造全部来源的公开返回类型（破坏 ABI），要么让新接口只承诺"最小公共能力"从而还是要包一层——不如
// 直接让调用方（装配根）在注册时用一个 lambda/投影把"消息列表"整理成 IReadOnlyList&lt;string&gt;，
// Hub 本身完全不需要认识任何具体诊断契约类型，新增诊断契约接入 Hub 的成本因此降到"装配根加一行
// Register 调用"，不需要改本文件一个字符——这正是任务书要求的"低成本接入"。
//
// 判断记录（复用而非重造去重/开关/级别映射）：<see cref="DiagnosticsHub"/> 内部持有的仍然是
// PresentationDiagnosticsConsoleForwarding.cs 里已经在 dotnet test 侧验证过的
// <see cref="PresentationDiagnosticsConsoleGate"/>/<see cref="IPresentationDiagnosticsConsoleSink"/>
// ——去重键、LRU 上限淘汰、Enabled 开关语义、"恒映射到控制台 Warning 不产生 Error"这条硬约束全部
// 原样复用，不另起一套。本 Hub 相对既有 <see cref="PresentationAssemblyDiagnosticsForwarder"/> 是
// 独立的第三个 gate 实例（各自 500 容量，互不挤占彼此去重表，同 SpriteRigDiagnosticsPump 与
// PresentationAssemblyDiagnosticsForwarder 两者既有的隔离惯例）——本 Hub 覆盖的 20+ 个来源分布在
// 完全不同的模块，共用一个 gate 不会有问题，但与"presentation 装配级单例"和"per-entity rig"两条
// 既有链路分开，避免三者互相挤占。
//
// 判断记录（消息加来源前缀）：既有两条链路（presentation 装配级 5 源、SpriteCharacterRig 逐实体）
// 各自要么只有一个来源、要么消息文本本身已经带实体 id，去重键不加前缀也不容易误撞；本 Hub 一次
// 集线 20+ 个来自不同模块的来源，两个不同模块凑巧写出完全相同的警告文本（如都写"未注入 XXX，已
// 跳过"）会被去重表错误地当成同一条、其中一个来源的首次出现被吞掉。转发文本统一加
// "[来源名] "前缀（Error 级另加 "[error]" 标记，见下方判断记录）先天避免这类误撞，代价是控制台
// 文本比原始消息长一点，可接受。
//
// 判断记录（Error 级仍恒映射到 Warning，但保留可辨识标记）：硬约束不变——全部经
// Debug.LogWarning，不产生 Debug.LogError（Unity Test Framework 会让未预期的 LogError 直接判
// PlayMode 测试失败）。为了不丢失"这条原本是 Error 级"这个信息，转发文本额外加 "[error]" 标记
// （格式 "[来源名][error] 消息"），纯粹是文本层面的区分，不改变实际写入控制台的日志级别。
using System;
using System.Collections;
using System.Collections.Generic;

namespace Adapter.Unity.Diagnostics
{
    /// <summary>
    /// 只读投影视图：把 <c>IReadOnlyList&lt;TSource&gt;</c>（如
    /// <c>Core.Foundation.EventBus.EventDiagnosticsErrorRecord</c> 这类"消息 + 可选异常"的错误记录
    /// 结构）按需转成 <c>IReadOnlyList&lt;string&gt;</c>，供 <see cref="DiagnosticsHub.Register"/>
    /// 使用——不预先整份拷贝/分配新列表（<see cref="this[int]"/> 惰性调用 <paramref name="selector"/>），
    /// 每帧只有新增的那一小段消息会被访问到（见 <see cref="DiagnosticsFeed.Pump"/>），不随累积消息总数
    /// 增长而重复付出整份投影的开销。完全泛型、不认识任何具体诊断契约类型，供任意装配根按需构造。
    /// </summary>
    public sealed class ProjectedReadOnlyList<TSource> : IReadOnlyList<string>
    {
        private readonly IReadOnlyList<TSource> _source;
        private readonly Func<TSource, string> _selector;

        public ProjectedReadOnlyList(IReadOnlyList<TSource> source, Func<TSource, string> selector)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        public string this[int index] => _selector(_source[index]);

        public int Count => _source.Count;

        public IEnumerator<string> GetEnumerator()
        {
            for (var i = 0; i < _source.Count; i++)
            {
                yield return _selector(_source[i]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// 单个诊断来源的记账状态：持有 Warnings/可选 Errors 两个"只增不减"列表的引用（调用方保证——
    /// 这是所有既有 InMemory*Diagnostics 实现的共同惯例，见判断记录），记住各自上次转发到第几条，
    /// <see cref="Pump"/> 时只处理新增的那一段，不重新扫描已转发过的部分。
    /// </summary>
    internal sealed class DiagnosticsFeed
    {
        private readonly string _sourceName;
        private readonly IReadOnlyList<string> _warnings;
        private readonly IReadOnlyList<string>? _errors;
        private int _warnSeen;
        private int _errorSeen;

        public DiagnosticsFeed(string sourceName, IReadOnlyList<string> warnings, IReadOnlyList<string>? errors)
        {
            _sourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
            _warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
            _errors = errors;
        }

        public void Pump(Adapter.Unity.Presentation.PresentationDiagnosticsConsoleGate gate, Adapter.Unity.Presentation.IPresentationDiagnosticsConsoleSink sink)
        {
            for (; _warnSeen < _warnings.Count; _warnSeen++)
            {
                var text = "[" + _sourceName + "] " + _warnings[_warnSeen];
                if (gate.ShouldForward(text))
                {
                    sink.Warn(text);
                }
            }

            if (_errors != null)
            {
                for (; _errorSeen < _errors.Count; _errorSeen++)
                {
                    // 判断记录：Error 级恒映射到控制台 Warning（硬约束），加 "[error]" 标记保留
                    // "这条原本是 Error 级"的可辨识信息，见文件顶部判断记录。
                    var text = "[" + _sourceName + "][error] " + _errors[_errorSeen];
                    if (gate.ShouldForward(text))
                    {
                        sink.Warn(text);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 诊断契约统一转发集线器：装配根按 <see cref="Register"/> 逐个登记诊断来源（一次性、装配期
    /// 完成，见文件顶部判断记录），每帧调用一次 <see cref="Pump"/>（惯例同既有
    /// PresentationAssemblyDiagnosticsForwarder/SpriteRigDiagnosticsPump，紧跟在它们之后同一步调用）。
    /// 新增诊断契约接入本机制的完整成本：装配根在来源可用处加一行 <c>hub.Register(name, x.Warnings,
    /// x.Errors)</c>（无 Errors 的契约传 <c>null</c>）——不需要修改本文件、不需要新增任何接口实现。
    /// </summary>
    public sealed class DiagnosticsHub
    {
        public const int DefaultCapacity = Adapter.Unity.Presentation.PresentationDiagnosticsConsoleGate.DefaultCapacity;

        private readonly List<DiagnosticsFeed> _feeds = new List<DiagnosticsFeed>();
        private readonly Adapter.Unity.Presentation.PresentationDiagnosticsConsoleGate _gate;
        private readonly Adapter.Unity.Presentation.IPresentationDiagnosticsConsoleSink _sink;

        /// <summary>可关闭：默认开启，惯例同 <see cref="Adapter.Unity.Presentation.PresentationDiagnosticsConsoleGate.Enabled"/>
        /// ——本 Hub 是独立于既有两条转发路径的第三个开关（各自的 gate 本就相互独立，见文件顶部
        /// 判断记录）。</summary>
        public bool Enabled
        {
            get => _gate.Enabled;
            set => _gate.Enabled = value;
        }

        public DiagnosticsHub(
            Adapter.Unity.Presentation.IPresentationDiagnosticsConsoleSink sink,
            bool enabled = true,
            int capacity = DefaultCapacity)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _gate = new Adapter.Unity.Presentation.PresentationDiagnosticsConsoleGate(enabled, capacity);
        }

        /// <summary>登记一个诊断来源。<paramref name="warnings"/>/<paramref name="errors"/> 为 <c>null</c>
        /// 时静默跳过、不登记（同 PresentationAssemblyDiagnosticsForwarder 对可选来源"未启用/未提供"
        /// 的既有处理惯例），不阻断其它来源登记。<paramref name="errors"/> 单独为 <c>null</c> 表示该
        /// 契约只有 Warn 一级（如 IPresentationDiagnostics/IUiDiagnostics/IGobjDiagnostics 等），只轮询
        /// Warnings。</summary>
        public void Register(string sourceName, IReadOnlyList<string>? warnings, IReadOnlyList<string>? errors = null)
        {
            if (warnings == null)
            {
                return;
            }

            if (sourceName == null)
            {
                throw new ArgumentNullException(nameof(sourceName));
            }

            _feeds.Add(new DiagnosticsFeed(sourceName, warnings, errors));
        }

        /// <summary>逐一轮询全部已登记来源自上次调用以来新增的警告/错误，经共享 gate 去重后转发到
        /// sink。供三个生产装配入口每帧调用一次（紧跟既有 PresentationAssemblyDiagnosticsForwarder.Pump()/
        /// SpriteRigDiagnosticsPump 之后，同一步骤内完成）。</summary>
        public void Pump()
        {
            for (var i = 0; i < _feeds.Count; i++)
            {
                _feeds[i].Pump(_gate, _sink);
            }
        }
    }
}

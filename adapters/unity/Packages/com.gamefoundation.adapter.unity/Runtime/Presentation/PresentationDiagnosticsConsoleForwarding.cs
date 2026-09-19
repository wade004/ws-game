#nullable enable
// 诊断转发到引擎控制台（feat/diagnostics-console-forward，2026-09-19）：presentation 层运行期诊断
// （Presentation.VfxSfx.Contracts.IPresentationDiagnostics）此前只记进内存（PresentationDiagnosticsRecorder），
// 从不外发到引擎控制台——真实游戏里缺资源就是静默显示占位方块，控制台一行输出都没有（本次 PlayMode
// 排查的观测盲区）。本文件是纯逻辑部分（级别映射/去重/上限淘汰/开关），不引用任何 UnityEngine
// API，供 dotnet test 侧直接验证；真正调用 UnityEngine.Debug 的胶水见同目录
// UnityPresentationDiagnosticsConsoleSink.cs。
//
// 判断记录（改动为什么只落在这里，不动 presentation/）：IPresentationDiagnostics 的全部现有消费方
// （VfxPlayer/SfxPlayer/CompositeFeedbackSink/FeedbackBinder/ViewBinder/HitFrameSyncPolicy）都在各自
// 构造函数里接受可选 `diagnostics` 参数，但 presentation/assembly/PresentationAssembly.cs 装配这些
// 类型时从未把这个参数对外暴露或注入同一个共享实例——各自默认各自 `new PresentationDiagnosticsRecorder()`，
// adapters/unity 拿不到这些实例的引用，无法转发。真正对外公开的诊断源只有
// Presentation.Render.SpriteCharacterRig.Diagnostics 这一个只读属性（该类型未走构造期注入模式，固定
// 用一个内存 recorder 并公开暴露，见该类型顶部"复用 IPresentationDiagnostics"判断记录）。本次改动
// 只覆盖这一个可达诊断源（对应背景里"资源加载失败保留占位方块"的原始场景，
// SpriteCharacterRig.HandleResourceLoadCompleted 判断记录），VfxPlayer/SfxPlayer 等其余诊断源需要
// presentation/assembly 新增一个可选注入点（ABI 只新增字段，但改动面在 presentation/，超出本任务
// "只落在引擎适配层"的声明范围）——标注待设计层确认，不擅自改 presentation/。
//
// 判断记录（级别映射）：IPresentationDiagnostics 契约当前只有 Warn(string) 一个方法，没有 Error 级
// （对照同仓库其它诊断契约，如 Core.Foundation.Expr.IExprDiagnostics 同时有 Warn/Error 两级——
// presentation 这一个刻意只留 Warn，见 IPresentationDiagnostics.cs 类型注释"一律记一条警告，不抛
// 异常"）。因此本次不存在"错误级输入"需要 downgrade：全部转发内容恒为引擎控制台 Warning
// （Debug.LogWarning），不会产生 Debug.LogError。硬约束仍然写在这里备查：若该契约未来新增 Error
// 级（只能以默认接口成员形式新增，ABI 只新增），新增的 Error 路径也必须映射到控制台 Warning，
// 不能映射到 Error——Unity Test Framework 会让未预期的 LogError 直接判测试失败，288 条 PlayMode
// 用例里多条命中资源缺失路径，用 Error 级会大面积误伤（本次任务书硬约束）。
//
// 判断记录（去重键与上限淘汰策略）：去重键就是消息文本本身（不需要拼接级别前缀——只有 Warn 一个
// 级别，见上）。上限用 LRU（最近最少使用）淘汰：登记表满员时淘汰最久未被再次命中的一条腾位置给
// 新消息，而不是"停止去重、此后每次都转发"——长会话下持续产生大量不同去重键（如许多不同实体各自
// 独立的资源缺失消息，见 SpriteCharacterRig 消息文本携带 entity id）时，LRU 保证内存有界，代价是
// 极少数情况下一条早被淘汰的旧消息重新出现会被当作"新消息"再转发一次（可接受——它本来就已经很久
// 没有再出现过，重新提醒不算噪音）。容量默认 500（经验值：远超过一次典型调试会话里会出现的不同诊断
//消息种类数，实践中基本不会触发淘汰；即便触发也只是退化为"该消息许久未见后再次出现时会再提醒一次"，
// 不影响正确性）。
using System;
using System.Collections.Generic;
using Presentation.VfxSfx.Contracts;

namespace Adapter.Unity.Presentation
{
    /// <summary>诊断转发的最终落地目标——抽象掉具体引擎 API，供 <see cref="PresentationDiagnosticsConsoleGate"/>
    /// 相关类型在不引用 UnityEngine 的前提下描述"转发到控制台"这件事，也便于单测用假实现断言调用
    /// 次数/参数。生产实现见 <c>UnityPresentationDiagnosticsConsoleSink</c>（同目录，引用
    /// UnityEngine.Debug）。</summary>
    public interface IPresentationDiagnosticsConsoleSink
    {
        void Warn(string message);
    }

    /// <summary>去重 + 上限淘汰 + 开关的纯逻辑核心，不持有任何 sink/inner 引用——只回答"这条消息现在
    /// 该不该被转发"，调用方（<see cref="PresentationDiagnosticsConsoleForwarder"/>/
    /// <see cref="SpriteRigDiagnosticsPump"/>）负责真正调用 sink。单独拆出便于两个调用方共享同一份
    /// 去重状态与单测覆盖（见判断记录：容量/淘汰策略）。</summary>
    public sealed class PresentationDiagnosticsConsoleGate
    {
        /// <summary>见文件顶部判断记录"去重键与上限淘汰策略"：经验值，未特别指定时使用。</summary>
        public const int DefaultCapacity = 500;

        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<string>> _index;
        private readonly LinkedList<string> _lru;

        /// <summary>可关闭：默认开启（见任务书硬约束"默认开启"）。关闭时 <see cref="ShouldForward"/>
        /// 恒返回 <c>false</c>，且不记账（不会把关闭期间路过的消息计入去重表——重新开启后这些消息
        /// 仍视为"未见过"，会被当作新消息转发一次）。</summary>
        public bool Enabled { get; set; }

        public PresentationDiagnosticsConsoleGate(bool enabled = true, int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "去重表容量必须为正数");
            }

            Enabled = enabled;
            _capacity = capacity;
            _index = new Dictionary<string, LinkedListNode<string>>(StringComparer.Ordinal);
            _lru = new LinkedList<string>();
        }

        /// <summary>返回 <c>true</c> 表示本次调用是该消息文本自上次转发（或从未转发）以来第一次
        /// 出现，调用方应当据此转发一次；返回 <c>false</c> 表示未开启，或该消息仍在去重窗口内
        /// （已经转发过、尚未被 LRU 淘汰）。</summary>
        public bool ShouldForward(string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (!Enabled)
            {
                return false;
            }

            if (_index.TryGetValue(message, out var existing))
            {
                // 命中：刷新为最近使用，但本身不是"新"消息，不转发。
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return false;
            }

            if (_index.Count >= _capacity)
            {
                var oldest = _lru.Last;
                if (oldest != null)
                {
                    _lru.RemoveLast();
                    _index.Remove(oldest.Value);
                }
            }

            var node = _lru.AddFirst(message);
            _index[message] = node;
            return true;
        }
    }

    /// <summary>
    /// <see cref="IPresentationDiagnostics"/> 的装饰器：转发给 <paramref name="inner"/>（既有内存
    /// 记录语义完全不变，调用方/测试仍可读 <c>PresentationDiagnosticsRecorder.Warnings</c>）之外，
    /// 经 <see cref="PresentationDiagnosticsConsoleGate"/> 去重后再转发一次到 <see cref="IPresentationDiagnosticsConsoleSink"/>。
    /// 目前没有生产装配代码持有能直接注入 <see cref="IPresentationDiagnostics"/> 的构造点（见文件
    /// 顶部判断记录），本类型是为将来 presentation/assembly 补上注入点时准备好的可复用组件；当前
    /// 唯一接入的诊断源（<c>SpriteCharacterRig.Diagnostics</c>）走 <see cref="SpriteRigDiagnosticsPump"/>
    /// 这条轮询路径，不经过本类型。
    /// </summary>
    public sealed class PresentationDiagnosticsConsoleForwarder : IPresentationDiagnostics
    {
        private readonly IPresentationDiagnostics _inner;
        private readonly IPresentationDiagnosticsConsoleSink _sink;
        private readonly PresentationDiagnosticsConsoleGate _gate;

        public PresentationDiagnosticsConsoleForwarder(
            IPresentationDiagnostics inner,
            IPresentationDiagnosticsConsoleSink sink,
            PresentationDiagnosticsConsoleGate? gate = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _gate = gate ?? new PresentationDiagnosticsConsoleGate();
        }

        public void Warn(string message)
        {
            _inner.Warn(message);

            // 级别映射：见文件顶部判断记录——IPresentationDiagnostics 只有 Warn 一级，恒映射到
            // 控制台 Warning，不产生 Error。
            if (_gate.ShouldForward(message))
            {
                _sink.Warn(message);
            }
        }
    }

    /// <summary>
    /// 轮询式转发——弥补 <see cref="PresentationDiagnosticsConsoleForwarder"/> 需要构造期注入、但
    /// <c>SpriteCharacterRig.Diagnostics</c> 没有注入点（固定内部 <c>new PresentationDiagnosticsRecorder()</c>，
    /// 只读属性对外暴露）这一缺口：按引擎侧每帧调用一次 <see cref="Pump"/>，比较该 recorder
    /// 累积的 <c>Warnings</c> 相对上一次已经转发到第几条，只转发新增的那一段。同一进程内会有多个
    /// <c>SpriteCharacterRig</c> 实例（每个绑定实体各自一份 recorder），按 recorder 自身的引用身份
    /// （默认对象相等性）分别记账，互不干扰。</summary>
    public sealed class SpriteRigDiagnosticsPump
    {
        private readonly Dictionary<PresentationDiagnosticsRecorder, int> _lastForwardedCount =
            new Dictionary<PresentationDiagnosticsRecorder, int>();

        public void Pump(PresentationDiagnosticsRecorder recorder, PresentationDiagnosticsConsoleGate gate, IPresentationDiagnosticsConsoleSink sink)
        {
            if (recorder == null) throw new ArgumentNullException(nameof(recorder));
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            var warnings = recorder.Warnings;
            var start = _lastForwardedCount.TryGetValue(recorder, out var seen) ? seen : 0;

            for (var i = start; i < warnings.Count; i++)
            {
                if (gate.ShouldForward(warnings[i]))
                {
                    sink.Warn(warnings[i]);
                }
            }

            _lastForwardedCount[recorder] = warnings.Count;
        }
    }
}

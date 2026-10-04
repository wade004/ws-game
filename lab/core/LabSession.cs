using System;

namespace Lab
{
    /// <summary>
    /// 一次可单步推进的实验室运行（<see cref="LabHost.Start"/>）。脚本回放（<see cref="RunToEnd"/>）与交互式试玩（<see cref="Advance"/> + <see cref="Inject"/>）
    /// 共用同一个宿主装配与同一份逐步顺序（06 第 1 节"一个内核"）；本类型只是把原先写死在 <see cref="LabHost.Run(LabHostOptions, LabScenario, InputScript, LabCatalog?, LabRunVariant?, LabHostExtension?)"/>
    /// 里的"推进循环"交还给调用方。
    /// <para>
    /// 判断记录（实时会话的脚本即日志）：实时会话的 <see cref="Script"/> 持有的事件清单就是会话日志——<see cref="Inject"/> 注入的每个事件
    /// 都盖上"下一个将要执行的固定步"的戳并追加进去，所以 <see cref="Script"/> 在任何时刻都是"到目前为止这一局的可回放脚本"；
    /// <see cref="Finish"/> 把它的时长定为实际跑过的步数。回放这份脚本得到的逻辑组与试玩时逐字节一致（注入只发生在两次 <see cref="Advance"/> 之间，
    /// 即固定步之间，不会落在一步的中间）。
    /// </para>
    /// </summary>
    public sealed class LabSession
    {
        private readonly Func<int> _tick;
        private readonly Action<double> _advance;
        private readonly Action<ScriptEvent> _inject;
        private readonly Func<LabRecording> _finish;
        private readonly Action<double>? _setFrameStep;
        private readonly int _duration;
        private bool _finished;

        /// <summary>本次运行的脚本；实时会话里它的事件清单随注入增长（见类型判断记录）。</summary>
        public InputScript Script { get; }

        public LabRecording Recording { get; }

        /// <summary>宿主上下文（世界、出场标签、玩家 id）；没有扩展点时为 null（只有带扩展的会话才创建上下文）。</summary>
        public LabHostContext? Context { get; }

        public bool Live { get; }

        /// <summary>固定步长（秒）。</summary>
        public double StepSeconds { get; }

        /// <summary>脚本声明的帧长（秒）。</summary>
        public double FrameSeconds { get; }

        /// <summary>下一个将要执行的固定步序号（= 已完成的固定步数）。</summary>
        public int Tick => _tick();

        public bool Finished => _finished;

        /// <summary>脚本声明的总固定步数（实时会话为 <see cref="int.MaxValue"/>）；<see cref="Tick"/> 达到它之后再推进不再执行固定步。</summary>
        public int DurationTicks => _duration;

        internal LabSession(
            InputScript script, LabRecording recording, LabHostContext? context, bool live, int duration, double stepSeconds,
            double frameSeconds, Func<int> tick, Action<double> advance, Action<ScriptEvent> inject, Func<LabRecording> finish,
            Action<double>? setFrameStep = null)
        {
            _setFrameStep = setFrameStep;
            Script = script;
            Recording = recording;
            Context = context;
            Live = live;
            _duration = duration;
            StepSeconds = stepSeconds;
            FrameSeconds = frameSeconds;
            _tick = tick;
            _advance = advance;
            _inject = inject;
            _finish = finish;
        }

        /// <summary>推进一帧（<paramref name="seconds"/> 秒的模拟时间；其中触发零到多个固定步，并做一次表现同步）。</summary>
        public void Advance(double seconds)
        {
            if (_finished)
            {
                throw new InvalidOperationException("会话已结束");
            }

            if (seconds <= 0.0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "帧长必须是有限正数");
            }

            _advance(seconds);
        }

        /// <summary>注入一条实时输入事件（只用于实时会话）；事件的 <c>Tick</c> 被忽略，改盖当前 <see cref="Tick"/>。</summary>
        public void Inject(ScriptEvent e)
        {
            if (!Live)
            {
                throw new InvalidOperationException("只有实时会话可以注入事件");
            }

            if (_finished)
            {
                throw new InvalidOperationException("会话已结束");
            }

            _inject(e ?? throw new ArgumentNullException(nameof(e)));
        }

        /// <summary>按脚本声明的时长跑到底（以脚本帧长逐帧推进）并收尾；脚本回放的入口（<see cref="LabHost.Run(LabHostOptions, LabScenario, InputScript, LabCatalog?, LabRunVariant?, LabHostExtension?)"/>）。</summary>
        public LabRecording RunToEnd() => RunToEnd(1.0);

        /// <summary>
        /// 同 <see cref="RunToEnd()"/>，另按 <paramref name="timeScale"/> 缩放表现时钟（M5-S7，手感设计 06 第 3.5 节"慢放到四分之一速对照"）：
        /// 每个表现帧推进 <c>脚本帧长 × timeScale</c> 的模拟时间，所以 0.25 倍时同样的固定步被摊到四倍多的表现帧上，
        /// 视图、特效、镜头按更细的帧距推进——慢放的是表现，不是逻辑：固定步序列、每步的输入与世界状态与 1 倍时逐位相同，
        /// 逻辑类度量逐字节一致（<c>feellab run --time-scale</c> 与 <c>LabKernelTests</c> 逐个证明）。<paramref name="timeScale"/> 必须是有限正数；
        /// 1 倍时与 <see cref="RunToEnd()"/> 走同一条代码、记录逐位相同。
        /// </summary>
        public LabRecording RunToEnd(double timeScale)
        {
            if (Live)
            {
                throw new InvalidOperationException("实时会话没有预定时长，不能 RunToEnd");
            }

            if (!(timeScale > 0.0) || double.IsInfinity(timeScale))
            {
                throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "时间尺度必须是有限正数");
            }

            var dt = timeScale == 1.0 ? FrameSeconds : FrameSeconds * timeScale;
            if (timeScale != 1.0)
            {
                _setFrameStep?.Invoke(dt);
            }

            var guard = 0;
            var maxFrames = (int)Math.Ceiling(_duration * StepSeconds / dt) + 10 + _duration;
            while (Tick < _duration)
            {
                if (++guard > maxFrames)
                {
                    throw new InvalidOperationException("实验室宿主在预期帧数内没有推进完全部固定步（时钟累加异常）");
                }

                _advance(dt);
            }

            return Finish();
        }

        /// <summary>收尾（释放资源、定稿记录）；只能调用一次，返回记录。</summary>
        public LabRecording Finish()
        {
            if (_finished)
            {
                throw new InvalidOperationException("会话已结束");
            }

            _finished = true;
            return _finish();
        }
    }
}

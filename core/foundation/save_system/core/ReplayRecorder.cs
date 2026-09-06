using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="IReplayRecorder"/> 的默认实现（T1-9，见 10_存档与持久化.md 第 8 节
    /// "回放 = 固定步长 + 分流随机源初始状态 + 输入录像"）。纯内存累积，不读取任何系统时间、
    /// 不使用线程/反射；调用方负责在录制期间保持"先 <see cref="BeginRecording"/>、
    /// 期间按 tick 顺序调用 <see cref="RecordInput"/>、结束后调用 <see cref="Export"/>"的顺序。
    /// </summary>
    public sealed class ReplayRecorder : IReplayRecorder
    {
        private readonly double _stepSeconds;
        private readonly List<ReplayInputRecord> _inputs = new List<ReplayInputRecord>();
        private readonly List<ReplayStepRecord> _steps = new List<ReplayStepRecord>();
        private readonly List<ReplayEndTurnRecord> _endTurns = new List<ReplayEndTurnRecord>();

        private IReadOnlyDictionary<string, RngStreamState>? _rngSeeds;
        private ulong _masterSeed;
        private long _tickCount;
        private bool _began;

        public ReplayRecorder(double stepSeconds)
        {
            if (stepSeconds <= 0)
            {
                throw new ArgumentException("stepSeconds 必须为正数", nameof(stepSeconds));
            }

            _stepSeconds = stepSeconds;
        }

        public void BeginRecording(ulong masterSeed, IReadOnlyDictionary<string, RngStreamState> rngSeeds)
        {
            _masterSeed = masterSeed;
            _rngSeeds = rngSeeds ?? throw new ArgumentNullException(nameof(rngSeeds));
            _inputs.Clear();
            _steps.Clear();
            _endTurns.Clear();
            _tickCount = 0;
            _began = true;
        }

        public void RecordInput(long tick, ReplayInputRecord intent)
        {
            EnsureBegan();

            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            if (intent.Tick != tick)
            {
                throw new ArgumentException(
                    $"tick 参数（{tick}）与 intent.Tick（{intent.Tick}）不一致", nameof(tick));
            }

            _inputs.Add(intent);

            if (tick > _tickCount)
            {
                _tickCount = tick;
            }
        }

        /// <summary>
        /// 记录本次录像覆盖的总 tick 数（不属于 <see cref="IReplayRecorder"/> 契约——契约只知道
        /// "记过哪些 tick 提交过输入"，不知道"整段回放一共跑了多少 tick"，后者只有驱动主循环的
        /// 调用方知道，例如末尾若干 tick 没有任何输入也仍属于录像范围）。调用方应在跑完全部
        /// tick 后、调用 <see cref="Export"/> 之前调用本方法；不调用时 <see cref="Export"/> 退化为
        /// 用"记录过输入的最大 tick 序号"作为 <see cref="ReplayData.TickCount"/>。
        /// </summary>
        public void SetTickCount(long tickCount)
        {
            EnsureBegan();

            if (tickCount < 0)
            {
                throw new ArgumentException("tickCount 不能为负数", nameof(tickCount));
            }

            _tickCount = tickCount;
        }

        /// <summary>记录某个 tick 实际使用的 <see cref="SimStep"/>（见 <see cref="IReplayRecorder.RecordStep"/>、
        /// <see cref="ReplayStepRecord"/> 类型注释）。不要求与 <see cref="RecordInput"/> 一一对应
        /// （很多 tick 没有任何输入，仍应记步——离散步下"没人提交新意图、当前行动者是 AI"同样是
        /// 一个合法的 tick）。</summary>
        public void RecordStep(long tick, SimStep step)
        {
            EnsureBegan();
            _steps.Add(ReplayStepRecord.FromStep(tick, step));

            if (tick > _tickCount)
            {
                _tickCount = tick;
            }
        }

        /// <summary>见 <see cref="IReplayRecorder.RecordEndTurn"/>。</summary>
        public void RecordEndTurn(long tick, Id actorId)
        {
            EnsureBegan();
            _endTurns.Add(new ReplayEndTurnRecord(tick, actorId));

            if (tick > _tickCount)
            {
                _tickCount = tick;
            }
        }

        public ReplayData Export()
        {
            EnsureBegan();
            return new ReplayData(_rngSeeds!, _inputs.ToArray(), _stepSeconds, _tickCount, _steps.ToArray(), endTurns: _endTurns.ToArray(), masterSeed: _masterSeed);
        }

        private void EnsureBegan()
        {
            if (!_began)
            {
                throw new InvalidOperationException("必须先调用 BeginRecording 才能录制/导出回放数据");
            }
        }
    }
}

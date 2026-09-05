using System;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="ISimClockHost"/> 的默认实现：连续模式累积器主循环（见 03_运行时骨架.md
    /// 第 3.1 节五步）。本类不读取任何系统时间源——<c>realDeltaSeconds</c> 完全由调用方
    /// （引擎适配层 <c>IClock.onFrame</c> 回调）传入，也不使用线程/反射，保证核心库的
    /// 确定性（见落地方案与分阶段计划.md 第 4.1 节"核心库不读取任何引擎时间源"）。
    /// </summary>
    public sealed class SimClockHost : ISimClockHost
    {
        private readonly IWorldSim _world;

        private double _stepSeconds;
        private int _maxCatchUpSteps;
        private bool _isPaused;
        private double _timeScale;
        private double _accumulator;
        private long _tickIndex;

        /// <summary>离散模式下用于表现插值的独立累加器（见 <see cref="Mode"/> 注释、
        /// <see cref="Advance"/> 离散分支判断记录）：与 <see cref="_accumulator"/>（连续模式的
        /// tick 累积器）互不干扰，模式切回连续时 <see cref="_accumulator"/> 保留切换前的值继续
        /// 使用，不需要额外的换算或清零。</summary>
        private double _discretePhaseAccumulator;

        public SimClockHost(IWorldSim world, SimLoopOptions? options = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));

            var resolvedOptions = options ?? new SimLoopOptions();
            ValidateStepSeconds(resolvedOptions.StepSeconds);
            ValidateMaxCatchUpSteps(resolvedOptions.MaxCatchUpSteps);

            _stepSeconds = resolvedOptions.StepSeconds;
            _maxCatchUpSteps = resolvedOptions.MaxCatchUpSteps;
            _timeScale = resolvedOptions.DefaultTimeScale;
            _accumulator = 0.0;
            _tickIndex = 0;
        }

        public double StepSeconds => _stepSeconds;

        public int MaxCatchUpSteps => _maxCatchUpSteps;

        public bool IsPaused => _isPaused;

        public double TimeScale => _timeScale;

        public long TickIndex => _tickIndex;

        /// <summary>累计模拟秒数 = <see cref="TickIndex"/> × <see cref="StepSeconds"/>，
        /// 用一次乘法直接算出，不逐 tick 累加浮点数——避免大量小步长累加造成的浮点误差
        /// 累积（任务书明确要求）。</summary>
        public double SimTimeSeconds => _tickIndex * _stepSeconds;

        public TimeModelMode Mode { get; set; } = TimeModelMode.Continuous;

        public void ConfigureStep(double stepSeconds, int maxCatchUpSteps)
        {
            ValidateStepSeconds(stepSeconds);
            ValidateMaxCatchUpSteps(maxCatchUpSteps);
            _stepSeconds = stepSeconds;
            _maxCatchUpSteps = maxCatchUpSteps;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
        }

        public void SetTimeScale(double scale)
        {
            if (scale < 0)
            {
                throw new ArgumentException("时间缩放系数不能为负数", nameof(scale));
            }

            _timeScale = scale;
        }

        /// <summary>
        /// 按 03 第 3.1 节五步处理一次推进：
        /// 1) 取得 <paramref name="realDeltaSeconds"/>；
        /// 2) 慢动作缩放；暂停时跳过第 3~4 步（累积器不增长、不产生 tick）；
        /// 3) 累加进 <see cref="_accumulator"/>；
        /// 4) 循环调用 <c>WorldSim.Tick(Continuous)</c> 并扣减步长，单次调用最多补偿
        ///    <see cref="MaxCatchUpSteps"/> 次，超出部分直接丢弃（见本类型注释下方判断记录）；
        /// 5) 返回 <c>accumulator / stepSeconds</c> 得到的插值系数 alpha。
        /// </summary>
        public double Advance(double realDeltaSeconds)
        {
            if (realDeltaSeconds < 0)
            {
                throw new ArgumentException("realDeltaSeconds 不能为负数", nameof(realDeltaSeconds));
            }

            if (Mode == TimeModelMode.Discrete)
            {
                return AdvanceDiscrete(realDeltaSeconds);
            }

            if (!_isPaused)
            {
                var scaledDelta = realDeltaSeconds * _timeScale;
                _accumulator += scaledDelta;

                var ticksThisCall = 0;
                while (_accumulator >= _stepSeconds && ticksThisCall < _maxCatchUpSteps)
                {
                    _world.Tick(SimStep.Continuous(_stepSeconds));
                    _accumulator -= _stepSeconds;
                    _tickIndex++;
                    ticksThisCall++;
                }

                if (ticksThisCall >= _maxCatchUpSteps && _accumulator >= _stepSeconds)
                {
                    // 判断记录（丢弃策略：清零而非取模）：
                    // 03 第 3.1 节步骤 4 只规定"超出部分的时间直接丢弃而不追赶"，未规定丢弃
                    // 到什么程度；任务书给出"acc 置为 acc % step 或直接清零，二选一"。
                    // 选择清零：取模会把这部分本该被丢弃的"债务"保留为一段不足一步的余量，
                    // 在长时间卡顿反复发生的场景下，这段余量会在下一次 Advance 里悄悄并入
                    // 新的真实时间，让"追赶行为"在多次调用之间产生难以预期的耦合，也让"单帧
                    // 最多补偿 N 步"这条上限在观感上变得不够"硬"；触发补偿上限本身就意味着
                    // 这段时间的推进已经是有损的（"死亡螺旋"保护，见 ADR-0003），清零给出一个
                    // 干净、可预测的起点——每次触发上限后都从零开始重新累积，代价只是多丢弃
                    // 不到一个步长的模拟时间，对确定性回放不构成影响（丢弃发生在
                    // SimClockHost 这一层，不改变 tick 内部的计算逻辑与随机数消耗）。
                    _accumulator = 0.0;
                }
            }

            return _accumulator / _stepSeconds;
        }

        /// <summary>
        /// 离散模式分支（见 03 第 9 节 <c>advance</c> 注释"离散模式：改由 TurnScheduler.nextStep
        /// 产生步，本方法只驱动表现插值，不产生模拟步"）：不调用 <see cref="IWorldSim.Tick"/>、
        /// 不推进 <see cref="_tickIndex"/>/<see cref="_accumulator"/>，只用一个独立的 0～1 循环
        /// 累加器（<see cref="_discretePhaseAccumulator"/>）供表现层做纯视觉性的待机动画/呼吸效果
        /// 插值——离散步之间没有"上一步/这一步"的位置差可插值（一次 <c>Discrete</c> 步是"这名
        /// 行动者的一次行动"，不是固定的位移量），因此这里给出的 alpha 只是一个随真实时间循环的
        /// 相位，不表示任何模拟状态插值，调用方不应像连续模式那样用它做位置线性插值。暂停/慢动作
        /// 语义与连续模式一致。
        /// </summary>
        private double AdvanceDiscrete(double realDeltaSeconds)
        {
            if (!_isPaused && _stepSeconds > 0)
            {
                _discretePhaseAccumulator += realDeltaSeconds * _timeScale;
                _discretePhaseAccumulator %= _stepSeconds;
            }

            return _discretePhaseAccumulator / _stepSeconds;
        }

        private static void ValidateStepSeconds(double stepSeconds)
        {
            if (stepSeconds <= 0)
            {
                throw new ArgumentException("stepSeconds 必须为正数", nameof(stepSeconds));
            }
        }

        private static void ValidateMaxCatchUpSteps(int maxCatchUpSteps)
        {
            if (maxCatchUpSteps <= 0)
            {
                throw new ArgumentException("maxCatchUpSteps 必须为正数", nameof(maxCatchUpSteps));
            }
        }
    }
}

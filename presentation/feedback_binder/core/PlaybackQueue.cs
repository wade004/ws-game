using System;
using System.Collections.Generic;
using Presentation.FeedbackBinder.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// 本地播放队列（见 09_表现层.md 第 6.4 节"离散模式下的动作回放"）：<see cref="QueueMode.Immediate"/>
    /// 下每一步立即同步执行，从不进队列、从不触发 <see cref="Finished"/>（09 原文"immediate 模式不发
    /// [presentation.playback_finished]"，见 <see cref="QueueMode"/> 类型注释）；
    /// <see cref="QueueMode.Sequential"/> 下按到达顺序逐条缓冲，<see cref="Update"/> 按
    /// <see cref="Contracts.FeedbackOptions.SequentialStepSeconds"/>（经 <see cref="SpeedMultiplier"/>
    /// 缩放）推进节奏，队列清空（最后一步刚执行完）时触发 <see cref="Finished"/> 恰好一次。
    /// </summary>
    public sealed class PlaybackQueue
    {
        public QueueMode Mode { get; set; }

        /// <summary>加速倍率（09 第 6.4 节"加速与跳过...是表现层本地设置，不改变已经结算的战斗
        /// 结果"）；必须为正数。</summary>
        public double SpeedMultiplier
        {
            get => _speedMultiplier;
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "SpeedMultiplier 必须为正数");
                }
                _speedMultiplier = value;
            }
        }

        private double _speedMultiplier = 1.0;

        private readonly double _stepSeconds;
        private readonly Queue<Action> _pending = new Queue<Action>();
        private double _elapsedInCurrentStep;

        /// <summary>队列从非空变为空时触发恰好一次（<see cref="QueueMode.Immediate"/> 下从不触发，
        /// 见类型注释）。</summary>
        public event Action? Finished;

        public PlaybackQueue(double stepSeconds)
        {
            if (stepSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepSeconds), "stepSeconds 必须为正数");
            }
            _stepSeconds = stepSeconds;
        }

        public int PendingCount => _pending.Count;

        /// <summary><see cref="Mode"/> 为 <see cref="QueueMode.Immediate"/> 时立即同步调用
        /// <paramref name="step"/>；为 <see cref="QueueMode.Sequential"/> 时入队，交给
        /// <see cref="Update"/> 按节奏逐条回放。</summary>
        public void Enqueue(Action step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));

            if (Mode == QueueMode.Immediate)
            {
                step();
                return;
            }

            _pending.Enqueue(step);
        }

        /// <summary>按 <paramref name="dt"/> 推进 <see cref="QueueMode.Sequential"/> 队列；
        /// <see cref="QueueMode.Immediate"/> 下队列本就始终为空，本方法是空操作。</summary>
        public void Update(double dt)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            _elapsedInCurrentStep += dt * _speedMultiplier;
            var effectiveStep = _stepSeconds;

            while (_pending.Count > 0 && _elapsedInCurrentStep >= effectiveStep)
            {
                _elapsedInCurrentStep -= effectiveStep;
                RunOne();
            }
        }

        /// <summary>立即按顺序执行全部剩余步骤（09 第 6.4 节"是否允许整体跳过"），随后触发
        /// <see cref="Finished"/>（若确有步骤被执行）。</summary>
        public void Skip()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            while (_pending.Count > 0)
            {
                RunOne();
            }
        }

        private void RunOne()
        {
            var step = _pending.Dequeue();
            step();

            if (_pending.Count == 0)
            {
                _elapsedInCurrentStep = 0;
                Finished?.Invoke();
            }
        }
    }
}

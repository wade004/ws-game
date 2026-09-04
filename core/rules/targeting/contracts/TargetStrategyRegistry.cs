using System;
using System.Collections.Generic;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// 目标来源策略的注入点（见 00_架构总则.md 第 4 节原则 10"策略注入"、01_分层与依赖.md 第 8
    /// 节第 3 种合法调用方式"游戏层实现一个自定义的目标选择策略并注册到 L2 规则层的 TargetHost
    /// 策略表中"）。内置六种来源（见 <see cref="BuiltinTargetStrategies"/>）与游戏层自定义来源
    /// 经由同一个 <see cref="Register"/> 入口登记，<see cref="Core.Rules.Targeting.TargetHost"/>
    /// 本身不识别任何具体策略名字面量（见落地方案 T2-9 行"禁止目标链策略硬编码……而不经策略注入
    /// 机制"）。
    /// </summary>
    public sealed class TargetStrategyRegistry
    {
        private readonly Dictionary<string, ITargetSourceStrategy> _byName = new Dictionary<string, ITargetSourceStrategy>(StringComparer.Ordinal);

        /// <summary>登记一个目标来源策略；<paramref name="strategy"/>.Name 已被占用时抛
        /// <see cref="InvalidOperationException"/>（同名策略视为调用方编程错误，不做"后者覆盖
        /// 前者"的静默处理，呼应 <c>JsonObject</c> 对重复键的处理惯例）。</summary>
        public void Register(ITargetSourceStrategy strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException(nameof(strategy));
            }

            if (string.IsNullOrEmpty(strategy.Name))
            {
                throw new ArgumentException("策略 Name 不能为空", nameof(strategy));
            }

            if (_byName.ContainsKey(strategy.Name))
            {
                throw new InvalidOperationException($"目标来源策略名重复注册：\"{strategy.Name}\"");
            }

            _byName.Add(strategy.Name, strategy);
        }

        /// <summary>按名字取得已注册策略；未注册时抛 <see cref="ArgumentException"/>（与
        /// <see cref="Core.Rules.Common.ITargetHost"/> 对未知链 id 的处理同一惯例：调用方传入了
        /// 一个本应在数据校验期就被拦下的非法值）。</summary>
        public ITargetSourceStrategy Get(string name)
        {
            if (_byName.TryGetValue(name, out var strategy))
            {
                return strategy;
            }

            throw new ArgumentException($"未注册的目标来源策略：\"{name}\"", nameof(name));
        }

        /// <summary>按名字尝试取得已注册策略；未注册时返回 false，不抛异常。</summary>
        public bool TryGet(string name, out ITargetSourceStrategy? strategy) => _byName.TryGetValue(name, out strategy);

        /// <summary>全部已注册策略名，供 <see cref="ChainDefValidationRule"/> 构造期传入作为
        /// "已知来源名单"（见任务书"IValidationRule 需要策略名清单"）。</summary>
        public IReadOnlyCollection<string> Names => _byName.Keys;
    }
}

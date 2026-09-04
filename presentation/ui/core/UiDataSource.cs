using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using EventHandler = Core.Foundation.EventBus.EventHandler;

namespace Presentation.Ui
{
    /// <summary>
    /// <see cref="IUiDataSource"/> 的默认实现（见 09_表现层.md 第 7.2 节）：把 <see cref="Query"/>
    /// 的路径字符串解析为段序列，按首段分派给注册的 <see cref="IUiPathProvider"/>；
    /// <see cref="Subscribe"/>/<see cref="Unsubscribe"/> 直接转发给构造期注入的
    /// <see cref="IEventBus"/>。本类型不持有、不缓存任何被误认为权威的逻辑数据（铁律 P1），
    /// 每次 <see cref="Query"/> 都是一次即时的下游查询。
    /// </summary>
    public sealed class UiDataSource : IUiDataSource
    {
        private readonly IEventBus _eventBus;
        private readonly IUiDiagnostics _diagnostics;
        private readonly Dictionary<string, IUiPathProvider> _providers = new Dictionary<string, IUiPathProvider>(StringComparer.Ordinal);

        public UiDataSource(IEventBus eventBus, IEnumerable<IUiPathProvider> providers, IUiDiagnostics? diagnostics = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _diagnostics = diagnostics ?? new InMemoryUiDiagnostics();

            if (providers == null) throw new ArgumentNullException(nameof(providers));
            foreach (var provider in providers)
            {
                if (provider == null)
                {
                    throw new ArgumentException("providers 不能包含 null 元素", nameof(providers));
                }

                if (_providers.ContainsKey(provider.Root))
                {
                    throw new ArgumentException($"路径首段 \"{provider.Root}\" 已被注册过一个 IUiPathProvider", nameof(providers));
                }

                _providers[provider.Root] = provider;
            }
        }

        public ExprValue? Query(string path)
        {
            if (!UiPathParser.TryParse(path, out var segments))
            {
                _diagnostics.Warn($"UI 路径 \"{path}\" 语法非法（应为小写点分段，段可带 [数字] 下标）");
                return null;
            }

            if (segments.Count == 0)
            {
                _diagnostics.Warn("UI 路径为空");
                return null;
            }

            var root = segments[0].Name;
            if (!_providers.TryGetValue(root, out var provider))
            {
                _diagnostics.Warn($"UI 路径 \"{path}\" 的首段 \"{root}\" 没有注册对应的 IUiPathProvider");
                return null;
            }

            var remaining = new List<UiPathSegment>(segments.Count - 1);
            for (var i = 1; i < segments.Count; i++)
            {
                remaining.Add(segments[i]);
            }

            return provider.Resolve(remaining, path, _diagnostics);
        }

        public SubscriptionHandle Subscribe(Id eventKey, EventHandler handler) => _eventBus.Subscribe(eventKey, handler);

        public void Unsubscribe(SubscriptionHandle handle) => handle?.Dispose();
    }
}

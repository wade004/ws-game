using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// <see cref="IHookRegistry"/> 的默认实现。无反射、无 LINQ 热路径分配：调用循环用普通
    /// for；回调列表在 <see cref="Invoke"/> 前复制为数组快照后再遍历，以支持"回调执行期间
    /// 取消订阅"而不影响本次正在进行的调用、也不抛异常（与 event_bus 的 <c>EventBus</c>
    /// 派发循环同一惯例）。
    /// </summary>
    public sealed class HookRegistry : IHookRegistry
    {
        private readonly IEventBus? _bus;
        private readonly HookRegistryOptions _options;
        private readonly IHookDiagnostics _diagnostics;

        private readonly Dictionary<Id, HookPointDefinition> _definitions = new Dictionary<Id, HookPointDefinition>();
        private readonly List<HookPointDefinition> _definitionOrder = new List<HookPointDefinition>();
        private readonly Dictionary<Id, List<CallbackEntry>> _callbacks = new Dictionary<Id, List<CallbackEntry>>();

        private long _registrationSequence;

        public HookRegistry(IEventBus? bus = null, HookRegistryOptions? options = null, IHookDiagnostics? diagnostics = null)
        {
            _options = options ?? new HookRegistryOptions();
            _diagnostics = diagnostics ?? new InMemoryHookDiagnostics();
            _bus = bus;

            if (_options.EmitInvokedEvent && _bus == null)
            {
                throw new ArgumentException(
                    "HookRegistryOptions.EmitInvokedEvent=true 时必须提供非空 IEventBus（构造函数 bus 参数）",
                    nameof(bus));
            }
        }

        public void DeclareHookPoint(Id hookId, string signature) =>
            DeclareHookPoint(new HookPointDefinition(hookId, signature));

        public void DeclareHookPoint(HookPointDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (_definitions.ContainsKey(definition.HookId))
            {
                throw new InvalidOperationException($"挂载点 \"{definition.HookId}\" 已声明，不能重复声明");
            }

            _definitions.Add(definition.HookId, definition);
            _definitionOrder.Add(definition);
            _callbacks.Add(definition.HookId, new List<CallbackEntry>());
        }

        public void DeclareFromDefinitions(IEnumerable<HookPointDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("挂载点定义列表不能包含 null 元素", nameof(definitions));
                }

                DeclareHookPoint(definition);
            }
        }

        public SubscriptionHandle Register(Id hookId, HookCallback callback, int order)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (!_definitions.TryGetValue(hookId, out var definition))
            {
                throw new InvalidOperationException($"挂载点 \"{hookId}\" 未声明，不能注册回调");
            }

            var list = _callbacks[hookId];
            if (!definition.AllowMultiple && list.Count > 0)
            {
                throw new InvalidOperationException(
                    $"挂载点 \"{hookId}\" 不允许多个回调（AllowMultiple=false），已有 {list.Count} 个回调");
            }

            var entry = new CallbackEntry(callback, order, _registrationSequence++);
            InsertSorted(list, entry);

            return new SubscriptionHandle(() =>
            {
                entry.IsCancelled = true;
                list.Remove(entry);
            });
        }

        public void Invoke(Id hookId, HookArgs args)
        {
            if (!_definitions.ContainsKey(hookId))
            {
                throw new InvalidOperationException($"挂载点 \"{hookId}\" 未声明，不能调用");
            }

            var callbackArgs = args ?? HookArgs.Empty;
            var list = _callbacks[hookId];
            var snapshot = list.ToArray();

            for (var i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                if (entry.IsCancelled)
                {
                    continue;
                }

                try
                {
                    entry.Callback(callbackArgs);
                }
                catch (Exception ex)
                {
                    _diagnostics.Error($"挂载点 \"{hookId}\" 的回调抛出异常，已跳过继续调用其余回调", ex);
                }
            }

            if (_options.EmitInvokedEvent)
            {
                _bus!.PublishImmediate(new HookInvokedEvent(hookId, list.Count));
            }
        }

        public IReadOnlyList<HookPointDefinition> HookPoints => _definitionOrder;

        public int CallbackCount(Id hookId) =>
            _callbacks.TryGetValue(hookId, out var list) ? list.Count : 0;

        /// <summary>按 order 升序插入，order 相同按注册先后（<see cref="CallbackEntry.Sequence"/>）。
        /// 用插入排序而非每次 <c>List.Sort</c>：注册频率低、列表通常很短，插入排序更直接，
        /// 且天然稳定（同 order 保持注册先后），不依赖 <c>List.Sort</c> 的不稳定排序实现。</summary>
        private static void InsertSorted(List<CallbackEntry> list, CallbackEntry entry)
        {
            var index = list.Count;
            while (index > 0 && Compare(list[index - 1], entry) > 0)
            {
                index--;
            }

            list.Insert(index, entry);
        }

        private static int Compare(CallbackEntry a, CallbackEntry b)
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : a.Sequence.CompareTo(b.Sequence);
        }

        private sealed class CallbackEntry
        {
            public HookCallback Callback { get; }

            public int Order { get; }

            public long Sequence { get; }

            public bool IsCancelled;

            public CallbackEntry(HookCallback callback, int order, long sequence)
            {
                Callback = callback;
                Order = order;
                Sequence = sequence;
            }
        }
    }
}

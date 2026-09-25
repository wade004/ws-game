using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// <see cref="IEventBus"/> 的默认实现。派发语义见落地方案与分阶段计划.md 第 4.2 节：
    /// "同步派发 + tick 末批处理"——<see cref="Enqueue"/> 只入队，<see cref="DispatchPending"/>
    /// 按入队顺序同步批量派发；<see cref="PublishImmediate"/> 供非 tick 上下文同步立即派发。
    ///
    /// 无反射、无 LINQ 热路径分配：派发循环用普通 for；订阅列表在派发前复制为数组快照后
    /// 再遍历，以支持"派发过程中取消订阅"而不影响本次正在进行的派发、也不抛异常。
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        private readonly IEventCatalog _catalog;
        private readonly EventBusOptions _options;
        private readonly IEventDiagnostics _diagnostics;

        /// <summary>诊断出口只读暴露（ABI 只新增只读属性，见 architecture/adr/0042-诊断契约统一转发到宿主控制台.md）：
        /// 供 adapters/unity 侧统一诊断转发机制轮询本实例累积的 Warnings/Errors，不改变本类型任何既有公开签名。</summary>
        public IEventDiagnostics Diagnostics => _diagnostics;
        private readonly IEventAudit _audit;

        private readonly Dictionary<Id, List<SubscriberEntry>> _subscribers = new Dictionary<Id, List<SubscriberEntry>>();
        private readonly List<IEvent> _pending = new List<IEvent>();

        private long _auditSequence;

        public EventBus(
            IEventCatalog catalog,
            EventBusOptions? options = null,
            IEventDiagnostics? diagnostics = null,
            IEventAudit? audit = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = options ?? new EventBusOptions();
            _diagnostics = diagnostics ?? new InMemoryEventDiagnostics();
            _audit = audit ?? new InMemoryEventAudit();
        }

        public SubscriptionHandle Subscribe(Id key, EventHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var entry = SubscriberEntry.ForRaw(handler, DescribeHandler(handler));
            AddSubscriber(key, entry);
            return new SubscriptionHandle(() => RemoveSubscriber(key, entry));
        }

        public SubscriptionHandle Subscribe<T>(Id key, EventHandler<T> handler) where T : IEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var typeName = typeof(T).Name;
            var description = DescribeHandler(handler);
            var entry = SubscriberEntry.ForTyped(evt =>
            {
                if (evt is T typed)
                {
                    handler(typed);
                }
                else
                {
                    _diagnostics.Warn(
                        $"Subscribe<{typeName}>(\"{key}\") 收到事件，但其运行时类型是 " +
                        $"\"{evt.GetType().Name}\"，与期望类型 \"{typeName}\" 不匹配，已跳过该订阅者本次调用");
                }
            }, description);
            AddSubscriber(key, entry);
            return new SubscriptionHandle(() => RemoveSubscriber(key, entry));
        }

        /// <summary>ADR-0086 根治（消费方第三十二批阻塞项第 2 条）：订阅者标识——尽力而为地从
        /// <paramref name="handler"/> 的委托方法信息拼出"声明类型.方法名"，供 <see cref="DispatchOne"/>
        /// 捕获订阅者异常时写进诊断消息（此前的诊断消息只有事件 key，完全不知道是哪个订阅者抛的）。
        /// <c>Subscribe</c>/<c>Subscribe&lt;T&gt;</c> 内部会把原始 <paramref name="handler"/> 包进闭包
        /// lambda 再交给 <see cref="SubscriberEntry"/>，包完之后就再也拿不到原始委托的方法信息了，
        /// 因此必须在包裹之前、调用方刚传入 <paramref name="handler"/> 的这一刻就取一次。多数订阅者
        /// 是具名方法或编译器生成的 lambda 闭包方法，<see cref="System.Reflection.MethodInfo.DeclaringType"/>
        /// 至少能定位到"哪个类型订阅的"，即便方法名是编译器生成的匿名名字也比完全没有强。</summary>
        private static string DescribeHandler(Delegate handler)
        {
            var method = handler.Method;
            var declaringType = method.DeclaringType?.FullName ?? "?";
            return declaringType + "." + method.Name;
        }

        public void Enqueue(IEvent evt)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            // CORE-170-03 根治：抑制作用域内直接丢弃，见 IEventBus.SuppressDispatch 判断记录——
            // 不进队列、不做 catalog 校验，读档/回滚期间的重放事件不产生任何外部可观察效果。
            if (_suppressDepth > 0)
            {
                return;
            }

            CheckCatalog(evt.Key, "Enqueue");
            _pending.Add(evt);
        }

        public int DispatchPending()
        {
            var totalDispatched = 0;
            var passes = 0;

            while (_pending.Count > 0)
            {
                if (passes >= _options.MaxDispatchPasses)
                {
                    _diagnostics.Error(
                        $"EventBus.DispatchPending 超过 MaxDispatchPasses（{_options.MaxDispatchPasses}），" +
                        $"已停止派发，剩余 {_pending.Count} 个事件留在队列中等待下一次调用",
                        null);
                    break;
                }

                passes++;

                // 取出本轮全部当前已入队事件；派发过程中新产生的事件会被 Enqueue 加进
                // 已清空的 _pending，进入下一轮，而不会混进本轮正在遍历的批次。
                var batch = _pending.ToArray();
                _pending.Clear();

                for (var i = 0; i < batch.Length; i++)
                {
                    DispatchOne(batch[i]);
                    totalDispatched++;
                }
            }

            return totalDispatched;
        }

        public void PublishImmediate(IEvent evt)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            // CORE-170-03 根治：同 Enqueue 判断记录——抑制作用域内直接丢弃，不派发给任何订阅者。
            if (_suppressDepth > 0)
            {
                return;
            }

            CheckCatalog(evt.Key, "PublishImmediate");
            DispatchOne(evt);
        }

        private int _suppressDepth;

        /// <summary>见 <see cref="IEventBus.SuppressDispatch"/> 判断记录。按引用计数支持嵌套调用：
        /// 只有最外层作用域 Dispose 后 <see cref="_suppressDepth"/> 才归零，恢复正常派发。</summary>
        public IDisposable SuppressDispatch()
        {
            _suppressDepth++;
            return new SuppressScope(this);
        }

        private void EndSuppress()
        {
            if (_suppressDepth > 0)
            {
                _suppressDepth--;
            }
        }

        private sealed class SuppressScope : IDisposable
        {
            private EventBus? _bus;

            public SuppressScope(EventBus bus)
            {
                _bus = bus;
            }

            public void Dispose()
            {
                _bus?.EndSuppress();
                _bus = null;
            }
        }

        private void CheckCatalog(Id key, string caller)
        {
            if (_catalog.IsRegistered(key))
            {
                return;
            }

            if (_options.StrictCatalog)
            {
                throw new InvalidOperationException(
                    $"{caller}: 事件 key \"{key}\" 未在 IEventCatalog（found.event_catalog）登记");
            }

            _diagnostics.Warn(
                $"{caller}: 事件 key \"{key}\" 未在 IEventCatalog（found.event_catalog）登记，" +
                "非严格模式（StrictCatalog=false）下继续处理");
        }

        private void DispatchOne(IEvent evt)
        {
            if (_options.AuditLog)
            {
                _audit.Record(evt.Key, _auditSequence);
            }

            _auditSequence++;

            if (!_subscribers.TryGetValue(evt.Key, out var subscribers) || subscribers.Count == 0)
            {
                return;
            }

            // 复制成数组快照后再遍历：允许订阅者在被调用期间为同一个 key 取消订阅
            // （包括取消自己），既不影响本次快照的遍历，也不会抛异常。
            var snapshot = subscribers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                if (entry.IsCancelled)
                {
                    continue;
                }

                try
                {
                    entry.Invoke(evt);
                }
                catch (Exception ex)
                {
                    // ADR-0086 根治（消费方第三十二批阻塞项第 2 条）：消息文本本身携带事件 key、
                    // 订阅者标识、异常类型、异常消息四项（此前只有事件 key 一项，排查时完全不知道
                    // 是哪个订阅者、抛的什么异常）；完整的 Exception（含堆栈）继续按 exception 参数
                    // 传给 _diagnostics.Error，不在这里把堆栈也拼进字符串——PlatformEventDiagnostics
                    // 等下游实现已经在 Error(message, exception) 里自行 ToString() 异常对象，重复拼接
                    // 只会让堆栈在输出里出现两次；真正会把 exception 参数丢掉、只保留 message 字符串
                    // 的适配层转发路径已在 ADR-0086 另一条决定里改为不丢失异常对象（见
                    // adapters/unity/Packages/.../Diagnostics/DiagnosticsHub.cs）。
                    _diagnostics.Error(
                        $"订阅者 \"{entry.Description}\" 处理事件 \"{evt.Key}\" 时抛出异常 " +
                        $"{ex.GetType().FullName}: {ex.Message}，已跳过继续派发给其它订阅者",
                        ex);
                }
            }
        }

        private void AddSubscriber(Id key, SubscriberEntry entry)
        {
            if (!_subscribers.TryGetValue(key, out var list))
            {
                list = new List<SubscriberEntry>();
                _subscribers[key] = list;
            }

            list.Add(entry);
        }

        private void RemoveSubscriber(Id key, SubscriberEntry entry)
        {
            entry.IsCancelled = true;
            if (_subscribers.TryGetValue(key, out var list))
            {
                list.Remove(entry);
            }
        }

        /// <summary>单个订阅登记项：包装非泛型/泛型两种处理函数为统一的调用入口。
        /// 用命名静态工厂方法（而不是两个参数都能隐式转换 lambda 的重载构造函数）
        /// 构造，避免 lambda 表达式在 <see cref="EventHandler"/> 与 <see cref="Action{IEvent}"/>
        /// 两个签名兼容的委托类型间产生二义性重载决议。</summary>
        private sealed class SubscriberEntry
        {
            private readonly Action<IEvent> _invoke;

            public bool IsCancelled;

            /// <summary>ADR-0086 新增：订阅者标识（见 <see cref="DescribeHandler"/>），
            /// <see cref="DispatchOne"/> 捕获异常时写进诊断消息。</summary>
            public string Description { get; }

            private SubscriberEntry(Action<IEvent> invoke, string description)
            {
                _invoke = invoke;
                Description = description;
            }

            public static SubscriberEntry ForRaw(EventHandler handler, string description) =>
                new SubscriberEntry(evt => handler(evt), description);

            public static SubscriberEntry ForTyped(Action<IEvent> invoke, string description) =>
                new SubscriberEntry(invoke, description);

            public void Invoke(IEvent evt) => _invoke(evt);
        }
    }
}

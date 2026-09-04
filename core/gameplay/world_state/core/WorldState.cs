using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SaveSystem;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// <see cref="IWorldState"/> 的默认（唯一）实现：带命名空间的标志字典（见 05 第 8 节、ADR-0008）。
    /// 同时实现 <see cref="IWorldFlags"/>（L3 载体层的依赖倒置最小子集，见
    /// <c>core/carriers/common/contracts/IWorldFlags.cs</c>"由 L4（<c>core/gameplay/world_state</c>）
    /// 或游戏组装根实现"）与 <see cref="IPersistable"/>（存档段 <see cref="SaveSections.WorldStateFlags"/>，
    /// 见 10 第 2.3、3 节步骤 2"先恢复标志字典，因为后续多数模块的条件判断依赖它"）。
    /// <para>
    /// 谁能写：05 第 8.2 节"任何持有该模块契约引用的系统都可以写……但每次写入必须带 writerId"——
    /// 本类型不做任何写权限强约束，约束靠内容评审与 ADR 流程（同该节原文）。
    /// </para>
    /// </summary>
    public sealed class WorldState : IWorldState, IWorldFlags, IPersistable
    {
        private const string WorldDomain = "world";

        private readonly SortedDictionary<Id, ExprValue> _flags = new SortedDictionary<Id, ExprValue>();
        private readonly Dictionary<Id, List<ChangeSubscriber>> _changeSubscribers = new Dictionary<Id, List<ChangeSubscriber>>();

        private readonly IEventBus _eventBus;
        private readonly WorldStateOptions _options;
        private readonly IWorldStateDiagnostics _diagnostics;

        public WorldState(IEventBus eventBus, WorldStateOptions? options = null, IWorldStateDiagnostics? diagnostics = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _options = options ?? new WorldStateOptions();
            _diagnostics = diagnostics ?? new InMemoryWorldStateDiagnostics();

            // 判断记录：OnChanged 不维护一份独立于事件总线的回调registry 并在 Set/Remove 内直接调用，
            // 而是本类型自己订阅一次 world.flag_changed（见下方 HandleDispatchedChange），由事件总线的
            // 实际派发（PublishImmediate 立即触发 / Enqueue 经 DispatchPending 延后触发）驱动
            // 按 key 过滤的回调——这样"OnChanged 在事件发布之后调用"与"DispatchMode.Enqueue 时事件
            // 延后到 DispatchPending"两条要求都由同一处派发时机自动满足，不需要两套时机各自维护。
            _eventBus.Subscribe<WorldFlagChangedEvent>(WorldStateEventKeys.FlagChanged, HandleDispatchedChange);
        }

        // -------------------------------------------------------------
        // IWorldState
        // -------------------------------------------------------------

        public ExprValue Get(Id flagKey) => _flags.TryGetValue(flagKey, out var value) ? value : ExprValue.OfBool(false);

        public void Set(Id flagKey, ExprValue value, Id writerId)
        {
            RequireWorldFlagKey(flagKey, nameof(flagKey));
            RequireValidId(writerId, nameof(writerId));
            if (_options.EnforceSchema)
            {
                ValidateAgainstSchema(flagKey, value);
            }

            var hadOld = _flags.TryGetValue(flagKey, out var storedOld);
            if (hadOld && storedOld.Equals(value))
            {
                // 05 第 8.2 节未明文规定，任务书拍板"值未变化不发事件"：避免同一 flagKey 反复写入
                // 同一个值时刷屏 world.flag_changed，也避免 OnChanged 订阅者收到"无意义"的通知。
                return;
            }

            var oldValue = hadOld ? storedOld : ExprValue.OfBool(false);
            _flags[flagKey] = value;
            Publish(new WorldFlagChangedEvent(flagKey, oldValue, value, writerId));
        }

        public bool Has(Id flagKey) => _flags.ContainsKey(flagKey);

        public bool Remove(Id flagKey, Id writerId)
        {
            RequireWorldFlagKey(flagKey, nameof(flagKey));
            RequireValidId(writerId, nameof(writerId));

            if (!_flags.TryGetValue(flagKey, out var oldValue))
            {
                return false;
            }

            _flags.Remove(flagKey);

            // 移除是"是否存在"本身的状态改变，即便 oldValue 恰好等于缺失时的默认值
            // （ExprValue.OfBool(false)）也仍然触发事件——不套用 Set 的"同值不发事件"规则
            // （见 IWorldState.Remove 注释判断记录）。
            Publish(new WorldFlagChangedEvent(flagKey, oldValue, ExprValue.OfBool(false), writerId));
            return true;
        }

        public SubscriptionHandle OnChanged(Id flagKey, FlagChangedCallback callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (!_changeSubscribers.TryGetValue(flagKey, out var list))
            {
                list = new List<ChangeSubscriber>();
                _changeSubscribers[flagKey] = list;
            }

            var subscriber = new ChangeSubscriber(callback);
            list.Add(subscriber);

            return new SubscriptionHandle(() =>
            {
                subscriber.IsCancelled = true;
                list.Remove(subscriber);
            });
        }

        public IReadOnlyList<Id> Keys => new List<Id>(_flags.Keys);

        public IReadOnlyList<Id> KeysUnder(Id prefix)
        {
            var prefixWithDot = prefix.Value + ".";
            var result = new List<Id>();
            // _flags 是 SortedDictionary<Id, ExprValue>，Id.CompareTo 按序数比较，天然按序遍历。
            foreach (var key in _flags.Keys)
            {
                if (key.Value == prefix.Value || key.Value.StartsWith(prefixWithDot, StringComparison.Ordinal))
                {
                    result.Add(key);
                }
            }
            return result;
        }

        public int Count => _flags.Count;

        // -------------------------------------------------------------
        // IWorldFlags（core/carriers/common 依赖倒置最小子集）
        // -------------------------------------------------------------

        // 判断记录：IWorldFlags.Get 返回 ExprValue?（未设置返回 null），与 IWorldState.Get（未设置
        // 返回 ExprValue.OfBool(false)）返回类型不同——二者方法名、参数都相同但返回类型不同，
        // 无法用同一个隐式成员同时满足两个接口（CLR 按名字+参数判定签名，不看返回类型），因此
        // IWorldFlags.Get 显式实现，语义上保留该接口注释"未设置过返回 null"的原意，不强行统一成
        // IWorldState 的"缺省按分组默认值"语义。IWorldFlags.Set/Has 与 IWorldState 同名同签名同返回类型，
        // 复用上面的公开成员即可，不需要显式实现。
        ExprValue? IWorldFlags.Get(Id flagKey) => _flags.TryGetValue(flagKey, out var value) ? (ExprValue?)value : null;

        // -------------------------------------------------------------
        // IPersistable（存档段 world_state_flags，见 10 第 2.3、3 节）
        // -------------------------------------------------------------

        public string SectionKey => SaveSections.WorldStateFlags;

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            foreach (var kv in _flags)
            {
                builder.Add(kv.Key.Value, ToJson(kv.Value));
            }
            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            _flags.Clear();

            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException(
                    $"world_state_flags 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            foreach (var kv in obj)
            {
                _flags[new Id(kv.Key)] = FromJson(kv.Key, kv.Value);
            }

            // 读档不是世界变化（05 第 8.2 节持久化只是"整体随存档写入"，不是一次业务写入）：
            // 不触发 world.flag_changed，也不经过 OnChanged。
        }

        // -------------------------------------------------------------
        // 内部帮助方法
        // -------------------------------------------------------------

        private void Publish(WorldFlagChangedEvent evt)
        {
            if (_options.Mode == WorldStateOptions.DispatchMode.Immediate)
            {
                _eventBus.PublishImmediate(evt);
            }
            else
            {
                _eventBus.Enqueue(evt);
            }
        }

        private void HandleDispatchedChange(WorldFlagChangedEvent evt)
        {
            if (!_changeSubscribers.TryGetValue(evt.FlagKey, out var list) || list.Count == 0)
            {
                return;
            }

            // 复制快照后再遍历，允许回调在被调用期间取消自己的订阅（惯例同 EventBus.DispatchOne）。
            var snapshot = list.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var subscriber = snapshot[i];
                if (subscriber.IsCancelled)
                {
                    continue;
                }

                try
                {
                    subscriber.Callback(evt.OldValue, evt.NewValue);
                }
                catch (Exception ex)
                {
                    _diagnostics.Warn(
                        $"OnChanged(\"{evt.FlagKey}\") 回调抛出异常，已跳过继续通知其它订阅者：{ex.Message}");
                }
            }
        }

        private static void RequireWorldFlagKey(Id flagKey, string paramName)
        {
            if (flagKey.Value == null || flagKey.Domain != WorldDomain)
            {
                throw new ArgumentException(
                    $"flagKey 必须以 \"world.\" 开头（05 第 8.2 节 flagKey 命名空间格式），实际为 \"{flagKey}\"",
                    paramName);
            }
        }

        private static void RequireValidId(Id id, string paramName)
        {
            if (id.Value == null)
            {
                throw new ArgumentException("必须是合法构造的 Id（不能是 default(Id)）", paramName);
            }
        }

        private void ValidateAgainstSchema(Id flagKey, ExprValue value)
        {
            WorldFlagSchemaEntry? best = null;
            foreach (var entry in _options.SchemaEntries)
            {
                if (!Matches(entry.NamespacePrefix, flagKey))
                {
                    continue;
                }

                if (best == null || entry.NamespacePrefix.Value.Length > best.Value.NamespacePrefix.Value.Length)
                {
                    best = entry;
                }
            }

            if (best == null)
            {
                throw new ArgumentException(
                    $"world.flag_schema 未登记任何覆盖 \"{flagKey}\" 的命名空间前缀（WorldStateOptions.EnforceSchema=true）",
                    nameof(flagKey));
            }

            if (best.Value.Kind != value.Kind)
            {
                throw new ArgumentException(
                    $"\"{flagKey}\" 的值类型 {value.Kind} 与 world.flag_schema 登记类型 {best.Value.Kind} 不匹配（WorldStateOptions.EnforceSchema=true）",
                    nameof(value));
            }
        }

        private static bool Matches(Id prefix, Id flagKey) =>
            flagKey.Value == prefix.Value || flagKey.Value.StartsWith(prefix.Value + ".", StringComparison.Ordinal);

        private static JsonValue ToJson(ExprValue value)
        {
            switch (value.Kind)
            {
                case ExprValueKind.Bool:
                    return JsonBool.Of(value.AsBool);

                case ExprValueKind.Int:
                    // 判断记录：整数用不带小数点的原始文本写出（TryGetInt64 据此在读档时识别为 Int），
                    // 与下面 Number 分支故意写出的".0"后缀互斥，二者共同保证 Int/Number 在
                    // JsonNumber 这同一个 JSON 种类下仍可无损区分（见 README 判断记录）。
                    return new JsonNumber(value.AsInt, value.AsInt.ToString(System.Globalization.CultureInfo.InvariantCulture));

                case ExprValueKind.Number:
                    return new JsonNumber(value.AsNumber, FormatNumberWithDecimalPoint(value.AsNumber));

                case ExprValueKind.String:
                    return new JsonString(value.AsString);

                case ExprValueKind.Id:
                    // Id 用 {"$id": "..."} 包装以区分 String（同为 JSON 字符串载体，任务书拍板）。
                    return new JsonObjectBuilder().Add("$id", new JsonString(value.AsId.Value)).Build();

                default:
                    throw new InvalidOperationException($"未知的 ExprValueKind：{value.Kind}");
            }
        }

        private static string FormatNumberWithDecimalPoint(double value)
        {
            var s = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0)
            {
                s += ".0";
            }
            return s;
        }

        private static ExprValue FromJson(string flagKeyText, JsonValue value)
        {
            switch (value)
            {
                case JsonBool b:
                    return ExprValue.OfBool(b.Value);

                case JsonNumber n:
                    return IsIntegerRawText(n) ? ExprValue.OfInt(RequireInt64(n, flagKeyText)) : ExprValue.OfNumber(n.Value);

                case JsonString s:
                    return ExprValue.OfString(s.Value);

                case JsonObject o when o.Count == 1 && o.TryGetValue("$id", out var idField) && idField is JsonString idText:
                    return ExprValue.OfId(new Id(idText.Value));

                default:
                    throw new FormatException(
                        $"world_state_flags 段字段 \"{flagKeyText}\" 的值不是受支持的 JSON 形状（实际种类：{value.Kind}）");
            }
        }

        private static bool IsIntegerRawText(JsonNumber n)
        {
            if (n.RawNumberText == null)
            {
                // 没有原始文本（如调用方手工构造的 JsonNumber）：退化为按数值本身是否为整数判断，
                // 与 ExprValue.ToString() 的 Number 格式化惯例（无小数点即视为整数文本）呼应，
                // 见本类型 ToJson 判断记录。
                return n.TryGetInt64(out _);
            }

            return n.RawNumberText.IndexOf('.') < 0 && n.RawNumberText.IndexOf('e') < 0 && n.RawNumberText.IndexOf('E') < 0;
        }

        private static long RequireInt64(JsonNumber n, string flagKeyText)
        {
            if (!n.TryGetInt64(out var v))
            {
                throw new FormatException($"world_state_flags 段字段 \"{flagKeyText}\" 声明为整数但无法解析为 Int64");
            }
            return v;
        }

        private sealed class ChangeSubscriber
        {
            public FlagChangedCallback Callback { get; }
            public bool IsCancelled;

            public ChangeSubscriber(FlagChangedCallback callback)
            {
                Callback = callback;
            }
        }
    }
}

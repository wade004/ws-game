using System;
using System.Collections.Generic;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Foundation.Localization
{
    /// <summary>
    /// <see cref="IL10nHost"/> 的默认实现（见本模块 README）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性读取 <c>l10n.locale</c>/<c>l10n.text</c> 建索引；
    /// 之后只读，不重新查询 registry（与 <c>data_registry</c> 的"只读访问"契约一致，本模块
    /// 不持有对 registry 的写权限、也不监听其 <c>Reload</c>）。
    /// </summary>
    public sealed class L10nHost : IL10nHost
    {
        private sealed class LocaleInfo
        {
            public Id Id;
            public Id? Fallback;
            public bool IsDefault;
        }

        private readonly IEventBus _bus;
        private readonly L10nOptions _options;
        private readonly IL10nDiagnostics _diagnostics;

        private readonly Dictionary<string, LocaleInfo> _locales = new Dictionary<string, LocaleInfo>(StringComparer.Ordinal);
        private readonly List<Id> _localeOrder = new List<Id>();
        private readonly Dictionary<string, string> _textIndex = new Dictionary<string, string>(StringComparer.Ordinal);

        private Id _currentLocale;

        public Id DefaultLocale { get; }

        public IReadOnlyList<Id> SupportedLocales => _localeOrder;

        public L10nHost(IDataRegistryView registry, IEventBus bus, L10nOptions? options = null, IL10nDiagnostics? diagnostics = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new L10nOptions();
            _diagnostics = diagnostics ?? new InMemoryL10nDiagnostics();

            var localeRecords = registry.GetAll("l10n.locale");
            Id? defaultLocale = null;
            int defaultCount = 0;

            foreach (var record in localeRecords)
            {
                var id = record.GetId("id");
                Id? fallback = record.TryGetId("fallback", out var fb) ? fb : (Id?)null;
                var isDefault = record.GetBool("is_default");

                var info = new LocaleInfo { Id = id, Fallback = fallback, IsDefault = isDefault };
                _locales[id.Value] = info;
                _localeOrder.Add(id);

                if (isDefault)
                {
                    defaultCount++;
                    defaultLocale = id;
                }
            }

            if (defaultLocale == null || defaultCount != 1)
            {
                throw new ArgumentException($"l10n.locale 必须恰好一条记录声明 is_default=true，实际 {defaultCount} 条");
            }

            foreach (var info in _locales.Values)
            {
                ValidateNoFallbackCycle(info);
            }

            DefaultLocale = defaultLocale.Value;
            _currentLocale = DefaultLocale;

            var textRecords = registry.GetAll("l10n.text");
            foreach (var record in textRecords)
            {
                var key = record.GetString("key");
                var locale = record.GetId("locale");
                var text = record.GetString("text");
                _textIndex[IndexKey(key, locale.Value)] = text;
            }
        }

        private void ValidateNoFallbackCycle(LocaleInfo start)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id.Value };
            var current = start;
            while (current.Fallback.HasValue)
            {
                var fallbackId = current.Fallback.Value;
                if (!_locales.TryGetValue(fallbackId.Value, out var next))
                {
                    throw new ArgumentException($"l10n.locale \"{current.Id}\" 的 fallback \"{fallbackId}\" 不是已声明的语言 id");
                }
                if (!visited.Add(next.Id.Value))
                {
                    throw new ArgumentException($"l10n.locale 回退链成环：从 \"{start.Id}\" 出发的回退链再次访问 \"{next.Id}\"");
                }
                current = next;
            }
        }

        private static string IndexKey(string key, string locale) => key + "@" + locale;

        public Id GetLocale() => _currentLocale;

        public void SetLocale(Id locale)
        {
            if (!_locales.ContainsKey(locale.Value))
            {
                throw new ArgumentException($"未声明的语言：\"{locale}\"", nameof(locale));
            }

            if (!_currentLocale.Equals(locale))
            {
                _currentLocale = locale;
                _bus.PublishImmediate(new L10nLanguageChangedEvent(locale));
            }
        }

        public bool HasText(Id key) => TryResolve(key.Value, _currentLocale, out _);

        public string Text(Id key, IReadOnlyDictionary<string, string>? vars = null)
        {
            if (TryResolve(key.Value, _currentLocale, out var text))
            {
                return SubstituteVars(text, key, vars);
            }

            _diagnostics.Warn($"缺失文本键 \"{key}\"（语言 \"{_currentLocale}\"，含回退链与默认语言均未找到）");
            switch (_options.MissingKeyPolicy)
            {
                case MissingKeyPolicy.ReturnEmpty: return string.Empty;
                case MissingKeyPolicy.ReturnMarker: return $"[[{key.Value}]]";
                case MissingKeyPolicy.ReturnKey:
                default:
                    return key.Value;
            }
        }

        /// <summary>沿"<paramref name="startLocale"/> → 回退链 → 默认语言"查找 <paramref name="key"/>
        /// 的文案；找到则通过 <paramref name="text"/> 返回 true，否则返回 false。</summary>
        private bool TryResolve(string key, Id startLocale, out string text)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var localeValue = startLocale.Value;

            while (true)
            {
                if (_textIndex.TryGetValue(IndexKey(key, localeValue), out var found))
                {
                    text = found;
                    return true;
                }

                visited.Add(localeValue);

                if (!_locales.TryGetValue(localeValue, out var info) || !info.Fallback.HasValue)
                {
                    break;
                }

                var next = info.Fallback.Value.Value;
                if (visited.Contains(next))
                {
                    break; // 成环情形已在构造期拦截，这里只是防御
                }
                localeValue = next;
            }

            // 回退链耗尽仍未找到：再查一次默认语言（04 第 7.2 节"某语言缺文本时回退到默认
            // 语言"——即便该语言自己声明的回退链没有走到默认语言，默认语言始终是最终兜底）。
            if (!visited.Contains(DefaultLocale.Value) && _textIndex.TryGetValue(IndexKey(key, DefaultLocale.Value), out var defaultFound))
            {
                text = defaultFound;
                return true;
            }

            text = string.Empty;
            return false;
        }

        /// <summary>变量代入：扫描 <c>{name}</c> 占位符，不支持嵌套（遇到未闭合或嵌套的
        /// <c>{</c> 时把该字符原样保留、继续扫描）。</summary>
        private string SubstituteVars(string text, Id key, IReadOnlyDictionary<string, string>? vars)
        {
            if (text.IndexOf('{') < 0) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '{')
                {
                    var close = text.IndexOf('}', i + 1);
                    var nextOpen = text.IndexOf('{', i + 1);
                    if (close < 0 || (nextOpen >= 0 && nextOpen < close))
                    {
                        sb.Append(c);
                        i++;
                        continue;
                    }

                    var varName = text.Substring(i + 1, close - i - 1);
                    if (vars != null && vars.TryGetValue(varName, out var value))
                    {
                        sb.Append(value);
                    }
                    else
                    {
                        sb.Append(text, i, close - i + 1);
                        if (_options.WarnOnMissingVar)
                        {
                            _diagnostics.Warn($"文本键 \"{key}\" 缺失变量 \"{varName}\"");
                        }
                    }
                    i = close + 1;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}

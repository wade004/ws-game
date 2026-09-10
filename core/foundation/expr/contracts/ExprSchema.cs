using System;
using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// <see cref="IExprSchema"/> 的可编程实现：调用方用 <see cref="Register"/> 逐条登记
    /// <c>group.key</c> 的签名，供内容校验期使用。
    /// </summary>
    public sealed class ExprSchema : IExprSchema
    {
        private readonly Dictionary<string, ExprSignature> _signatures = new Dictionary<string, ExprSignature>(StringComparer.Ordinal);

        /// <summary>按分组维护的已登记 key 集合（消费方反馈第三批第 18 条新增），与
        /// <see cref="_signatures"/> 在 <see cref="Register"/> 中同步更新——不是从 <see cref="_signatures"/>
        /// 的键反查（那样每次 <see cref="KnownKeys"/> 调用都要拆分并比较全部已登记签名的 key，
        /// 这里用一份按分组预先分桶的结构换取 O(该分组已登记数) 而不是 O(全部已登记数)）。</summary>
        private readonly Dictionary<string, List<string>> _keysByGroup = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public ExprSchema Register(string group, string key, ExprValueKind returnKind, params ExprValueKind[] argKinds)
        {
            if (string.IsNullOrEmpty(group)) throw new ArgumentException("group 不能为空", nameof(group));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 不能为空", nameof(key));

            _signatures[MakeKey(group, key)] = new ExprSignature(returnKind, argKinds ?? Array.Empty<ExprValueKind>());

            if (!_keysByGroup.TryGetValue(group, out var keys))
            {
                keys = new List<string>();
                _keysByGroup[group] = keys;
            }
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }

            return this;
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            return _signatures.TryGetValue(MakeKey(group, key), out signature);
        }

        /// <summary>消费方反馈第三批第 18 条：返回 <paramref name="group"/> 下全部已登记 key，
        /// 按序号（Ordinal）排序后返回——顺序在多次调用、多次进程运行之间保持一致（"确定性"），
        /// 不依赖 <see cref="Register"/> 的调用顺序（那样的顺序取决于调用方代码书写顺序，换一种
        /// 书写顺序结果就会不同，不满足"确定性"应有的可重现语义）。<paramref name="group"/> 为
        /// null、未知或该分组尚无任何已登记 key 时返回空集合。</summary>
        public IReadOnlyCollection<string> KnownKeys(string group)
        {
            if (group == null || !_keysByGroup.TryGetValue(group, out var keys) || keys.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(keys);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>消费方反馈第三批第 18 条：本登记表已出现过至少一个已登记 key 的全部分组，
        /// 同样按序号排序返回（与 <see cref="KnownKeys"/> 同一份"确定性"判断记录）。</summary>
        public IReadOnlyCollection<string> KnownGroups
        {
            get
            {
                if (_keysByGroup.Count == 0) return Array.Empty<string>();

                var result = new List<string>(_keysByGroup.Keys);
                result.Sort(StringComparer.Ordinal);
                return result;
            }
        }

        // group/key 合法字符集仅含小写字母、数字、下划线、点；用 '#' 分隔，避免
        // group="a", key="bc" 与 group="ab", key="c" 撞键。
        private static string MakeKey(string group, string key) => group + "#" + key;
    }
}

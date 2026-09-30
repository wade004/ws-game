using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tests.Rules.Culture
{
    /// <summary>
    /// 测试覆盖梳理 T-H13 的共享测试辅助：在 <c>using</c> 作用域内把<b>当前线程</b>的
    /// <see cref="CultureInfo.CurrentCulture"/>/<see cref="CultureInfo.CurrentUICulture"/> 切到指定文化，
    /// Dispose 时恢复原值。只改当前线程（xunit 并行执行时每个用例跑在自己的线程上，
    /// 不影响其它用例）；不碰 <see cref="CultureInfo.DefaultThreadCurrentCulture"/> 这类进程级默认值。
    /// <para>
    /// <see cref="Enter"/> 会校验目标文化确实生效（数字格式与不变文化不同）——若运行环境启用了
    /// 全球化不变模式，切文化会悄悄退化成不变文化，用例就成了永远绿的空测试，这里宁可直接抛异常。
    /// 空串表示不变文化（用来算"基准"输出）。同一份辅助在 Tests.Carriers/Gameplay/Rules/Sim 各有一份拷贝
    /// （各测试工程互不引用），命名空间各自独立。
    /// </para>
    /// </summary>
    public sealed class CultureScope : IDisposable
    {
        /// <summary>小数逗号（de-DE）、i 大小写特殊（tr-TR）、负号为 U+2212（sv-SE）三种非不变文化。</summary>
        public static readonly string[] NonInvariantNames = { "de-DE", "tr-TR", "sv-SE" };

        public static IEnumerable<object[]> NonInvariantCultures()
        {
            foreach (var name in NonInvariantNames)
            {
                yield return new object[] { name };
            }
        }

        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;
        private bool _disposed;

        private CultureScope(CultureInfo target)
        {
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = target;
            CultureInfo.CurrentUICulture = target;
        }

        public static CultureScope Enter(string cultureName)
        {
            var invariant = cultureName.Length == 0;
            var target = invariant ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureName);
            var scope = new CultureScope(target);
            if (!invariant)
            {
                // 与不变文化的格式化结果必须不同，否则说明切换没有实际生效。
                const double probe = -1234.5;
                if (probe.ToString(CultureInfo.CurrentCulture) == probe.ToString(CultureInfo.InvariantCulture))
                {
                    scope.Dispose();
                    throw new InvalidOperationException(
                        $"文化 \"{cultureName}\" 未实际生效（格式化结果与不变文化相同，可能启用了全球化不变模式），文化回归用例会变成空测试");
                }
            }

            return scope;
        }

        /// <summary>在指定文化下执行 <paramref name="produce"/> 并返回结果，随后恢复文化。</summary>
        public static T Run<T>(string cultureName, Func<T> produce)
        {
            using (Enter(cultureName))
            {
                return produce();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Core.Foundation.Common
{
    /// <summary>
    /// 分段线性曲线上的一个断点 <c>(x, y)</c>——数值设计"曲线表"的最小单元（数值总纲第 3 节原则 1、
    /// 落地清单 2.1 C2）。横轴 <see cref="X"/> 的语义（等级 / 物品等级 / 数值）由字段登记声明
    /// （<c>Core.Foundation.DataRegistry.CurveSchema</c>），本类型只承载数值本身。
    /// </summary>
    public readonly struct CurvePoint : IEquatable<CurvePoint>
    {
        public double X { get; }

        public double Y { get; }

        public CurvePoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(CurvePoint other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is CurvePoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public static bool operator ==(CurvePoint left, CurvePoint right) => left.Equals(right);

        public static bool operator !=(CurvePoint left, CurvePoint right) => !left.Equals(right);

        public override string ToString() =>
            "(" + X.ToString(CultureInfo.InvariantCulture) + ", " + Y.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// 分段线性曲线：一组按 <see cref="CurvePoint.X"/> 升序排列的断点，断点之间线性插值、两端之外
    /// 夹取到端点、空表恒为 0。这是数值设计全部"横轴为等级/物品等级/数值的曲线表"共用的唯一插值
    /// 实现（数值总纲第 3 节原则 1"曲线单调"、原则 5"尺度无关"；落地清单 2.1 C2；分阶段落地计划
    /// T-N0-1）——各模块的曲线表（物品预算曲线、评级换算曲线、抗性 <c>table</c> 分支……）一律委托到
    /// 本类型，不再各自复制一份插值循环。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <list type="number">
    /// <item><description><b>确定性</b>：只含加减乘除与比较，不含任何超越函数（11 第 8 节"引入新的
    /// 浮点超越函数运算路径时须评估是否需要定点化"——本类型没有这条路径）；断点排序用稳定插入排序，
    /// 相同 <c>x</c> 保持输入顺序，同一输入在任何平台上得到同一断点序列。</description></item>
    /// <item><description><b>插值式子固定为 <c>y0 + t × (y1 − y0)</c>，<c>t = (x − x0) / (x1 − x0)</c></b>
    /// ——与迁移前 <c>ItemBudgetCurve.Interpolate</c>/<c>StatHost.ConvertRating</c> 各自手写的式子
    /// 逐运算相同，保证既有曲线表迁移到本类型后结果逐位一致（分阶段落地计划 T-N0-4 验收标准）。
    /// 不改写成 <c>(y1 − y0) × (x − x0) / (x1 − x0)</c> 这类数学等价但浮点舍入不同的形式。</description></item>
    /// <item><description><b>越界夹取、空表为 0</b>：低于首断点取首断点 <c>y</c>、高于末断点取末断点
    /// <c>y</c>（沿 <c>ItemBudgetCurve</c> 既有约定"夹取到边界这一最不容易产生离谱结果的近似"）；
    /// 没有断点时恒返回 0（等价于"没有约束"，应已被 <c>required_field</c>/曲线校验规则拦下，这里只是
    /// 兜底不抛异常）。</description></item>
    /// <item><description><b>重复 <c>x</c></b>：允许存在（由校验规则决定是否阻断，本类型不做判断），
    /// 落在重复 <c>x</c> 上的查询取"以该 <c>x</c> 为右端的第一段"的右端点，即排序后先出现的那一个
    /// 断点的 <c>y</c>（与迁移前实现一致）。</description></item>
    /// <item><description><b>不做单调/有限性检查</b>：<see cref="IsNonDecreasing"/>/<see cref="IsFinite"/>
    /// 只是只读查询，供校验规则（<c>curve_monotonic_finite</c>，T-N0-3）与工具调用；构造与求值不因
    /// 非单调/非有限而抛异常——"内容错误在加载期由校验器报告，运行期不二次判定"（04 第 5 节）。</description></item>
    /// </list>
    /// </summary>
    public sealed class PiecewiseCurve
    {
        /// <summary>没有任何断点的曲线，<see cref="Evaluate"/> 恒为 0。</summary>
        public static readonly PiecewiseCurve Empty = new PiecewiseCurve(Array.Empty<CurvePoint>());

        private readonly CurvePoint[] _points;

        /// <summary>按 <see cref="CurvePoint.X"/> 升序（稳定）排列后的断点；只读快照，与构造时传入的
        /// 集合无共享。</summary>
        public IReadOnlyList<CurvePoint> Points => _points;

        /// <summary>断点个数。</summary>
        public int Count => _points.Length;

        /// <summary>复制 <paramref name="points"/> 并按 <c>x</c> 稳定排序（数据文件本身不要求有序，
        /// 本类型自行排序，不信任内容作者的书写顺序——沿 <c>ItemBudgetCurve.ParseEntries</c> 既有约定）。</summary>
        public PiecewiseCurve(IReadOnlyList<CurvePoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var copy = new CurvePoint[points.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = points[i];
            }

            StableSortByX(copy);
            _points = copy;
        }

        /// <summary>按 <paramref name="x"/> 在曲线上取值：空表返回 0；<paramref name="x"/> 不高于首
        /// 断点返回首断点 <c>y</c>，不低于末断点返回末断点 <c>y</c>；否则在包含它的那一段上线性插值
        /// （式子见类型顶部判断记录 2）。<paramref name="x"/> 为 NaN 时三段比较全不成立，落到末断点
        /// <c>y</c>——非有限输入属内容/调用错误，由校验规则与调用方各自把关，本方法不抛异常。</summary>
        public double Evaluate(double x)
        {
            if (_points.Length == 0)
            {
                return 0;
            }

            var first = _points[0];
            if (x <= first.X)
            {
                return first.Y;
            }

            var last = _points[_points.Length - 1];
            if (x >= last.X)
            {
                return last.Y;
            }

            for (var i = 0; i < _points.Length - 1; i++)
            {
                var lo = _points[i];
                var hi = _points[i + 1];
                if (x >= lo.X && x <= hi.X)
                {
                    if (hi.X == lo.X)
                    {
                        return lo.Y;
                    }

                    var t = (x - lo.X) / (hi.X - lo.X);
                    return lo.Y + t * (hi.Y - lo.Y);
                }
            }

            // 不可达：x 已被上面的两个边界分支夹在 [first.X, last.X] 之间。
            return last.Y;
        }

        /// <summary><c>y</c> 沿断点序列不递减（允许相等）。断点少于两个视为满足；任一相邻对比较不成立
        /// （含 NaN）即为 false。</summary>
        public bool IsNonDecreasing()
        {
            for (var i = 0; i < _points.Length - 1; i++)
            {
                if (!(_points[i + 1].Y >= _points[i].Y))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>全部断点的 <c>x</c>、<c>y</c> 均为有限数（既非 NaN 也非无穷）。空表视为满足。</summary>
        public bool IsFinite()
        {
            for (var i = 0; i < _points.Length; i++)
            {
                if (!IsFiniteNumber(_points[i].X) || !IsFiniteNumber(_points[i].Y))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFiniteNumber(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>稳定插入排序（曲线表断点数量是个位数到百位数，不值得为它引入不稳定的
        /// <c>Array.Sort</c> 再补索引比较；比较用 <see cref="double.CompareTo(double)"/>，NaN 有确定的
        /// 全序位置，保证排序结果与平台无关）。</summary>
        private static void StableSortByX(CurvePoint[] points)
        {
            for (var i = 1; i < points.Length; i++)
            {
                var key = points[i];
                var j = i - 1;
                while (j >= 0 && points[j].X.CompareTo(key.X) > 0)
                {
                    points[j + 1] = points[j];
                    j--;
                }

                points[j + 1] = key;
            }
        }
    }
}

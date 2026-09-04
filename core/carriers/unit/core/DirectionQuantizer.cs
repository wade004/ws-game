using System;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 把连续朝向角度量化为方向精灵索引（见 05 第 3.2 节"量化规则：仅 sprite 型外形使用：实际朝向
    /// 角度按 360 / direction_count 分桶，取最近桶对应的方向精灵"）。纯函数，不持有任何状态——是否
    /// 调用本函数（即该单位外形是 sprite 型还是 model 型）由表现层决定（见 05 第 3.2 节"量化只对
    /// sprite 型外形在表现层进行"），本函数只提供量化算法本身。
    /// </summary>
    public static class DirectionQuantizer
    {
        /// <summary>把 facingRadians（任意弧度值，含负数与超过 2π 的值）量化为 [0, directionCount)
        /// 区间的桶索引：0 号桶正对角度 0（即 +X 轴方向），按角度递增方向依次编号。directionCount
        /// 仅接受 05 第 3.2 节规定的 4/8/16 三档之一，其它值抛 ArgumentOutOfRangeException。桶边界
        /// （两桶正中间的角度）按 MidpointRounding.AwayFromZero 取整（05 原文未规定边界取整策略，
        /// 选用"远离零舍入"是判断记录：结果确定、易于测试断言，不依赖 Math.Round(double) 默认的
        /// "向偶数舍入"这一较不直观的隐式行为），再对 directionCount 取模折回 0 号桶（覆盖"最近 2π
        /// 边界四舍五入进位到 directionCount"的情形）。</summary>
        public static int Quantize(double facingRadians, int directionCount)
        {
            if (directionCount != 4 && directionCount != 8 && directionCount != 16)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(directionCount), directionCount,
                    "direction_count 仅支持 4/8/16 三档（见 05 第 3.2 节）");
            }

            var normalized = NormalizeAngle(facingRadians);
            var sectorSize = 2 * Math.PI / directionCount;
            var index = (int)Math.Round(normalized / sectorSize, MidpointRounding.AwayFromZero) % directionCount;
            return index;
        }

        /// <summary>把任意弧度值折算到 [0, 2π) 区间。</summary>
        private static double NormalizeAngle(double radians)
        {
            var twoPi = 2 * Math.PI;
            var result = radians % twoPi;
            if (result < 0)
            {
                result += twoPi;
            }

            return result;
        }
    }
}

using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 竖直轴（体积空间 / 横版二维能力包）的口味配置（手感设计/06 第 10 节勘误 9、<c>core/carriers/unit/README.md</c> 判断记录）。
    /// 挂在 <see cref="MovementOptions.Vertical"/> 上：<b>为 <c>null</c>（缺省）时世界没有竖直运动轴</b>，
    /// <see cref="Unit.HeightOffset"/> 只是表现参数（05 第 3.3 节），行为与引入本类型之前逐位一致。
    /// <para>
    /// 判断记录（单位）：重力与跳跃高度都用<b>世界单位</b>，不经手感标定换算——空间能力属于世界，不属于某个手感预设
    /// （同一张地图换预设，重力不该跟着变）；需要按身高比例声明的游戏在装配处自己乘标定参考身高。
    /// </para>
    /// </summary>
    public sealed class VerticalAxisOptions
    {
        /// <summary>重力加速度（世界单位/秒²，向下为正数值），必须为正有限数。默认 30。</summary>
        public double Gravity { get; set; } = 30.0;

        /// <summary><see cref="IVerticalMotion.Jump"/> 的跳跃顶点高度（世界单位，从脚下起算），必须为正有限数。默认 1.5。
        /// 起跳初速由 <c>sqrt(2 × Gravity × JumpHeight)</c> 算出。</summary>
        public double JumpHeight { get; set; } = 1.5;

        /// <summary>空中是否允许再次跳跃（二段跳）。默认 false：空中 <see cref="IVerticalMotion.Jump"/> 返回 false。</summary>
        public bool AllowAirJump { get; set; }

        /// <summary>校验取值，非法时抛 <see cref="ArgumentOutOfRangeException"/>（装配期调用，不静默夹取）。</summary>
        public void Validate()
        {
            if (!(Gravity > 0.0) || double.IsInfinity(Gravity))
            {
                throw new ArgumentOutOfRangeException(nameof(Gravity), Gravity, "重力必须为正的有限数");
            }

            if (!(JumpHeight > 0.0) || double.IsInfinity(JumpHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(JumpHeight), JumpHeight, "跳跃高度必须为正的有限数");
            }
        }
    }

    /// <summary>
    /// 竖直运动服务：单位在重力下的抛体运动（跳跃、击飞），驱动 <see cref="Unit.HeightOffset"/>。
    /// <para>
    /// 运动学：脚下高度 <c>h(t) = h0 + v0·t − g·t²/2</c>（<c>t</c> 为起飞后累计的模拟时间），首次 <c>h ≤ 0</c> 的步落地，
    /// 高度归零并结束飞行。地面恒为 0（没有斜坡与台阶，见 unit README 已知局限）。从地面起飞（<c>h0 = 0</c>）时落地所需步数为
    /// <c>⌈2·v0 / (g·dt)⌉</c>——复现用例据此由重力与初速算出期望值。
    /// </para>
    /// </summary>
    public interface IVerticalMotion
    {
        /// <summary>该单位当前是否在空中（本服务正在为它积分竖直运动）。</summary>
        bool IsAirborne(Id unitId);

        /// <summary>当前竖直速度（世界单位/秒，向上为正）；不在空中为 0。</summary>
        double GetVerticalSpeed(Id unitId);

        /// <summary>
        /// 以初速 <paramref name="initialSpeed"/>（世界单位/秒，向上为正，必须为正有限数）把单位抛起；已在空中则以当前高度重新起算
        /// （替换进行中的飞行，不叠加）。单位不存在或已死亡时返回 false。
        /// </summary>
        bool Launch(Id unitId, double initialSpeed);

        /// <summary>
        /// 以"顶点高度 <paramref name="apexHeight"/>"抛起（初速 <c>sqrt(2·g·H)</c>，H 为相对<b>当前脚下高度</b>再升高的量）。
        /// 供击飞按"飞多高"声明（设计者比初速直观）；约束同 <see cref="Launch"/>。
        /// </summary>
        bool LaunchToApex(Id unitId, double apexHeight);

        /// <summary>
        /// 玩家主动跳跃：以 <see cref="VerticalAxisOptions.JumpHeight"/> 起跳。单位在空中（且未开 <see cref="VerticalAxisOptions.AllowAirJump"/>）、
        /// 不存在或已死亡时返回 false，不改变任何状态。
        /// </summary>
        bool Jump(Id unitId);
    }
}

using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 竖直轴（体积空间 / 横版二维能力包）的口味配置（手感设计/06 第 1.2 节、<c>core/carriers/unit/README.md</c> 判断记录）。
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

        /// <summary>空中是否允许再次跳跃（二段跳，不限次）。默认 false：空中 <see cref="IVerticalMotion.Jump"/> 返回 false。
        /// 要限制次数用 <see cref="MaxAirJumps"/>（非空时覆盖本字段）。</summary>
        public bool AllowAirJump { get; set; }

        /// <summary>
        /// 空中横向控制比例（0～1，ADR-0130 追加决定）：<b>单位在空中（跳跃、被击飞、下落）时自愿移动（方向输入、路径跟随、追击）的速度 =
        /// 地面速度 × 本比例</b>；0 = 空中不接受移动输入（惯性不模拟，水平位置保持），1 = 与地面一致。<c>null</c>（缺省）= 不限制，
        /// 空中与地面完全一致——这是 1.95.0 的实际行为（空中仍按普通移动处理），缺省路径不读本字段、逐位不变。
        /// 受控位移（击退、动作位移）不受影响。
        /// </summary>
        public double? AirControl { get; set; }

        /// <summary>
        /// 空中最多还能再跳几次（<see cref="IVerticalMotion.Jump"/> 在空中成功的次数上限，每次离地重新计数；ADR-0130 追加决定，
        /// <see cref="AllowAirJump"/> 的推广）。<c>null</c>（缺省）= 由 <see cref="AllowAirJump"/> 决定：<c>false</c> 即 0 次、
        /// <c>true</c> 即不限次（与 1.95.0 一致）；非 null 时以本字段为准（<see cref="AllowAirJump"/> 被忽略）。被击飞后起跳按
        /// "已在空中"计数。必须不小于 0。
        /// </summary>
        public int? MaxAirJumps { get; set; }

        /// <summary>
        /// 地形高度查询（可选，ADR-0130 追加决定"地面高度"）：<c>null</c>（缺省）= 地面恒为 0、没有天花板，竖直运动不被地形阻挡
        /// （1.95.0 行为，逐位不变）。非空时：落地于该点的地面高度而不是 0、上升碰天花板竖直速度清零、地面行走脚下高度贴合地面
        /// （斜坡；每 tick 对位置变化过的地面单位重新取地面高度）。数据实现见 <c>MapTerrainHeights</c>（<c>world.map.terrain</c>），
        /// 引擎适配层可用物理射线实现。
        /// </summary>
        public Core.Foundation.EngineAdapter.ITerrainHeight2D? Terrain { get; set; }

        /// <summary>
        /// 台阶高度上限（世界单位，必须为正；ADR-0130 追加决定，需要 <see cref="Terrain"/>）：<c>null</c>（缺省）= 不阻挡，单位总是被抬到
        /// 前方地面高度。非空时：<b>前方地面高出脚下超过本值视为阻挡</b>（移动被挡，走与导航地形阻挡同一出口——方向移动按
        /// <c>Raycast</c> 截断并贴着阻挡停住、受控位移按阻挡策略结束、路径跟随以 <c>MoveStopReason.TerrainBlocked</c> 结束；
        /// 寻路本身也避开这些台阶，见 <see cref="ITerrainStepConstraint"/>）。判定是滑窗规则（<see cref="StepSampleDistance"/>）：
        /// 地面在一个窗口长度内升高超过本值即挡，因此它同时给出可走坡度上限（斜率 &gt; <c>StepHeight / StepSampleDistance</c> 的坡走不上去）；
        /// 空中的单位比较的是前方地面与自己当前脚下高度之差。本值只决定"能不能走上去"：走出平台边缘是否下落由
        /// <see cref="FallHeight"/> 决定，与是否声明本值无关。
        /// </summary>
        public double? StepHeight { get; set; }

        /// <summary>
        /// 落地时是否发 <c>unit.landed</c> 事件（<see cref="Core.Carriers.Common.UnitLandedEvent"/>，手感落地 M4-W1b）：<c>false</c>（缺省）= 不发，
        /// 事件流与引入之前逐位一致（重放/事件计数类的既有检查不受影响）；<c>true</c> = 每次飞行结束（跳跃、击飞、离开平台下落）发一条，带落地高度、
        /// 本次离地以来的空中时长与落地时的下落速度。
        /// </summary>
        public bool EmitLandedEvent { get; set; }

        /// <summary>台阶/坡度判定的窗口长度（世界单位，必须为正有限数）：地面在一个窗口内升高超过 <see cref="StepHeight"/> 即挡
        /// （滑窗规则，见 <c>TerrainStepMath</c>），也是走出平台边缘的悬崖判定窗口，同时是地形台阶特征的分辨率。默认 0.1。</summary>
        public double StepSampleDistance { get; set; } = 0.1;

        /// <summary>
        /// 走出平台边缘离地下落的落差阈值（世界单位，必须为正有限数；ADR-0130 追加决定，需要 <see cref="Terrain"/>）：贴地行走的单位本 tick 走过的线段上，
        /// 地面在一个 <see cref="StepSampleDistance"/> 窗口内下降超过它即视为悬崖，单位离地下落（从当前脚下高度起、初速 0 的抛体）；
        /// 缓下坡贴着地面走下。<c>null</c>（缺省）= 取 <see cref="StepHeight"/>，再缺省取 <see cref="StepSampleDistance"/>（即比 45° 更陡的下坡算悬崖）。
        /// 无论是否声明 <see cref="StepHeight"/>，走出平台都会下落——台阶高度只决定"能不能走上去"。
        /// </summary>
        public double? FallHeight { get; set; }

        /// <summary>
        /// 单向平台与移动平台（可选，ADR-0170，需要 <see cref="Terrain"/>——平台叠在地形之上；不需要地形起伏的横版关卡用
        /// <see cref="Core.Foundation.EngineAdapter.FlatTerrainHeight2D.Instance"/>）：<c>null</c>（缺省）= 没有平台，行为逐位不变。非空时：
        /// 下落中的单位脚下穿过平台顶面会落在平台上（上升中与平台下方的单位不受影响）、站在平台上的单位走出平台范围会离地下落（走出平台边缘，
        /// 同悬崖，可土狼起跳）、移动平台带走站在上面的单位、<see cref="IVerticalMotion.DropThrough"/> 让站在平台上的单位向下穿过它。
        /// 数据实现见 <c>MapPlatforms</c>（<c>world.map.platforms</c>）。
        /// </summary>
        public Core.Foundation.EngineAdapter.ITerrainPlatforms2D? Platforms { get; set; }

        /// <summary>校验取值，非法时抛 <see cref="ArgumentOutOfRangeException"/>（装配期调用，不静默夹取）。</summary>
        public void Validate()
        {
            if (Platforms != null && Terrain == null)
            {
                throw new ArgumentException("声明平台（Platforms）必须同时声明地形（Terrain）；平地关卡用 FlatTerrainHeight2D.Instance", nameof(Platforms));
            }

            if (!(Gravity > 0.0) || double.IsInfinity(Gravity))
            {
                throw new ArgumentOutOfRangeException(nameof(Gravity), Gravity, "重力必须为正的有限数");
            }

            if (!(JumpHeight > 0.0) || double.IsInfinity(JumpHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(JumpHeight), JumpHeight, "跳跃高度必须为正的有限数");
            }

            if (AirControl.HasValue && !(AirControl.Value >= 0.0 && AirControl.Value <= 1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(AirControl), AirControl.Value, "空中控制比例必须在 0～1 之间");
            }

            if (MaxAirJumps.HasValue && MaxAirJumps.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxAirJumps), MaxAirJumps.Value, "空中跳跃次数不能为负");
            }

            if (StepHeight.HasValue && !(StepHeight.Value > 0.0 && !double.IsInfinity(StepHeight.Value)))
            {
                throw new ArgumentOutOfRangeException(nameof(StepHeight), StepHeight.Value, "台阶高度必须为正的有限数");
            }

            if (!(StepSampleDistance > 0.0) || double.IsInfinity(StepSampleDistance))
            {
                throw new ArgumentOutOfRangeException(nameof(StepSampleDistance), StepSampleDistance, "台阶采样间距必须为正的有限数");
            }

            if (FallHeight.HasValue && !(FallHeight.Value > 0.0 && !double.IsInfinity(FallHeight.Value)))
            {
                throw new ArgumentOutOfRangeException(nameof(FallHeight), FallHeight.Value, "下落落差阈值必须为正的有限数");
            }
        }
    }

    /// <summary>
    /// 竖直运动服务：单位在重力下的抛体运动（跳跃、击飞），驱动 <see cref="Unit.HeightOffset"/>。
    /// <para>
    /// 运动学：脚下高度 <c>h(t) = h0 + v0·t − g·t²/2</c>（<c>t</c> 为起飞后累计的模拟时间），首次脚下高度不高于地面（平地即 <c>h ≤ 0</c>）的步落地，
    /// 高度回到落点的地面高度并结束飞行（缺省地面恒为 0；声明了 <see cref="VerticalAxisOptions.Terrain"/> 时是落点的地面高度，上升中碰到天花板速度清零）。从地面起飞（<c>h0 = 0</c>，平地）时落地所需步数为
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

        /// <summary>
        /// 本次离地以来已用掉的空中跳跃次数（地面起跳不计）；不在空中为 0。默认接口成员（既有实现无需改动）：恒 0。
        /// </summary>
        int AirJumpsUsed(Id unitId) => 0;

        /// <summary>
        /// 该单位此刻空中自愿移动的速度倍率（<see cref="VerticalAxisOptions.AirControl"/>）：在空中且声明了比例时返回该比例，其余恒 1。
        /// 默认接口成员：恒 1。
        /// </summary>
        double GetAirControl(Id unitId) => 1.0;

        /// <summary>
        /// 此刻全部在空中的单位（快照，供表现层观测空中阶段）。默认接口成员：空列表。
        /// </summary>
        System.Collections.Generic.IReadOnlyList<Id> AirborneUnits() => System.Array.Empty<Id>();

        /// <summary>
        /// 当前这次空中飞行是不是"走出平台边缘的自然下落"（没有被跳跃/击飞抛起过；ADR-0143 土狼时间的前提）。默认接口成员：恒 false。
        /// </summary>
        bool IsLedgeFall(Id unitId) => false;

        /// <summary>
        /// 离开平台边缘后的宽限起跳（土狼时间，ADR-0143）：单位处于 <see cref="IsLedgeFall"/> 的自然下落中时，按地面起跳处理——
        /// 以 <see cref="VerticalAxisOptions.JumpHeight"/> 起跳，<b>不</b>占用空中跳跃次数。不在边缘下落（地面上、被抛起的飞行中）返回 false，不改变状态。
        /// 默认接口成员：恒 false。
        /// </summary>
        bool JumpFromLedge(Id unitId) => false;

        /// <summary>
        /// 可变跳跃高度（ADR-0143）：单位正在上升时把竖直速度截断为当前速度的 <paramref name="ratio"/> 倍（0 &lt; ratio &lt; 1），保持当前高度、
        /// 空中跳跃次数与空中时间。不在空中或已在下降返回 false，不改变状态。默认接口成员：恒 false。
        /// </summary>
        bool CutAscent(Id unitId, double ratio) => false;

        /// <summary>
        /// 下穿单向平台（ADR-0170）：单位正站在一块平台上时，离地向下落（初速 0），并在这次飞行里不再被那块平台接住
        /// （落到下面的地面或别的平台才结束；不是边缘下落，不可土狼起跳）。不在平台上（地面上、空中、没有声明平台能力）返回 false，不改变状态。
        /// 默认接口成员：恒 false。
        /// </summary>
        bool DropThrough(Id unitId) => false;

        /// <summary>
        /// 向下俯冲（ADR-0170，下劈/俯冲斩一类动作）：<b>空中</b>的单位把竖直速度换成向下的 <paramref name="downSpeed"/>（世界单位/秒，正的有限数），
        /// 从当前高度继续抛体（重力照常加速），保持空中跳跃次数与空中时间。不在空中、单位不存在或已死亡返回 false，不改变状态；
        /// <paramref name="downSpeed"/> 不是正的有限数抛 <see cref="ArgumentOutOfRangeException"/>。默认接口成员：恒 false。
        /// </summary>
        bool Plunge(Id unitId, double downSpeed) => false;

        /// <summary>
        /// 单位当前站着的平台 id（<see cref="VerticalAxisOptions.Platforms"/>）；不在平台上返回 null。默认接口成员：恒 null。
        /// </summary>
        string? StandingPlatform(Id unitId) => null;
    }
}

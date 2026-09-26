using System.Collections.Generic;

namespace Presentation.Render
{
    /// <summary>命中帧同步策略（见 09_表现层.md 第 4.3 节"同步策略（二选一……）"）：
    /// <see cref="LogicDriven"/>（建议默认）逻辑层效果结算 tick 广播命中事件即播放命中反馈；
    /// <see cref="AnimKeyframeDriven"/> 延后到动画剪辑到达"命中关键帧"（<c>sprite</c> 型经
    /// <see cref="FrameAnimClip.HitFrameMarker"/>，<c>model</c> 型经 <c>display.anim_set</c> 登记的
    /// 关键帧事件，由 <c>IRenderer3D.onAnimEvent</c> 回调触发）才播放命中反馈的视觉呈现——两种策略都
    /// 不改变"逻辑判定结果与时机永远由规则层按固定步长决定"这一事实，只影响命中反馈动作的呈现时机
    /// （09 第 4.3 节末句）。</summary>
    public enum HitFrameSyncStrategy
    {
        LogicDriven,
        AnimKeyframeDriven,
    }

    /// <summary>
    /// 混合渲染约定的口味配置项（见 01 L5 模块表 <c>render</c> 行"策略配置项：排序精度、方向量化
    /// 档位数、外形类型选择"；09 第 3.6 节"参考分辨率……均为可配置项，具体数值不在本架构拍板"）。
    /// 本类型只给出合理默认值，具体数值由游戏层在接入时按 [13_新游戏接入指南.md] 的口味配置清单
    /// 覆盖，不属于架构拍板内容。
    /// </summary>
    public sealed class RenderOptions
    {
        /// <summary>默认方向量化档位数（4/8/16 之一，见 05 第 3.2 节）。默认 8。</summary>
        public int DirectionCount { get; set; } = 8;

        /// <summary>每逻辑单位对应的像素数，供高度偏移换算像素位移（见 09 第 3.4 节、
        /// <see cref="IRenderConventionHost.HeightOffsetToPixels"/>）。默认 32。</summary>
        public double PixelsPerUnit { get; set; } = 32.0;

        /// <summary>
        /// 缺口 8（方向索引重映射策略）：05 第 3.1 节量化方向索引的"index 0 = 角度 0（+X 轴），按角度
        /// 递增方向（逆时针）编号"是本架构固定的默认镜头朝向约定；具体游戏的镜头朝向/世界坐标轴习惯
        /// 与该默认不一致时（13 号文档口味配置项），不改 <c>DirectionSlots</c>/<c>DirectionQuantizer</c>
        /// 这两处固定原语，而是在 <see cref="IRenderConventionHost.ResolveDirectionSlot"/> 消费量化索引
        /// 之前先经本表重映射一次：<c>remappedIndex = DirectionIndexRemap[rawIndex]</c>。长度必须等于
        /// 当前方向档位数（<see cref="Presentation.Common.Direction.DirectionCount"/>），否则
        /// <see cref="RenderConventionHost"/> 按长度不匹配忽略本表、退化为恒等映射（不抛异常，避免游戏层
        /// 接入某个新方向档位数时忘记同步重映射表而中断渲染）。默认 null（恒等映射，即完全遵循 05 默认
        /// 约定，不做任何重映射）。
        /// </summary>
        public IReadOnlyList<int>? DirectionIndexRemap { get; set; }

        /// <summary>命中帧同步策略（见 <see cref="HitFrameSyncStrategy"/>、09 第 4.3 节"策略……按武器
        /// 表现档案/技能配置声明，建议默认值见括号"）。本口味配置项是"游戏级默认策略"这一层——按
        /// 09 原文"策略……按武器表现档案/技能配置声明"，更细粒度的按武器/技能覆盖不在本轮落地范围，
        /// 留待具体游戏在 <c>display.weapon_style</c>/技能配置补充对应字段时再接入（见
        /// <c>SpriteCharacterRig</c> 判断记录）。默认 <see cref="HitFrameSyncStrategy.LogicDriven"/>
        /// （09 建议默认）。</summary>
        public HitFrameSyncStrategy HitFrameSync { get; set; } = HitFrameSyncStrategy.LogicDriven;

        /// <summary>
        /// ADR-0094 决策 1：与方向档位数无关的朝向约定口味项之一——镜像 Y 分量（等价于把原始朝向角
        /// 取负：(cosθ, sinθ) 镜像 Y 后是 (cosθ, -sinθ) = (cos(-θ), sin(-θ))）。与
        /// <see cref="FacingAngleOffsetRadians"/> 一起，在 <see cref="IRenderConventionHost.ApplyFacingConvention"/>
        /// 里按"先镜像、再加偏移"的顺序作用在<b>原始朝向角</b>上（量化之前，见该方法注释），解决
        /// <see cref="DirectionIndexRemap"/> 只能按固定方向档位数登记一张表、换一档方向数（4/8/16）就要
        /// 重新配一张表的问题——本组口味项与方向档位数无关，同一份配置对任意档位数生效。默认
        /// <c>false</c>（不镜像，行为与改动前逐字节一致）。</summary>
        public bool MirrorFacingY { get; set; }

        /// <summary>
        /// ADR-0094 决策 1：与 <see cref="MirrorFacingY"/> 同组的角度偏移口味项（弧度，"先镜像、再加
        /// 偏移"顺序里的第二步），典型用途——具体引擎适配层把逻辑坐标系原样写成引擎坐标系时（如
        /// <c>+Y</c> 朝屏幕上方），若与 <see cref="Presentation.Common.DirectionSlots"/> 固定假定的
        /// "<c>+Y</c> 朝观察者"差 180°（见 <c>presentation/render/README.md</c>"朝向约定"一节），配
        /// <c>Math.PI</c> 即可修正——等价于按方向档位数手工登记一张旋转半圈的
        /// <see cref="DirectionIndexRemap"/>，但不需要随方向档位数（4/8/16）各配一张表。默认 <c>0</c>
        /// （不偏移，行为与改动前逐字节一致）。</summary>
        public double FacingAngleOffsetRadians { get; set; }
    }
}

#nullable enable
// LabPlaygroundExtension：手感试玩宿主（LabPlayground）的公开扩展点（ADR-0160）。
//
// 判断记录（为什么有它）：试玩宿主原先内置"演示场景"开关——额外数据根、环绕镜头、面板缺省收起、游戏内 HUD、角落提示、占位场景的指路文字。
// 演示场景搬去样板仓库后，宿主只留下这个扩展类；样板（以及任何游戏自己的试玩场景）继承它，经 <see cref="LabPlayground.Extension"/>
// 在 <see cref="LabPlayground.Begin"/> 之前挂上。舞台一侧的挂接见 <see cref="EngineStageExtension"/>（由 <see cref="CreateStageExtension"/> 给出）。
// 缺省实现全部是空操作：没有扩展时宿主与此前的占位美术试玩场景逐位一致。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FeelLab.Unity
{
    public abstract class LabPlaygroundExtension
    {
        /// <summary>
        /// 某个格子额外并入的数据根（仓库根相对路径；只含外形表之类的呈现数据时不改变逻辑）。宿主在装配数据集前读一次，已有的根不重复加。
        /// </summary>
        public virtual IReadOnlyList<string> ExtraDataRoots(string cell) => Array.Empty<string>();

        /// <summary>某个格子的舞台扩展（<see cref="EngineLabOptions.StageExtension"/>）；每次开局调用一次，null = 占位美术舞台。</summary>
        public virtual EngineStageExtension? CreateStageExtension(string cell) => null;

        /// <summary>
        /// 某个格子是否装鼠标环绕镜头（宿主还要求格子名以 <c>3d_</c> 开头、舞台相机带俯仰、<see cref="LabPlayground.OrbitCameraAllowed"/> 未关）。
        /// </summary>
        public virtual bool WantsOrbitCamera(string cell) => false;

        /// <summary>调试面板开局是否展开（false = 收起，F1 展开，屏上只留扩展自己的界面）。</summary>
        public virtual bool PanelInitiallyVisible => true;

        /// <summary>面板头部第二行的提示文字（例如占位美术场景指路到真实美术场景）；null = 不显示。</summary>
        public virtual string? PanelHint(string cell) => null;

        /// <summary>面板收起时右上角的一行提示；返回 null = 宿主画缺省的操作提示（左下角）。</summary>
        public virtual string? CornerHint(LabPlayground playground) => null;

        /// <summary>会话装配完成后调用一次（舞台已建好）：扩展在这里挂自己的界面，<paramref name="playground"/> 的 Transform 可作父物体。</summary>
        public virtual void OnSessionStarted(LabPlayground playground, EngineLabStage stage)
        {
        }

        /// <summary>会话结束（舞台释放之前）调用：扩展在这里销毁自己挂的界面。</summary>
        public virtual void OnSessionEnded()
        {
        }
    }
}

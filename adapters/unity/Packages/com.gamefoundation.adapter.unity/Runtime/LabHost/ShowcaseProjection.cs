#nullable enable
// ShowcaseProjection：演示场景（ADR-0154 / ADR-0157）的"呈现投影"——同一份导演、同一份界面，既能画在 2D 正交俯视相机下，
// 也能画在 2.5D 固定俯角透视相机下。
//
// 判断记录（直立广告牌，而不是新写一套 2.5D 导演）：适配层的 2D 渲染器把精灵、特效摆在世界平面（Z = 0）上，俯角相机看到的是它们"躺在地上"的
// 透视投影（见 <c>UnityCamera</c> 顶部判断记录：不做 billboard 是渲染层的取舍，不在相机里偷偷做）。真实美术的角色、道具、命中火花要"站起来"，
// 所以演示场景在舞台这一层做广告牌：角色/道具/特效的朝向取舞台相机当前姿态（与相机平面平行、脚底枢轴落在地面点上），地面砖、阴影、冲击波环仍躺在地面。
// 朝向读的是相机的实时姿态（模板预设会改俯角，见 <c>EngineLabStage.ApplyTemplateFraming</c>），所以换模板后广告牌自动跟着转。
// 判断记录（只改引擎侧）：投影只决定"引擎侧物体摆在哪、朝哪"，不读也不写逻辑世界，逻辑指纹与录制度量不受影响。
// 判断记录（2D 逐位不变）：平面模式（<see cref="Upright"/> 为假）下全部换算退化为此前的 2D 公式——抬高 = 世界 Y 加高度、朝向 = 绕 Z 轴旋转，
// 所以 2D 演示场景的画面与既有用例不变。
using UnityEngine;

namespace Adapter.Unity.LabHost
{
    internal sealed class ShowcaseProjection
    {
        private readonly Camera? _camera;

        public ShowcaseProjection(Camera? camera, bool upright)
        {
            _camera = camera;
            Upright = upright && camera != null;
        }

        /// <summary>真：2.5D 固定俯角——角色/道具/特效是与相机平面平行的直立广告牌；假：2D 正交俯视（全部躺在世界平面上）。</summary>
        public bool Upright { get; }

        /// <summary>舞台相机当前姿态（平面模式恒为单位旋转）。</summary>
        public Quaternion CameraRotation => Upright ? _camera!.transform.rotation : Quaternion.identity;

        /// <summary>"向上"在世界里的方向：广告牌立起来的方向（直立模式取相机上轴），平面模式是世界 +Y。</summary>
        public Vector3 Up => Upright ? _camera!.transform.up : Vector3.up;

        /// <summary>地面点抬高 <paramref name="height"/>（世界单位）后的世界坐标：直立模式沿相机上轴抬，平面模式沿世界 Y 加高度。</summary>
        public Vector3 Lift(Vector2 ground, float height) => new Vector3(ground.x, ground.y, 0f) + Up * height;

        /// <summary>广告牌朝向：与相机平面平行，再在屏幕平面内转 <paramref name="screenDegrees"/>；平面模式就是绕 Z 轴转该角度。</summary>
        public Quaternion Facing(float screenDegrees) => CameraRotation * Quaternion.Euler(0f, 0f, screenDegrees);

        /// <summary>躺在地面上的朝向（阴影、冲击波环、地面砖）：绕世界 Z 轴转 <paramref name="degrees"/>。</summary>
        public static Quaternion Flat(float degrees) => Quaternion.Euler(0f, 0f, degrees);

        /// <summary>
        /// 地面上朝向 <paramref name="facingRadians"/> 的方向在屏幕平面里的角度（度）：平面模式就是朝向角本身；直立模式把地面方向投到相机的右轴与上轴上
        /// （俯角把屏幕纵向压扁 cos(俯角) 倍），挥砍拖影这类"沿出手方向画的条"据此转，才与角色看上去的朝向一致。
        /// </summary>
        public float ScreenAngleDegrees(double facingRadians)
        {
            if (!Upright)
            {
                return (float)(facingRadians * 180.0 / System.Math.PI);
            }

            var dir = new Vector3((float)System.Math.Cos(facingRadians), (float)System.Math.Sin(facingRadians), 0f);
            var t = _camera!.transform;
            return Mathf.Atan2(Vector3.Dot(dir, t.up), Vector3.Dot(dir, t.right)) * Mathf.Rad2Deg;
        }
    }
}

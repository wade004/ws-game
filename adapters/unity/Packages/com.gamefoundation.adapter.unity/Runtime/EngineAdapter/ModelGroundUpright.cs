#nullable enable
// ModelGroundUpright：让模型预制体在"地面 = 世界 XY 平面、固定俯角相机在 -Z 一侧"的世界里真正站立在地面上（ADR-0158）。
//
// 判断记录（为什么需要它）：UnityRenderer3D.SetPlacement 把锚点根放在 (planePos.X, planePos.Y, 0)，只绕世界 Y 轴转 -facing，
// 抬高 height 写在可见内容根的局部 Y 上（类型顶部"三维放置的坐标换算"判断记录）——即模型默认沿世界 +Y 立在地面平面里，
// 在固定俯角相机下等于"头朝北、脸朝 -Z 躺着"的人被从脚端斜看，近大远小的方向是反的。游戏的模型预制体可以自己声明
// "我要站在地面上"：在可见内容根下放一个枢轴物体、挂本组件，枢轴每帧在 LateUpdate 里按锚点根当前的朝向重新摆姿态：
//   - 向上 = 世界 -Z（地面在 Z = 0，固定俯角相机在 -Z 一侧，朝向相机的一侧就是物理上的上方）；
//   - 朝向 = 锚点根的 Y 轴转角还原出的 facing（弧度，0 = 世界 +X，逆时针为正），模型自己的"正面"约定由枢轴下的子物体写好；
//   - 抬高 = 可见内容根局部 Y（渲染器写的 height）折成沿 -Z 的世界抬高。
// 不改渲染器、不改契约：渲染器仍只管锚点根与可见内容根；没有挂本组件的预制体行为与此前逐位一致。
// 已知局限：本组件接管枢轴姿态，可见内容根上的前倾（IRenderer3D.SetLean）对挂了本组件的预制体不生效。
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class ModelGroundUpright : MonoBehaviour
    {
        private void LateUpdate() => Apply();

        /// <summary>按锚点根与可见内容根当前状态摆正枢轴（LateUpdate 每帧调用；测试可手动调用）。</summary>
        public void Apply()
        {
            var visual = transform.parent;
            var anchor = visual != null ? visual.parent : null;
            if (visual == null || anchor == null)
            {
                return;
            }

            var forward = anchor.rotation * Vector3.forward;
            var anchorYawDegrees = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg; // 锚点根 = Ry(-facing)
            var facingDegrees = -anchorYawDegrees;
            var lift = visual.localPosition.y * anchor.lossyScale.y;
            transform.rotation = Quaternion.Euler(0f, 0f, facingDegrees) * Quaternion.Euler(-90f, 0f, 0f);
            transform.position = anchor.position + new Vector3(0f, 0f, -lift);
        }
    }
}

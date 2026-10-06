#nullable enable
// GpuFrameProbe：实验室引擎宿主的 GPU 帧耗时探针（M4-W4）。
//
// 判断记录（为什么不用 FrameTimingManager）：Unity 的帧计时接口（FrameTimingManager）按"播放器帧"给数，并且结果要晚几帧才可读；
// 实验室的一次运行是在同一个播放器帧里同步跑完整段模拟的，运行期间根本不会有新的帧计时产生，读到的只会是运行之前的无关帧。
// 所以探针自己测：每个被驱动的帧把舞台相机真实渲染一次到一张小的离屏纹理，再发起一次纹理回读并等它完成——回读完成意味着这一帧
// 提交给 GPU 的全部工作都做完了，从提交到完成的墙钟时间就是这一帧的 GPU 完成耗时（含一次小纹理回读的固定开销，量级可忽略）。
// 它是真实时钟度量（RealTime 类），只按倍率上限比较。
//
// 判断记录（什么环境取得到）：需要真实的图形设备与异步回读支持。批处理无图形模式（-nographics，图形接口为 Null）取不到：此时探针标为不可用，
// 原因写进引擎记录，度量 gpu_frame_status 为 unavailable、数值为 -1 的哨兵值（不是缺失）。带图形设备的编辑器/批处理（不带 -nographics）与
// 播放器构建取得到。渲染失败或回读报错时整次运行改标不可用，不留一半有数一半没数的分位数。
using Adapter.Unity;
using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FeelLab.Unity
{
    internal sealed class GpuFrameProbe : IDisposable
    {
        private const int Width = 64;
        private const int Height = 36;

        private readonly Camera _camera;
        private RenderTexture? _target;

        public bool Available { get; private set; }

        /// <summary>不可用的原因（可用时为空）。</summary>
        public string UnavailableReason { get; private set; } = string.Empty;

        public GpuFrameProbe(Camera camera)
        {
            _camera = camera;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    UnavailableReason = "图形设备为 Null（批处理无图形模式），没有 GPU 可计时";
                    return;
                }

                if (!SystemInfo.supportsAsyncGPUReadback)
                {
                    UnavailableReason = "当前图形接口不支持异步回读，无法等待 GPU 完成";
                    return;
                }

                _target = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32) { name = "EngineLabGpuProbe" };
                _target.Create();
                _camera.targetTexture = _target;
                Available = true;
            }
            catch (Exception ex)
            {
                UnavailableReason = "探针创建失败：" + ex.Message;
                Available = false;
            }
        }

        /// <summary>渲染一帧并等 GPU 完成，返回毫秒；失败返回 false（并把探针标为不可用）。</summary>
        public bool TryMeasure(out double milliseconds)
        {
            milliseconds = 0.0;
            if (!Available || _target == null)
            {
                return false;
            }

            try
            {
                var watch = Stopwatch.StartNew();
                _camera.Render();
                var request = AsyncGPUReadback.Request(_target, 0);
                request.WaitForCompletion();
                watch.Stop();
                if (request.hasError)
                {
                    UnavailableReason = "纹理回读报错";
                    Available = false;
                    return false;
                }

                milliseconds = watch.Elapsed.TotalMilliseconds;
                return true;
            }
            catch (Exception ex)
            {
                UnavailableReason = "渲染计时失败：" + ex.Message;
                Available = false;
                return false;
            }
        }

        public void Dispose()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }

            Available = false;
        }
    }
}

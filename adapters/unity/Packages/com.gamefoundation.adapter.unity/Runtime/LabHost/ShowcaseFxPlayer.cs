#nullable enable
// ShowcaseFxPlayer：演示场景的世界空间序列帧特效播放器（挥砍拖影、命中火花、尘土、冲击波环）。
//
// 判断记录（为什么不走 VfxPlayer）：框架的特效播放器按"特效定义行 + 锚点"出特效，不能指定朝向与缩放；挥砍拖影要跟着出手方向转、火花要按冲击等级放缩。
// 这里只做"把资源加载器解出的序列帧资产按模拟时间播一遍"这一件事：资源仍走框架资源加载器（vfx/<名>/atlas.png + frames.json，枢轴与像素密度取自 frames.json），
// 时间用舞台按帧长推进的模拟时间（不用墙钟），所以顿帧、暂停、时间尺度都自然生效。特效只读逻辑事件，不回流。
using System;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.LabHost
{
    internal sealed class ShowcaseFxPlayer
    {
        private sealed class Fx
        {
            public GameObject Go = null!;
            public SpriteRenderer Renderer = null!;
            public UnityResourceLoader.EffectFrame[] Frames = Array.Empty<UnityResourceLoader.EffectFrame>();
            public int Index;
            public double Elapsed;
            public bool Loop;
            public double Fade;
        }

        private readonly Transform _root;
        private readonly int _layer;
        private readonly UnityResourceLoader _loader;
        private readonly List<Fx> _active = new List<Fx>();
        private readonly Stack<Fx> _pool = new Stack<Fx>();
        private readonly HashSet<Id> _requested = new HashSet<Id>();

        public ShowcaseFxPlayer(Transform root, int layer, UnityResourceLoader loader)
        {
            _root = root;
            _layer = layer;
            _loader = loader;
        }

        public int Spawned { get; private set; }

        public int ActiveCount => _active.Count;

        /// <summary>请求加载（只发一次）；失败原因由资源加载器记诊断。</summary>
        public void Preload(Id effectId)
        {
            if (_requested.Add(effectId))
            {
                _loader.LoadAsync(effectId, ResourceKind.Effect, (id, ok) => { });
            }
        }

        /// <summary>在世界位置播一个特效；资源还没就绪返回 false（调用方不重试，错过一次无妨）。</summary>
        public bool Spawn(Id effectId, Vector2 position, float rotationDegrees, float scale, Color tint, int sortingOrder)
        {
            if (!_loader.TryGetEffect(effectId, out var asset) || asset.Frames.Length == 0)
            {
                return false;
            }

            var fx = _pool.Count > 0 ? _pool.Pop() : Create();
            fx.Frames = asset.Frames;
            fx.Loop = asset.Loop;
            fx.Index = 0;
            fx.Elapsed = 0.0;
            fx.Go.transform.position = new Vector3(position.x, position.y, 0f);
            fx.Go.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            fx.Go.transform.localScale = new Vector3(scale, scale, 1f);
            fx.Renderer.sprite = fx.Frames[0].Sprite;
            fx.Renderer.color = tint;
            fx.Renderer.sortingOrder = sortingOrder;
            fx.Go.SetActive(true);
            _active.Add(fx);
            Spawned++;
            return true;
        }

        private Fx Create()
        {
            var go = new GameObject("ShowcaseFx") { layer = _layer };
            go.transform.SetParent(_root, false);
            var r = go.AddComponent<SpriteRenderer>();
            return new Fx { Go = go, Renderer = r };
        }

        public void Update(double dt)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var fx = _active[i];
                fx.Elapsed += dt;
                var frames = fx.Frames;
                var index = fx.Index;
                var elapsed = fx.Elapsed;
                // 按每帧时长累计找当前帧。
                var t = 0.0;
                var found = -1;
                for (var k = 0; k < frames.Length; k++)
                {
                    t += Math.Max(1e-4, frames[k].Duration);
                    if (elapsed < t)
                    {
                        found = k;
                        break;
                    }
                }

                if (found < 0)
                {
                    if (fx.Loop)
                    {
                        fx.Elapsed = 0.0;
                        found = 0;
                    }
                    else
                    {
                        fx.Go.SetActive(false);
                        _active.RemoveAt(i);
                        _pool.Push(fx);
                        continue;
                    }
                }

                if (found != index)
                {
                    fx.Index = found;
                    fx.Renderer.sprite = frames[found].Sprite;
                }
            }
        }

        public void Dispose()
        {
            foreach (var fx in _active)
            {
                if (fx.Go != null)
                {
                    UnityEngine.Object.Destroy(fx.Go);
                }
            }

            foreach (var fx in _pool)
            {
                if (fx.Go != null)
                {
                    UnityEngine.Object.Destroy(fx.Go);
                }
            }

            _active.Clear();
            _pool.Clear();
        }
    }
}

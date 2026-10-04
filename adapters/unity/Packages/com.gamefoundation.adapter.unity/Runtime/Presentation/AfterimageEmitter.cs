#nullable enable
// AfterimageEmitter：精灵残影（ADR-0148，手感设计/07 第 3 节 trail_start/trail_end 标记 + 手感字段 afterimage_enabled）的 Unity 落地。
//
// 判断记录：
//   - 形态：残影是"精灵根节点下全部 SpriteRenderer 的半透明副本"，按固定间隔留在原地、线性淡出后销毁；不改变真实渲染，
//     不引入任何数据表。拖尾（vfx.def）与残影是两件独立的事：前者由 AnimMarkerDirector 经特效通路播放，后者经本组件。
//   - 驱动：Update 用 Time.deltaTime 推进 Step；测试直接调用 Step(dt) 得到确定结果。Active 关闭后已生成的残影继续淡出至消失。
//   - 没有精灵渲染器的实体（纯模型 View）不挂本组件：UnityModelView 不实现 IAfterimageTarget，开关静默 no-op。
using System.Collections.Generic;
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    public sealed class AfterimageEmitter : MonoBehaviour
    {
        /// <summary>两次残影之间的间隔（秒）。</summary>
        public const float SpawnIntervalSeconds = 0.05f;

        /// <summary>单个残影的存活时长（秒），线性淡出。</summary>
        public const float LifetimeSeconds = 0.2f;

        /// <summary>残影起始不透明度。</summary>
        public const float StartAlpha = 0.5f;

        private sealed class Ghost
        {
            public GameObject Object = null!;
            public SpriteRenderer[] Renderers = System.Array.Empty<SpriteRenderer>();
            public float[] BaseAlpha = System.Array.Empty<float>();
            public float Age;
        }

        private readonly List<Ghost> _ghosts = new List<Ghost>();
        private float _sinceSpawn;

        /// <summary>是否正在生成残影。</summary>
        public bool Active { get; set; }

        /// <summary>当前存活的残影数量（测试/诊断用）。</summary>
        public int GhostCount => _ghosts.Count;

        /// <summary>迄今生成的残影总数（测试/诊断用）。</summary>
        public int SpawnedTotal { get; private set; }

        private void Update() => Step(Time.deltaTime);

        /// <summary>推进 <paramref name="dt"/> 秒：生成（Active 时，到间隔就留一个）、淡出、销毁到期的残影。</summary>
        public void Step(float dt)
        {
            if (Active)
            {
                _sinceSpawn += dt;
                if (_sinceSpawn >= SpawnIntervalSeconds || SpawnedTotal == 0)
                {
                    _sinceSpawn = 0f;
                    Spawn();
                }
            }
            else
            {
                _sinceSpawn = SpawnIntervalSeconds; // 重新开启时立刻留第一个
            }

            for (var i = _ghosts.Count - 1; i >= 0; i--)
            {
                var ghost = _ghosts[i];
                ghost.Age += dt;
                if (ghost.Age >= LifetimeSeconds)
                {
                    Object.Destroy(ghost.Object);
                    _ghosts.RemoveAt(i);
                    continue;
                }

                var fade = 1f - ghost.Age / LifetimeSeconds;
                for (var r = 0; r < ghost.Renderers.Length; r++)
                {
                    var c = ghost.Renderers[r].color;
                    c.a = ghost.BaseAlpha[r] * fade;
                    ghost.Renderers[r].color = c;
                }
            }
        }

        private void Spawn()
        {
            var sources = GetComponentsInChildren<SpriteRenderer>();
            var copies = new List<SpriteRenderer>(sources.Length);
            var alphas = new List<float>(sources.Length);
            var holder = new GameObject("Afterimage");
            foreach (var source in sources)
            {
                if (source == null || source.sprite == null || !source.enabled || source.transform.IsChildOf(holder.transform))
                {
                    continue;
                }

                var go = new GameObject("AfterimageLayer");
                go.transform.SetParent(holder.transform, false);
                go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                go.transform.localScale = source.transform.lossyScale;
                var copy = go.AddComponent<SpriteRenderer>();
                copy.sprite = source.sprite;
                copy.flipX = source.flipX;
                copy.flipY = source.flipY;
                copy.sortingLayerID = source.sortingLayerID;
                copy.sortingOrder = source.sortingOrder - 1;
                var color = source.color;
                var alpha = StartAlpha * color.a;
                color.a = alpha;
                copy.color = color;
                copies.Add(copy);
                alphas.Add(alpha);
            }

            if (copies.Count == 0)
            {
                Object.Destroy(holder);
                return;
            }

            SpawnedTotal++;
            _ghosts.Add(new Ghost { Object = holder, Renderers = copies.ToArray(), BaseAlpha = alphas.ToArray() });
        }

        private void OnDestroy()
        {
            foreach (var ghost in _ghosts)
            {
                if (ghost.Object != null)
                {
                    Object.Destroy(ghost.Object);
                }
            }

            _ghosts.Clear();
        }
    }
}

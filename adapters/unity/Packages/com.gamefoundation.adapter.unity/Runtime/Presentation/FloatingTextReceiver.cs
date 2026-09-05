#nullable enable
// FloatingTextReceiver：09_表现层.md 第 6.1 节 FloatingText 反馈动作的 Unity 侧落地。
//
// 判断记录：PresentationAssemblyOptions.OnFloatingText 的默认实现是空操作（见该类型注释：
// "具体飘字 UI 控件池不属于本框架任何一个 L5 模块的契约范围"）——本类型是"具体游戏/引擎侧"这一层
// 应该提供的实现之一，不是契约缺口的修补。用世界空间 TextMeshPro（不是 TextMeshProUGUI/Canvas，
// 飘字直接贴在世界坐标位置上随镜头一起移动，不需要屏幕空间转换）+ 对象池（避免频繁伤害数字造成
// GC 压力）。颜色只能按 FloatingTextStyleDef.ColorRef 的 Id 字面量做启发式匹配（暴击/普通两色），
// 因为 ColorRef 契约本身只是一个"颜色引用 Id"，具体色值解析属于表现资源管理范畴（该类型注释
// "具体色值由表现资源管理，本类型不解释语义"），框架没有提供 Id -> Color 的查询能力，如实记录。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using TMPro;
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    public sealed class FloatingTextReceiver
    {
        private sealed class ActiveText
        {
            public TextMeshPro Text = null!;
            public float Remaining;
        }

        private const float LifeSeconds = 1.2f;
        private const float RiseSpeed = 0.6f;

        private readonly Transform _root;
        private readonly Func<Id, Vec2?> _positionResolver;
        private readonly IReadOnlyDictionary<Id, FloatingTextStyleDef> _styles;
        private readonly Queue<TextMeshPro> _pool = new Queue<TextMeshPro>();
        private readonly List<ActiveText> _active = new List<ActiveText>();

        public FloatingTextReceiver(
            Transform root,
            Func<Id, Vec2?> positionResolver,
            IReadOnlyDictionary<Id, FloatingTextStyleDef> styles)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _positionResolver = positionResolver ?? throw new ArgumentNullException(nameof(positionResolver));
            _styles = styles ?? throw new ArgumentNullException(nameof(styles));
        }

        /// <summary>迄今为止成功生成过的飘字数量（PlayMode 测试断言"至少产生一次飘字"用）。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>绑定给 <c>PresentationAssemblyOptions.OnFloatingText</c>。</summary>
        public void Show(Id entityId, Id styleId, string text)
        {
            var basePos = _positionResolver(entityId) ?? Vec2.Zero;
            var tmp = Rent();
            tmp.text = text;
            tmp.color = ResolveColor(styleId);
            tmp.transform.position = new Vector3((float)basePos.X, (float)basePos.Y + 1.2f, -0.1f);
            tmp.gameObject.SetActive(true);
            _active.Add(new ActiveText { Text = tmp, Remaining = LifeSeconds });
            SpawnedCount++;
        }

        /// <summary>由 <c>GameFoundationBootstrap.Update</c> 每帧调用：推进上浮位移与生命周期。</summary>
        public void Tick(float deltaSeconds)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var entry = _active[i];
                entry.Text.transform.position += new Vector3(0f, RiseSpeed * deltaSeconds, 0f);
                entry.Remaining -= deltaSeconds;

                if (entry.Remaining <= 0f)
                {
                    entry.Text.gameObject.SetActive(false);
                    _pool.Enqueue(entry.Text);
                    _active.RemoveAt(i);
                }
            }
        }

        private Color ResolveColor(Id styleId)
        {
            if (_styles.TryGetValue(styleId, out var style) &&
                style.ColorRef.Value.IndexOf("crit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.35f, 0.2f, 1f);
            }
            return Color.white;
        }

        private TextMeshPro Rent()
        {
            if (_pool.Count > 0)
            {
                return _pool.Dequeue();
            }

            var go = new GameObject("FloatingText");
            go.transform.SetParent(_root, worldPositionStays: false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = 3.5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }
    }
}

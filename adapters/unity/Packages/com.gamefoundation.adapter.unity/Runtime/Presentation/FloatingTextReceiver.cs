#nullable enable
// FloatingTextReceiver：09_表现层.md 第 6.1 节 FloatingText 反馈动作的 Unity 侧落地。
//
// 判断记录：PresentationAssemblyOptions.OnFloatingText 的默认实现是空操作（见该类型注释：
// "具体飘字 UI 控件池不属于本框架任何一个 L5 模块的契约范围"）——本类型是"具体游戏/引擎侧"这一层
// 应该提供的实现之一，不是契约缺口的修补。用世界空间 TextMeshPro（不是 TextMeshProUGUI/Canvas，
// 飘字直接贴在世界坐标位置上随镜头一起移动，不需要屏幕空间转换）+ 对象池（避免频繁伤害数字造成
// GC 压力）。颜色只能按 FloatingTextStyleDef.ColorRef 的 Id 字面量做启发式匹配（暴击、闪避、普通三色），
// 因为 ColorRef 契约本身只是一个"颜色引用 Id"，具体色值解析属于表现资源管理范畴（该类型注释
// "具体色值由表现资源管理，本类型不解释语义"），框架没有提供 Id -> Color 的查询能力，如实记录。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Adapter.Unity.Ui;
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

        // 判断记录（ADR-0178，3D 世界）：默认构造按 2D 约定摆位（逻辑位置 x、y 直接是世界 x、y，抬高 1.2 后放在 z=-0.1），
        // 世界里「上」不是 +y 的游戏（逻辑坐标铺在地面、世界 z 为负向上）用下面的三维锚点构造：锚点直接给出世界坐标，
        // 上浮方向与相机另外传入，飘字每帧朝向相机。两种构造共用同一个 Show/Tick 出口，行为只在锚点与朝向上不同。
        private readonly Func<Id, Vector3?>? _worldAnchor;
        private readonly Func<Camera?>? _cameraProvider;
        private readonly Vector3 _riseDirection = Vector3.up;

        // 判断记录（ADR-0178，绘制序）：世界空间文字的绘制序默认是 0，而精灵按「图层 × 1000 − 排序 Y」落绘制序（见 UnityRenderer2D 的排序约定），
        // 单位、地面、远景层的绘制序都比 0 大时，飘字会被它们盖住（样板游戏截图里「闪避」整句看不见）。飘字是叠在场景之上的反馈，
        // 绘制序取排序约定能用到的最大档之上（精灵层 -32..32 × 1000 + ±500，短整型上限 32767），两种构造一致。
        public const int FloatingTextSortingOrder = 32000;

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

        /// <summary>
        /// 三维锚点构造：<paramref name="worldAnchorResolver"/> 直接返回实体头顶的世界坐标（找不到实体返回 null，则落在根节点位置）；
        /// 飘字沿 <paramref name="riseDirection"/> 上浮；<paramref name="cameraProvider"/> 返回的相机不为空时，飘字每帧朝向它。
        /// </summary>
        public FloatingTextReceiver(
            Transform root,
            Func<Id, Vector3?> worldAnchorResolver,
            IReadOnlyDictionary<Id, FloatingTextStyleDef> styles,
            Func<Camera?> cameraProvider,
            Vector3 riseDirection)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _worldAnchor = worldAnchorResolver ?? throw new ArgumentNullException(nameof(worldAnchorResolver));
            _positionResolver = _ => null;
            _styles = styles ?? throw new ArgumentNullException(nameof(styles));
            _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
            _riseDirection = riseDirection;
        }

        /// <summary>迄今为止成功生成过的飘字数量（PlayMode 测试断言"至少产生一次飘字"用）。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>绑定给 <c>PresentationAssemblyOptions.OnFloatingText</c>。</summary>
        public void Show(Id entityId, Id styleId, string text)
        {
            var tmp = Rent();
            tmp.text = text;
            tmp.font = ResolveFont(text);
            tmp.color = ResolveColor(styleId);
            if (_worldAnchor != null)
            {
                tmp.transform.position = _worldAnchor(entityId) ?? _root.position;
            }
            else
            {
                var basePos = _positionResolver(entityId) ?? Vec2.Zero;
                tmp.transform.position = new Vector3((float)basePos.X, (float)basePos.Y + 1.2f, -0.1f);
            }

            FaceCamera(tmp);
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
                entry.Text.transform.position += _riseDirection * (RiseSpeed * deltaSeconds);
                FaceCamera(entry.Text);
                entry.Remaining -= deltaSeconds;

                if (entry.Remaining <= 0f)
                {
                    entry.Text.gameObject.SetActive(false);
                    _pool.Enqueue(entry.Text);
                    _active.RemoveAt(i);
                }
            }
        }

        private void FaceCamera(TextMeshPro text)
        {
            var camera = _cameraProvider?.Invoke();
            if (camera != null)
            {
                text.transform.rotation = camera.transform.rotation;
            }
        }

        // 判断记录（ADR-0178，中文飘字）：TMP 内置默认字体没有 CJK 字形，「闪避」这类文字字符会显示成方框或空白；
        // 套件字体（UiSkin.Font：皮肤覆盖字体，缺省为内置 Noto Sans CJK）有。纯 ASCII 文本（伤害数字）仍用 TMP 默认字体，
        // 与引入此判断之前逐位一致；套件字体取不到时（抛出）退回默认字体，不让飘字因字体问题失败。
        private static TMP_FontAsset ResolveFont(string text)
        {
            var fallback = TMP_Settings.defaultFontAsset;
            if (!ContainsNonAscii(text))
            {
                return fallback;
            }

            try
            {
                return UiSkin.Font ?? fallback;
            }
            catch (InvalidOperationException)
            {
                return fallback;
            }
        }

        private static bool ContainsNonAscii(string text)
        {
            foreach (var ch in text)
            {
                if (ch > 0x7F)
                {
                    return true;
                }
            }

            return false;
        }

        private Color ResolveColor(Id styleId)
        {
            if (_styles.TryGetValue(styleId, out var style))
            {
                var colorRef = style.ColorRef.Value;
                if (colorRef.IndexOf("crit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new Color(1f, 0.35f, 0.2f, 1f);
                }

                // ADR-0178：闪避/未命中这类「没有造成数值」的飘字用淡青色，与白色伤害数字区分。
                if (colorRef.IndexOf("dodge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    colorRef.IndexOf("miss", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new Color(0.55f, 0.9f, 1f, 1f);
                }
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
            tmp.sortingOrder = FloatingTextSortingOrder;
            return tmp;
        }
    }
}

#nullable enable
// EffectSequencePlayer：UnityRenderer2D.EmitParticle 在 ResourceKind.Effect 资源解析成功时
// 播放的最小序列帧动画组件（见 UnityResourceLoader.EffectAsset 判断记录：占位美术的"特效"是
// atlas.png + frames.json 描述的序列帧，不是预制好的 ParticleSystem）。挂一个 SpriteRenderer，
// 按 frames.json 的 fps/frame_duration/loop 逐帧切换 sprite；非循环播放完最后一帧后自动停止并
// 触发 OnFinished，供 UnityRenderer2D 回收进对象池（同 ParticleSystem 池的既有惯例）。
//
// ADR-0074 新增：Play 按 Core.Foundation.EngineAdapter.VfxBlendMode 给 SpriteRenderer 选材质——
// alpha（缺省）用 Unity 为该 SpriteRenderer 自动指派的默认材质（与改动前逐字一致，不主动赋值）；
// additive 用适配层包内自带的占位材质资产（见 GetAdditiveMaterial 判断记录）。本组件是对象池复用
// 的（EffectSequencePlayerPool），每次 Play 都必须显式落地这一次的混合模式，不能"alpha 就不管"——
// 否则复用同一个实例的下一次播放会继承上一次残留的材质。
using System;
using UnityEngine;
using Core.Foundation.EngineAdapter;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class EffectSequencePlayer : MonoBehaviour
    {
        private Sprite[] _frames = Array.Empty<Sprite>();
        private double[] _durations = Array.Empty<double>();
        private bool _loop;
        private int _frameIndex;
        private double _elapsedInFrame;
        private bool _playing;
        private SpriteRenderer? _renderer;

        /// <summary>ADR-0074 新增：本实例的 <see cref="SpriteRenderer"/> 刚创建时 Unity 自动指派的
        /// 默认材质——同 <see cref="Renderer"/> 判断记录同一套"显式 null 检查、不用 ??="惯例，
        /// 首次经 <see cref="Renderer"/> 取到渲染器时惰性捕获一次；<see cref="ApplyBlendMode"/> 的
        /// alpha 分支据此显式复原（不是"不管它"），保证对象池复用时不会残留上一次播放留下的
        /// additive 材质。</summary>
        private Material? _defaultMaterial;

        private static Material? _additiveMaterial;
        private static bool _additiveMaterialLoadAttempted;

        /// <summary>非循环播放自然结束时触发一次，供调用方回收本实例。</summary>
        public event Action? OnFinished;

        // 判断记录（W3b 修复：不用 ??/??= 惰性初始化 UnityEngine.Object 字段）：C# 的 ??/??=
        // 运算符对引用类型只做 CLR 层面的真 null 判断，不会调用 UnityEngine.Object 重载的
        // operator==（后者额外检查原生对象是否已被销毁，即"伪 null"，见 Unity 官方文档"Custom
        // == operator..."一节的一贯提醒）——本类型此前用 `_renderer ??= gameObject.GetComponent
        // <SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();` 在批处理 PlayMode 测试
        // 环境下实测触发 `SpriteRenderer.set_sprite` 抛 `MissingComponentException`（首次真正驱动
        // EffectSequencePlayer.Play 的 PlayMode 用例复现，见 UnityRenderer2DTests.
        // EmitParticle_WithRealSequenceFrameResource_TriggersEffectSequencePlayer_NotFallback 判断
        // 记录）：`gameObject.AddComponent<SpriteRenderer>()` 在该场景下返回的引用未能通过后续
        // `_renderer.sprite = ...` 的原生对象存活性检查，怀疑与 `??=` 把一个"实际存活但 CLR 引用
        // 相等性判断异常"的返回值直接缓存有关。改为显式 `if (_renderer == null)`（触发
        // UnityEngine.Object 的重载 == ，真正检查原生对象是否存活）后不再复现。
        private SpriteRenderer Renderer
        {
            get
            {
                if (_renderer == null)
                {
                    _renderer = gameObject.GetComponent<SpriteRenderer>();
                    if (_renderer == null)
                    {
                        _renderer = gameObject.AddComponent<SpriteRenderer>();
                    }
                }
                return _renderer;
            }
        }

        public void Play(Sprite[] frames, double[] frameDurations, bool loop) =>
            Play(frames, frameDurations, loop, VfxBlendMode.Alpha);

        /// <summary>ADR-0074 新增重载：承载混合模式。既有 3 参 <see cref="Play(Sprite[], double[], bool)"/>
        /// 保留、签名不变，转发到本重载并固定传 <see cref="VfxBlendMode.Alpha"/>——alpha 分支行为与
        /// 改动前逐字一致。</summary>
        public void Play(Sprite[] frames, double[] frameDurations, bool loop, VfxBlendMode blendMode)
        {
            _frames = frames;
            _durations = frameDurations;
            _loop = loop;
            _frameIndex = 0;
            _elapsedInFrame = 0;
            _playing = frames.Length > 0;

            ApplyBlendMode(blendMode);

            if (_playing)
            {
                Renderer.sprite = _frames[0];
            }
        }

        /// <summary>ADR-0074 判断记录（为什么不用 SpriteRenderer.material 而用 sharedMaterial）：
        /// <c>material</c> 的 getter 会隐式实例化一份材质副本（"instanced material"），每次 Play 都
        /// 产生一次分配、且对象池复用场景下越积越多；本组件的两份材质（默认/additive）都是共享资产，
        /// 不需要逐实例修改材质属性，<c>sharedMaterial</c> 直接引用资产本身，零分配。</summary>
        private void ApplyBlendMode(VfxBlendMode blendMode)
        {
            var renderer = Renderer;

            if (_defaultMaterial == null)
            {
                _defaultMaterial = renderer.sharedMaterial;
            }

            renderer.sharedMaterial = blendMode == VfxBlendMode.Additive
                ? GetAdditiveMaterial(_defaultMaterial)
                : _defaultMaterial;
        }

        /// <summary>ADR-0074 新增：additive 占位材质，经 <c>Resources.Load</c> 取适配层包内自带的
        /// <c>GameFoundation/materials/vfx_additive</c> 资产（同 Model 型占位资产一贯的
        /// "Resources/GameFoundation/&lt;kind&gt;/&lt;name&gt;" 约定路径，见
        /// <c>UnityResourceLoader.ResolveModelResourcesPath</c> 判断记录同款惯例；生成器见
        /// <c>Adapter.Unity.EditorTools.GeneratePlaceholderVfxAssets</c>）。全部实例共享同一份加载结果
        /// （静态缓存，同 <c>UnityRenderer2D.GetPlaceholderSprite</c>"只生成一次并缓存复用"惯例）；
        /// 资产缺失（消费方工程未同步该资源，或包版本过旧）时只尝试加载一次、记一条 warning 并退回
        /// <paramref name="fallback"/>（该次 additive 请求降级为默认材质，不阻断游戏运行，同本文件
        /// 一贯的防御性惯例——一次性 <c>Debug.LogWarning</c>，不按调用次数刷屏）。</summary>
        private static Material? GetAdditiveMaterial(Material? fallback)
        {
            if (_additiveMaterial != null)
            {
                return _additiveMaterial;
            }

            if (_additiveMaterialLoadAttempted)
            {
                return fallback;
            }

            _additiveMaterialLoadAttempted = true;
            _additiveMaterial = Resources.Load<Material>("GameFoundation/materials/vfx_additive");

            if (_additiveMaterial == null)
            {
                Debug.LogWarning(
                    "[EffectSequencePlayer] 找不到 additive 占位材质 Resources/GameFoundation/materials/vfx_additive，" +
                    "本次及后续 additive 播放请求降级为默认（alpha）材质。");
                return fallback;
            }

            return _additiveMaterial;
        }

        public void StopImmediately()
        {
            _playing = false;
        }

        private void Update()
        {
            if (!_playing || _frames.Length == 0)
            {
                return;
            }

            _elapsedInFrame += Time.deltaTime;
            var currentDuration = _durations[_frameIndex] > 0 ? _durations[_frameIndex] : 0.05;

            while (_elapsedInFrame >= currentDuration)
            {
                _elapsedInFrame -= currentDuration;
                _frameIndex++;

                if (_frameIndex >= _frames.Length)
                {
                    if (_loop)
                    {
                        _frameIndex = 0;
                    }
                    else
                    {
                        _frameIndex = _frames.Length - 1;
                        _playing = false;
                        Renderer.sprite = _frames[_frameIndex];
                        OnFinished?.Invoke();
                        return;
                    }
                }

                Renderer.sprite = _frames[_frameIndex];
                currentDuration = _durations[_frameIndex] > 0 ? _durations[_frameIndex] : 0.05;
            }
        }
    }
}

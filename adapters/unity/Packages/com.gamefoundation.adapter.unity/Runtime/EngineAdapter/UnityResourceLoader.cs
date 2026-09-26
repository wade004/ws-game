#nullable enable
// UnityResourceLoader：IResourceLoader 的 Unity 引擎实现。
//
// 消费方反馈第 32 条（ADR-0025）：sprite_set_id/icon_id 两个字段的"资源引用 id → 资产相对路径"
// 规则已收口为公开静态类 Core.Foundation.EngineAdapter.AssetRefConventions（SpriteSetDirectory/
// IconFile/TryParseSpriteSetId/TryParseIconId），本类型的 StripCategoryPrefix 私有方法现转发到
// 该类型的同名方法，不再独立维护一份拷贝，见该方法与 AssetRefConventions 类型注释。
//
// 资源 id → 相对路径映射规则（与 architecture/14_资产规格书模板.md 第 1.2 节文件名模板保持
// 同一套命名，具体做法参照 presentation/render/core/SpriteViewBase.ResolveLayerResourceId 已经
// 采用的"去掉类别前缀（第一个点分段）、剩余点号换下划线"规则）：
//   相对路径 = "GameFoundation/<kind 子目录>/<资源引用id去掉类别前缀，点号换下划线>.<扩展名>"
//   其中 <kind 子目录> 与 <扩展名> 按 ResourceKind 固定：
//     Image     -> "sprites/<name>.png"
//     Audio     -> "audio/<name>.wav"（占位音频约定为标准 PCM16 WAV，见 判断记录）
//     DataTable -> "data/<name>.json"
//   根目录固定为 Application.streamingAssetsPath（跨平台只读只读资源目录，桌面平台上是普通
//   文件系统路径，可直接用 System.IO 同步读取，见下）。Font 种类不走这条规则（不是
//   StreamingAssets 下的裸字节，而是 Resources/Fonts/<name> 下已被资产管线导入好的字体资产，
//   见下"判断记录（Font 资源种类）"）。
//
// 判断记录（加载方式）：不使用 UnityWebRequest/协程，改用 System.Threading.Tasks.Task 在后台
// 线程做纯文件字节读取（不调用任何 UnityEngine API，线程安全），读取完成后把结果放进一个
// 线程安全队列；真正需要调用 UnityEngine API 的解码步骤（Texture2D/AudioClip 构造、写入内部
// 缓存、触发 LoadCallback）全部在 <see cref="Tick"/> 里于主线程完成——Tick 由
// UnityEngineHost.Update 每帧调用一次，因此回调总是在主线程的下一次 IClock.onFrame 之前排队
// 执行，满足 02 第 2 节第 2 条"由适配层实现保证回调总在主线程排队执行"的线程约定。
//
// 判断记录（音频解码）：Unity 没有跨平台的"任意压缩音频字节数组 -> AudioClip"同步公开 API
// （UnityWebRequestMultimedia 是唯一内置方案，但要求协程与 URI，且仍需假定具体编解码格式）；
// 本实现改为直接解析标准 PCM16 WAV（RIFF/WAVE 头 + 'fmt '/'data' 分块），足以覆盖占位音频与
// 大多数游戏音频制作管线导出的未压缩 WAV，具体音频格式选型见 architecture/选型/（本文档不预设
// 结论）。非 WAV/非 PCM16 数据会解析失败并按"加载失败"回调 false。
//
// 判断记录（Font 资源种类，缺口 1 已解决——约定见包 README"资源 id → 路径规则"）：Unity 运行期
// 没有公开 API 能把任意字体文件字节数组转换成可用于 TMP 渲染的字体资产
// （TMP_FontAsset.CreateFontAsset 需要一个已被 Unity 资产管线导入过的 UnityEngine.Font 对象，
// 而非裸字节），因此 Font 种类不能沿用其它种类"后台线程读字节 + Task.Run"的通用路径。约定：
// 字体资源 id 形如 "font.<name>" 时，<name> 为该 id 去掉 "font." 前缀、点号换下划线后的结果
// （与其它种类共用同一条 StripCategoryPrefix 规则），对应一个已被 Unity 资产管线预先导入好的
// Font 资产，路径固定为 "Resources/Fonts/<name>"（该资产必须实际存在于某个 Resources/Fonts/
// 目录下，才能被 Resources.Load<Font> 取到——本仓库当前由 build.ps1 -SyncContent 把
// assets/_placeholder/fonts/*.otf|*.ttf 同步进 adapters/unity/Assets/Framework/Resources/Fonts/，
// 见该脚本判断记录）。Resources.Load 只能在主线程调用，因此 LoadAsync 对 Font 种类不走
// Task.Run 后台字节读取，而是把请求排入 Tick() 处理的专用队列，在下一次 Tick（仍由
// UnityEngineHost.Update 驱动）里于主线程调用 Resources.Load<Font> 完成判定——资产存在即视为
// "已加载"（IsLoaded 返回 true），不存在则按"加载失败"回调 false，保持"回调总在 Tick 里于
// 主线程排队执行"这条既有线程约定不变。加载成功后的 UnityEngine.Font 对象供
// UnityUISurface.ResolveFontAsset 取用以生成/复用对应的 TMP_FontAsset（该步骤仍需要
// TMP_FontAsset.CreateFontAsset，逻辑见 UnityUISurface.cs）。
//
// Scene/NavMesh/Effect 三个种类（ADR-0016 决策 5 新增，取代此前 SceneRouter 借用
// ResourceKind.DataTable 的工作绕）：
//   Scene/NavMesh —— 与 DataTable 同一套"读文本、只校验存在性"处理：SceneRouter 只关心加载
//     成功/失败，从不解析内容（见 SceneRouter.cs 类型注释），本加载器分别落到
//     "scene/<name>.json"/"nav_mesh/<name>.json" 两个独立子目录（build.ps1 生成对应占位文件）。
//   Effect —— vfx.def.resource_ref 指向一个目录（"vfx/<name>/atlas.png" + "frames.json"，
//     结构同 assets/_placeholder/vfx/<name>/，见 toolchain/gen_placeholder_assets.py 产出），
//     不是单一文件；本加载器为 Effect 单独走一条"同时读 atlas 字节 + frames.json 文本"的后台
//     加载路径，主线程按 frames.json 描述的帧矩形切出多张 Sprite，装配成 EffectAsset 供
//     UnityRenderer2D.EmitParticle 优先使用（找不到时回退内建通用粒子效果）。
//
// W6-B 新增（ADR-0017 决策 a/b，ResourceKind.Model）：三维模型预制体不是"任意压缩字节数组"，
// 与 Font 同一处境——运行期没有公开 API 能把裸字节反序列化成可用的 GameObject 层级/骨骼/
// Animator 绑定，只能消费已经被 Unity 资产管线预先导入好的资源，经 Resources.Load<GameObject>
// 取用（同 Font 种类"判断记录"的同一约束，见类型顶部该节）。约定：模型资源 id 形如
// "model.<name>" 时，<name> 为该 id 去掉 "model." 前缀、点号换下划线后的结果（与其它种类共用
// 同一条 StripCategoryPrefix 规则），对应路径固定为
// "Resources/GameFoundation/models/<name>"（该预制体必须实际存在于某个
// Resources/GameFoundation/models/ 目录下才能被 Resources.Load<GameObject> 取到；本迭代由
// Editor/GeneratePlaceholderModelAssets.cs 一次性生成 placeholder_biped 并提交生成结果，见该
// 脚本与包 README"资源路径约定"一节）。Resources.Load 只能在主线程调用，因此 LoadAsync 对
// Model 种类同 Font 一样不走 Task.Run 后台字节读取路径，改为把请求排入 Tick() 处理的专用队列，
// 在下一次 Tick 里于主线程调用 Resources.Load<GameObject> 完成判定——资产存在即视为"已加载"
// （IsLoaded 返回 true，且缓存进 <see cref="_modelPrefabs"/> 供 <see cref="TryGetModelPrefab"/>/
// <see cref="UnityRenderer3D.CreateModelInstance"/> 取用），不存在则按"加载失败"回调 false，
// 保持"回调总在 Tick 里于主线程排队执行"这条既有线程约定不变。
//
// 与 Model 同一套 Resources.Load 约定的姊妹路径——动画剪辑资产（供 model 型 <c>display.anim_set</c>
// 消费，见 <see cref="ResolveAnimClipResourcesPath"/>）：<c>display.anim_set.clips[*].resource_ref</c>
// 对 model 型剪辑指向一个已导入的 <see cref="UnityEngine.AnimationClip"/> 资产，路径固定为
// "Resources/GameFoundation/anim_clips/<name>"，与 <see cref="AnimClipResolver"/>/
// <see cref="Adapter.Unity.Presentation.UnityViewFactory"/> 把 <c>events</c> 数据驱动写回该资产的
// <c>AnimationClip.events</c>（关键帧事件）配合使用，见两者判断记录。
//
// 12 §5 勘误判断记录（取代此前"本类型不直接消费这条约定，Resources.Load<AnimationClip> 由调用方
// 直接同步调用"的立场——architecture/落地计划/audit-85f1f4f-20260908/ 第九方审核"动画剪辑事件登记
// 契约差异"）：ADR-0017 决策 1"renderer/消费方只经 IResourceLoader 取资源，不直接碰
// UnityEngine.Resources"同样适用于 AnimationClip 种类，不应该只对 Model/Font 生效。新增
// ResourceKind.AnimationClip，处理方式与 Model/Font 同一套"主线程专用队列，Resources.Load 只能在
// 主线程调用"惯例（见 <see cref="_pendingAnimClipLoads"/>/<see cref="FinishAnimClipLoad"/>）；
// <see cref="UnityRenderer3D.ResolveLegacyClip"/>（legacy Animation 兜底播放路径）与
// <see cref="Adapter.Unity.Presentation.UnityViewFactory.RegisterModelClipEvents"/>（关键帧事件登记）
// 均改为经 <see cref="TryLoadAnimationClipSync"/>/<see cref="TryGetAnimationClip"/> 取用，本类型自身
// 是唯一调用 <c>Resources.Load&lt;AnimationClip&gt;</c> 的地方。
//
// W6-CLIP 新增（同一判断记录的姊妹条款——mesh_ref 资源合同，AUD-05 根治）：
// <c>display.equip_visual.mesh_ref</c>（04 第 7.1.2 节）引用一个 <see cref="ResourceKind.Model"/>
// 种类资源（与 <c>model_ref</c> 同一命名空间与同一条 <see cref="ResolveModelResourcesPath"/> 路径
// 约定，不单独新增资源种类——mesh_ref 与 model_ref 都指向"三维几何资产"，只是消费方（分别是
// SetSlotMesh 与 CreateModelInstance）对同一份已加载资源的用法不同）：若该资源解析为模型预制体
// （<see cref="_modelPrefabs"/> 命中或 <see cref="TryLoadModelSync"/> 成功），从中提取网格——优先取
// 与 slotId 同名的子对象（约定同 <see cref="UnityRenderer3D"/> 的 FindDeep"子对象名逐字等于槽位 Id
// 的 Value"）上的 <see cref="SkinnedMeshRenderer"/>/<see cref="MeshFilter"/> 网格，找不到该子对象或
// 该子对象不挂网格渲染组件时退回预制体上首个挂网格渲染组件的子对象（<c>GetComponentInChildren</c>，
// 深度优先，含根节点自身）；若该资源本身就是一个独立网格资产（同一约定路径下没有 GameObject 但有
// 一个 Mesh，理论上的"资源不是预制体"分支，见 <see cref="TryGetOrLoadSlotMesh"/> 判断记录），直接
// 使用该网格。提取结果缓存进 <see cref="_extractedSlotMeshes"/>，避免同一 (resourceId, slotId) 组合
// 反复遍历层级。
//
// ADR-0038 适配层接线（判断记录，决策 8 第二项提前落地的理由）：ADR-0038 决策 8 原把"已落地引擎
// 适配层实现改为转发决策 5 新增的公开路由入口"列为不在本次决策范围的后续项，理由是当时（契约/API/
// 校验/工具链落地批）尚未迁移样例数据，本类型既有实现（ResolveEffectDir 固定按 vfx 子目录、
// ResolvePath 对非 layer 类别的 Image 种类固定退化为 sprites/<name>.png）与彼时数据取值仍然吻合，
// 不转发不产生行为分歧，只是"多一份未来需要同步维护的拷贝"这一较低等级的风险。但下一批任务已把
// 样例数据迁移到 ADR-0038 新前缀（sprite 型动画剪辑 anim.* -> sprite_anim.*，纸娃娃层 mesh_ref 的
// sprite.* -> paperdoll.*），本类型若不同步改动，ResolveEffectDir 会继续把 sprite_anim.* 误当 vfx
// 目录解析（找不到对应特效目录）、ResolvePath 会继续把 paperdoll.* 误当扁平 sprites/*.png 解析
// （找不到对应纸娃娃层文件）——"不转发"从"多一份拷贝的维护风险"升级为"运行期解析规则与已迁移数据
// 不匹配的正确性缺陷"，因此本批把决策 8 第二项提前到本次落地；决策 8 第一项（sprite 型"按行 id 末段
// 命名"隐式接线改显式）与本次数据迁移无因果关系，仍按 ADR 原意留给后续任务，不在本次范围。以下四处
// 改为直接转发 AssetRefConventions 的对应公开方法，不再各自维护一份算法拷贝：
//   - ResolveModelResourcesPath 转发 AssetRefConventions.ModelLogicalPath。
//   - ResolveAnimClipResourcesPath 转发 AssetRefConventions.AnimClipLogicalPath。
//   - ResolveEffectDir 改为按类别前缀分派（vfx -> VfxResourceDir，sprite_anim -> SpriteAnimDir），
//     经 AssetRefConventions.ResolvePathSpace 取相对路径，不再硬编码 vfx 子目录。
//   - ResolvePath 对 ResourceKind.Image 新增 paperdoll 类别分支，转发
//     AssetRefConventions.PaperdollLayerFile；非 layer/paperdoll 类别的通用回退分支此后只覆盖
//     sprite 类别本身（sprite_set_id 的 Image 种类加载，04/09 已知的既有简化，不在 ADR-0038 四
//     字段范围内，本次不改动）。
// 落地时本机没有引擎批处理编译/测试环境，以下四处改动当时无法在本机重新验证；已于 2026-09-19 在
// 真实引擎环境（Unity 许可恢复后）跑通完整 check.ps1 验证通过（全部 29 步，PlayMode 288/288），
// 详见 architecture/落地计划/待引擎环境验证清单-2026-09-19-资源引用类别前缀适配层接线.md（已更新
// 为验证记录）与 CHANGELOG.md [Unreleased]"引擎适配层接线"小节"验证结果"。
//
// ADR-0096 判断记录（消费方第四十二批，阻塞——运行时解码贴图带 mip 链）：本类型三条解码路径
// （<see cref="DecodeMapLayerSprite"/>/<see cref="TryDecodeImage"/>/<see cref="TryDecodeEffect"/>）
// 此前统一固定 <c>new Texture2D(2, 2, TextureFormat.RGBA32, false)</c>（不生成 mip 链）后
// <c>LoadImage</c>，源资产密度高于屏幕实际显示密度时（4K 分辨率下角色按 0.13～0.69 倍缩小等常见
// 场景）欠采样锯齿明显，移动/缩放时贴图闪烁。现默认对三条路径开启 mip 链（<see cref="TextureSampling"/>，
// 见 <see cref="TextureSamplingOptions"/>），过滤模式默认三线性（无 mip 时自动降级为双线性，见
// <see cref="ApplyTextureSampling"/> 判断记录），地图分层图额外声明各向异性等级（默认 4，地图整体
// 拉伸摆放，观察角度导致的贴图倾斜比角色精灵更常见）。逐帧动画（<see cref="TryDecodeEffect"/>）开启
// mip 链时每帧改为切成独立纹理（<see cref="Texture2D.GetPixels(int,int,int,int)"/> 取块 + 新纹理
// <c>SetPixels32</c>/<c>Apply(updateMipmaps: true)</c>，原图集切完即销毁）——Unity 的 mip 链是按
// 整张纹理生成的，同一图集上不同帧的相邻区域会互相"渗色"进对方的低级 mip，逐帧独立纹理是唯一能让
// 每帧 mip 链正确反映自身内容而不掺杂集内其它帧像素的做法；帧矩形坐标与 <c>frames.json</c> 原有
// 传给 <c>Sprite.Create</c> 的 <c>Rect</c> 同一套约定（原点左下），<c>GetPixels(x,y,w,h)</c> 同样
// 原点左下，无需坐标翻转。已知限制：①只影响此后经本加载器解码的资源，不回溯已缓存的贴图（见
// <see cref="TextureSamplingOptions"/> 类型顶部）；②逐帧动画开启独立纹理后，加载时多一次 CPU 端
// 像素拷贝（<c>GetPixels</c> 逐帧读取），且帧与帧之间不再共享同一张图集的 GPU 纹理内存（显存
// 占用随帧数增长，典型逐帧动画显存开销上升约 33%，即 mip 链本身在 RGBA32 上的固定开销，见
// CHANGELOG 对应条目）；③关闭 <see cref="TextureSamplingOptions.MipChainForEffects"/> 时保持改动前
// "全部帧共用同一张图集纹理、无 mip"的行为，与之前逐字节一致。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityResourceLoader : IResourceLoader
    {
        /// <summary>解码出的一帧序列帧动画（<see cref="EffectAsset"/> 的元素）。</summary>
        public readonly struct EffectFrame
        {
            public readonly Sprite Sprite;
            public readonly double Duration;

            public EffectFrame(Sprite sprite, double duration)
            {
                Sprite = sprite;
                Duration = duration;
            }
        }

        /// <summary>一个 <c>ResourceKind.Effect</c> 资源解码后的可播放形态：按 frames.json 顺序切好
        /// 的 Sprite 序列 + 每帧时长 + 是否循环。</summary>
        public sealed class EffectAsset
        {
            public EffectFrame[] Frames { get; }
            public bool Loop { get; }

            public EffectAsset(EffectFrame[] frames, bool loop)
            {
                Frames = frames;
                Loop = loop;
            }
        }

        /// <summary>[ADR-0080](../../../../../../../architecture/adr/0080-地图分层图接入运行期渲染.md)
        /// 新增：一个 <see cref="ResourceKind.MapLayers"/> 资源解码后的可绘制形态——<c>ground</c>/
        /// <c>overlay</c> 是必需层，恒非空（未能解码到两者时整次加载判定为失败，见 <see cref="FinishMapLayersLoad"/>）；
        /// <c>decal</c> 是可选层，该地图没有 <c>decal.png</c> 时为 <c>null</c>（不是缺陷，见 ADR-0080
        /// 决策 6）。</summary>
        public sealed class MapLayerAsset
        {
            public Sprite Ground { get; }
            public Sprite Overlay { get; }
            public Sprite? Decal { get; }

            public MapLayerAsset(Sprite ground, Sprite overlay, Sprite? decal)
            {
                Ground = ground;
                Overlay = overlay;
                Decal = decal;
            }
        }

        /// <summary>一次 <see cref="ResourceKind.MapLayers"/> 加载在后台线程读完字节后，排入
        /// <see cref="_mapLayersCompletions"/> 等待下一次 <see cref="Tick"/> 在主线程解码（同
        /// <see cref="PendingCompletion"/> 用于 Effect 种类的既有惯例——单个资源涉及多份字节，不能
        /// 复用只携带单一 <see cref="PendingCompletion.Bytes"/> 的通用结构）。</summary>
        private struct PendingMapLayersCompletion
        {
            public Id ResourceId;
            public bool ReadSuccess;
            public byte[]? GroundBytes;
            public byte[]? OverlayBytes;

            /// <summary>null 表示该地图没有 decal.png（可选层缺失，ADR-0080 决策 6），不是读取失败。</summary>
            public byte[]? DecalBytes;
            public LoadCallback Callback;
        }

        /// <summary>一次 Font 种类的加载请求，排队等到下一次 <see cref="Tick"/> 在主线程调用
        /// <c>Resources.Load</c> 完成判定（见类型顶部"Font 资源种类"判断记录）。</summary>
        private struct PendingFontLoad
        {
            public Id ResourceId;
            public string ResourcesFontName;
            public LoadCallback Callback;
        }

        /// <summary>一次 <see cref="ResourceKind.Model"/> 种类的加载请求，排队等到下一次
        /// <see cref="Tick"/> 在主线程调用 <see cref="TryLoadModelSync"/> 完成判定（见类型
        /// 顶部"W6-B 新增"判断记录，同 <see cref="PendingFontLoad"/> 同一套处理惯例）。PR140-02
        /// 文档漂移根治：路径解析统一收到 <see cref="TryLoadModelSync"/>/<see cref="ResolveModelResourcesPath"/>
        /// 里，本结构不再单独持有一份重复解析出来的路径。</summary>
        private struct PendingModelLoad
        {
            public Id ResourceId;
            public LoadCallback Callback;
        }

        /// <summary>一次 <see cref="ResourceKind.AnimationClip"/> 种类的加载请求，排队等到下一次
        /// <see cref="Tick"/> 在主线程调用 <see cref="TryLoadAnimationClipSync"/> 完成判定（12 §5
        /// 勘误新增，同 <see cref="PendingModelLoad"/> 同一套处理惯例）。</summary>
        private struct PendingAnimClipLoad
        {
            public Id ResourceId;
            public LoadCallback Callback;
        }

        private struct PendingCompletion
        {
            public Id ResourceId;
            public ResourceKind Kind;
            public byte[]? Bytes;
            public bool ReadSuccess;
            public LoadCallback Callback;

            /// <summary>仅 <see cref="ResourceKind.Effect"/> 使用：frames.json 的文本内容
            /// （<see cref="Bytes"/> 此时承载 atlas.png 的字节）。</summary>
            public string? EffectFramesJson;

            /// <summary>[ADR-0095](../../../../../../../architecture/adr/0095-逐帧动画枢轴与像素密度取自所属精灵集.md)
            /// 新增，仅 <see cref="ResourceKind.Effect"/> 使用：本次加载请求携带的
            /// <see cref="ResourceLoadHints.SpriteSetId"/>（经 <see cref="LoadAsync(Id,ResourceKind,ResourceLoadHints,LoadCallback)"/>
            /// 传入，旧版三参 <see cref="LoadAsync(Id,ResourceKind,LoadCallback)"/> 固定为 <c>null</c>，
            /// 行为与改动前逐字节一致）。</summary>
            public Id? SpriteSetId;

            /// <summary>ADR-0095 决策 5，仅 <see cref="ResourceKind.Effect"/> 使用：true 表示本次是
            /// "同一资源已按某精灵集提示解码过，本次以不同提示重新请求"的复用场景——<see cref="Bytes"/>
            /// 只是一个占位空数组、<see cref="EffectFramesJson"/> 为 <c>null</c>，不重新解码，直接复用
            /// <see cref="_effects"/> 已有的缓存结果，见 <see cref="LoadEffectAsync"/> 判断记录。</summary>
            public bool EffectReuseCache;
        }

        private static readonly string DefaultRootDir = Path.Combine(Application.streamingAssetsPath, "GameFoundation");

        /// <summary>
        /// [ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md) 新增，
        /// 仅供本包测试程序集使用（<c>AssemblyInfo.cs</c> 已对 <c>Adapter.Unity.Tests.Runtime</c>/
        /// <c>Adapter.Unity.Tests.Editor</c> 开放 <c>InternalsVisibleTo</c>）：单元测试用它临时把
        /// <see cref="RootDir"/> 指向一个不在真实 StreamingAssets 布局下的临时目录，验证"按方向档位
        /// 分层"（结构②，<c>toolchain/asset_import/sprite_cmd.py</c> 产出）anchors.json 的解析逻辑，
        /// 不需要把测试夹具塞进真实 Unity 资产管线（新文件落在 <c>Assets/</c> 下会触发 meta 门禁，见
        /// AGENTS.md 第 1 节"派单若会在 Unity 导入范围内新建文件"）。为 <c>null</c> 时使用真实计算值
        /// （<see cref="DefaultRootDir"/>）。本字段是 <c>static</c>（跨全部加载器实例共享），测试必须
        /// 在用完后（<c>[TearDown]</c>）还原为 <c>null</c>，避免同进程内其它用例串味。
        /// </summary>
        internal static string? RootDirOverrideForTests;

        private static string RootDir => RootDirOverrideForTests ?? DefaultRootDir;

        /// <summary>仅 <see cref="ResourceKind.Font"/> 使用：主线程专用队列（见类型顶部"Font 资源
        /// 种类"判断记录），不与 <see cref="_completions"/> 共用——后者由后台线程写入，前者只在
        /// 主线程内部排队等到下一次 <see cref="Tick"/> 处理，不需要并发安全的队列类型。</summary>
        private readonly Queue<PendingFontLoad> _pendingFontLoads = new Queue<PendingFontLoad>();

        /// <summary>仅 <see cref="ResourceKind.Model"/> 使用：主线程专用队列，同
        /// <see cref="_pendingFontLoads"/> 同一套惯例（见类型顶部"W6-B 新增"判断记录）。</summary>
        private readonly Queue<PendingModelLoad> _pendingModelLoads = new Queue<PendingModelLoad>();

        /// <summary>仅 <see cref="ResourceKind.AnimationClip"/> 使用：主线程专用队列，同
        /// <see cref="_pendingModelLoads"/> 同一套惯例（12 §5 勘误新增）。</summary>
        private readonly Queue<PendingAnimClipLoad> _pendingAnimClipLoads = new Queue<PendingAnimClipLoad>();

        private readonly HashSet<Id> _loading = new HashSet<Id>();
        private readonly HashSet<Id> _loaded = new HashSet<Id>();
        private readonly ConcurrentQueue<PendingCompletion> _completions = new ConcurrentQueue<PendingCompletion>();

        /// <summary>仅 <see cref="ResourceKind.MapLayers"/> 使用：后台线程读完字节后的完成队列，同
        /// <see cref="_completions"/> 惯例（并发安全，后台线程写入、<see cref="Tick"/> 在主线程排空）。</summary>
        private readonly ConcurrentQueue<PendingMapLayersCompletion> _mapLayersCompletions = new ConcurrentQueue<PendingMapLayersCompletion>();

        private readonly Dictionary<Id, Sprite> _sprites = new Dictionary<Id, Sprite>();
        private readonly Dictionary<Id, AudioClip> _audioClips = new Dictionary<Id, AudioClip>();
        private readonly Dictionary<Id, Font> _fonts = new Dictionary<Id, Font>();
        private readonly Dictionary<Id, string> _dataTableText = new Dictionary<Id, string>();
        private readonly Dictionary<Id, string> _sceneText = new Dictionary<Id, string>();
        private readonly Dictionary<Id, string> _navMeshText = new Dictionary<Id, string>();
        private readonly Dictionary<Id, EffectAsset> _effects = new Dictionary<Id, EffectAsset>();

        /// <summary>[ADR-0096] 仅当 <see cref="TextureSamplingOptions.MipChainForEffects"/> 开启时使用：
        /// 一次 <see cref="TryDecodeEffect"/> 解码按帧切出的独立纹理列表（见该方法判断记录——每帧一张
        /// 独立纹理，不再共享同一张图集纹理），随对应资源 <see cref="Unload"/> 一并销毁，避免显存泄漏。
        /// 关闭该开关时保持改动前"全部帧共用同一张图集纹理"的行为，本字典不记录该资源的条目。</summary>
        private readonly Dictionary<Id, List<Texture2D>> _effectFrameTextures = new Dictionary<Id, List<Texture2D>>();

        /// <summary>[ADR-0095] 决策 5：记录每个已成功解码的 <see cref="ResourceKind.Effect"/> 资源
        /// 最近一次实际使用的 <see cref="ResourceLoadHints.SpriteSetId"/>（<c>null</c> 是一种合法取值，
        /// 表示"按无提示解码"）。供 <see cref="LoadEffectAsync"/> 判断"再次请求携带的提示是否与已解码
        /// 结果不一致"，见该方法判断记录。</summary>
        private readonly Dictionary<Id, Id?> _effectSpriteSetIdByResource = new Dictionary<Id, Id?>();

        /// <summary>[ADR-0095] 决策 5：<see cref="_effectSpriteSetIdByResource"/> 命中"不同提示重新
        /// 请求"这一分支时的 Warn 去重集合——按资源 id 只记一次，避免同一资源被反复以不同提示引用时
        /// 刷屏。</summary>
        private readonly HashSet<Id> _warnedEffectSpriteSetConflict = new HashSet<Id>();

        /// <summary>ADR-0080 新增：<see cref="ResourceKind.MapLayers"/> 已加载的分层图资产缓存，供
        /// <see cref="TryGetMapLayerAsset"/> 取用。</summary>
        private readonly Dictionary<Id, MapLayerAsset> _mapLayers = new Dictionary<Id, MapLayerAsset>();

        /// <summary>W6-B 新增：<see cref="ResourceKind.Model"/> 已加载的预制体资产缓存，供
        /// <see cref="TryGetModelPrefab"/>/<see cref="UnityRenderer3D.CreateModelInstance"/> 取用。</summary>
        private readonly Dictionary<Id, GameObject> _modelPrefabs = new Dictionary<Id, GameObject>();

        /// <summary>12 §5 勘误新增：<see cref="ResourceKind.AnimationClip"/> 已加载的剪辑资产缓存，
        /// 供 <see cref="TryGetAnimationClip"/> 取用（见类型顶部"12 §5 勘误判断记录"）。</summary>
        private readonly Dictionary<Id, AnimationClip> _animationClips = new Dictionary<Id, AnimationClip>();

        /// <summary>W6-CLIP 新增（AUD-05 根治）：<c>mesh_ref</c> 资源本身就是一个独立网格资产（不是
        /// 模型预制体）时的已加载缓存，见类型顶部"W6-CLIP 新增"判断记录第 4 步。</summary>
        private readonly Dictionary<Id, Mesh> _standaloneMeshes = new Dictionary<Id, Mesh>();

        /// <summary>W6-CLIP 新增（AUD-05 根治）：从模型预制体按 (resourceId, slotId 裸 Value 或
        /// 空字符串表示"未指定槽位，直接退回首个网格") 提取出的网格缓存，避免同一组合反复遍历层级；
        /// slotId 未指定时用空字符串作为该维度的键。</summary>
        private readonly Dictionary<(Id ResourceId, string SlotKey), Mesh> _extractedSlotMeshes =
            new Dictionary<(Id, string), Mesh>();

        /// <summary>本加载器使用的像素-单位换算比全局默认值，供 Sprite.Create 使用；与
        /// architecture/14_资产规格书模板.md 第 2.2 节"pixels_per_unit"游戏填写项对应，
        /// 框架层给一个可运行的默认值 100，具体项目在其接入记录里覆盖。
        /// <para>
        /// [ADR-0081](../../../../../../../architecture/adr/0081-精灵集自带像素密度在运行期生效.md)
        /// 判断记录（不再是唯一取值来源，取代此前"本加载器不读取项目专属配置，避免 L-1 反向依赖游戏
        /// 内容"这条字段注释——该顾虑对本次改动不成立）：<see cref="TryDecodeImage"/> 解码某个精灵集下
        /// 的图像（"layer." 纸娃娃层类别、"sprite." 类别本身直接当 Image 使用两种形态，见
        /// <see cref="ResolveImagePixelsPerUnit"/> 判断记录）时，优先读取该精灵集自己随身携带的
        /// anchors.json 顶层 <c>pixels_per_unit</c>——这是资产自身的伴生文件（适配层本来就在读同一
        /// 目录下的图片，与读取 <c>frames.json</c>/<c>atlas.json</c> 同一性质），不是任何项目专属配置
        /// 或内容数据表，不构成 L-1"适配层反向依赖游戏内容"；该字段缺失、非正数、anchors.json 文件
        /// 不存在或解析失败时才回退到本属性，回退路径与改动前逐字节一致。不属于任何精灵集的图像资源
        /// （<c>paperdoll.</c>/<c>icon.</c> 等其它类别，见 <see cref="ResolveImagePixelsPerUnit"/>
        /// 判断记录）与 <see cref="ResourceKind.Effect"/>（<see cref="TryDecodeEffect"/>，
        /// <c>frames.json</c> 没有该字段、vfx 图集没有"精灵集"归属层级，本轮不接入）始终只使用本
        /// 属性，不受影响。
        /// </para></summary>
        public float PixelsPerUnit { get; set; } = 100f;

        /// <summary>
        /// [ADR-0096](../../../../../../../architecture/adr/0096-运行时解码贴图带多级渐远链.md) 新增：
        /// 本加载器三条解码路径共用的贴图采样参数（是否生成 mip 链、过滤模式、地图分层图各向异性
        /// 等级），见 <see cref="TextureSamplingOptions"/> 类型顶部判断记录。可写属性，调用方可随时
        /// 整体替换或修改其属性；只影响此后新发起的解码，不回溯已缓存的贴图。
        /// </summary>
        public TextureSamplingOptions TextureSampling { get; set; } = new TextureSamplingOptions();

        /// <summary>ADR-0081 新增，[ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md)
        /// 扩展：按精灵集相对目录（如 <c>"sprites/placeholder_hero"</c>）缓存该集 anchors.json 解析出
        /// 的全部信息（<see cref="SpriteSetAnchorsInfo"/>：<c>pixels_per_unit</c> 声明 + 按方向档位分
        /// 索引的脚底锚点，见该类型注释）——两条决策共用同一份解析结果，同一份磁盘 IO，不为枢轴信息
        /// 新增第二次读盘（ADR-0091 决策 4）。<see cref="SpriteSetAnchorsInfo.Empty"/> 表示"该集未声明/
        /// 无效/文件不存在/解析失败"这一结论本身，同样要缓存，避免对没有声明的集反复做 IO（ADR-0081
        /// 决策 6）。生命周期与本加载器既有资源缓存（<see cref="_sprites"/> 等字段）同一套口径——随本
        /// 加载器实例存活，没有独立的清理入口（本类型当前也没有"整体清缓存/重载"入口，<see cref="Unload"/>
        /// 只按单个资源 id 清理，与本缓存的"按精灵集目录"粒度不是同一维度，不需要跟随 <see cref="Unload"/>
        /// 清理——同一精灵集的 anchors.json 内容不会因为某个资源被 Unload 而改变）。</summary>
        private readonly Dictionary<string, SpriteSetAnchorsInfo> _spriteSetAnchorsCache = new Dictionary<string, SpriteSetAnchorsInfo>();

        /// <summary>测试/诊断用（同 <see cref="PendingLoadCount"/> 惯例）：本加载器实际执行过
        /// anchors.json 磁盘读取+ 解析的次数（缓存命中不计数，见 <see cref="ReadSpriteSetAnchors"/>）。
        /// 供 PlayMode 测试验证"同一精灵集连续解码多张图，anchors.json 只被读一次"（ADR-0081 验收
        /// 标准 3，ADR-0091 决策 4 延续同一计数器语义），不属于 <see cref="IResourceLoader"/> 契约本身。</summary>
        public int SpriteSetAnchorsJsonReadCount { get; private set; }

        /// <summary>
        /// [ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md) 新增：
        /// 单个精灵集 anchors.json 解析出的、供 <see cref="ResolveImagePixelsPerUnit"/>/
        /// <see cref="ResolveImagePivot"/> 共用的全部信息（同一次磁盘 IO 产出，见决策 4）。
        /// </summary>
        private sealed class SpriteSetAnchorsInfo
        {
            /// <summary>anchors.json 不存在/顶层不是对象/解析失败——与改动前"该集未声明"的回退路径
            /// 逐字节一致。</summary>
            public static readonly SpriteSetAnchorsInfo Empty = new SpriteSetAnchorsInfo(null, null, null, false);

            /// <summary>顶层 <c>pixels_per_unit</c>（ADR-0081 决策 1），<c>null</c> 表示缺失/非数字/
            /// 不大于 0。</summary>
            public float? PixelsPerUnit { get; }

            /// <summary>ADR-0091 决策 1：按方向档位名索引的脚底锚点——像素坐标（原点左上，与 anchors.json
            /// 原文一致）+ 该方向声明的画布像素尺寸（用于与实际解码纹理尺寸比对，不一致时以纹理为准并
            /// 记 Warn，见决策 1）。两种 anchors.json 结构在解析期已统一抹平成同一形状（结构①取
            /// <c>directions.&lt;dir&gt;.root</c> + 顶层单一 <c>canvas</c>；结构②取
            /// <c>&lt;dir&gt;.anchors.root</c> + 该方向自己的 <c>canvas_size</c>）。<c>null</c>/空表示
            /// 该集没有任何可用的 root 声明，调用方一律回退默认枢轴 (0.5,0.5)。</summary>
            public IReadOnlyDictionary<string, (double X, double Y, int? CanvasWidth, int? CanvasHeight)>? DirectionRoots { get; }

            /// <summary>ADR-0091 决策 2：解码资源 id 未携带方向信息时使用的回退方向档位键——按"全部
            /// 方向 root 相同则用它，否则取 authored_directions[0]/directions 第一个键（结构①）或顶层
            /// 第一个键（结构②）"这条规则在解析期算好、缓存一次，调用方不重复判定。仅当
            /// <see cref="DirectionRoots"/> 非空时才有意义。</summary>
            public string? FallbackDirectionKey { get; }

            /// <summary>ADR-0091 决策 2：<see cref="FallbackDirectionKey"/> 是否因"各方向 root 声明不
            /// 一致、被迫选取兜底方向"而生效——决定调用方是否需要记一条 Warn（全部方向 root 相同时不
            /// 必警告，任选其一结果都一样）。</summary>
            public bool FallbackDirectionKeyIsAmbiguous { get; }

            public SpriteSetAnchorsInfo(
                float? pixelsPerUnit,
                IReadOnlyDictionary<string, (double X, double Y, int? CanvasWidth, int? CanvasHeight)>? directionRoots,
                string? fallbackDirectionKey,
                bool fallbackDirectionKeyIsAmbiguous)
            {
                PixelsPerUnit = pixelsPerUnit;
                DirectionRoots = directionRoots;
                FallbackDirectionKey = fallbackDirectionKey;
                FallbackDirectionKeyIsAmbiguous = fallbackDirectionKeyIsAmbiguous;
            }
        }

        public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _loading.Add(resourceId);

            if (kind == ResourceKind.Font)
            {
                // Resources.Load 只能在主线程调用，不走后台 Task.Run 字节读取路径，见类型顶部
                // "Font 资源种类"判断记录。
                _pendingFontLoads.Enqueue(new PendingFontLoad
                {
                    ResourceId = resourceId,
                    ResourcesFontName = "Fonts/" + StripCategoryPrefix(resourceId.Value),
                    Callback = callback
                });
                return;
            }

            if (kind == ResourceKind.Model)
            {
                // Resources.Load 只能在主线程调用，不走后台 Task.Run 字节读取路径，见类型顶部
                // "W6-B 新增"判断记录，同 Font 种类同一套处理。
                _pendingModelLoads.Enqueue(new PendingModelLoad
                {
                    ResourceId = resourceId,
                    Callback = callback
                });
                return;
            }

            if (kind == ResourceKind.AnimationClip)
            {
                // Resources.Load 只能在主线程调用，不走后台 Task.Run 字节读取路径，见类型顶部
                // "12 §5 勘误判断记录"，同 Model/Font 种类同一套处理。
                _pendingAnimClipLoads.Enqueue(new PendingAnimClipLoad
                {
                    ResourceId = resourceId,
                    Callback = callback
                });
                return;
            }

            if (kind == ResourceKind.Effect)
            {
                // ADR-0095：无提示的旧调用路径，spriteSetId 固定 null，与改动前逐字节一致，见
                // LoadEffectAsync 判断记录。
                LoadEffectAsync(resourceId, spriteSetId: null, callback);
                return;
            }

            if (kind == ResourceKind.MapLayers)
            {
                // ADR-0080：resourceId 是 world.map 行 id 本身（不是单层的资源引用），三个固定文件名
                // 经 AssetRefConventions 的公开推导方法解析，与内容导入工具链落地产物逐字节一致，见
                // 该类型判断记录。ground/overlay 必需，二者均读取成功才算整次加载成功；decal 可选，
                // 文件不存在不算失败（AGENTS.md"运行时路径不静默降级"这条约束针对的是"该失败却假装
                // 成功"，可选资源缺失本就不是失败，属于该约束的合法边界，不是被绕过）。
                var groundPath = Path.Combine(RootDir, AssetRefConventions.MapGroundFile(resourceId).Replace('/', Path.DirectorySeparatorChar));
                var overlayPath = Path.Combine(RootDir, AssetRefConventions.MapOverlayFile(resourceId).Replace('/', Path.DirectorySeparatorChar));
                var decalPath = Path.Combine(RootDir, AssetRefConventions.MapDecalFile(resourceId).Replace('/', Path.DirectorySeparatorChar));

                Task.Run(() =>
                {
                    byte[]? groundBytes = null;
                    byte[]? overlayBytes = null;
                    byte[]? decalBytes = null;
                    var ok = false;
                    try
                    {
                        if (File.Exists(groundPath) && File.Exists(overlayPath))
                        {
                            groundBytes = File.ReadAllBytes(groundPath);
                            overlayBytes = File.ReadAllBytes(overlayPath);
                            if (File.Exists(decalPath))
                            {
                                decalBytes = File.ReadAllBytes(decalPath);
                            }
                            ok = true;
                        }
                    }
                    catch
                    {
                        ok = false;
                    }

                    _mapLayersCompletions.Enqueue(new PendingMapLayersCompletion
                    {
                        ResourceId = resourceId,
                        ReadSuccess = ok,
                        GroundBytes = groundBytes,
                        OverlayBytes = overlayBytes,
                        DecalBytes = decalBytes,
                        Callback = callback
                    });
                });
                return;
            }

            var path = ResolvePath(resourceId, kind);

            Task.Run(() =>
            {
                byte[]? bytes = null;
                var ok = false;
                try
                {
                    if (File.Exists(path))
                    {
                        bytes = File.ReadAllBytes(path);
                        ok = true;
                    }
                }
                catch
                {
                    ok = false;
                }

                _completions.Enqueue(new PendingCompletion
                {
                    ResourceId = resourceId,
                    Kind = kind,
                    Bytes = bytes,
                    ReadSuccess = ok,
                    Callback = callback
                });
            });
        }

        /// <summary>
        /// [ADR-0095](../../../../../../../architecture/adr/0095-逐帧动画枢轴与像素密度取自所属精灵集.md)
        /// 新增：带 <see cref="ResourceLoadHints"/> 的加载入口，覆盖 <see cref="IResourceLoader"/>
        /// 默认接口成员——当前只有 <see cref="ResourceKind.Effect"/>（sprite 型逐帧动画/vfx 特效图集）
        /// 消费 <paramref name="hints"/>；其余种类不使用提示，直接转调旧三参重载，行为与改动前逐字节
        /// 一致。</summary>
        public void LoadAsync(Id resourceId, ResourceKind kind, ResourceLoadHints hints, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (kind == ResourceKind.Effect)
            {
                LoadEffectAsync(resourceId, hints.SpriteSetId, callback);
                return;
            }

            LoadAsync(resourceId, kind, callback);
        }

        /// <summary>
        /// [ADR-0095] <see cref="ResourceKind.Effect"/> 资源的实际加载入口，供旧版三参
        /// <see cref="LoadAsync(Id,ResourceKind,LoadCallback)"/>（<paramref name="spriteSetId"/> 固定
        /// <c>null</c>）与新版四参 <see cref="LoadAsync(Id,ResourceKind,ResourceLoadHints,LoadCallback)"/>
        /// （<paramref name="spriteSetId"/> 取自 <see cref="ResourceLoadHints.SpriteSetId"/>）共用。
        /// <para>
        /// 决策 5"缓存冲突"判断记录：本方法维护 <see cref="_effectSpriteSetIdByResource"/>，记录每个
        /// 已成功解码的资源最近一次实际使用的 <paramref name="spriteSetId"/>。若本次请求的取值与已
        /// 记录的不同（含"此前无提示、这次有提示"或反之），说明同一个逐帧动画资源被两个不同归属的
        /// 调用方引用——这理论上不该发生（同一资源引用 id 应当只属于一个精灵集），但发生时不重新
        /// 解码、直接复用已缓存的 <see cref="_effects"/> 结果（沿用第一次解码时用的枢轴/像素密度），
        /// 只按资源 id 去重记一条 Warn，不抛异常、不产生第二份不一致的解码结果。已知限制：这意味着
        /// 若第一次解码时提示有误，本方法不会因为后续一次"正确"的请求而自我纠正，需要重新加载器实例
        /// 生命周期（如重进场景）才会重新解码——与本加载器对其它全部资源种类"从不做增量失效检测，
        /// 缓存只增不减"的既有惯例一致，不是本次新增的缺口。
        /// </para>
        /// </summary>
        private void LoadEffectAsync(Id resourceId, Id? spriteSetId, LoadCallback callback)
        {
            _loading.Add(resourceId);

            if (_effectSpriteSetIdByResource.TryGetValue(resourceId, out var usedSpriteSetId) &&
                usedSpriteSetId != spriteSetId)
            {
                if (_warnedEffectSpriteSetConflict.Add(resourceId))
                {
                    Debug.LogWarning(
                        $"[UnityResourceLoader] 逐帧动画 \"{resourceId.Value}\" 此前已按精灵集提示 " +
                        $"\"{(usedSpriteSetId.HasValue ? usedSpriteSetId.Value.Value : "(无)")}\" 解码，" +
                        $"本次以不同提示 \"{(spriteSetId.HasValue ? spriteSetId.Value.Value : "(无)")}\" " +
                        "重新请求加载，复用已解码结果，不重复解码（ADR-0095 决策 5）。");
                }

                // 排进 _completions（而不是同步调用 callback）：保持"回调总在下一次 Tick 排队执行"
                // 这条既有线程约定不变，见类型顶部"判断记录（加载方式）"。不需要真正的磁盘 IO，
                // 不经 Task.Run 后台线程。
                _completions.Enqueue(new PendingCompletion
                {
                    ResourceId = resourceId,
                    Kind = ResourceKind.Effect,
                    Bytes = Array.Empty<byte>(),
                    ReadSuccess = true,
                    Callback = callback,
                    EffectReuseCache = true
                });
                return;
            }

            var effectDir = ResolveEffectDir(resourceId);
            Task.Run(() =>
            {
                byte[]? atlasBytes = null;
                string? framesJson = null;
                var ok = false;
                try
                {
                    var atlasPath = Path.Combine(effectDir, "atlas.png");
                    var framesPath = Path.Combine(effectDir, "frames.json");
                    if (File.Exists(atlasPath) && File.Exists(framesPath))
                    {
                        atlasBytes = File.ReadAllBytes(atlasPath);
                        framesJson = File.ReadAllText(framesPath);
                        ok = true;
                    }
                }
                catch
                {
                    ok = false;
                }

                _completions.Enqueue(new PendingCompletion
                {
                    ResourceId = resourceId,
                    Kind = ResourceKind.Effect,
                    Bytes = atlasBytes,
                    EffectFramesJson = framesJson,
                    ReadSuccess = ok,
                    Callback = callback,
                    SpriteSetId = spriteSetId
                });
            });
        }

        /// <summary>测试/诊断用：仍在等待后台线程读取完成、尚未在主线程 <see cref="Tick"/> 处理完的
        /// 资源加载请求数（<see cref="LoadAsync"/> 里 <c>_loading.Add</c>，<see cref="FinishOnMainThread"/>/
        /// <see cref="FinishFontLoad"/> 里 <c>_loading.Remove</c>）。判断记录（PlayMode 测试根治
        /// "相邻重负载用例的迟到异步日志"用）：本加载器把实际文件读取丢进 <c>Task.Run</c> 后台线程
        /// （见类型顶部"加载方式"判断记录），结果排进 <see cref="_completions"/>，真正的解码/诊断
        /// 日志只在下一次 <see cref="Tick"/>（由 <c>UnityEngineHost.Update</c> 每帧调用）于主线程
        /// 处理完成时才发生——若某条 PlayMode 用例在后台线程尚未写回结果时就结束（场景卸载/
        /// NUnit 进入下一条用例），这次迟到的 <see cref="Tick"/> 处理会在下一条完全无关的用例执行
        /// 窗口内触发，产生的任何 <c>Debug.LogWarning</c>/<c>LogError</c> 被 Unity Test Framework
        /// 记到那条无辜用例头上。触发大量资源首次引用的重负载 PlayMode 套件（见
        /// <c>VerticalSliceTests</c>）应在收尾（<c>[UnityTearDown]</c>）轮询本属性直到归零，让全部
        /// 异步加载在本用例自己的执行窗口内落地，见该测试类判断记录。</summary>
        public int PendingLoadCount => _loading.Count;

        public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);

        public double GetLoadProgress(Id resourceId)
        {
            if (_loaded.Contains(resourceId)) return 1.0;
            // 粗粒度估算（02 第 1.7 节允许）：仍在后台读取中记为 0.5，未发起过加载记为 0。
            return _loading.Contains(resourceId) ? 0.5 : 0.0;
        }

        public void Unload(Id resourceId)
        {
            _loaded.Remove(resourceId);
            _sprites.Remove(resourceId);
            _audioClips.Remove(resourceId);
            _fonts.Remove(resourceId);
            _dataTableText.Remove(resourceId);
            _sceneText.Remove(resourceId);
            _navMeshText.Remove(resourceId);
            _effects.Remove(resourceId);
            _mapLayers.Remove(resourceId);
            _modelPrefabs.Remove(resourceId);
            _animationClips.Remove(resourceId);
            _standaloneMeshes.Remove(resourceId);
            RemoveExtractedSlotMeshesFor(resourceId);

            // ADR-0095 决策 5：显式 Unload 后视为该资源的解码记录已作废，下一次 LoadAsync（无论
            // 携带什么提示）都应该正常重新解码，不应被当成"与此前不同提示"的冲突场景。
            _effectSpriteSetIdByResource.Remove(resourceId);
            _warnedEffectSpriteSetConflict.Remove(resourceId);

            // [ADR-0096]：开启逐帧独立纹理时（MipChainForEffects），每帧纹理不再共享图集，需要随本
            // 资源一并显式销毁，否则 Sprite 被移出 _effects 缓存后其独立纹理仍然常驻显存，泄漏。
            if (_effectFrameTextures.TryGetValue(resourceId, out var frameTextures))
            {
                for (var i = 0; i < frameTextures.Count; i++)
                {
                    if (frameTextures[i] != null)
                    {
                        UnityEngine.Object.Destroy(frameTextures[i]);
                    }
                }
                _effectFrameTextures.Remove(resourceId);
            }
        }

        /// <summary>见 <see cref="Unload"/>：<see cref="_extractedSlotMeshes"/> 用组合键
        /// (ResourceId, SlotKey)，无法直接 <c>Dictionary.Remove(resourceId)</c>，逐一筛出属于
        /// <paramref name="resourceId"/> 的条目再移除。</summary>
        private void RemoveExtractedSlotMeshesFor(Id resourceId)
        {
            List<(Id, string)>? toRemove = null;
            foreach (var key in _extractedSlotMeshes.Keys)
            {
                if (key.ResourceId == resourceId)
                {
                    (toRemove ??= new List<(Id, string)>()).Add(key);
                }
            }
            if (toRemove == null)
            {
                return;
            }
            for (var i = 0; i < toRemove.Count; i++)
            {
                _extractedSlotMeshes.Remove(toRemove[i]);
            }
        }

        /// <summary>由 UnityEngineHost.Update 每帧调用：把后台线程读完的文件字节在主线程完成
        /// 引擎侧解码并触发调用方回调。</summary>
        internal void Tick()
        {
            while (_pendingFontLoads.Count > 0)
            {
                FinishFontLoad(_pendingFontLoads.Dequeue());
            }

            while (_pendingModelLoads.Count > 0)
            {
                FinishModelLoad(_pendingModelLoads.Dequeue());
            }

            while (_pendingAnimClipLoads.Count > 0)
            {
                FinishAnimClipLoad(_pendingAnimClipLoads.Dequeue());
            }

            while (_completions.TryDequeue(out var pending))
            {
                FinishOnMainThread(pending);
            }

            while (_mapLayersCompletions.TryDequeue(out var pendingMapLayers))
            {
                FinishMapLayersLoad(pendingMapLayers);
            }
        }

        /// <summary>在主线程完成一次 Font 资源的加载判定：路径存在的已导入字体资产即视为
        /// "已加载"（见类型顶部"Font 资源种类"判断记录），不存在则按"加载失败"回调 false。</summary>
        private void FinishFontLoad(PendingFontLoad pending)
        {
            _loading.Remove(pending.ResourceId);

            var font = Resources.Load<Font>(pending.ResourcesFontName);
            if (font == null)
            {
                pending.Callback(pending.ResourceId, false);
                return;
            }

            _fonts[pending.ResourceId] = font;
            _loaded.Add(pending.ResourceId);
            pending.Callback(pending.ResourceId, true);
        }

        /// <summary>在主线程完成一次 <see cref="ResourceKind.Model"/> 资源的加载判定：路径存在的
        /// 已导入预制体资产即视为"已加载"并缓存进 <see cref="_modelPrefabs"/>（见类型顶部"W6-B 新增"
        /// 判断记录），不存在则按"加载失败"回调 false。PR140-02 文档漂移根治：复用
        /// <see cref="TryLoadModelSync"/> 完成实际解析与缓存写入，与同步路径共用同一份逻辑，不再各自
        /// 独立调用 <c>Resources.Load</c>，见该方法判断记录。</summary>
        private void FinishModelLoad(PendingModelLoad pending)
        {
            _loading.Remove(pending.ResourceId);

            var success = TryLoadModelSync(pending.ResourceId, out _);
            pending.Callback(pending.ResourceId, success);
        }

        /// <summary>在主线程完成一次 <see cref="ResourceKind.AnimationClip"/> 资源的加载判定，同
        /// <see cref="FinishModelLoad"/> 惯例（12 §5 勘误新增）：复用 <see cref="TryLoadAnimationClipSync"/>
        /// 完成实际解析与缓存写入。</summary>
        private void FinishAnimClipLoad(PendingAnimClipLoad pending)
        {
            _loading.Remove(pending.ResourceId);

            var success = TryLoadAnimationClipSync(pending.ResourceId, out _);
            pending.Callback(pending.ResourceId, success);
        }

        private void FinishOnMainThread(PendingCompletion pending)
        {
            _loading.Remove(pending.ResourceId);

            if (!pending.ReadSuccess || pending.Bytes == null)
            {
                pending.Callback(pending.ResourceId, false);
                return;
            }

            bool success;
            switch (pending.Kind)
            {
                case ResourceKind.Image:
                    success = TryDecodeImage(pending.ResourceId, pending.Bytes);
                    break;
                case ResourceKind.Audio:
                    success = TryDecodeWav(pending.ResourceId, pending.Bytes);
                    break;
                case ResourceKind.DataTable:
                    _dataTableText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                case ResourceKind.Scene:
                    _sceneText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                case ResourceKind.NavMesh:
                    _navMeshText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                case ResourceKind.Effect:
                    // ADR-0095 决策 5：复用场景（见 LoadEffectAsync 判断记录）不重新解码，直接复用
                    // 已有的 _effects 缓存——理论上此时缓存必定命中（LoadEffectAsync 只在命中过一次
                    // 成功解码记录后才会进入这条分支），ContainsKey 只是防御性写法，不代表存在缓存
                    // 未命中却标了 EffectReuseCache 的正常路径。
                    success = pending.EffectReuseCache
                        ? _effects.ContainsKey(pending.ResourceId)
                        : pending.EffectFramesJson != null &&
                            TryDecodeEffect(pending.ResourceId, pending.Bytes, pending.EffectFramesJson, pending.SpriteSetId);
                    break;
                default:
                    success = false;
                    break;
            }

            if (success)
            {
                _loaded.Add(pending.ResourceId);
            }

            pending.Callback(pending.ResourceId, success);
        }

        /// <summary>ADR-0080：在主线程完成一次 <see cref="ResourceKind.MapLayers"/> 加载判定——
        /// ground/overlay 字节均已在后台线程读到才算 <see cref="PendingMapLayersCompletion.ReadSuccess"/>
        /// 为 true；本方法据此各自解码成 <see cref="Sprite"/>，decal 字节为 null（该地图没有 decal.png，
        /// 可选层缺失）时 <see cref="MapLayerAsset.Decal"/> 同样为 null，不视为解码失败。</summary>
        private void FinishMapLayersLoad(PendingMapLayersCompletion pending)
        {
            _loading.Remove(pending.ResourceId);

            if (!pending.ReadSuccess || pending.GroundBytes == null || pending.OverlayBytes == null)
            {
                pending.Callback(pending.ResourceId, false);
                return;
            }

            var ground = DecodeMapLayerSprite(pending.GroundBytes);
            var overlay = DecodeMapLayerSprite(pending.OverlayBytes);
            if (ground == null || overlay == null)
            {
                pending.Callback(pending.ResourceId, false);
                return;
            }

            var decal = pending.DecalBytes != null ? DecodeMapLayerSprite(pending.DecalBytes) : null;

            _mapLayers[pending.ResourceId] = new MapLayerAsset(ground, overlay, decal);
            _loaded.Add(pending.ResourceId);
            pending.Callback(pending.ResourceId, true);
        }

        /// <summary>把地图分层图字节解码为 <see cref="Sprite"/>：与 <see cref="TryDecodeImage"/> 同一套
        /// <c>Texture2D.LoadImage</c> 解码路径，像素-单位换算比固定用 <see cref="PixelsPerUnit"/>
        /// 全局默认值——地图分层图不属于任何精灵集（没有伴生 anchors.json，见 ADR-0081"不属于任何
        /// 精灵集的图像资源"判断记录同一处境），且消费方（<see cref="UnityRenderer2D.CreateMapLayerInstance"/>）
        /// 会按调用方传入的世界矩形整体拉伸摆放，本方法解码出的 Sprite 具体像素-单位换算比数值本身
        /// 不影响最终摆放的世界尺寸，只影响中间量，取全局默认值即可，不必读取任何伴生声明。解码失败
        /// （非法图片字节）返回 null。</summary>
        private Sprite? DecodeMapLayerSprite(byte[] bytes)
        {
            var mipChain = TextureSampling.MipChainForMapLayers;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            ApplyTextureSampling(texture, mipChain, "地图分层图");
            // [ADR-0096]：各向异性过滤只在有 mip 链时才有意义，关闭 mip 链时回落到引擎默认值 1
            // （不声明各向异性），不是"关了 mip 但仍强行按声明值设置"这种没有实际效果的中间态。
            texture.anisoLevel = mipChain ? TextureSampling.MapLayerAnisoLevel : 1;

            return Sprite.Create(
                texture,
                new UnityEngine.Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);
        }

        /// <summary>枢轴回退默认值——不属于任何精灵集、anchors.json 缺失/解析失败/无 root/root 越界
        /// 时统一使用，与改动前逐字节一致（<c>Sprite.Create</c> 此前固定写死的取值）。</summary>
        private static readonly Vector2 DefaultPivot = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// [ADR-0096](../../../../../../../architecture/adr/0096-运行时解码贴图带多级渐远链.md) 判断
        /// 记录：<see cref="DecodeMapLayerSprite"/>/<see cref="TryDecodeImage"/>/<see cref="TryDecodeEffect"/>
        /// （逐帧独立纹理分支）三处共用的"套用采样参数"收口——<paramref name="mipChainRequested"/> 为
        /// <c>true</c>（对应资源种类的 <c>MipChainFor*</c> 开关已开启）时，先核实
        /// <c>texture.mipmapCount</c> 是否真的大于 1（部分平台/纹理格式组合可能不生成 mip，只记一条
        /// Warn 不抛异常，与本加载器既有"运行时路径不静默降级、但资源解析失败不中断整体加载"的惯例
        /// 一致——mip 缺失不是加载失败，是画质降级，不应阻断资源可用），再采用
        /// <see cref="TextureSamplingOptions.FilterMode"/>；为 <c>false</c> 时，若该属性仍是默认值
        /// <see cref="UnityEngine.FilterMode.Trilinear"/>，自动降级为
        /// <see cref="UnityEngine.FilterMode.Bilinear"/>（没有 mip 链时 Trilinear 无级间可插值，等价于
        /// Bilinear，直接采用更明确，不依赖引擎自身的隐式行为），显式配置为其它取值（如 Point）时原样
        /// 保留，不强行覆盖。
        /// </summary>
        private void ApplyTextureSampling(Texture2D texture, bool mipChainRequested, string diagnosticContext)
        {
            if (mipChainRequested)
            {
                if (texture.mipmapCount <= 1)
                {
                    Debug.LogWarning(
                        $"[UnityResourceLoader] {diagnosticContext} 请求生成 mip 链，但解码后 " +
                        $"mipmapCount={texture.mipmapCount}，可能受当前平台/纹理格式限制未能生成" +
                        "（ADR-0096）。");
                }
                texture.filterMode = TextureSampling.FilterMode;
                return;
            }

            texture.filterMode = TextureSampling.FilterMode == FilterMode.Trilinear
                ? FilterMode.Bilinear
                : TextureSampling.FilterMode;
        }

        private bool TryDecodeImage(Id resourceId, byte[] bytes)
        {
            var mipChain = TextureSampling.MipChainForImages;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                return false;
            }

            ApplyTextureSampling(texture, mipChain, $"图像资源 \"{resourceId.Value}\"");

            var sprite = Sprite.Create(
                texture,
                new UnityEngine.Rect(0, 0, texture.width, texture.height),
                ResolveImagePivot(resourceId, texture.width, texture.height),
                ResolveImagePixelsPerUnit(resourceId));
            sprite.name = resourceId.Value;
            _sprites[resourceId] = sprite;
            return true;
        }

        /// <summary>
        /// [ADR-0081](../../../../../../../architecture/adr/0081-精灵集自带像素密度在运行期生效.md)
        /// 决策 1：解析 <paramref name="resourceId"/>（<see cref="ResourceKind.Image"/>）应使用的
        /// 像素-单位换算比——若该资源属于某个精灵集且该集 anchors.json 顶层声明了合法（大于 0）的
        /// <c>pixels_per_unit</c>，用它；否则（不属于任何精灵集、未声明、非正数、anchors.json 不
        /// 存在、解析失败）一律回退 <see cref="PixelsPerUnit"/>，回退路径与改动前逐字节一致，不影响
        /// 既有资产。按精灵集相对目录缓存解析结果（决策 6，见 <see cref="_spriteSetAnchorsCache"/>
        /// 判断记录），同一精灵集下解码第二张及之后的图不再重复读盘。
        /// </summary>
        private float ResolveImagePixelsPerUnit(Id resourceId)
        {
            if (!TryResolveSpriteSetRelativeDir(resourceId, out var spriteSetRelativeDir, out _))
            {
                return PixelsPerUnit;
            }

            var info = GetOrReadSpriteSetAnchors(spriteSetRelativeDir);
            return info.PixelsPerUnit ?? PixelsPerUnit;
        }

        /// <summary>
        /// [ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md)
        /// 决策 1：解析 <paramref name="resourceId"/>（<see cref="ResourceKind.Image"/>）应使用的
        /// 精灵枢轴（归一化 0..1，Unity <c>Sprite.Create</c> 的 <c>pivot</c> 实参语义，原点左下）——
        /// 若该资源属于某个精灵集、该集 anchors.json 声明了对应方向的脚底锚点 <c>root</c>（像素坐标，
        /// 原点左上），枢轴 = <c>(root.x / 实际纹理宽度, 1 - root.y / 实际纹理高度)</c>（宽高取
        /// <paramref name="textureWidth"/>/<paramref name="textureHeight"/> 这两个已解码出的实际值，
        /// 不取 anchors.json 声明的画布——两者一致时结果等价，不一致时以实际纹理为准并记一条 Warn，
        /// 详见本方法内比对逻辑）；否则（不属于任何精灵集、anchors.json 不存在/解析失败/该方向无
        /// <c>root</c>/root 越界）一律回退 <see cref="DefaultPivot"/>，回退路径与改动前逐字节一致，
        /// 不影响既有资产。与 <see cref="ResolveImagePixelsPerUnit"/> 共用同一份
        /// <see cref="_spriteSetAnchorsCache"/>（决策 4：不为枢轴信息新增第二次磁盘 IO）。
        /// </summary>
        private Vector2 ResolveImagePivot(Id resourceId, int textureWidth, int textureHeight)
        {
            if (textureWidth <= 0 || textureHeight <= 0)
            {
                // 理论不应发生（TryDecodeImage 已确认 Texture2D.LoadImage 成功才会走到这里），
                // 纯防御性分支，避免下面的除法产生 NaN。
                return DefaultPivot;
            }

            if (!TryResolveSpriteSetRelativeDir(resourceId, out var spriteSetRelativeDir, out var directionSlot))
            {
                return DefaultPivot;
            }

            var info = GetOrReadSpriteSetAnchors(spriteSetRelativeDir);
            if (info.DirectionRoots == null || info.DirectionRoots.Count == 0)
            {
                return DefaultPivot;
            }

            var resolvedDirectionKey = directionSlot;
            var usedFallbackDirection = false;
            if (resolvedDirectionKey == null)
            {
                resolvedDirectionKey = info.FallbackDirectionKey;
                usedFallbackDirection = true;
            }

            if (resolvedDirectionKey == null || !info.DirectionRoots.TryGetValue(resolvedDirectionKey, out var root))
            {
                return DefaultPivot;
            }

            if (usedFallbackDirection && info.FallbackDirectionKeyIsAmbiguous)
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 精灵集 \"{spriteSetRelativeDir}\" 各方向脚底锚点声明不一致，" +
                    $"资源 \"{resourceId.Value}\" 未携带方向信息，回退使用 \"{resolvedDirectionKey}\" 方向的" +
                    "锚点计算枢轴（ADR-0091 决策 2）。");
            }

            if (root.X < 0 || root.Y < 0 || root.X > textureWidth || root.Y > textureHeight)
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 精灵集 \"{spriteSetRelativeDir}\" 方向 \"{resolvedDirectionKey}\" " +
                    $"声明的脚底锚点 ({root.X}, {root.Y}) 超出实际解码纹理尺寸 {textureWidth}x{textureHeight}，" +
                    "回退默认枢轴 (0.5, 0.5)（ADR-0091 决策 3）。");
                return DefaultPivot;
            }

            if (root.CanvasWidth.HasValue && root.CanvasHeight.HasValue &&
                (root.CanvasWidth.Value != textureWidth || root.CanvasHeight.Value != textureHeight))
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 精灵集 \"{spriteSetRelativeDir}\" 方向 \"{resolvedDirectionKey}\" " +
                    $"anchors.json 声明画布 {root.CanvasWidth}x{root.CanvasHeight} 与实际解码纹理尺寸 " +
                    $"{textureWidth}x{textureHeight} 不一致，按实际纹理尺寸换算枢轴（ADR-0091 决策 1）。");
            }

            return new Vector2((float)(root.X / textureWidth), 1f - (float)(root.Y / textureHeight));
        }

        /// <summary>
        /// ADR-0081 判断记录（"不属于任何精灵集的图像资源走什么路径"，任务书要求本方法自行判断并
        /// 在 ADR 里写明）：判断 <paramref name="resourceId"/>（<see cref="ResourceKind.Image"/>）是否
        /// 属于某个精灵集，是则给出该精灵集在资产根目录下的相对目录路径（正斜杠分隔，同
        /// <see cref="AssetRefConventions.SpriteSetDirectory"/> 路径空间，可直接拼进 <see cref="RootDir"/>
        /// 做文件系统访问），以及该资源 id 自带的方向档位（[ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md)
        /// 新增 <paramref name="directionSlot"/> 出参，供 <see cref="ResolveImagePivot"/> 使用；
        /// <see cref="ResolveImagePixelsPerUnit"/> 不需要方向，丢弃该出参）。覆盖两类形态：
        /// <list type="bullet">
        /// <item><c>"layer."</c> 类别（身体纸娃娃层与 ADR-0071 装备覆盖层，两者共用同一套
        /// <c>sprites/&lt;精灵集名&gt;/&lt;方向&gt;/&lt;层名&gt;.png</c> 三级目录，见本类型
        /// <see cref="ResolvePath"/> 方法"U2-1 判断记录"）：精灵集目录段即双下划线分隔三段资源 id
        /// 的第一段，方向档位是第二段——层文件本就落在该精灵集目录下，段数不是恰好 3 段时（不满足
        /// 假定形状）视为不属于任何精灵集，同 <see cref="ResolvePath"/> 对该情形的既有容错退化一致。</item>
        /// <item><c>"sprite."</c> 类别本身（<c>sprite_set_id</c> 直接当 Image 资源 id 使用的既有
        /// 简化，见 <see cref="ResolvePath"/> 类型顶部"ADR-0038 适配层接线"一节"非 layer/paperdoll
        /// 类别的通用回退分支此后只覆盖 sprite 类别本身"）：资源 id 本身就是 <c>sprite_set_id</c>，
        /// 经 <see cref="AssetRefConventions.SpriteSetDirectory"/> 直接算出目录——该资源本来就是
        /// "这个精灵集"，用它自己的 anchors.json 合乎直觉；不携带方向信息（<paramref name="directionSlot"/>
        /// 为 <c>null</c>），枢轴解析按 ADR-0091 决策 2 的回退方向处理。</item>
        /// </list>
        /// 其余类别均不落在任何精灵集目录下，返回 <c>false</c>，调用方据此直接回退全局
        /// <see cref="PixelsPerUnit"/>/<see cref="DefaultPivot"/>，不做多余的 anchors.json 查找/IO：
        /// <list type="bullet">
        /// <item><c>"paperdoll."</c>——扁平单文件（<see cref="AssetRefConventions.PaperdollLayerFile"/>），
        /// 不落在任何 <c>sprites/&lt;name&gt;/</c> 目录下，没有伴生的 anchors.json。</item>
        /// <item><c>"icon."</c> 等其它类别——落在 <c>icons/</c> 等与精灵集无关的独立子目录（见
        /// <see cref="AssetRefConventions.IconFile"/>），同样没有精灵集语义。</item>
        /// </list>
        /// </summary>
        private static bool TryResolveSpriteSetRelativeDir(Id resourceId, out string spriteSetRelativeDir, out string? directionSlot)
        {
            var value = resourceId.Value;
            if (IsLayerCategory(value))
            {
                var layerName = StripCategoryPrefix(value);
                var parts = layerName.Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length == 3)
                {
                    spriteSetRelativeDir = "sprites/" + parts[0];
                    directionSlot = parts[1];
                    return true;
                }
                spriteSetRelativeDir = null!;
                directionSlot = null;
                return false;
            }

            if (CategoryPrefixOf(value) == "sprite")
            {
                spriteSetRelativeDir = AssetRefConventions.SpriteSetDirectory(resourceId);
                directionSlot = null;
                return true;
            }

            spriteSetRelativeDir = null!;
            directionSlot = null;
            return false;
        }

        /// <summary>ADR-0081/[ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md)：
        /// 按精灵集相对目录取缓存，未命中才调用 <see cref="ReadSpriteSetAnchors"/> 做磁盘 IO（决策 4：
        /// <see cref="ResolveImagePixelsPerUnit"/>/<see cref="ResolveImagePivot"/> 共用同一份缓存，不
        /// 各自维护、不重复读盘）。</summary>
        private SpriteSetAnchorsInfo GetOrReadSpriteSetAnchors(string spriteSetRelativeDir)
        {
            if (!_spriteSetAnchorsCache.TryGetValue(spriteSetRelativeDir, out var info))
            {
                info = ReadSpriteSetAnchors(spriteSetRelativeDir);
                _spriteSetAnchorsCache[spriteSetRelativeDir] = info;
            }
            return info;
        }

        /// <summary>ADR-0081/ADR-0091：实际做一次 anchors.json 磁盘 IO + 解析（只在
        /// <see cref="GetOrReadSpriteSetAnchors"/> 的缓存未命中时调用一次，见
        /// <see cref="SpriteSetAnchorsJsonReadCount"/> 判断记录），一次性解析出
        /// <c>pixels_per_unit</c>（ADR-0081）与按方向档位分层的脚底锚点（ADR-0091 决策 1/4）。文件
        /// 不存在、顶层不是 JSON 对象、JSON 语法解析失败——返回 <see cref="SpriteSetAnchorsInfo.Empty"/>
        /// （两条信息均视为"未声明"，调用方各自回退全局默认值），不抛异常，与本加载器其余资源解析
        /// 失败时"降级、不中断"的既有惯例一致（见类型顶部"判断记录（加载方式）"等既有段落）。</summary>
        private SpriteSetAnchorsInfo ReadSpriteSetAnchors(string spriteSetRelativeDir)
        {
            SpriteSetAnchorsJsonReadCount++;

            try
            {
                var anchorsPath = Path.Combine(
                    RootDir, spriteSetRelativeDir.Replace('/', Path.DirectorySeparatorChar), "anchors.json");
                if (!File.Exists(anchorsPath))
                {
                    return SpriteSetAnchorsInfo.Empty;
                }

                var json = File.ReadAllText(anchorsPath);
                if (!(JsonReader.Parse(json) is JsonObject obj))
                {
                    return SpriteSetAnchorsInfo.Empty;
                }

                float? pixelsPerUnit = null;
                if (obj.TryGetValue("pixels_per_unit", out var ppuVal) && ppuVal is JsonNumber ppuNum)
                {
                    var declared = (float)ppuNum.Value;
                    pixelsPerUnit = declared > 0f ? declared : (float?)null;
                }

                var directionRoots = ParseDirectionRoots(obj, out var fallbackKey, out var ambiguous);
                return new SpriteSetAnchorsInfo(pixelsPerUnit, directionRoots, fallbackKey, ambiguous);
            }
            catch
            {
                return SpriteSetAnchorsInfo.Empty;
            }
        }

        /// <summary>
        /// [ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md) 决策 1：
        /// 兼容仓库内并存的两种 anchors.json 顶层结构（ADR-0081 背景已记录、本次仍未收敛，见该 ADR
        /// "已知不一致/待办"一节）：
        /// <list type="bullet">
        /// <item>结构①（<c>toolchain/gen_placeholder_assets.py</c> 产出，整集单一顶层）：顶层
        /// <c>canvas: {width, height}</c>（全部方向共用同一画布）+ <c>directions: {"&lt;方向&gt;":
        /// {"root": [x, y], ...}}</c>。</item>
        /// <item>结构②（<c>toolchain/asset_import/sprite_cmd.py</c> 产出，按方向档位分层）：顶层键
        /// 即方向档位名，值形如 <c>{"canvas_size": [w, h], "anchors": {"root": [x, y], ...}}</c>
        /// （<c>pixels_per_unit</c> 是唯一的兄弟标量键，不是方向档位，见 ADR-0081 决策 A）。</item>
        /// </list>
        /// 两种结构互斥判定：顶层含 <c>directions</c> 且其值是 JSON 对象 → 结构①；否则遍历顶层除
        /// <c>pixels_per_unit</c> 外的键，值形如 <c>{"anchors": {"root": [...]}, ...}</c> 的视为一个
        /// 方向档位 → 结构②。都未命中（既无 <c>directions</c> 也无任何方向档位含 <c>root</c>）时返回
        /// <c>null</c>，调用方回退默认枢轴。
        /// </summary>
        private static IReadOnlyDictionary<string, (double X, double Y, int? CanvasWidth, int? CanvasHeight)>? ParseDirectionRoots(
            JsonObject root, out string? fallbackDirectionKey, out bool fallbackDirectionKeyIsAmbiguous)
        {
            fallbackDirectionKey = null;
            fallbackDirectionKeyIsAmbiguous = false;

            if (root.TryGetValue("directions", out var directionsVal) && directionsVal is JsonObject directionsObj)
            {
                int? canvasWidth = null;
                int? canvasHeight = null;
                if (root.TryGetValue("canvas", out var canvasVal) && canvasVal is JsonObject canvasObj)
                {
                    canvasWidth = TryGetJsonInt(canvasObj, "width");
                    canvasHeight = TryGetJsonInt(canvasObj, "height");
                }

                var map = new Dictionary<string, (double X, double Y, int? CanvasWidth, int? CanvasHeight)>(StringComparer.Ordinal);
                string? firstKeyInFileOrder = null;
                foreach (var entry in directionsObj)
                {
                    if (entry.Value is JsonObject dirObj && TryGetRootAnchor(dirObj, out var x, out var y))
                    {
                        map[entry.Key] = (x, y, canvasWidth, canvasHeight);
                        firstKeyInFileOrder ??= entry.Key;
                    }
                }
                if (map.Count == 0)
                {
                    return null;
                }

                // 决策 2：结构①优先用 authored_directions[0]（该数组本就是"这个精灵集有哪些方向档位"
                // 的权威声明，见 gen_placeholder_assets.py 产出），它不存在/不在 map 里时退化用
                // directions 对象里（JsonObject 保序，见该类型注释"保持键的插入顺序"）第一个有合法
                // root 的键——不依赖字典枚举顺序，全部取自按文件顺序遍历得到的 firstKeyInFileOrder。
                string? authoredFirst = null;
                if (root.TryGetValue("authored_directions", out var authoredVal) && authoredVal is JsonArray authoredArr &&
                    authoredArr.Count > 0 && authoredArr[0] is JsonString authoredFirstStr)
                {
                    authoredFirst = authoredFirstStr.Value;
                }
                var preferredKey = authoredFirst != null && map.ContainsKey(authoredFirst) ? authoredFirst : firstKeyInFileOrder!;

                AssignFallbackKey(map, preferredKey, out fallbackDirectionKey, out fallbackDirectionKeyIsAmbiguous);
                return map;
            }

            {
                var map = new Dictionary<string, (double X, double Y, int? CanvasWidth, int? CanvasHeight)>(StringComparer.Ordinal);
                string? firstKeyInFileOrder = null;
                foreach (var entry in root)
                {
                    if (entry.Key == "pixels_per_unit")
                    {
                        continue;
                    }
                    if (entry.Value is JsonObject dirObj &&
                        dirObj.TryGetValue("anchors", out var anchorsVal) && anchorsVal is JsonObject anchorsObj &&
                        TryGetRootAnchor(anchorsObj, out var x, out var y))
                    {
                        int? canvasWidth = null;
                        int? canvasHeight = null;
                        if (dirObj.TryGetValue("canvas_size", out var canvasSizeVal) && canvasSizeVal is JsonArray canvasArr &&
                            canvasArr.Count >= 2 && canvasArr[0] is JsonNumber cw && canvasArr[1] is JsonNumber ch)
                        {
                            canvasWidth = (int)cw.Value;
                            canvasHeight = (int)ch.Value;
                        }
                        map[entry.Key] = (x, y, canvasWidth, canvasHeight);
                        firstKeyInFileOrder ??= entry.Key;
                    }
                }
                if (map.Count == 0)
                {
                    return null;
                }

                // 决策 2：结构②用顶层第一个方向档位键（即 firstKeyInFileOrder，按 JsonObject 保序的
                // 文件出现顺序，不依赖字典枚举顺序）。
                AssignFallbackKey(map, firstKeyInFileOrder!, out fallbackDirectionKey, out fallbackDirectionKeyIsAmbiguous);
                return map;
            }
        }

        /// <summary>决策 2：判断 <paramref name="map"/> 里全部方向的 root 是否完全相同——相同则
        /// <paramref name="preferredKey"/>（任选其一，结果都一样）不算"有歧义"，调用方不必记 Warn；
        /// 不同则 <paramref name="preferredKey"/> 是"被迫选中"的兜底方向，<paramref name="ambiguous"/>
        /// 置 true，调用方在实际命中该回退路径时记一条 Warn。</summary>
        private static void AssignFallbackKey(
            Dictionary<string, (double X, double Y, int? CanvasWidth, int? CanvasHeight)> map,
            string preferredKey,
            out string fallbackDirectionKey,
            out bool ambiguous)
        {
            var first = map[preferredKey];
            ambiguous = false;
            foreach (var kv in map)
            {
                if (kv.Value.X != first.X || kv.Value.Y != first.Y)
                {
                    ambiguous = true;
                    break;
                }
            }
            fallbackDirectionKey = preferredKey;
        }

        /// <summary>从形如 <c>{"root": [x, y], ...}</c> 的 JSON 对象取出 <c>root</c> 二元像素坐标
        /// （结构①的 <c>directions.&lt;dir&gt;</c>、结构②的 <c>&lt;dir&gt;.anchors</c> 两处调用点共用
        /// 同一套取值逻辑）。<c>root</c> 缺失、不是数组、长度不足 2、元素不是数字——均返回
        /// <c>false</c>，调用方视为该方向无可用 root 声明。</summary>
        private static bool TryGetRootAnchor(JsonObject obj, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (!obj.TryGetValue("root", out var val) || !(val is JsonArray arr) || arr.Count < 2)
            {
                return false;
            }
            if (!(arr[0] is JsonNumber nx) || !(arr[1] is JsonNumber ny))
            {
                return false;
            }
            x = nx.Value;
            y = ny.Value;
            return true;
        }

        private static int? TryGetJsonInt(JsonObject obj, string key)
        {
            if (obj.TryGetValue(key, out var val) && val is JsonNumber num)
            {
                return (int)num.Value;
            }
            return null;
        }

        /// <summary>解析 <c>frames.json</c>（结构见 <see cref="EffectFramesDocument"/>：消费方反馈
        /// 第 77 条（[ADR-0054](../../../../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)）
        /// 收口，JSON 结构本身的解析（含 <c>loop</c>/<c>fps</c>/<c>frame_duration</c>/单帧
        /// <c>duration</c> 四组缺省值推算）已提到 <c>Core.Foundation.EngineAdapter</c>，
        /// 不再是本方法私有实现）+ <c>atlas.png</c> 字节，按每帧矩形从图集切出 Sprite，装配成
        /// <see cref="EffectAsset"/>。
        /// <para>
        /// 判断记录（单帧 <c>w</c>/<c>h</c> 缺省时的兜底值为何仍留在本方法、不随其余字段一起迁移，
        /// 见 <see cref="EffectFrameData.Width"/> 类型注释）：兜底值是"已解码图集纹理的整宽/整高"，
        /// 只有加载完 <paramref name="atlasBytes"/> 之后才能拿到，不是纯 JSON 解析阶段能确定的信息，
        /// 因此 <see cref="EffectFramesDocument"/> 把缺省的 <c>w</c>/<c>h</c> 留成 <c>null</c>，本方法
        /// 拿到纹理尺寸后在这里补上——与改动前逐字节相同的兜底值，只是计算发生的位置不同。
        /// </para>
        /// <para>
        /// [ADR-0095](../../../../../../../architecture/adr/0095-逐帧动画枢轴与像素密度取自所属精灵集.md)
        /// 新增 <paramref name="spriteSetId"/>：<c>null</c> 时（vfx 特效图集、或调用方给不出归属信息）
        /// 逐字节保留改动前行为——枢轴固定 <c>(0.5, 0.5)</c>、像素密度固定 <see cref="PixelsPerUnit"/>；
        /// 非 <c>null</c> 时（sprite 型逐帧动画，归属某个精灵集）经 <see cref="ResolveEffectPixelsPerUnit"/>/
        /// <see cref="ResolveEffectPivot"/> 按 frames.json 显式声明 > 精灵集提示 > 全局默认的优先级解析，
        /// 见两方法判断记录。
        /// </para>
        /// </summary>
        /// <summary>
        /// [ADR-0096] 决策 3 判断记录（逐帧图集开启 mip 链时按帧切成独立纹理）：Unity 的 mip 链按
        /// 整张纹理生成，若继续让全部帧共享同一张图集纹理，某一帧的低级 mip 会掺入图集里相邻帧的
        /// 像素（"渗色"）——缩小观察时一帧会隐约看见邻帧内容。本方法在
        /// <see cref="TextureSamplingOptions.MipChainForEffects"/> 开启时，先解码图集到一张临时纹理
        /// （<paramref name="atlasBytes"/>，不为它生成 mip、只用来读像素，切完帧即销毁），再用
        /// <see cref="Texture2D.GetPixels(int,int,int,int)"/> 按帧矩形取出像素块，写入一张该帧专属
        /// 尺寸的新纹理（<c>SetPixels32</c> + <c>Apply(updateMipmaps: true)</c>），让每帧的 mip 链只由
        /// 自己的像素生成。<c>GetPixels(x, y, w, h)</c> 与既有代码传给 <c>Sprite.Create</c> 的
        /// <c>Rect</c> 同一套坐标约定（原点左下，见 <c>frames.json</c> 既有解析与测试夹具的像素排布），
        /// 不需要额外的 Y 轴翻转。关闭该开关时保持改动前"全部帧共用同一张图集纹理、无 mip"的行为，
        /// 逐字节不变。
        /// </summary>
        private bool TryDecodeEffect(Id resourceId, byte[] atlasBytes, string framesJson, Id? spriteSetId)
        {
            if (!EffectFramesDocument.TryParse(framesJson, out var document))
            {
                return false;
            }

            var mipChain = TextureSampling.MipChainForEffects;

            // 图集本身在开启逐帧独立纹理时只是像素来源、不作为任何 Sprite 的最终纹理，不需要生成
            // mip（见本方法判断记录）；关闭时图集本身就是全部帧共用的最终纹理，与改动前一致地不生成
            // mip（是否生成 mip 由下面 mipChain 分支各自决定）。
            var atlasTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!atlasTexture.LoadImage(atlasBytes))
            {
                UnityEngine.Object.Destroy(atlasTexture);
                return false;
            }

            if (!mipChain)
            {
                ApplyTextureSampling(atlasTexture, mipChainRequested: false, $"逐帧动画 \"{resourceId.Value}\" 图集");
            }

            var pixelsPerUnit = ResolveEffectPixelsPerUnit(spriteSetId, document);

            var frames = new EffectFrame[document.Frames.Count];
            List<Texture2D>? frameTextures = mipChain ? new List<Texture2D>(document.Frames.Count) : null;

            for (var i = 0; i < document.Frames.Count; i++)
            {
                var frameData = document.Frames[i];
                var w = (int)(frameData.Width ?? atlasTexture.width);
                var h = (int)(frameData.Height ?? atlasTexture.height);
                var pivot = ResolveEffectPivot(resourceId, spriteSetId, document, w, h);

                Sprite sprite;
                if (mipChain)
                {
                    // 判断记录：Texture2D.GetPixels32 没有按矩形取块的重载（只有整图/按 mip 级两种），
                    // 按矩形取块只有 GetPixels(x, y, blockWidth, blockHeight) 这一套（返回 Color[]），
                    // 改用它配 SetPixels，效果与 GetPixels32/SetPixels32 等价，只是中间类型是
                    // Color 而不是 Color32。
                    var frameTexture = new Texture2D(w, h, TextureFormat.RGBA32, true);
                    frameTexture.SetPixels(atlasTexture.GetPixels((int)frameData.X, (int)frameData.Y, w, h));
                    frameTexture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                    ApplyTextureSampling(frameTexture, mipChainRequested: true,
                        $"逐帧动画 \"{resourceId.Value}\" 第 {i} 帧");

                    sprite = Sprite.Create(
                        frameTexture, new UnityEngine.Rect(0, 0, w, h), pivot, pixelsPerUnit);
                    frameTextures!.Add(frameTexture);
                }
                else
                {
                    sprite = Sprite.Create(
                        atlasTexture,
                        new UnityEngine.Rect((float)frameData.X, (float)frameData.Y, w, h),
                        pivot,
                        pixelsPerUnit);
                }
                sprite.name = $"{resourceId.Value}_frame{i}";

                frames[i] = new EffectFrame(sprite, frameData.Duration);
            }

            if (mipChain)
            {
                // 图集只是像素来源，全部帧已切成独立纹理，图集本身不再被任何 Sprite 引用，立即销毁。
                UnityEngine.Object.Destroy(atlasTexture);
                _effectFrameTextures[resourceId] = frameTextures!;
            }

            _effects[resourceId] = new EffectAsset(frames, document.Loop);
            _effectSpriteSetIdByResource[resourceId] = spriteSetId;
            return true;
        }

        /// <summary>
        /// [ADR-0095] 决策 4：<see cref="ResourceKind.Effect"/> 逐帧动画应使用的像素-单位换算比——
        /// <c>frames.json</c> 显式声明 <see cref="EffectFramesDocument.PixelsPerUnit"/> 时优先用它
        /// （内容侧对特殊画布的动画有出口）；否则 <paramref name="spriteSetId"/> 非 <c>null</c> 且该
        /// 精灵集 anchors.json 声明了合法 <c>pixels_per_unit</c> 时用它（同 ADR-0081 对
        /// <see cref="ResourceKind.Image"/> 的既有规则，复用同一份 <see cref="GetOrReadSpriteSetAnchors"/>
        /// 缓存，不新增磁盘 IO）；两者都没有时回退 <see cref="PixelsPerUnit"/> 全局默认值——
        /// <paramref name="spriteSetId"/> 为 <c>null</c>（vfx 特效图集等不属于任何精灵集的情形）时
        /// 与改动前逐字节一致。
        /// </summary>
        private float ResolveEffectPixelsPerUnit(Id? spriteSetId, EffectFramesDocument document)
        {
            if (document.PixelsPerUnit.HasValue)
            {
                return (float)document.PixelsPerUnit.Value;
            }

            if (!spriteSetId.HasValue)
            {
                return PixelsPerUnit;
            }

            var info = GetOrReadSpriteSetAnchors(AssetRefConventions.SpriteSetDirectory(spriteSetId.Value));
            return info.PixelsPerUnit ?? PixelsPerUnit;
        }

        /// <summary>
        /// [ADR-0095] 决策 3/4：<see cref="ResourceKind.Effect"/> 逐帧动画单帧应使用的精灵枢轴
        /// （归一化 0..1，<c>Sprite.Create</c> 的 <c>pivot</c> 实参语义，原点左下）。优先级：
        /// <list type="number">
        /// <item><c>frames.json</c> 顶层显式声明 <see cref="EffectFramesDocument.Root"/>：直接按
        /// <c>(root.x / 帧宽, 1 - root.y / 帧高)</c> 换算，不涉及方向、不比对精灵集画布（这是内容侧
        /// 对该动画的显式覆盖，越过精灵集归属推导）。</item>
        /// <item><paramref name="spriteSetId"/> 非 <c>null</c> 且该精灵集 anchors.json 声明了脚底
        /// 锚点：方向解析——<paramref name="resourceId"/> 带 <c>__&lt;dir&gt;__</c> 段（同
        /// <see cref="ProbeLayersSequential"/> 产出的逐层剪辑候选 id 形状）时用该方向；不带方向
        /// （整身默认剪辑，见 <see cref="RegisterDefaultClips"/>）时同 ADR-0091 决策 2：该集全部
        /// 方向 root 相同直接用，不同则取"首个方向档位"并记一条 Warn。取得 root 后换算公式同
        /// <see cref="ResolveImagePivot"/>，但宽高比对目标是"帧尺寸"而不是"整张图集纹理尺寸"——
        /// 逐帧动画的每一帧本身才是"这一格该长什么样"的单位，与静态图像的整张纹理是同一语义层级
        /// （决策 3"帧尺寸与 anchors 画布不一致：以帧为准并记 Warn"）。</item>
        /// <item>以上均不满足（不属于任何精灵集、anchors.json 缺失/解析失败/该方向无 root/root
        /// 越界）：回退 <see cref="DefaultPivot"/>，与改动前逐字节一致，vfx 特效图集
        /// （<paramref name="spriteSetId"/> 恒为 <c>null</c>）完全不受影响。</item>
        /// </list>
        /// </summary>
        private Vector2 ResolveEffectPivot(
            Id resourceId, Id? spriteSetId, EffectFramesDocument document, int frameWidth, int frameHeight)
        {
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                // 防御性分支，理论不应发生（TryDecodeEffect 已确认图集解码成功，帧宽高来自图集
                // 尺寸或 frames.json 显式声明，见该方法判断记录）。
                return DefaultPivot;
            }

            if (document.Root.HasValue)
            {
                var explicitRoot = document.Root.Value;
                return new Vector2((float)(explicitRoot.X / frameWidth), 1f - (float)(explicitRoot.Y / frameHeight));
            }

            if (!spriteSetId.HasValue)
            {
                return DefaultPivot;
            }

            var spriteSetRelativeDir = AssetRefConventions.SpriteSetDirectory(spriteSetId.Value);
            var info = GetOrReadSpriteSetAnchors(spriteSetRelativeDir);
            if (info.DirectionRoots == null || info.DirectionRoots.Count == 0)
            {
                return DefaultPivot;
            }

            var directionSlot = ExtractSpriteAnimDirectionSlot(StripCategoryPrefix(resourceId.Value));
            var resolvedDirectionKey = directionSlot;
            var usedFallbackDirection = false;
            if (resolvedDirectionKey == null)
            {
                resolvedDirectionKey = info.FallbackDirectionKey;
                usedFallbackDirection = true;
            }

            if (resolvedDirectionKey == null || !info.DirectionRoots.TryGetValue(resolvedDirectionKey, out var root))
            {
                return DefaultPivot;
            }

            if (usedFallbackDirection && info.FallbackDirectionKeyIsAmbiguous)
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 精灵集 \"{spriteSetRelativeDir}\" 各方向脚底锚点声明不一致，" +
                    $"逐帧动画 \"{resourceId.Value}\" 未携带方向信息，回退使用 \"{resolvedDirectionKey}\" " +
                    "方向的锚点计算枢轴（ADR-0095 决策 3，沿用 ADR-0091 决策 2）。");
            }

            if (root.X < 0 || root.Y < 0 || root.X > frameWidth || root.Y > frameHeight)
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 精灵集 \"{spriteSetRelativeDir}\" 方向 \"{resolvedDirectionKey}\" " +
                    $"声明的脚底锚点 ({root.X}, {root.Y}) 超出逐帧动画 \"{resourceId.Value}\" 帧尺寸 " +
                    $"{frameWidth}x{frameHeight}，回退默认枢轴 (0.5, 0.5)（ADR-0095 决策 3）。");
                return DefaultPivot;
            }

            if (root.CanvasWidth.HasValue && root.CanvasHeight.HasValue &&
                (root.CanvasWidth.Value != frameWidth || root.CanvasHeight.Value != frameHeight))
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 精灵集 \"{spriteSetRelativeDir}\" 方向 \"{resolvedDirectionKey}\" " +
                    $"anchors.json 声明画布 {root.CanvasWidth}x{root.CanvasHeight} 与逐帧动画 " +
                    $"\"{resourceId.Value}\" 帧尺寸 {frameWidth}x{frameHeight} 不一致，按帧尺寸换算枢轴" +
                    "（ADR-0095 决策 3）。");
            }

            return new Vector2((float)(root.X / frameWidth), 1f - (float)(root.Y / frameHeight));
        }

        /// <summary>[ADR-0095] 决策 3：从已去除类别前缀的 <c>sprite_anim</c> 资源名里提取方向档位——
        /// 与 <see cref="TryResolveSpriteSetRelativeDir"/> 对 <c>"layer."</c> 类别的既有判定同一形状：
        /// 按 <c>"__"</c> 分段，恰好 3 段（<c>基础名__方向__层名</c>，见
        /// <see cref="ProbeLayersSequential"/> 产出的候选 id）时取中间段为方向；其余段数（0/1/2 段，
        /// 例如整身默认剪辑 <c>"sample_hero_idle"</c>、无方向的逐层剪辑
        /// <c>"sample_hero_idle__hand_main"</c>）视为"不带方向信息"，调用方按 ADR-0091 决策 2 的
        /// 兜底规则处理。</summary>
        private static string? ExtractSpriteAnimDirectionSlot(string strippedName)
        {
            var parts = strippedName.Split(new[] { "__" }, StringSplitOptions.None);
            return parts.Length == 3 ? parts[1] : null;
        }

        private bool TryDecodeWav(Id resourceId, byte[] bytes)
        {
            if (!WavDecoder.TryDecode(bytes, out var channels, out var sampleRate, out var samples))
            {
                return false;
            }

            var clip = AudioClip.Create(resourceId.Value, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            _audioClips[resourceId] = clip;
            return true;
        }

        /// <summary>供其它 Unity* 引擎适配层实现（渲染/音频）按资源 id 取回已解码对象，
        /// 不属于 IResourceLoader 契约本身，与 adapters/stub 的"测试专用方法"惯例同理，
        /// 这里是"引擎实现之间的内部协作方法"。</summary>
        public bool TryGetSprite(Id resourceId, out Sprite sprite) => _sprites.TryGetValue(resourceId, out sprite!);

        public bool TryGetAudioClip(Id resourceId, out AudioClip clip) => _audioClips.TryGetValue(resourceId, out clip!);

        /// <summary>供 <see cref="Adapter.Unity.EngineAdapter.UnityUISurface"/> 按 <c>fontId</c>
        /// 取回已加载的 <see cref="UnityEngine.Font"/> 资产（缺口 1 已解决，见类型顶部"Font 资源
        /// 种类"判断记录），未加载/找不到时返回 false，调用方自行回退默认字体。</summary>
        public bool TryGetFont(Id resourceId, out Font font) => _fonts.TryGetValue(resourceId, out font!);

        public bool TryGetDataTableText(Id resourceId, out string text) => _dataTableText.TryGetValue(resourceId, out text!);

        public bool TryGetSceneText(Id resourceId, out string text) => _sceneText.TryGetValue(resourceId, out text!);

        public bool TryGetNavMeshText(Id resourceId, out string text) => _navMeshText.TryGetValue(resourceId, out text!);

        /// <summary>供 <see cref="UnityRenderer2D.EmitParticle"/> 按 <c>effectId</c> 取回已解码的
        /// 序列帧特效资产（<see cref="ResourceKind.Effect"/>，未加载/加载失败时返回 false，调用方
        /// 回退播放内建通用效果，见该方法判断记录）。</summary>
        public bool TryGetEffect(Id resourceId, out EffectAsset asset) => _effects.TryGetValue(resourceId, out asset!);

        /// <summary>ADR-0080：供 <see cref="UnityRenderer2D.CreateMapLayerInstance"/> 按地图 id 取回
        /// 已解码的地图分层图资产（<see cref="ResourceKind.MapLayers"/>，未加载/加载失败时返回
        /// false）。</summary>
        public bool TryGetMapLayerAsset(Id mapId, out MapLayerAsset asset) => _mapLayers.TryGetValue(mapId, out asset!);

        /// <summary>W6-B 新增：供 <see cref="UnityRenderer3D.CreateModelInstance"/> 按 <c>modelId</c>
        /// 取回已加载的模型预制体（见类型顶部"W6-B 新增"判断记录）。未加载/找不到时返回 false——
        /// <see cref="UnityRenderer3D.CreateModelInstance"/> 据此回退为 <see cref="TryLoadModelSync"/>
        /// （该方法契约本身是同步的，不能等待 <see cref="LoadAsync"/> 走完 Tick 排队，见其判断记录），
        /// 两条路径共用同一个 <see cref="ResolveModelResourcesPath"/> 约定，互不冲突。</summary>
        public bool TryGetModelPrefab(Id resourceId, out GameObject prefab) => _modelPrefabs.TryGetValue(resourceId, out prefab!);

        /// <summary>12 §5 勘误新增：供 <see cref="UnityRenderer3D.ResolveLegacyClip"/>/
        /// <see cref="Adapter.Unity.Presentation.UnityViewFactory.RegisterModelClipEvents"/> 按
        /// <c>resourceId</c> 取回已加载的动画剪辑（见类型顶部"12 §5 勘误判断记录"）。未加载/找不到时
        /// 返回 false。</summary>
        public bool TryGetAnimationClip(Id resourceId, out AnimationClip clip) => _animationClips.TryGetValue(resourceId, out clip!);

        /// <summary>
        /// 12 §5 勘误新增，与 <see cref="TryLoadModelSync"/> 同一套判断记录（"谁来碰
        /// <c>UnityEngine.Resources</c> 应当固定只有本加载器一处"）：<see cref="UnityRenderer3D.ResolveLegacyClip"/>/
        /// <see cref="Adapter.Unity.Presentation.UnityViewFactory.RegisterModelClipEvents"/> 的调用点
        /// 本身都是同步的（legacy 播放路径与事件登记都不适合等 <see cref="LoadAsync"/> 走完 Tick
        /// 排队），仍然需要一条同步解析路径；命中缓存直接复用，未命中时同步调用一次并写回缓存，与
        /// <see cref="FinishAnimClipLoad"/>（<see cref="LoadAsync"/> 异步路径排队处理后走到的方法）
        /// 共用同一份缓存写入逻辑。
        /// </summary>
        public bool TryLoadAnimationClipSync(Id resourceId, out AnimationClip clip)
        {
            if (_animationClips.TryGetValue(resourceId, out clip!))
            {
                return true;
            }

            var path = ResolveAnimClipResourcesPath(resourceId);
            var loaded = Resources.Load<AnimationClip>(path);
            if (loaded == null)
            {
                clip = null!;
                return false;
            }

            _animationClips[resourceId] = loaded;
            _loaded.Add(resourceId);
            clip = loaded;
            return true;
        }

        /// <summary>
        /// W6-CLIP 新增（AUD-05 根治，取代 <see cref="UnityRenderer3D"/> 此前"把 <c>mesh_ref</c> 当独立
        /// <c>Mesh</c> 直接 <c>Resources.Load&lt;Mesh&gt;</c>"的立场——见类型顶部"W6-CLIP 新增"判断
        /// 记录）：按 <paramref name="resourceId"/>（<c>display.equip_visual.mesh_ref</c>，与
        /// <c>model_ref</c> 同一条 <see cref="ResourceKind.Model"/> 资源合同）与可选的
        /// <paramref name="slotId"/>（提取时优先命中的同名子对象）解析出一个可用于槽位换装的
        /// <see cref="Mesh"/>：
        /// <list type="number">
        /// <item>已加载的模型预制体缓存命中：从预制体层级提取（见 <see cref="TryExtractMeshFromPrefab"/>）。</item>
        /// <item>已提取/已加载的独立网格资源缓存命中：直接复用。</item>
        /// <item>尚未加载：同步尝试解析为模型预制体（<see cref="TryLoadModelSync"/>，与
        /// <see cref="UnityRenderer3D.CreateModelInstance"/> 同步兜底同一惯例）并提取。</item>
        /// <item>预制体解析失败（同一约定路径下没有 GameObject）：尝试直接同步加载为独立
        /// <see cref="Mesh"/> 资产（"若是独立网格资源也可直接使用"）。</item>
        /// </list>
        /// 全部失败时返回 false，调用方（<see cref="UnityRenderer3D.ApplySlotMesh"/>）据此保留当前槽位
        /// 网格并发起异步加载，不在这里记诊断日志/发起加载——本方法只负责"能否解析"，是否降级、要不要
        /// 发起异步加载、加载完成后如何原地替换是调用方职责。
        /// </summary>
        public bool TryGetOrLoadSlotMesh(Id resourceId, Id? slotId, out Mesh mesh)
        {
            var slotKey = slotId?.Value ?? string.Empty;
            var cacheKey = (resourceId, slotKey);
            if (_extractedSlotMeshes.TryGetValue(cacheKey, out mesh!))
            {
                return true;
            }

            if (_modelPrefabs.TryGetValue(resourceId, out var cachedPrefab) &&
                TryExtractMeshFromPrefab(cachedPrefab, slotId, out mesh))
            {
                _extractedSlotMeshes[cacheKey] = mesh;
                return true;
            }

            if (_standaloneMeshes.TryGetValue(resourceId, out mesh!))
            {
                _extractedSlotMeshes[cacheKey] = mesh;
                return true;
            }

            if (TryLoadModelSync(resourceId, out var loadedPrefab))
            {
                if (TryExtractMeshFromPrefab(loadedPrefab, slotId, out mesh))
                {
                    _extractedSlotMeshes[cacheKey] = mesh;
                    return true;
                }
                mesh = null!;
                return false;
            }

            // 见类型顶部"W6-CLIP 新增"判断记录第 4 步：同一约定路径下没有 GameObject，尝试直接当作
            // 独立网格资产解析。
            var path = ResolveModelResourcesPath(resourceId);
            var loadedMesh = Resources.Load<Mesh>(path);
            if (loadedMesh == null)
            {
                mesh = null!;
                return false;
            }

            _standaloneMeshes[resourceId] = loadedMesh;
            _loaded.Add(resourceId);
            _extractedSlotMeshes[cacheKey] = loadedMesh;
            mesh = loadedMesh;
            return true;
        }

        /// <summary>见 <see cref="TryGetOrLoadSlotMesh"/> 判断记录：优先取 <paramref name="slotId"/>
        /// 同名子对象上的网格渲染组件；未指定 <paramref name="slotId"/>、子对象找不到，或子对象不挂
        /// 网格渲染组件时，退回预制体上首个挂网格渲染组件的子对象（深度优先，含根节点自身）。</summary>
        private static bool TryExtractMeshFromPrefab(GameObject prefab, Id? slotId, out Mesh mesh)
        {
            if (slotId.HasValue)
            {
                var slotTransform = FindDeep(prefab.transform, slotId.Value.Value);
                if (slotTransform != null && TryGetRendererMesh(slotTransform, out mesh))
                {
                    return true;
                }
            }

            var skinnedAny = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinnedAny != null && skinnedAny.sharedMesh != null)
            {
                mesh = skinnedAny.sharedMesh;
                return true;
            }

            var filterAny = prefab.GetComponentInChildren<MeshFilter>(true);
            if (filterAny != null && filterAny.sharedMesh != null)
            {
                mesh = filterAny.sharedMesh;
                return true;
            }

            mesh = null!;
            return false;
        }

        private static bool TryGetRendererMesh(Transform transform, out Mesh mesh)
        {
            var skinned = transform.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                mesh = skinned.sharedMesh;
                return true;
            }

            var filter = transform.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                return true;
            }

            mesh = null!;
            return false;
        }

        /// <summary>递归按精确名字查找子物体，惯例同 <see cref="UnityRenderer3D"/> 同名私有方法（本类型
        /// 需要在预制体模板——尚未 Instantiate——上查找，不能复用该实例方法，见
        /// <see cref="TryExtractMeshFromPrefab"/>）。</summary>
        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// 判断记录（PR140-02 文档漂移根治，取代此前"<see cref="UnityRenderer3D.CreateModelInstance"/>
        /// 缓存未命中时自己直接调用 <c>UnityEngine.Resources.Load</c>"的立场——
        /// <c>architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md</c> 决策 1"renderer 只消费
        /// <see cref="IResourceLoader"/> 已加载/占位资源，不隐式加载"）：本方法把"按 modelId 同步解析
        /// 一次 Resources 资产"这件事从 <see cref="UnityRenderer3D"/> 挪进本加载器——<c>CreateModelInstance</c>
        /// 契约本身是同步的，无法像 <see cref="LoadAsync"/> 那样排队等 <see cref="Tick"/>，仍然需要一条
        /// 同步解析路径，但"谁来碰 <c>UnityEngine.Resources</c>"应当固定只有本加载器一处，不是"renderer
        /// 也顺手自己调一次"——这正是决策 1 的字面要求（"renderer 不隐式加载"，不隐式加载不等于"渲染器
        /// 自己不调用 Resources.Load 就算了、缓存旁路可以"，而是"渲染器只应该经由 <see cref="IResourceLoader"/>
        /// 拿资源"）。命中缓存（<see cref="_modelPrefabs"/>，<see cref="LoadAsync"/>/本方法此前已经解析
        /// 成功过的同一个 <paramref name="resourceId"/>）时直接复用，不重复调用 Resources.Load；未命中
        /// 时同步调用一次并写回缓存——与 <see cref="FinishModelLoad"/>（<see cref="LoadAsync"/> 异步路径
        /// 排队处理后走到的方法）共用同一份缓存写入逻辑，本方法与
        /// <see cref="LoadAsync"/>/<see cref="Tick"/> 解析的是完全同一套状态，不是两套互相独立、可能
        /// 产生不同结果的实现。
        /// </summary>
        public bool TryLoadModelSync(Id resourceId, out GameObject prefab)
        {
            if (_modelPrefabs.TryGetValue(resourceId, out prefab!))
            {
                return true;
            }

            var path = ResolveModelResourcesPath(resourceId);
            var loaded = Resources.Load<GameObject>(path);
            if (loaded == null)
            {
                prefab = null!;
                return false;
            }

            _modelPrefabs[resourceId] = loaded;
            _loaded.Add(resourceId);
            prefab = loaded;
            return true;
        }

        /// <summary>W6-B 新增：把 <see cref="ResourceKind.Model"/> 种类资源引用 id 解析为
        /// <c>Resources.Load</c> 可消费的相对路径（不含扩展名，见类型顶部"W6-B 新增"判断记录）。
        /// ADR-0038 适配层接线（判断记录，见类型顶部"ADR-0038 适配层接线"一节）：现直接转发
        /// <see cref="AssetRefConventions.ModelLogicalPath"/>，不再独立维护一份
        /// "GameFoundation/models/&lt;name&gt;" 拼接算法——两者此前逐字节一致（ADR-0037 背景第 3
        /// 点已核实），转发后不产生任何字符串差异，只是消除重复实现本身。</summary>
        public static string ResolveModelResourcesPath(Id resourceId) =>
            AssetRefConventions.ModelLogicalPath(resourceId);

        /// <summary>W6-B 新增：把 model 型 <c>display.anim_set.clips[*].resource_ref</c> 解析为
        /// <c>Resources.Load&lt;AnimationClip&gt;</c> 可消费的相对路径，与 <see cref="ResolveModelResourcesPath"/>
        /// 同一套 <see cref="StripCategoryPrefix"/> 规则、不同子目录（见类型顶部"与 Model 同一套
        /// Resources.Load 约定的姊妹路径"判断记录）。ADR-0038 适配层接线：现直接转发
        /// <see cref="AssetRefConventions.AnimClipLogicalPath"/>，同 <see cref="ResolveModelResourcesPath"/>
        /// 判断记录同一理由。</summary>
        public static string ResolveAnimClipResourcesPath(Id resourceId) =>
            AssetRefConventions.AnimClipLogicalPath(resourceId);

        /// <summary>把资源引用 id 解析为磁盘路径，规则见类型顶部注释。</summary>
        /// <remarks>
        /// U2-1 判断记录（"layer." 类别的嵌套路径特例）：<c>presentation/render/core/
        /// SpriteViewBase.ResolveLayerResourceId</c> 产出的纸娃娃层资源 id 形如
        /// <c>"layer.&lt;spriteSetName&gt;__&lt;directionSlotName&gt;__&lt;layerName&gt;"</c>
        /// （双下划线分隔三段，见该方法判断记录：文件名部分直接复用 14 第 1.2 节命名模板，只在外面
        /// 包一层 <c>"layer."</c> 域前缀）。但 <c>toolchain/gen_placeholder_assets.py</c> 生成的占位
        /// 精灵集实际磁盘布局是"目录按方向/层分层"（<c>sprites/&lt;spriteSet&gt;/&lt;direction&gt;/
        /// &lt;layer&gt;.png</c>，见 <c>data/_sample/README.md</c>"判断记录（占位资产实际文件组织与
        /// 14 第 1.2 节命名模板的差异，如实记录不代为修正）"），不是单一扁平文件名——若按其余
        /// 资源种类的"去掉类别前缀、点号换下划线、直接拼成一个文件名"通用规则处理，会尝试查找一个
        /// 从不存在的扁平文件（如 <c>sprites/placeholder_hero__front__body.png</c>）。任务书明确
        /// 授权"若导入工具的输出布局与 UnityResourceLoader 期望的路径不一致，以 14 §1.2 模板为准
        /// 修正加载器那一侧，不改文档"；本方法据此只对 <c>"layer."</c> 这一个类别加特例：把双下划线
        /// 分隔的三段还原成三级目录，其余类别（<c>sprite.</c>/<c>icon.</c>/<c>data.</c> 等）的既有
        /// 扁平解析规则不变（既有测试 <c>ResolvePath_StripsCategoryPrefixAndUsesKindSubfolder</c>
        /// 之类的用例仍然覆盖非 layer 类别）。
        /// </remarks>
        /// <remarks>
        /// ADR-0038 适配层接线判断记录（"paperdoll" 类别新分支）：<c>display.equip_visual.mesh_ref</c>
        /// 的 sprite 型取值此前（数据迁移前仍是 <c>sprite.*</c> 前缀）没有专门分支，落进下方通用规则
        /// 被当作扁平文件 <c>sprites/&lt;name&gt;.png</c> 解析——与 <see cref="AssetRefConventions.
        /// SpriteSetDirectory"/> 承载的"精灵集目录"语义混淆（该方法产出的是目录，不是扁平文件），
        /// 且从未被任何真实占位资源覆盖过（见 ADR-0038 决策 4"落地期核实结论"，两者语义不等价）。
        /// 数据迁移后该字段改用独立的 <c>paperdoll</c> 前缀，本方法新增专门分支，转发
        /// <see cref="AssetRefConventions.PaperdollLayerFile"/>（<c>paperdoll/&lt;name&gt;.png</c>
        /// 单个扁平文件，与 <c>assets/_sample/paperdoll/&lt;name&gt;.png</c> 同一套命名，见
        /// <c>toolchain/resource_layout_map.json</c> 新增的 <c>paperdoll</c> 映射项），不再落进
        /// 通用规则误当 <c>sprites/</c> 子目录下的文件。
        /// </remarks>
        public static string ResolvePath(Id resourceId, ResourceKind kind)
        {
            if (kind == ResourceKind.Image && IsLayerCategory(resourceId.Value))
            {
                var layerName = StripCategoryPrefix(resourceId.Value);
                var parts = layerName.Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length == 3)
                {
                    return Path.Combine(RootDir, "sprites", parts[0], parts[1], parts[2] + ".png");
                }
                // 段数不是恰好 3 段：不是本判断记录假定的纸娃娃层资源 id 形状，退化为通用规则
                // （下方按扁平文件名解析，大概率找不到文件、按"资源缺失"处理，不抛异常）。
            }

            if (kind == ResourceKind.Image && IsPaperdollCategory(resourceId.Value))
            {
                var relativePath = AssetRefConventions.PaperdollLayerFile(resourceId)
                    .Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(RootDir, relativePath);
            }

            var name = StripCategoryPrefix(resourceId.Value);
            switch (kind)
            {
                case ResourceKind.Image: return Path.Combine(RootDir, "sprites", name + ".png");
                case ResourceKind.Audio: return Path.Combine(RootDir, "audio", name + ".wav");
                case ResourceKind.DataTable: return Path.Combine(RootDir, "data", name + ".json");
                case ResourceKind.Scene: return Path.Combine(RootDir, "scene", name + ".json");
                case ResourceKind.NavMesh: return Path.Combine(RootDir, "nav_mesh", name + ".json");
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    "未知的资源种类（Effect 走 ResolveEffectDir，Font 走 Resources.Load，均不经本方法）");
            }
        }

        /// <summary>解析 <c>ResourceKind.Effect</c> 资源 id 到目录（不是单一文件，见类型顶部
        /// 注释）：<c>GameFoundation/vfx/&lt;资源引用id去掉类别前缀、点号换下划线&gt;/</c>
        /// （<c>vfx.def.resource_ref</c>）或 <c>GameFoundation/sprite_anim/&lt;同上&gt;/</c>
        /// （sprite 型 <c>display.anim_set.clips[*].resource_ref</c>/<c>display.weapon_style.
        /// auto_attack_anim</c>/<c>cast_anim_override</c>，ADR-0038 决策 2 新增前缀），与
        /// <c>assets/_placeholder/vfx/&lt;name&gt;/</c>/<c>assets/_sample/sprite_anim/&lt;name&gt;/</c>
        /// 同一套命名（build.ps1 把两棵目录整体同步到 StreamingAssets 时保持该相对路径不变，见
        /// <c>toolchain/resource_layout_map.json</c> 新增的 <c>sprite_anim</c> 映射项）。
        /// <para>
        /// ADR-0038 适配层接线（判断记录，见类型顶部"ADR-0038 适配层接线"一节）：此前本方法固定按
        /// <c>vfx</c> 子目录拼接，不区分资源引用 id 的类别前缀——数据迁移前 <c>ResourceKind.Effect</c>
        /// 唯一的消费方（<c>vfx.def.resource_ref</c>、sprite 型动画剪辑，后者当时仍用 <c>anim.*</c>
        /// 前缀）实际都落在同一个 <c>vfx/</c> 目录下，固定拼接不产生错误；数据迁移后 sprite 型动画剪辑
        /// 改用 <c>sprite_anim.*</c> 前缀、落在独立的 <c>sprite_anim/</c> 目录，固定拼接会让这一支
        /// 找不到文件。现改为经 <see cref="AssetRefConventions.ResolvePathSpace"/> 按类别前缀分派
        /// 到 <see cref="AssetRefConventions.VfxResourceDir"/>/<see cref="AssetRefConventions.SpriteAnimDir"/>
        /// 取相对路径，不再硬编码单一子目录——遇到既非 <c>vfx</c> 也非 <c>sprite_anim</c> 的类别前缀
        /// （数据/调用方错误，理论不应发生，见 <c>RefCategoryFieldRule</c> 对这两个字段的合法类别集合
        /// 约束）时会经该方法抛出 <see cref="ArgumentException"/>，不再像此前那样静默按 vfx 目录尝试
        /// 一个必然找不到的路径——符合 AGENTS.md"运行时路径不静默降级"的既有规则。
        /// </para></summary>
        public static string ResolveEffectDir(Id resourceId)
        {
            var (_, relativePath) = AssetRefConventions.ResolvePathSpace(resourceId);
            return Path.Combine(RootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool IsLayerCategory(string resourceRefId) => CategoryPrefixOf(resourceRefId) == "layer";

        /// <summary>ADR-0038 适配层接线新增：判断 <c>ResourceKind.Image</c> 资源引用 id 是否为
        /// <c>paperdoll</c> 类别（<c>display.equip_visual.mesh_ref</c> 的 sprite 型取值，ADR-0038
        /// 决策 4 附带条款新增前缀），同 <see cref="IsLayerCategory"/> 同一套"取第一个点分段"判定，
        /// 见 <see cref="ResolvePath"/> 判断记录。</summary>
        private static bool IsPaperdollCategory(string resourceRefId) => CategoryPrefixOf(resourceRefId) == "paperdoll";

        private static string CategoryPrefixOf(string resourceRefId)
        {
            var dotIndex = resourceRefId.IndexOf('.');
            return dotIndex < 0 ? resourceRefId : resourceRefId.Substring(0, dotIndex);
        }

        /// <summary>消费方反馈第 32 条（ADR-0025）：此前本方法独立实现"去掉类别前缀、点号换下划线"
        /// 规则，与 <c>SpriteViewBase</c> 的同名私有方法字节级相同但各自维护；现转发到
        /// <see cref="AssetRefConventions.StripCategoryPrefix"/>，全仓唯一实现见该类型。</summary>
        private static string StripCategoryPrefix(string resourceRefId) =>
            AssetRefConventions.StripCategoryPrefix(resourceRefId);
    }
}

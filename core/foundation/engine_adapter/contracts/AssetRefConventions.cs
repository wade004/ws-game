using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 32 条（[ADR-0025](../../../../architecture/adr/0025-资源引用标识到资产相对路径的约定纳入公开契约.md)）：
    /// 把"资源引用 id → 资产相对路径"这条此前只存在于各处私有实现里的约定（见下方"判断记录（此前的
    /// 重复实现）"）收口为单一公开静态类，供引擎适配层与内容工具共同引用，不再各自维护一份、容易
    /// 漂移的拷贝。
    /// <para>
    /// 覆盖范围：本类型只收口 <c>display.map</c> 的 <c>sprite_set_id</c>/<c>icon_id</c> 两个字段
    /// （04 第 7.1 节，架构文档 14_资产规格书模板.md 第 1.2 节"命名规则"给出的规格书口径）目前
    /// 已有真实消费方（<c>Core.Foundation.EngineAdapter.IResourceLoader</c> 实现、
    /// <c>presentation/render</c>、<c>toolchain/asset_import</c>）在用的两条具体规则；14 第 1.2
    /// 节描述的"通用扁平文件名模板"覆盖更广的资源引用 id 集合（<c>model_ref</c>/<c>resource_ref</c>
    /// 等），本次不展开收口——那些字段目前各自的调用点（<c>UnityResourceLoader.ResolveModelResourcesPath</c>
    /// 等）已各自独立、无重复实现，不构成消费方反馈第 32 条指出的"无公开函数"问题（该问题原文特指
    /// <c>sprite_set_id</c>/<c>icon_id</c> 两个字段，见回复文档）。
    /// </para>
    /// <para>
    /// 判断记录（此前的重复实现，本次收口的对象）：在新增本类型之前，"去掉资源引用 id 的类别前缀
    /// （第一个点分段），剩余部分把点号换成下划线"这条规则在仓库里至少有两处相互独立的私有实现——
    /// <c>Adapter.Unity.EngineAdapter.UnityResourceLoader.StripCategoryPrefix</c>（引擎适配层，消费
    /// 侧）与 <c>Presentation.Render.Core.SpriteViewBase.StripCategoryPrefix</c>（框架表现层，产出
    /// 纸娃娃层资源 id 时用来编码 <c>spriteSetName</c> 片段）；两者字节级相同但各自维护，任何一处
    /// 修改都不会自动同步到另一处。<c>icon_id → 资产相对路径</c>这条规则则完全没有公开实现——
    /// 只存在于 <c>toolchain/asset_import/sprite_cmd.py</c>/<c>icon_cmd.py</c> 两个 Python 脚本里，
    /// C# 侧从未表达过，编辑器等消费方只能照抄 <c>data/_sample</c> 的具体样例反推（见回复文档"现行
    /// 约定原文"一节）。本类型新增后，上述两处 <c>StripCategoryPrefix</c> 私有实现均已改为转发到
    /// <see cref="StripCategoryPrefix"/>，不再各自持有独立副本；Python 侧新增
    /// <c>toolchain/asset_import/ref_conventions.py</c> 按同一规则独立实现（两种语言无法共享同一份
    /// 源码，靠 <c>toolchain/tests/test_ref_conventions.py</c> 与本类型对应的 C# 单元测试用同一组
    /// 样例互相对照，任一侧改动规则而另一侧未同步会被测试捕获，见该测试文件判断记录）。
    /// </para>
    /// </summary>
    /// <summary>
    /// 消费方反馈第 65 条第二批收口：把 <c>vfx.def.resource_ref</c>/<c>sfx.def.resource_ref</c>（含
    /// <c>variants</c>）/<c>display.anim_set.clips.resource_ref</c>（model 型）/
    /// <c>display.equip_visual.mesh_ref</c>/<c>model_ref</c>（model 型）四类字段各自此前分散在
    /// <c>toolchain/asset_import/vfx_cmd.py</c>、<c>toolchain/asset_import/check_cmd.py</c>（sfx 规则）、
    /// <c>Adapter.Unity.EngineAdapter.UnityResourceLoader</c>（<c>ResolveEffectDir</c>/
    /// <c>ResolvePath(Audio)</c>/<c>ResolveModelResourcesPath</c>/<c>ResolveAnimClipResourcesPath</c>）
    /// 的内联路径推导规则，逐一收口进本类型的公开静态方法，与 <see cref="SpriteSetDirectory"/>/
    /// <see cref="IconFile"/> 同一惯例。
    /// <para>
    /// 判断记录（VFX/SFX 与 model/anim_set 两组方法返回值语义不同，均据实收口，不发明新规则）：
    /// <see cref="VfxResourceDir"/>/<see cref="SfxResourceFile"/> 返回的是"相对资产根目录
    /// （<c>toolchain/asset_import</c> 的 <c>--assets-root</c>，默认仓库 <c>assets/</c>）的相对路径"，
    /// 与 <see cref="SpriteSetDirectory"/>/<see cref="IconFile"/> 同一路径空间，可直接与
    /// <c>assets/&lt;dataset&gt;/</c> 拼接后做文件系统存在性检查（见 <c>check_cmd.py</c>）。
    /// <see cref="AnimClipLogicalPath"/>/<see cref="ModelLogicalPath"/> 返回的则是 Unity
    /// <c>UnityEngine.Resources.Load</c> 可消费的"逻辑资源路径"（不含扩展名、不以任何"资产根目录"
    /// 为基准，实际对应 <c>adapters/unity/Assets/Resources/GameFoundation/&lt;子目录&gt;/&lt;name&gt;</c>
    /// 下已被 Unity 资产管线预先导入好的资源，见 <c>UnityResourceLoader.ResolveModelResourcesPath</c>/
    /// <c>ResolveAnimClipResourcesPath</c> 类型顶部"W6-B 新增"判断记录）——与前两者不是同一路径空间，
    /// 不能拼进 <c>assets/&lt;dataset&gt;/</c> 做文件存在性检查（消费方反馈第 66 条回复文档已就此
    /// 单独说明，见该文档"待设计层确认"一节）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="AnimClipLogicalPath"/>/<see cref="ModelLogicalPath"/> 已知覆盖边界，非本次
    /// 引入的新问题——如实记录，不代为修正）：<c>display.anim_set.clips.resource_ref</c> 与
    /// <c>display.equip_visual.mesh_ref</c> 两个字段在运行期实际存在"按消费实体 kind 决定走哪条
    /// 资源规则"的多态——<c>Adapter.Unity.Presentation.UnityViewFactory.RegisterDefaultClips</c>
    /// 对 sprite 型实体按"<c>display.map</c> 行 id 末段"命名约定默认接线同一张 <c>display.anim_set</c>
    /// 表，把 <c>resource_ref</c> 当 <see cref="ResourceKind.Effect"/>（即 <see cref="VfxResourceDir"/>
    /// 同一路径空间）加载，与 model 型经 <c>anim_set_ref</c> 显式字段消费同一张表时把 <c>resource_ref</c>
    /// 当 <c>ResourceKind.AnimationClip</c>（本方法路径空间）加载是两条并存的规则；
    /// <c>Presentation.Render.SpriteViewBase.HandleItemEquipped</c>（该类型判断记录"缺口 10"）同样把
    /// <c>mesh_ref</c> 直接当 <see cref="ResourceKind.Image"/> 资源 id 使用，与 model 型经
    /// <c>UnityResourceLoader.TryGetOrLoadSlotMesh</c> 消费同一字段是另一组并存规则。本类型只收口
    /// 04/09 文档与 <c>EquipVisualDef</c>/<c>AnimSetDef</c> 类型注释描述的 model 型规则（与
    /// <c>ResolveModelResourcesPath</c>/<c>ResolveAnimClipResourcesPath</c> 逐字对应）；sprite 型的
    /// 两条并存规则是表现层既有的"已知简化"（各自类型注释已如此标注），不在本次收口范围，见消费方
    /// 反馈第 65/66 条回复文档"待设计层确认"一节。
    /// </para>
    /// </summary>
    public static class AssetRefConventions
    {
        /// <summary>去掉资源引用 id 的"类别前缀"（第一个点分段，如 <c>sprite.creature.wolf_grey</c>
        /// 的 <c>sprite</c>、<c>icon.item.sample_blade</c> 的 <c>icon</c>），剩余部分把点号换成
        /// 下划线（架构文档 14 第 1.2 节命名模板"资源引用 id 去掉类别前缀，点号换下划线"）。id 不含
        /// 点号时（不满足 04 第 2.1 节 id 格式，理论不应发生）原样返回，不抛异常——与此前两处私有实现
        /// 的既有容错行为一致，调用方按后续解析结果是否合法自行处理。</summary>
        public static string StripCategoryPrefix(string resourceRefId)
        {
            if (resourceRefId == null) throw new ArgumentNullException(nameof(resourceRefId));
            var dotIndex = resourceRefId.IndexOf('.');
            var withoutCategory = dotIndex < 0 ? resourceRefId : resourceRefId.Substring(dotIndex + 1);
            return withoutCategory.Replace('.', '_');
        }

        /// <summary>把 <c>display.map.sprite_set_id</c>（形如 <c>sprite.&lt;category&gt;.&lt;name&gt;</c>，
        /// <c>category</c> 取值见 <c>Core.Foundation.DisplayInfo.DisplaySchemas.Categories</c>）解析为
        /// 该精灵集在资产根目录下的相对目录路径（正斜杠分隔，不以 <c>/</c> 结尾，不含扩展名——
        /// 目录下按方向档位/层名进一步展开，见 14 第 1.2 节"磁盘布局"）：
        /// <c>"sprites/&lt;category&gt;_&lt;name&gt;"</c>。与
        /// <c>toolchain/asset_import/sprite_cmd.py</c> 的 <c>sprite_out_dir</c> 计算逐字节一致
        /// （该脚本判断记录"输出路径：目录名与运行时资源 id 解析规则对齐"）。</summary>
        public static string SpriteSetDirectory(Id spriteSetId) =>
            "sprites/" + StripCategoryPrefix(spriteSetId.Value);

        /// <summary>把 <c>display.map.icon_id</c>（形如 <c>icon.&lt;category&gt;.&lt;name&gt;</c>）
        /// 解析为该图标在资产根目录下的相对文件路径（正斜杠分隔，含 <c>.png</c> 扩展名）：
        /// <c>"icons/&lt;category&gt;/&lt;name&gt;.png"</c>——与
        /// <see cref="SpriteSetDirectory"/> 不同，<c>category</c> 段保留为独立子目录、不与
        /// <c>name</c> 拼接扁平化。与 <c>toolchain/asset_import/icon_cmd.py</c>/<c>sprite_cmd.py</c>
        /// （随精灵集登记的 <c>icon.png</c>）的 <c>icon_out_path</c> 计算逐字节一致（两脚本判断
        /// 记录一致，见回复文档"现行约定原文"一节）；<c>toolchain/import_sample_assets.py</c> 产出的
        /// <c>data/_sample</c> 全部 <c>icon_id</c> 样例（如 <c>icon.creature.sample_beast</c> →
        /// <c>icons/creature/sample_beast.png</c>）已用本方法逐一核对一致，见对应单元测试。</summary>
        /// <exception cref="FormatException"><paramref name="iconId"/> 去掉类别前缀后段数不足 2
        /// （即不满足 <c>icon.&lt;category&gt;.&lt;name&gt;</c> 至少三段的形状），无法拆出
        /// <c>category</c>/<c>name</c> 两部分。</exception>
        public static string IconFile(Id iconId)
        {
            var remainder = StripCategoryPrefixDotted(iconId.Value);
            var dotIndex = remainder.IndexOf('.');
            if (dotIndex < 0)
            {
                throw new FormatException(
                    $"icon_id \"{iconId.Value}\" 去掉类别前缀后剩余 \"{remainder}\" 不含点号，" +
                    "无法拆出 category/name 两段（期望形如 icon.<category>.<name>）");
            }
            var category = remainder.Substring(0, dotIndex);
            var name = remainder.Substring(dotIndex + 1).Replace('.', '_');
            return "icons/" + category + "/" + name + ".png";
        }

        /// <summary>见 <see cref="IconFile"/>：与 <see cref="StripCategoryPrefix"/> 的区别是不把
        /// 剩余部分的点号替换成下划线——<see cref="IconFile"/> 需要先按第一个剩余点号切出
        /// <c>category</c>，不能提前把该分隔符本身也替换掉。</summary>
        private static string StripCategoryPrefixDotted(string resourceRefId)
        {
            var dotIndex = resourceRefId.IndexOf('.');
            return dotIndex < 0 ? resourceRefId : resourceRefId.Substring(dotIndex + 1);
        }

        /// <summary><see cref="SpriteSetDirectory"/> 的反向解析：给定形如
        /// <c>"sprites/&lt;category&gt;_&lt;name&gt;"</c> 的相对目录路径（允许前导/末尾多余的
        /// <c>/</c>），尝试还原出 <c>sprite_set_id</c>。判断记录（有损逆运算的边界）：正向编码把
        /// <c>category</c> 与 <c>name</c> 之间的点号也换成了下划线，与 <c>name</c> 本身可能含有的
        /// 下划线无法从字符串本身区分——本方法假定 <c>category</c> 是 04 已登记域名清单之外的一个
        /// "显示类别"枚举值（<see cref="Core.Foundation.DisplayInfo.DisplaySchemas.Categories"/>：
        /// <c>skill</c>/<c>aura</c>/<c>item</c>/<c>creature</c>/<c>gobj</c>/<c>projectile</c>，均不含
        /// 下划线），按"第一个下划线之前的部分是 category"切分——与
        /// <c>toolchain/asset_import/ref_conventions.py</c> 的
        /// <c>try_parse_sprite_set_id</c> 同一假设、同一切分规则。不满足
        /// <c>"sprites/"</c> 前缀，或去掉前缀后不含下划线（无法切出 category/name 两段）时返回
        /// <c>false</c>。</summary>
        public static bool TryParseSpriteSetId(string relativeDirectory, out Id spriteSetId)
        {
            spriteSetId = default;
            if (string.IsNullOrEmpty(relativeDirectory))
            {
                return false;
            }

            var trimmed = relativeDirectory.Trim('/');
            const string prefix = "sprites/";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var flattened = trimmed.Substring(prefix.Length);
            var underscoreIndex = flattened.IndexOf('_');
            if (underscoreIndex <= 0 || underscoreIndex >= flattened.Length - 1)
            {
                return false;
            }

            var category = flattened.Substring(0, underscoreIndex);
            var name = flattened.Substring(underscoreIndex + 1);
            return Id.TryParse($"sprite.{category}.{name}", out spriteSetId);
        }

        /// <summary><see cref="IconFile"/> 的反向解析：给定形如
        /// <c>"icons/&lt;category&gt;/&lt;name&gt;.png"</c> 的相对文件路径（允许前导 <c>/</c>，
        /// 扩展名大小写不敏感、允许缺省），尝试还原出 <c>icon_id</c>。不满足
        /// <c>"icons/"</c> 前缀、路径段数不是恰好 <c>category/name.ext</c> 两段，或还原出的 id 不满足
        /// <see cref="Id"/> 格式时返回 <c>false</c>。</summary>
        public static bool TryParseIconId(string relativeFilePath, out Id iconId)
        {
            iconId = default;
            if (string.IsNullOrEmpty(relativeFilePath))
            {
                return false;
            }

            var trimmed = relativeFilePath.TrimStart('/');
            const string prefix = "icons/";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var remainder = trimmed.Substring(prefix.Length);
            var segments = remainder.Split('/');
            if (segments.Length != 2 || segments[0].Length == 0 || segments[1].Length == 0)
            {
                return false;
            }

            var category = segments[0];
            var fileName = segments[1];
            var extDot = fileName.LastIndexOf('.');
            var name = extDot < 0 ? fileName : fileName.Substring(0, extDot);
            if (name.Length == 0)
            {
                return false;
            }

            return Id.TryParse($"icon.{category}.{name}", out iconId);
        }

        /// <summary>把 <c>vfx.def.resource_ref</c> 解析为该特效资源在资产根目录下的相对目录路径
        /// （正斜杠分隔，不以 <c>/</c> 结尾）：<c>"vfx/&lt;资源引用id去掉类别前缀，点号换下划线&gt;"</c>
        /// ——目录下固定含 <c>atlas.png</c>/<c>frames.json</c> 两个文件（见
        /// <c>toolchain/asset_import/vfx_cmd.py</c> 落地产物、
        /// <c>UnityResourceLoader.ResolveEffectDir</c> 类型顶部"Scene/NavMesh/Effect 三个种类"判断
        /// 记录）。与 <c>vfx_cmd.py</c> 的 <c>out_dir</c>（该脚本按 <c>flatten_id_segment(strip_domain(id))</c>
        /// 算出目录名、再拼进 <c>resource_ref</c>）、<c>UnityResourceLoader.ResolveEffectDir</c>（对
        /// 已落地的 <c>resource_ref</c> 值做 <see cref="StripCategoryPrefix"/>）三处逐一核对一致——
        /// <c>resource_ref</c> 由工具产出时保证去掉类别前缀后不再含点号（<c>flatten_id_segment</c>
        /// 已把原 id 内的点号换成双下划线），此时 <see cref="StripCategoryPrefix"/> 的"点号换单
        /// 下划线"这一步不会被触发，三处对同一个合法 <c>resource_ref</c> 结果逐字节相同。</summary>
        public static string VfxResourceDir(Id resourceRefId) =>
            "vfx/" + StripCategoryPrefix(resourceRefId.Value);

        /// <summary>把 <c>sfx.def.resource_ref</c>（或 <c>variants</c> 列表内的单个 Id）解析为该音频
        /// 变体在资产根目录下的相对文件路径（正斜杠分隔，含 <c>.wav</c> 扩展名）：
        /// <c>"sfx/&lt;资源引用id去掉类别前缀，点号换下划线&gt;.wav"</c>——扁平文件，非子目录（见
        /// <c>toolchain/asset_import/sfx_cmd.py</c> 落地产物、
        /// <c>UnityResourceLoader.ResolvePath(ResourceKind.Audio)</c>）。判断记录（与
        /// <c>check_cmd.py</c> 既有内联实现 <c>strip_domain(resource_ref) + ".wav"</c> 的差异，
        /// 不影响任何当前可产出数据）：<c>check_cmd.py</c> 此前只去掉域前缀、不替换剩余点号，本方法
        /// 与 <see cref="VfxResourceDir"/> 同一惯例改用 <see cref="StripCategoryPrefix"/>（去掉类别
        /// 前缀后把剩余点号也换成下划线）；<c>sfx_cmd.py</c> 产出的 <c>resource_ref</c> 恒为
        /// <c>"sfx.&lt;flatten_id_segment 结果&gt;_v&lt;N&gt;"</c>，类别前缀之后不再含点号，两种算法
        /// 对其结果逐字节相同——只有手工编写、未经 <c>sfx_cmd.py</c> 产出、且类别前缀之后仍带点号的
        /// <c>resource_ref</c>（不满足工具产出契约的越界输入）才会让两种算法给出不同结果，属已核实的
        /// 边界情形，不影响 <c>_sample</c> 等任何现有数据集，见消费方反馈第 65 条回复文档。</summary>
        public static string SfxResourceFile(Id resourceRefId) =>
            "sfx/" + StripCategoryPrefix(resourceRefId.Value) + ".wav";

        /// <summary>把 model 型 <c>display.anim_set.clips[*].resource_ref</c> 解析为
        /// <c>UnityEngine.Resources.Load&lt;AnimationClip&gt;</c> 可消费的相对路径（不含扩展名，不以
        /// 任何"资产根目录"为基准，见本类型顶部"VFX/SFX 与 model/anim_set 两组方法返回值语义不同"
        /// 判断记录）：<c>"GameFoundation/anim_clips/&lt;资源引用id去掉类别前缀，点号换下划线&gt;"</c>
        /// ——与 <c>UnityResourceLoader.ResolveAnimClipResourcesPath</c> 逐字对应（该方法与
        /// <see cref="ResolveModelResourcesPath"/> 同一套 <see cref="StripCategoryPrefix"/> 规则）。
        /// 仅覆盖 model 型消费该字段时的规则；sprite 型的并存规则见本类型顶部判断记录，不在本方法
        /// 覆盖范围。命名上以 <c>LogicalPath</c> 与 <c>Dir</c>/<c>File</c> 区分两种路径空间——本方法
        /// 与 <see cref="ModelLogicalPath"/> 返回引擎侧已导入的逻辑资源路径，<see cref="VfxResourceDir"/>/
        /// <see cref="SfxResourceFile"/> 返回资产根目录相对路径。</summary>
        public static string AnimClipLogicalPath(Id resourceRefId) =>
            "GameFoundation/anim_clips/" + StripCategoryPrefix(resourceRefId.Value);

        /// <summary>把 <c>display.map.model_ref</c>、model 型 <c>display.equip_visual.mesh_ref</c>/
        /// <c>model_ref</c> 解析为 <c>UnityEngine.Resources.Load&lt;GameObject&gt;</c> 可消费的相对
        /// 路径（不含扩展名，不以任何"资产根目录"为基准，见本类型顶部判断记录）：
        /// <c>"GameFoundation/models/&lt;资源引用id去掉类别前缀，点号换下划线&gt;"</c>——与
        /// <c>UnityResourceLoader.ResolveModelResourcesPath</c> 逐字对应。仅覆盖 model 型消费
        /// <c>mesh_ref</c>/<c>model_ref</c> 时的规则（<c>model_ref</c> 本身只有 model 型一种消费
        /// 路径，见本类型顶部判断记录第二段；<c>mesh_ref</c> 另有 sprite 型并存规则，不在本方法覆盖
        /// 范围）。命名上以 <c>LogicalPath</c> 与 <c>Dir</c>/<c>File</c> 区分两种路径空间——本方法
        /// 与 <see cref="AnimClipLogicalPath"/> 返回引擎侧已导入的逻辑资源路径，<see cref="VfxResourceDir"/>/
        /// <see cref="SfxResourceFile"/> 返回资产根目录相对路径。</summary>
        public static string ModelLogicalPath(Id resourceRefId) =>
            "GameFoundation/models/" + StripCategoryPrefix(resourceRefId.Value);

        /// <summary>
        /// [ADR-0038](../../../../architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md) 决策 2：
        /// 新增类别前缀 <c>sprite_anim</c>，把 sprite 型消费实体的动画帧资源从 <c>anim</c> 前缀
        /// （决策 3：该前缀此后专属 model 型的 <see cref="AnimClipLogicalPath"/>）中拆出，避免同一
        /// 前缀按消费方不同落在两种路径空间。把 <c>sprite_anim.&lt;name&gt;</c> 解析为该动画帧资源
        /// 在资产根目录下的相对目录路径（正斜杠分隔，不以 <c>/</c> 结尾）：
        /// <c>"sprite_anim/&lt;资源引用id去掉类别前缀，点号换下划线&gt;"</c>——目录结构与
        /// <see cref="VfxResourceDir"/> 同构（内含图集与帧数据两个文件，同一套帧动画资产表达方式，
        /// 见该 ADR 决策 2 理由段）。与 <see cref="VfxResourceDir"/>/<see cref="SfxResourceFile"/>
        /// 同一路径空间（资产根相对），可直接拼进 <c>assets/&lt;dataset&gt;/</c> 做文件存在性检查。
        /// </summary>
        public static string SpriteAnimDir(Id resourceRefId) =>
            "sprite_anim/" + StripCategoryPrefix(resourceRefId.Value);

        /// <summary>
        /// [ADR-0038](../../../../architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md) 决策 4
        /// 附带条款落地：<c>display.equip_visual.mesh_ref</c> 的 sprite 型取值此前直接把原始值当
        /// "纸娃娃层资源 id"使用（<see cref="Presentation.Render.SpriteViewBase.HandleItemEquipped"/>，
        /// 见该类型判断记录"缺口 10"），未经任何路径推导函数中转。落地期核实结论（见任务报告"第 0 步
        /// 核实"）：该语义与 <see cref="SpriteSetDirectory"/> 承载的"精灵集目录标识"**不等价**——
        /// <see cref="SpriteSetDirectory"/> 产出一个目录（内部按方向档位/层名进一步展开为多个文件），
        /// 而 sprite 型 <c>mesh_ref</c> 运行期经通用 <c>ResourceKind.Image</c> 非 <c>"layer."</c>
        /// 特例分支解析为单个扁平文件（不按方向拆分，见 <c>SpriteViewBase</c> 判断记录"<c>mesh_ref</c>
        /// 不经方向档位换算"），二者路径空间形状不同（目录 vs 单文件）。按决策 4 附带条款、决策 1
        /// 总原则，新增独立类别前缀 <c>paperdoll</c>（对应架构已有术语"纸娃娃层"，同
        /// <c>display.map.paperdoll_layers</c> 字段命名），不再让 <c>sprite</c> 前缀被迫承担两种
        /// 不同语义。把 <c>paperdoll.&lt;category&gt;.&lt;name&gt;</c> 解析为该纸娃娃层覆盖资源在
        /// 资产根目录下的相对文件路径（正斜杠分隔，含 <c>.png</c> 扩展名）：
        /// <c>"paperdoll/&lt;资源引用id去掉类别前缀，点号换下划线&gt;.png"</c>——单独一个子目录（不与
        /// <see cref="SpriteSetDirectory"/> 共享 <c>sprites/</c> 根，理由同该 ADR 决策 2"长期共享同一
        /// 目录会让两边各自的产出与消费约定互相绑架"），与 <see cref="SfxResourceFile"/> 同一惯例
        /// （资产根相对、扁平单文件、带扩展名）。
        /// <para>
        /// 判断记录（本方法落地时只新增契约，不迁移数据/不改引擎适配层；数据迁移任务已完成后半段）：
        /// 本方法新增时任务范围明确"本任务只做契约、API、校验、工具链，不动数据、不动引擎适配层"，
        /// 当时 <c>data/_sample/display/display.equip_visual.json</c> 现有 <c>mesh_ref:
        /// "sprite.item.sample_hero_hat_test"</c> 一行仍用旧 <c>sprite</c> 前缀。后续数据迁移任务
        /// （见 CHANGELOG 对应条目）已把该行改为 <c>paperdoll.item.sample_hero_hat_test</c>；
        /// <c>SpriteViewBase</c>/<c>UnityResourceLoader</c> 改为实际调用本方法（引擎适配层改为转发）
        /// 仍是后续任务，未随数据迁移一并完成，见 CHANGELOG"未完成/后续任务"小节。
        /// </para>
        /// </summary>
        public static string PaperdollLayerFile(Id resourceRefId) =>
            "paperdoll/" + StripCategoryPrefix(resourceRefId.Value) + ".png";

        /// <summary>
        /// [ADR-0038](../../../../architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md) 决策 5：
        /// 资源引用标识最终落在哪一类磁盘/引擎资源命名空间——供调用方（如内容工具的资源存在性检查、
        /// 资源预览）判断"能不能拼进资产根目录做文件系统检查"，不能与相对路径字符串本身混淆（两个
        /// 空间下都可能产出形状相似的相对路径字符串，但只有 <see cref="AssetRootRelative"/> 能与
        /// 资产导入工具的 <c>--assets-root</c> 拼接后做真实文件存在性检查；<see cref="EngineLogicalPath"/>
        /// 是引擎适配层内部一个单一、非按数据集分区的资源目录树，见 ADR-0037 决策 3）。
        /// </summary>
        public enum AssetRefPathSpace
        {
            /// <summary>相对内容工具 <c>--assets-root</c> 的资产根目录，可与具体数据集目录拼接后做
            /// 文件系统存在性检查（<see cref="SpriteSetDirectory"/>/<see cref="IconFile"/>/
            /// <see cref="VfxResourceDir"/>/<see cref="SfxResourceFile"/>/<see cref="SpriteAnimDir"/>/
            /// <see cref="PaperdollLayerFile"/> 六个方法均属本空间）。</summary>
            AssetRootRelative,

            /// <summary>引擎适配层内部已导入好的逻辑资源路径（<see cref="AnimClipLogicalPath"/>/
            /// <see cref="ModelLogicalPath"/> 两个方法属本空间），不落在资产导入工具的资产根目录下，
            /// 不能做文件系统存在性检查（见 ADR-0037 决策 3）。</summary>
            EngineLogicalPath,
        }

        /// <summary>
        /// [ADR-0038](../../../../architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md) 决策 5：
        /// 公开路由总入口——输入资源引用标识，按其类别前缀（第一个点分段）唯一确定应使用的推导方法，
        /// 输出（路径空间, 相对路径）二元组。分派表见 <see cref="KnownCategories"/>；遇到未登记的
        /// 类别前缀，或标识不含任何点号（无法取出类别前缀），均视为错误，不做静默兜底——错误信息
        /// 同时给出收到的前缀与合法前缀集合，便于调用方定位内容笔误。
        /// <para>
        /// 判断记录（Python 侧独立实现，不共享源码）：
        /// <c>toolchain/asset_import/ref_conventions.py</c> 的 <c>resolve_path_space</c> 是本方法的
        /// Python 对应实现，两侧各自独立维护，靠同一组输入/期望值对照测试互相校核（沿用 ADR-0037
        /// 决策 1 确立的跨语言对照惯例，见 <c>toolchain/tests/test_ref_conventions.py</c>/
        /// <c>AssetRefConventionsTests.cs</c>）。
        /// </para>
        /// </summary>
        public static (AssetRefPathSpace Space, string RelativePath) ResolvePathSpace(Id resourceRefId)
        {
            var value = resourceRefId.Value;
            var dotIndex = value.IndexOf('.');
            if (dotIndex < 0)
            {
                throw new ArgumentException(
                    $"资源引用 \"{value}\" 不含类别前缀（无点号），合法类别前缀集合：{string.Join("/", KnownCategories)}",
                    nameof(resourceRefId));
            }

            var category = value.Substring(0, dotIndex);
            switch (category)
            {
                case "sprite":
                    return (AssetRefPathSpace.AssetRootRelative, SpriteSetDirectory(resourceRefId));
                case "icon":
                    return (AssetRefPathSpace.AssetRootRelative, IconFile(resourceRefId));
                case "vfx":
                    return (AssetRefPathSpace.AssetRootRelative, VfxResourceDir(resourceRefId));
                case "sfx":
                    return (AssetRefPathSpace.AssetRootRelative, SfxResourceFile(resourceRefId));
                case "sprite_anim":
                    return (AssetRefPathSpace.AssetRootRelative, SpriteAnimDir(resourceRefId));
                case "paperdoll":
                    return (AssetRefPathSpace.AssetRootRelative, PaperdollLayerFile(resourceRefId));
                case "anim":
                    return (AssetRefPathSpace.EngineLogicalPath, AnimClipLogicalPath(resourceRefId));
                case "model":
                    return (AssetRefPathSpace.EngineLogicalPath, ModelLogicalPath(resourceRefId));
                default:
                    throw new ArgumentException(
                        $"资源引用 \"{value}\" 的类别前缀 \"{category}\" 不合法，合法类别前缀集合：{string.Join("/", KnownCategories)}",
                        nameof(resourceRefId));
            }
        }

        /// <summary>见 <see cref="ResolvePathSpace"/>：全部已登记的合法类别前缀，供该方法与校验规则
        /// 的错误信息使用（单一来源，不在多处复制同一份字面量清单）。顺序与 <see cref="ResolvePathSpace"/>
        /// 的 <c>switch</c> 分支一致，供人类阅读；不保证其它场景下的顺序稳定性。</summary>
        public static readonly IReadOnlyList<string> KnownCategories = new[]
        {
            "sprite", "icon", "vfx", "sfx", "sprite_anim", "paperdoll", "anim", "model",
        };

        /// <summary>
        /// 消费方反馈第 77 条（[ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)）：
        /// <see cref="VfxResourceDir"/> 目录下固定含 <c>atlas.png</c>（图集，正斜杠分隔文件路径）——
        /// 该文件名此前只写在 <c>toolchain/asset_import/vfx_cmd.py</c> 模块文档字符串与
        /// <c>UnityResourceLoader.ResolveEffectDir</c> 类型注释里，从未提升为可编译期引用的方法。
        /// 与 <see cref="SpriteAnimAtlasFile"/> 同一惯例（两个类别前缀共用同一套"目录+atlas.png+
        /// frames.json"磁盘布局，见 <see cref="SpriteAnimDir"/> 类型注释"目录结构与
        /// <see cref="VfxResourceDir"/> 同构"）。</summary>
        public static string VfxAtlasFile(Id resourceRefId) => VfxResourceDir(resourceRefId) + "/atlas.png";

        /// <summary>
        /// [ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)：
        /// <see cref="VfxResourceDir"/> 目录下固定含 <c>frames.json</c>（序列帧数据，正斜杠分隔文件
        /// 路径）——结构见 <see cref="EffectFramesDocument"/>，与运行时
        /// <c>Adapter.Unity.EngineAdapter.UnityResourceLoader.TryDecodeEffect</c> 消费的文件同一份。</summary>
        public static string VfxFramesFile(Id resourceRefId) => VfxResourceDir(resourceRefId) + "/frames.json";

        /// <summary>
        /// [ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)：
        /// <see cref="SpriteAnimDir"/> 目录下固定含 <c>atlas.png</c>，与 <see cref="VfxAtlasFile"/>
        /// 同一磁盘布局（两个类别前缀共用同一套打包实现，见 <c>toolchain/asset_import/atlas.py</c>
        /// <c>pack_atlas</c>）。</summary>
        public static string SpriteAnimAtlasFile(Id resourceRefId) => SpriteAnimDir(resourceRefId) + "/atlas.png";

        /// <summary>
        /// [ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)：
        /// <see cref="SpriteAnimDir"/> 目录下固定含 <c>frames.json</c>，结构与 <see cref="VfxFramesFile"/>
        /// 完全相同（见 <see cref="EffectFramesDocument"/>），两个类别前缀在运行时经同一个
        /// <c>ResourceKind.Effect</c> 加载路径消费（<c>UnityResourceLoader.ResolveEffectDir</c>
        /// 按类别前缀分派目录，之后走同一套 <c>TryDecodeEffect</c> 解码逻辑）。</summary>
        public static string SpriteAnimFramesFile(Id resourceRefId) => SpriteAnimDir(resourceRefId) + "/frames.json";

        /// <summary>
        /// [ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)：
        /// <see cref="SpriteSetDirectory"/> 目录下固定含 <c>atlas.png</c>（正斜杠分隔文件路径，见
        /// <c>toolchain/asset_import/sprite_cmd.py</c> 落地产物）。
        /// <para>
        /// 判断记录（不新增精灵集的 <c>frames.json</c> 对应方法——两类资源目录内文件名形似但结构不同，
        /// 不应被误当同一契约）：精灵集目录内与 <c>atlas.png</c> 配套的索引文件是 <c>atlas.json</c>
        /// （<c>{"frames": {"&lt;帧名&gt;": {x,y,w,h}}}</c>，按方向档位/层名分帧、不含
        /// <c>fps</c>/<c>loop</c>/<c>duration</c>），不是 <see cref="VfxFramesFile"/>/
        /// <see cref="SpriteAnimFramesFile"/> 的 <c>frames.json</c> 结构（按序列帧顺序排列、含播放
        /// 时序字段，见 <see cref="EffectFramesDocument"/>）；且 <c>atlas.json</c>/<c>atlas.png</c>
        /// 目前只被内容工具自身（<c>check_cmd.py</c> 存在性校验）消费，运行时精灵渲染走的是同目录下
        /// 按方向档位/层名展开的独立扁平文件（见 <see cref="SpriteSetDirectory"/> 类型注释"该方法
        /// 产出的是目录，内部按方向档位/层名进一步展开为多个文件"），不经 <c>atlas.png</c>/
        /// <c>atlas.json</c>。消费方反馈第 77 条原文与既有代码均未要求为精灵集新增序列帧读取契约，
        /// 本次只补 <c>atlas.png</c> 文件名这一项，不臆造 <c>atlas.json</c> 对应方法或复用
        /// <see cref="EffectFramesDocument"/>。
        /// </para>
        /// </summary>
        public static string SpriteSetAtlasFile(Id spriteSetId) => SpriteSetDirectory(spriteSetId) + "/atlas.png";

        /// <summary>
        /// 消费方反馈第 75 条（[ADR-0053](../../../../architecture/adr/0053-地图分层图路径约定纳入公开契约.md)）：
        /// 把 <c>world.map</c> 行的 <c>id</c>（形如 <c>world.&lt;map&gt;</c>，见
        /// <c>toolchain/asset_import/map_cmd.py</c> 的 <c>map_id = f"world.{args.map}"</c>）解析为
        /// 该地图分层图在资产根目录下的相对目录路径（正斜杠分隔，不以 <c>/</c> 结尾，不含扩展名——
        /// 目录下按层名进一步展开为最多四个文件，见 <see cref="MapGroundFile"/> 等四个方法）：
        /// <c>"maps/&lt;map&gt;"</c>——与 <see cref="SpriteSetDirectory"/>/<see cref="VfxResourceDir"/>
        /// 等六个既有方法同一路径空间（<see cref="AssetRefPathSpace.AssetRootRelative"/>，可直接与
        /// <c>assets/&lt;dataset&gt;/</c> 拼接后做文件系统存在性检查），同一"去掉类别前缀、点号换
        /// 下划线"推导规则。与 <c>map_cmd.py</c> 的 <c>out_dir</c> 计算（去掉 <c>assets_root</c>/
        /// <c>dataset</c> 两段前缀后剩余部分）逐字节一致。
        /// <para>
        /// 判断记录（已知边界，不影响任何现有数据，同 <see cref="SfxResourceFile"/> 类型注释同类
        /// 判断记录的记法）：<c>map_cmd.py</c> 的 <c>out_dir</c> 直接拼接 <c>args.map</c> 原始字符串
        /// 作为单一目录段，不做任何转义；本方法则先经 <see cref="StripCategoryPrefix"/> 把
        /// <c>world.</c> 之后剩余部分的点号也换成下划线。二者仅在 <c>args.map</c> 本身含点号时
        /// （即地图名写成多段 id 形状，如 <c>zone.dungeon</c>）才会给出不同结果；仓库内目前全部
        /// 地图名（`--map` 取值、<c>data/_sample</c>/<c>data/_framework</c> 两套 <c>world.map</c>
        /// 数据行的 id）均为不含点号的单段名称，两种算法结果逐字节相同。
        /// </para>
        /// </summary>
        public static string MapDirectory(Id mapId) =>
            "maps/" + StripCategoryPrefix(mapId.Value);

        /// <summary>
        /// [ADR-0053](../../../../architecture/adr/0053-地图分层图路径约定纳入公开契约.md)：地面层
        /// （框架固定项，必需，见 14 第 9 节"场景与地图"）在资产根目录下的相对文件路径（正斜杠分隔，
        /// 含 <c>.png</c> 扩展名）：<c>"maps/&lt;map&gt;/ground.png"</c>，与
        /// <c>map_cmd.py</c> 落地产物逐字节一致。</summary>
        public static string MapGroundFile(Id mapId) => MapDirectory(mapId) + "/ground.png";

        /// <summary>
        /// [ADR-0053](../../../../architecture/adr/0053-地图分层图路径约定纳入公开契约.md)：前景
        /// 遮挡层（框架固定项，必需，遮挡单位的近景建筑/树冠等）在资产根目录下的相对文件路径：
        /// <c>"maps/&lt;map&gt;/overlay.png"</c>，与 <c>map_cmd.py</c> 落地产物逐字节一致。</summary>
        public static string MapOverlayFile(Id mapId) => MapDirectory(mapId) + "/overlay.png";

        /// <summary>
        /// [ADR-0053](../../../../architecture/adr/0053-地图分层图路径约定纳入公开契约.md)：装饰层
        /// （可选，不遮挡单位的贴花/痕迹，14 第 9.1 节"装饰层"）在资产根目录下的相对文件路径：
        /// <c>"maps/&lt;map&gt;/decal.png"</c>，与 <c>map_cmd.py</c> 落地产物逐字节一致。</summary>
        public static string MapDecalFile(Id mapId) => MapDirectory(mapId) + "/decal.png";

        /// <summary>
        /// [ADR-0053](../../../../architecture/adr/0053-地图分层图路径约定纳入公开契约.md)：导航
        /// 标注参考图（可选，供引擎适配层侧手工绘制导航数据时参考，不是 14 第 9.1 节列出的可视资产
        /// 层本身）在资产根目录下的相对文件路径：<c>"maps/&lt;map&gt;/nav_hint.png"</c>，与
        /// <c>map_cmd.py</c> 落地产物逐字节一致。</summary>
        public static string MapNavHintFile(Id mapId) => MapDirectory(mapId) + "/nav_hint.png";
    }
}

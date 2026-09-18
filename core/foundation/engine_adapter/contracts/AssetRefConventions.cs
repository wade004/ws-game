using System;
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
    }
}

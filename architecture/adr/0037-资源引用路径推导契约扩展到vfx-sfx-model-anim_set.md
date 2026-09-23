# ADR-0037 资源引用路径推导契约扩展到 vfx/sfx/model/anim_set

状态：已拍板 2026-09-18（决策 2 部分被 [ADR-0038](0038-资源引用类别前缀唯一决定路径空间.md) 取代——
sprite 型并存规则改由独立类别前缀承载各自的路径空间，不再是"是否收口为与 model 型平级契约"的
待裁决并存规则，详见 ADR-0038 决策 7；决策 2"sprite 型两处实现自称已知简化/未决，留给消费方反馈
第 66 条回复文档待设计层确认"这一登记，其中 `display.equip_visual.mesh_ref` 一项已由
[ADR-0071](0071-装备纸娃娃层随朝向换图.md) 裁决、`display.anim_set.clips`/`display.weapon_style`
sprite 型剪辑一项已由 [ADR-0072](0072-纸娃娃层逐层播放剪辑.md) 裁决，均不再是待裁决状态；本 ADR
其余决策不变）

## 背景

消费方反馈第 65 条（2026-09-18，`docs/上游反馈-待转发-2026-09-18.md` 第 112～124 行）：[ADR-0025]
(0025-资源引用标识到资产相对路径的约定纳入公开契约.md) 只收口了 `sprite_set_id`/`icon_id` 两个
字段的"资源引用标识 → 资产相对路径"公开契约，`vfx.def.resource_ref`、`sfx.def.resource_ref`
（含 `variants`）、`display.anim_set.clips.resource_ref`、`display.equip_visual.mesh_ref`/
`model_ref` 均无对应公开推导入口，各消费点各自独立实现，未收口成公开契约。ADR-0025 决策 5 本就
明确把这一类字段列为"后续若发现同类重复实现问题，按同一模式（新增 ADR + 收口为公开契约）逐个
处理"的待办，本次即是该待办的落地。

核实现行实现（见回复文档"现行约定原文"一节，逐处引用源码判断记录）后确认：

1. **vfx**：内容导入工具链的产出入口与校验入口、已落地引擎适配层实现的资源加载入口三处对合法
   `resource_ref`（工具产出、类别前缀之后不再含点号）结果逐字节一致：`"vfx/<资源引用id去掉类别
   前缀，点号换下划线>"`（相对资产导入工具的资产根目录的一个目录，内含图集与帧数据两个文件）。
2. **sfx**：内容导入工具链的产出入口、已落地引擎适配层实现的资源加载入口，与本次改动前校验入口
   的内联实现三处对合法 `resource_ref` 结果同样逐字节一致：`"sfx/<资源引用id去掉类别前缀，点号
   换下划线>.wav"`（扁平文件）。校验入口此前的内联实现只去掉域前缀、不替换剩余点号，与产出入口/
   加载入口统一采用的"去掉类别前缀、点号换下划线"字面算法不同——对当前所有可产出数据
   （`resource_ref` 恒由产出入口按既有归一化规则生成、类别前缀之后不再含点号）两种算法结果相同，
   只有手工编写、不满足工具产出契约（类别前缀之后仍带点号）的越界输入才会分歧，属已核实、不影响
   现有数据集的边界情形。
3. **model/anim_set（`display.equip_visual.mesh_ref`/`model_ref`、`display.anim_set.clips.
   resource_ref`）**：已落地引擎适配层实现已经有这两个字段唯一的运行期加载路径推导实现（分别
   对应"模型逻辑路径"/"动画片段逻辑路径"两个模板，均为引擎侧已导入逻辑资源路径，不含扩展名、
   不以资产导入工具的资产根目录为基准——这两类资源由引擎适配层内部一个编辑器脚本直接生成进其
   自身的资源目录树，不经过内容导入工具链的任何子命令，不落在资产导入工具的资产根目录下）。这两
   个已落地实现此前从未被公开契约收口，消费方只能照抄源码或反推样例数据集。

同时核实到一处此前未被反馈原文与既有文档记录的架构性事实：**`display.anim_set.clips.
resource_ref` 与 `display.weapon_style.auto_attack_anim`/`cast_anim_override` 三个字段在运行期
实际按"消费实体的 `display.map.kind`"存在两条并存的加载规则**——model 型经 `display.map.
anim_set_ref` 显式字段消费同一张 `display.anim_set` 表时，`resource_ref` 按本 ADR 收口的"引擎侧
已导入逻辑资源路径"规则加载；sprite 型则经引擎适配层表现层实现里一处"按 `display.map` 行 id
末段命名"的默认接线约定消费同一张表，把 `resource_ref` 当成与 `VfxResourceDir` 同一路径空间
（相对资产导入工具的资产根目录）的资源加载——两条规则均是各自实现类型注释里已经如实标注的既有
实现，不是本次新引入的问题。`display.equip_visual.mesh_ref` 同样存在并存规则：框架表现层一处
实现（该类型注释自称"缺口 10"）对 sprite 型实体把 `mesh_ref` 直接当图片类资源 id 使用，与 model
型经已落地引擎适配层实现消费同一字段并存。`model_ref` 没有类似的 sprite 型并存规则（sprite 型
表现层不处理装备槽挂载显示模式）。

## 决策

1. **新增四个公开静态方法**，放在框架契约面里全部消费方都能引用到的一个公共位置（内容导入工具
   链侧同步新增等价函数，两种运行环境之间不共享同一份源码，只能各自实现、互相对照），与
   `SpriteSetDirectory`/`IconFile` 同一收口方式：
   - `VfxResourceDir(Id resourceRefId)` → `"vfx/<name>"`（相对资产根目录的目录，收口
     `vfx.def.resource_ref`）。
   - `SfxResourceFile(Id resourceRefId)` → `"sfx/<name>.wav"`（相对资产根目录的文件，收口
     `sfx.def.resource_ref`/`variants`）。
   - `AnimClipLogicalPath(Id resourceRefId)` → 引擎侧已导入逻辑资源路径模板（收口 model 型
     `display.anim_set.clips.resource_ref`）。
   - `ModelLogicalPath(Id resourceRefId)` → 引擎侧已导入逻辑资源路径模板（收口 `display.map.
     model_ref`、model 型 `display.equip_visual.mesh_ref`/`model_ref`）。
   四者均复用既有"去掉类别前缀"规则求 `<name>`，与背景第 1/2/3 点核实的现行实现逐字节对应，不
   发明新规则。命名上以 `LogicalPath` 与 `Dir`/`File` 区分两种路径空间——返回"引擎侧已导入逻辑
   资源路径"的两个方法用 `LogicalPath` 后缀，返回"资产根目录相对路径"的两个方法沿用 `Dir`/`File`
   后缀，避免调用方仅凭方法名混淆两种不能互换使用的路径语义。
2. **只收口 model 型规则，sprite 型并存规则本次不纳入**：`AnimClipLogicalPath` 只承诺 model 型
   消费 `display.anim_set.clips.resource_ref` 时的路径；`ModelLogicalPath` 对 `mesh_ref` 只承诺
   model 型消费时的路径。sprite 型的两条并存规则（背景一节末段所述）是表现层既有的、各自实现类型注释
   已标注为"已知简化"的局部实现细节，是否要把它们也提升为同等地位的公开契约、以及如何在数据层面
   区分"这一行该按哪条规则消费"，留给消费方反馈第 66 条回复文档"待设计层确认"一节，本 ADR 不代为
   裁决。
3. **内容导入工具链的资产存在性校验不据此扩展到这四个字段**：`VfxResourceDir`/`SfxResourceFile`
   返回的相对路径与校验入口现有的资产根目录语义兼容，但 `AnimClipLogicalPath`/`ModelLogicalPath`
   返回的是引擎侧已导入逻辑资源路径，指向的资源实际落在引擎适配层内部一个单一、非按数据集分区
   的资源目录树里，不在校验入口的资产根目录检查域内；且 `display.equip_visual.mesh_ref`/
   `display.anim_set.clips.resource_ref`/`display.weapon_style.auto_attack_anim`/
   `cast_anim_override` 存在决策 2 所述的 sprite/model 并存规则，校验入口单看数据表本身无法确定
   某一行该按哪条规则核对，勉强实现会产生假阳性/假阴性。本次不新增这一域的检查，详见消费方反馈
   第 66 条回复文档。
4. **已落地引擎适配层实现暂不改为转发本次新增方法**：与 `SpriteSetDirectory` 落地时（ADR-0025）
   不同，本次未在本机跑通引擎侧批处理编译/测试验证转发改动，保持引擎适配层代码不动，列为后续
   项——两处实现目前逐字节一致（背景第 3 点已核实），不转发不产生行为分歧，只是多一份未来需要
   同步维护的拷贝，风险与 ADR-0025 落地前重复实现问题同构，留给后续任务处理。

## 后果

### 正面

- 消费方（内容编辑器项目的资源试听等功能）获得四个字段的权威路径推导来源，不再需要自建临时替代
  实现反推固定拼接规则。
- 内容导入工具链多处内联路径拼接改为调用同一份共享实现，消除潜在漂移；框架契约面与内容导入工具
  链两侧各自的自动化测试用同一组输入/期望值表互相对照。
- 如实记录并公开了 `anim_set`/`mesh_ref` 两个字段此前从未被任何文档明确指出的 sprite/model 并存
  规则，为后续设计层裁决提供了核实过的事实基础，不再是隐性状态。

### 负面

- `AnimClipLogicalPath`/`ModelLogicalPath` 与 `VfxResourceDir`/`SfxResourceFile` 返回值不是同一
  路径空间（前者是引擎侧已导入逻辑资源路径，后者是资产根目录相对路径），调用方需要仔细区分，误用
  会产生看似合理但实际找不到文件的路径字符串——已在两侧方法的文档注释与本 ADR 决策 3 明确标注，
  并在决策 1 用 `LogicalPath`/`Dir`/`File` 命名后缀从名字上加以区分。
- sprite 型并存规则本次未收口，消费方若误以为 `AnimClipLogicalPath`/`ModelLogicalPath` 覆盖全部
  消费场景，对 sprite 型实体套用会得到错误路径；已在文档注释与本 ADR 决策 2 显式标注边界。

## 备选方案与为什么不选

- **一并把 sprite 型并存规则也收口为公开方法（如再新增覆盖 sprite 型的对应方法）**：sprite 型
  规则目前只由表现层两处实现以"默认接线约定"的形式体现，04/09 文档从未把它写成 `display.
  anim_set`/`display.equip_visual` 字段本身的正式语义（两处实现的类型注释均自称"已知简化"/
  "缺口"），贸然收口成与 model 型平级的公开契约，相当于在没有设计层确认的情况下，把一个局部实现
  简化直接提升为架构承诺——按 AGENTS.md"不擅自拍板设计冲突"的要求，留给消费方反馈第 65/66 条
  回复文档"待设计层确认"一节。
- **校验入口仍然实现这四个字段的文件存在性检查，只是对 model 型场景跳过、只查 sprite 型场景**：
  校验入口无法从 `display.anim_set`/`display.equip_visual`/`display.weapon_style` 任一行本身
  确定性地判断该行只会被 sprite 型消费而不会同时/未来被 model 型消费（如某条 `display.anim_set`
  样例记录当前未被任何 `anim_set_ref` 引用，不能保证永远如此）；实现一个"启发式判断+文件检查"
  的组合，风险是把一个未决的设计问题（决策 2）用代码悄悄裁决掉，比不实现更容易误导消费方。

## 魔兽世界的对应做法

无直接对应（客户端资源路径推导属魔兽世界客户端内部实现细节，未公开也无权威资料）。

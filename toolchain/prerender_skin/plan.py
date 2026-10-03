"""渲染计划：把渲染配置展开成"要渲染哪些剪辑、每帧取样时刻、产出哪些目录"，以及规格文件。

时间轴的唯一来源是既有的 sprite 版假人姿势集（``std_dummy_poses``）：键清单、相位、每相帧数（``max(1, floor(ms*fps/1000+0.5))``）、
帧起点/取样时刻（``build.frame_times``）、事件（``build.clip_events``）全部复用，本模块不持有第二份时间规则，
所以序列帧的帧数、帧时长、事件时刻与 sprite 版假人逐键逐位一致（04 第 5 节命名事件、W5/W6 命中对齐修复由此得以保持）。
引擎侧的标准 model 型剪辑用同一批帧边界做关键帧，渲染按同一取样时刻求值。
"""

from __future__ import annotations

from dataclasses import dataclass, field

from std_dummy_model_clips import config as MC
from std_dummy_poses import build as SB
from std_dummy_poses import config as SC

from .config import BODY_LAYER, RenderConfig

SPEC_SUFFIX = ".prerender.json"
DEFAULT_CLIPS_RESOURCES_DIR = "GameFoundation/anim_clips"


@dataclass(frozen=True)
class ClipPlan:
    """一份要渲染的剪辑资源（别名键不单列，折叠到目标键）。"""

    key: str                      # 姿势键（标准 04 §2.1 语法）
    mass: str | None              # 体量档；主集 None
    stem: str                     # 输出资源名（目录名前缀）：<stem_prefix>[<体量>_]<键点号换下划线>
    source_clip: str              # 引擎侧标准剪辑资产名（std_dummy_[<体量>_]<键点号换下划线>）
    loop: bool
    frame_count: int
    frames_per_phase: tuple
    total_ms: int
    sample_ms: tuple              # 每帧取样时刻（循环剪辑取帧起点，非循环取帧中点，与 sprite 版同）
    durations_s: tuple            # 每帧时长（秒，6 位小数，与 sprite 版 frames.json 同写法）
    events: tuple                 # 数据行事件 [{"name", "time_pct"}]

    @property
    def resource_ref(self) -> str:
        return "sprite_anim." + self.stem


@dataclass
class RenderPlan:
    config: RenderConfig
    clips: list[ClipPlan] = field(default_factory=list)         # 要渲染的资源（主集在前，体量组随后）
    rows: dict = field(default_factory=dict)                    # 数据行用：{(mass|None): {键: ClipPlan}}（含别名键）

    def total_frames(self) -> int:
        return sum(c.frame_count for c in self.clips)

    def variants(self) -> list[str]:
        """每个 剪辑×方向 要渲染的层变体：all（整身合成）；有装备层时再加 body 与各装备层。"""
        cfg = self.config
        return ["all"] + ([BODY_LAYER] + cfg.layer_names if cfg.layers else [])

    def render_count(self) -> int:
        """引擎侧要出的图像总数（帧 × 方向 × 层变体）。"""
        return self.total_frames() * len(self.config.effective_slots()) * len(self.variants())


def _stem(cfg: RenderConfig, key: str, mass: str | None) -> str:
    return cfg.stem_prefix + (f"{mass}_" if mass else "") + key.replace(".", "_")


def clip_plan(cfg: RenderConfig, c: SC.ClipDef) -> ClipPlan:
    """一条非别名剪辑定义的渲染计划。"""
    plan = SC.phase_frame_plan(c, cfg.fps)
    times = SB.frame_times(c, cfg.fps)
    return ClipPlan(
        key=c.key, mass=c.mass, stem=_stem(cfg, c.key, c.mass),
        source_clip=MC.clip_state_name(c.key, c.mass), loop=c.loop,
        frame_count=sum(n for _name, n, _d in plan),
        frames_per_phase=tuple(n for _name, n, _d in plan),
        total_ms=c.total_ms,
        sample_ms=tuple(round(s, 6) for _t0, _d, s in times),
        durations_s=tuple(round(d / 1000.0, 6) for _t0, d, _s in times),
        events=tuple(SB.clip_events(c)),
    )


def build_plan(cfg: RenderConfig) -> RenderPlan:
    """展开渲染计划。键集合 = 配置的键 ∪ 它们的别名目标；每个体量组（配置的 mass_groups）同一键集合。"""
    selected = set(cfg.keys)
    tiers: list[str | None] = [None] + list(cfg.mass_groups)
    plan = RenderPlan(config=cfg)
    for mass in tiers:
        defs = SC.build_clip_defs() if mass is None else SC.mass_clip_defs(mass)
        by_key = {c.key: c for c in defs}
        rows: dict[str, tuple[ClipPlan, SC.ClipDef]] = {}
        need: dict[str, ClipPlan] = {}
        for c in defs:
            if c.key not in selected:
                continue
            target = by_key[c.alias_of] if c.alias_of else c
            if target.key not in need:
                need[target.key] = clip_plan(cfg, target)
            rows[c.key] = (need[target.key], c)
        # 渲染顺序 = 标准键清单顺序（确定性）
        order = [c.key for c in defs]
        plan.clips += [need[k] for k in order if k in need]
        plan.rows[mass] = rows
    return plan


def engine_clip_names(plan: RenderPlan) -> list[str]:
    return [c.source_clip for c in plan.clips]


def row_entries(plan: RenderPlan, mass: str | None) -> dict:
    """某体量档（None = 主集）的数据行剪辑表 {键: {resource_ref, events}}；键顺序 = 标准键清单顺序。别名键指向目标资源。"""
    return {key: {"resource_ref": cp.resource_ref, "events": [dict(e) for e in cp.events]}
            for key, (cp, _c) in plan.rows[mass].items()}


def anim_set_rows(plan: RenderPlan) -> list[dict]:
    """要并入 display.anim_set.json 的行：主集一行；有体量组时再加中体量空覆盖行与各档覆盖行（与 sprite 版假人同形，04 §7）。"""
    cfg = plan.config
    main_row = {"id": cfg.anim_set_id, "clips": row_entries(plan, None)}
    if cfg.pose_standard:
        main_row["pose_standard"] = True
    rows = [main_row]
    if cfg.mass_groups:
        rows.append({"id": SC.mass_anim_set_id(cfg.anim_set_id, SC.MASS_MAIN_TIER), "extends": cfg.anim_set_id, "clips": {}})
        for m in cfg.mass_groups:
            rows.append({"id": SC.mass_anim_set_id(cfg.anim_set_id, m), "extends": cfg.anim_set_id,
                         "clips": row_entries(plan, m)})
    return rows


def spec_clip_entry(cp: ClipPlan) -> dict:
    return {
        "resource_ref": cp.resource_ref, "source_clip": cp.source_clip, "loop": cp.loop,
        "frame_count": cp.frame_count, "frames_per_phase": list(cp.frames_per_phase), "total_ms": cp.total_ms,
        "sample_ms": list(cp.sample_ms), "events": [dict(e) for e in cp.events],
    }


def build_spec(plan: RenderPlan) -> dict:
    """规格文件内容（预渲染产物的机器可读清单，复现渲染与自检用）。"""
    cfg = plan.config
    return {
        "generator": "toolchain/run_prerender_skin.py",
        "doc": "architecture/手感设计/04_姿势与动画契约.md 第 6.2 节、ADR-0140",
        "config": cfg.to_dict(),
        "variants": plan.variants(),
        "layout": ("sprite_anim/<stem>[__<方向>[__<层>]]：<stem> 与 <stem>__front 同为整身默认朝向；"
                   "<stem>__<方向> 为整身合成；有装备层时 <stem>__<方向>__body 与 <stem>__<方向>__<装备层名> 为逐层剪辑"),
        "frame_count_rule": "每相 max(1, floor(ms*fps/1000+0.5))；相内帧时长均分（与 std_dummy_poses 同函数）",
        "clips": {cp.stem: spec_clip_entry(cp) for cp in plan.clips},
        "keys": {("" if m is None else m + ":") + key: cp.stem for m, rows in plan.rows.items() for key, (cp, _c) in rows.items()},
    }


def chunk_clips(clips: list, max_frames: int) -> list[list]:
    """把剪辑按帧数贪心分块（每块至少一份剪辑，帧数不超过 max_frames，超大的单份剪辑自成一块）；max_frames <= 0 = 不分块。
    分块只为限制中间裸帧的磁盘占用（一次渲染的裸帧体积 = 帧数 × 方向 × 层变体 × 画布字节），不影响产物。"""
    if max_frames <= 0 or not clips:
        return [list(clips)]
    chunks: list[list] = []
    cur: list = []
    n = 0
    for cp in clips:
        if cur and n + cp.frame_count > max_frames:
            chunks.append(cur)
            cur, n = [], 0
        cur.append(cp)
        n += cp.frame_count
    if cur:
        chunks.append(cur)
    return chunks


def build_job(plan: RenderPlan, out_dir: str, result_path: str, clips_resources_dir: str = DEFAULT_CLIPS_RESOURCES_DIR,
              clips: list | None = None) -> dict:
    """引擎侧作业 JSON（字段与 SkinPrerenderRunner.Job 一一对应）。``clips`` 给定时只渲染这一块（缺省 = 计划全部剪辑）。"""
    cfg = plan.config
    yaws = cfg.yaw_deg()
    return {
        "skin": cfg.skin, "out_dir": out_dir, "result_path": result_path, "clips_resources_dir": clips_resources_dir,
        "required_bones": [name for name, _p, _r in MC.BONES],
        "canvas_w": cfg.canvas[0], "canvas_h": cfg.canvas[1], "pixels_per_unit": cfg.pixels_per_unit,
        "pivot_x": float(cfg.pivot_px[0]), "pivot_y": float(cfg.pivot_px[1]),
        "camera_pitch_deg": cfg.camera_pitch_deg, "lighting": cfg.lighting,
        "light_euler_deg": list(cfg.light_euler_deg), "light_color": list(cfg.light_color),
        "ambient_color": ([1.0, 1.0, 1.0] if cfg.lighting == "unlit" else list(cfg.ambient_color)),
        "position_scale": cfg.position_scale, "unity_layer": cfg.unity_layer,
        "slots": [{"name": s, "yaw_deg": yaws[s]} for s in cfg.effective_slots()],
        "layers": [{"name": n, "selectors": list(sel)} for n, sel in cfg.layers],
        "clips": [{"stem": cp.source_clip, "sample_ms": list(cp.sample_ms)} for cp in (plan.clips if clips is None else clips)],
    }

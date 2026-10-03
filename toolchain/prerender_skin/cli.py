"""命令行：读渲染配置 -> 计划 -> 引擎渲染 -> 组装 -> 自检 -> 数据校验。

退出码：0 通过；1 自检/数据校验失败或引擎失败；2 配置不合法（未启动引擎）；3 引擎拒绝蒙皮（缺骨骼等，清单已打印）。
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from .assemble import clean_outputs, write_assets, write_data, write_spec
from .config import ConfigError, RenderConfig, SkinRefused, load_config
from .plan import DEFAULT_CLIPS_RESOURCES_DIR, build_job, build_plan, chunk_clips
from .unity_runner import EngineError, raise_for_result, unity_backend
from .verify import print_report, verify

TOOLCHAIN_DIR = Path(__file__).resolve().parent.parent
EXIT_OK, EXIT_FAILED, EXIT_CONFIG, EXIT_REFUSED = 0, 1, 2, 3


def run_external_validation(assets_root: Path, data_root: Path, dataset: str, log=print) -> bool:
    """数据行过 ``validate_data --strict`` 与 ``import_assets check``（两者都读 <根>/<数据集>/ 布局）。"""
    ok = True
    cmds = [
        ("validate_data --strict", [sys.executable, str(TOOLCHAIN_DIR / "validate_data.py"), "--strict",
                                    "--data-root", str(data_root), "--dataset", dataset]),
        ("import_assets check", [sys.executable, str(TOOLCHAIN_DIR / "import_assets.py"), "check",
                                 "--dataset", dataset, "--assets-root", str(assets_root), "--data-root", str(data_root)]),
    ]
    for label, cmd in cmds:
        proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if proc.returncode != 0:
            ok = False
            log(f"错误：{label} 退出码 {proc.returncode}")
            tail = (proc.stdout + proc.stderr).strip().splitlines()[-25:]
            for line in tail:
                log("    " + line)
        else:
            log(f"{label}：通过")
    return ok


DEFAULT_CHUNK_FRAMES = 800


def run_pipeline(cfg: RenderConfig, assets_out: Path, data_out: Path, backend, work_dir: Path,
                 clean: bool = False, clips_resources_dir: str = DEFAULT_CLIPS_RESOURCES_DIR,
                 chunk_frames: int = DEFAULT_CHUNK_FRAMES, log=print):
    """渲染（backend，按帧数分块，每块一次引擎批处理）+ 组装 + 自检。返回 (自检报告, 计划, 最后一块的引擎结果)。拒绝/引擎失败以异常抛出。

    分块只为限制中间裸帧的磁盘占用（全键全方向全体量时裸帧约 6 GB，分块后峰值约 chunk_frames 帧的量）；骨骼/装备层选择器的校验在
    第一块就会拒绝（此时还没有写任何产物），产物与不分块逐字节相同。"""
    plan = build_plan(cfg)
    raw_dir = work_dir / "raw"
    chunks = chunk_clips(plan.clips, chunk_frames)
    if clean:
        log(f"--clean：删除 {clean_outputs(plan, assets_out)} 项（仅本次计划涉及的 sprite_anim/<stem>* 与规格文件）")
    t0 = time.perf_counter()
    n_dirs = 0
    result: dict = {}
    for i, chunk in enumerate(chunks):
        if raw_dir.exists():
            shutil.rmtree(raw_dir)
        raw_dir.mkdir(parents=True)
        chunk_work = work_dir / f"chunk{i:03d}"
        job = build_job(plan, str(raw_dir), str(chunk_work / "result.json"), clips_resources_dir, clips=chunk)
        result = backend(job, chunk_work)
        raise_for_result(result)
        n_dirs += write_assets(plan, raw_dir, assets_out, clips=chunk)
        shutil.rmtree(raw_dir, ignore_errors=True)
        if len(chunks) > 1:
            log(f"  分块 {i + 1}/{len(chunks)} 完成（{len(chunk)} 份剪辑）")
    spec_path = write_spec(plan, assets_out)
    data_path = write_data(plan, data_out)
    log(f"渲染与组装完成：{plan.render_count()} 张图（{len(plan.clips)} 份剪辑 / {plan.total_frames()} 帧 × "
        f"{len(cfg.effective_slots())} 方向 × {len(plan.variants())} 层变体），引擎 {len(chunks)} 次批处理，{n_dirs} 个资源目录，"
        f"{time.perf_counter() - t0:.1f}s；规格 {spec_path.as_posix()}；数据行 {data_path.as_posix()}")
    report = verify(plan, assets_out, data_out)
    return report, plan, result


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="把标准骨骼蒙皮预渲染成 sprite 型序列帧（手感设计/04 第 6.2 节，ADR-0140）")
    ap.add_argument("config", help="渲染配置 JSON（字段见 toolchain/prerender_skin/README.md）")
    ap.add_argument("--assets-root", required=False, help="资产根；产物写到 <资产根>/<数据集>/sprite_anim/")
    ap.add_argument("--data-root", required=False, help="数据根；数据行写到 <数据根>/<数据集>/display/display.anim_set.json")
    ap.add_argument("--dataset", required=False, help="数据集名（资产根与数据根下的同名子目录）")
    ap.add_argument("--unity-project", default=None, help="引擎工程根（含 Assets/ 与 Packages/），缺省 <仓库>/adapters/unity")
    ap.add_argument("--unity-exe", default=None, help="Unity 可执行文件（缺省：环境变量 UNITY_EXE，再到 Hub 固定版本）")
    ap.add_argument("--clips-resources-dir", default=DEFAULT_CLIPS_RESOURCES_DIR,
                    help=f"标准剪辑资产的 Resources 目录（缺省 {DEFAULT_CLIPS_RESOURCES_DIR}）")
    ap.add_argument("--work-dir", default=None, help="中间文件目录（裸帧、作业与引擎日志）；缺省临时目录，成功后删除")
    ap.add_argument("--keep-work", action="store_true", help="成功后也保留中间文件目录")
    ap.add_argument("--chunk-frames", type=int, default=DEFAULT_CHUNK_FRAMES,
                    help=f"每次引擎批处理渲染的剪辑帧数上限（缺省 {DEFAULT_CHUNK_FRAMES}；0 = 不分块）：限制中间裸帧的磁盘占用")
    ap.add_argument("--clean", action="store_true", help="组装前删除本次计划涉及的 sprite_anim/<stem>* 与规格文件（只限这批）")
    ap.add_argument("--plan-only", action="store_true", help="只展开并打印渲染计划，不启动引擎")
    ap.add_argument("--no-external-validate", action="store_true", help="不跑 validate_data --strict 与 import_assets check")
    args = ap.parse_args(argv)

    try:
        cfg = load_config(args.config)
        plan = build_plan(cfg)
    except ConfigError as exc:
        print(f"配置错误：{exc}", file=sys.stderr)
        return EXIT_CONFIG

    print(f"计划：{cfg.anim_set_id}，{len(plan.clips)} 份剪辑资源 / {plan.total_frames()} 帧，"
          f"方向 {cfg.effective_slots()}，层变体 {plan.variants()}，共 {plan.render_count()} 张图")
    if args.plan_only:
        for cp in plan.clips:
            print(f"  {cp.stem}  <- {cp.source_clip}  {cp.frame_count} 帧 {cp.total_ms}ms {'循环' if cp.loop else '单次'}")
        return EXIT_OK
    if not (args.assets_root and args.data_root and args.dataset):
        print("配置错误：渲染需要 --assets-root、--data-root 与 --dataset", file=sys.stderr)
        return EXIT_CONFIG

    assets_root, data_root = Path(args.assets_root).resolve(), Path(args.data_root).resolve()
    assets_out, data_out = assets_root / args.dataset, data_root / args.dataset
    project = Path(args.unity_project).resolve() if args.unity_project else TOOLCHAIN_DIR.parent / "adapters" / "unity"
    work_dir = Path(args.work_dir).resolve() if args.work_dir else Path(tempfile.mkdtemp(prefix="prerender_skin_"))
    try:
        backend = unity_backend(project, args.unity_exe)
        report, _plan, _result = run_pipeline(cfg, assets_out, data_out, backend, work_dir, clean=args.clean,
                                              clips_resources_dir=args.clips_resources_dir,
                                              chunk_frames=args.chunk_frames)
    except SkinRefused as exc:
        print(f"拒绝：{exc}", file=sys.stderr)
        for k, v in exc.details.items():
            if v:
                print(f"  {k}: {v}", file=sys.stderr)
        return EXIT_REFUSED
    except EngineError as exc:
        print(f"引擎错误：{exc}（中间文件保留在 {work_dir}）", file=sys.stderr)
        return EXIT_FAILED
    rc = print_report(report)
    if rc == 0 and not args.no_external_validate:
        if not run_external_validation(assets_root, data_root, args.dataset):
            rc = EXIT_FAILED
    if rc == 0 and not args.keep_work and not args.work_dir:
        shutil.rmtree(work_dir, ignore_errors=True)
    elif rc != 0 or args.keep_work or args.work_dir:
        print(f"中间文件：{work_dir}")
    return rc

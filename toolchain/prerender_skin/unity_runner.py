"""引擎后端：把作业 JSON 交给 Unity 批处理（SkinPrerenderRunner.RunBatch），读回结果。

后端接口（便于测试替换）：``backend(job: dict, work_dir: Path) -> dict``，返回结果字典（字段同 SkinPrerenderRunner.Result，
即 status / message / files / missing_bones ...），并保证 ``job["out_dir"]`` 下已有每个 剪辑×方向×层 的裸 RGBA 文件。
本模块的 :func:`unity_backend` 是真实后端；测试用的桩后端在 toolchain/tests 里（用 std_dummy_poses 的几何人偶渲染同形文件）。
"""

from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

from .config import SkinRefused

DEFAULT_UNITY_VERSION = "6000.3.23f1"
ENTRY_METHOD = "Adapter.Unity.SkinPrerender.Editor.SkinPrerenderRunner.RunBatch"
EXIT_REFUSED = 3


class EngineError(RuntimeError):
    """引擎侧失败（非"拒绝"）：找不到引擎、批处理异常退出、没产出结果文件等。"""


def resolve_unity_exe(explicit: str | None = None) -> str:
    """优先级：命令行参数 > 环境变量 UNITY_EXE > Program Files 下 Hub 的固定版本 > 命令 Unity.exe（PATH）。"""
    if explicit:
        return explicit
    env = os.environ.get("UNITY_EXE")
    if env:
        return env
    pf = os.environ.get("ProgramFiles")
    if pf:
        cand = Path(pf) / "Unity" / "Hub" / "Editor" / DEFAULT_UNITY_VERSION / "Editor" / "Unity.exe"
        if cand.is_file():
            return str(cand)
    return "Unity.exe"


def unity_command(unity_exe: str, project: Path, job_path: Path, log_path: Path) -> list[str]:
    # 不带 -nographics：渲染要真实的图形设备。-quit 由 RunBatch 里的 EditorApplication.Exit 代劳，所以不加。
    return [unity_exe, "-batchmode", "-projectPath", str(project), "-executeMethod", ENTRY_METHOD,
            "-skinPrerenderJob", str(job_path), "-logFile", str(log_path)]


def check_project_free(project: Path) -> None:
    """同一引擎工程同一时刻只能开一个实例（工程锁）。能确定在占用就提前报错，省得等引擎自己失败。"""
    lock = project / "Temp" / "UnityLockfile"
    if lock.is_file():
        try:
            with open(lock, "r+b"):
                pass
        except OSError as exc:
            raise EngineError(f"引擎工程 {project} 正被另一个 Unity 实例占用（{lock} 被锁）：先关闭它再运行") from exc


def unity_backend(project: Path, unity_exe: str | None = None, timeout_s: int = 3600, log=print):
    """构造真实后端。``project`` 是引擎工程根（含 Assets/ 与 Packages/ 的目录）。"""
    exe = resolve_unity_exe(unity_exe)

    def run(job: dict, work_dir: Path) -> dict:
        work_dir.mkdir(parents=True, exist_ok=True)
        job_path = work_dir / "job.json"
        log_path = work_dir / "unity.log"
        result_path = Path(job["result_path"])
        if result_path.exists():
            result_path.unlink()
        job_path.write_text(json.dumps(job, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
        check_project_free(project)
        cmd = unity_command(exe, project, job_path, log_path)
        log(f"启动引擎批处理：{exe}（工程 {project}，日志 {log_path}）")
        try:
            proc = subprocess.run(cmd, timeout=timeout_s)
        except FileNotFoundError as exc:
            raise EngineError(f"找不到 Unity 可执行文件 {exe}：用 --unity-exe 或环境变量 UNITY_EXE 指定") from exc
        except subprocess.TimeoutExpired as exc:
            raise EngineError(f"引擎批处理超过 {timeout_s} 秒仍未结束（日志 {log_path}）") from exc
        if not result_path.is_file():
            raise EngineError(f"引擎批处理退出码 {proc.returncode}，没有产出结果文件 {result_path}（日志 {log_path}）")
        result = json.loads(result_path.read_text(encoding="utf-8"))
        result["_exit_code"] = proc.returncode
        return result

    return run


def raise_for_result(result: dict) -> None:
    """把结果字典里的失败转成异常：refused -> SkinRefused（带逐项清单），其它非 ok -> EngineError。"""
    status = result.get("status")
    if status == "ok":
        return
    if status == "refused":
        details = {k: result.get(k) or [] for k in
                   ("missing_bones", "duplicate_bones", "bad_selectors", "overlapping_renderers", "missing_clips")}
        raise SkinRefused(result.get("message", "引擎拒绝渲染这套蒙皮"), details)
    raise EngineError(f"引擎渲染失败：{result.get('message', '(无信息)')}")

"""真实 dist 产物回归用例共用的"取最新已存在发布包"辅助。

判断记录：

1. 为什么不按 ``VERSION`` 找包。发布流程（``build.ps1 -Release``）先把 ``VERSION`` 写回为目标版本、
   再跑发布前门禁，此时 ``dist/ws-game-<目标版本>.zip|.lock`` 还没生成；按 ``VERSION`` 找包的用例会
   整批 skip，撞上门禁的"pytest skip 上限 0"（1.92.0 首次发布在此阻塞）。改为在 ``dist/`` 里按语义化
   版本排序取当前已存在、zip 与 lock 成对齐全的最大版本，不依赖 ``VERSION``。
2. ``dist/`` 里一个成对的包都没有（干净 checkout、从未发布过）才 skip，属环境性。
3. 只认严格 ``ws-game-<主>.<次>.<修订>.zip`` 与同名 ``.lock``：``-samples.zip``、``-dryrun`` 目录等
   不参与。
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Optional, Tuple

_ZIP_RE = re.compile(r"^ws-game-(\d+)\.(\d+)\.(\d+)\.zip$")


def find_latest_dist_package(dist_dir: Path) -> Optional[Tuple[str, Path, Path]]:
    """返回 ``(版本号, zip 路径, lock 路径)``；没有任何成对包时返回 None。"""
    if not dist_dir.is_dir():
        return None
    best: Optional[Tuple[Tuple[int, int, int], Path, Path]] = None
    for zip_path in dist_dir.iterdir():
        m = _ZIP_RE.match(zip_path.name)
        if not m or not zip_path.is_file():
            continue
        lock_path = zip_path.with_suffix(".lock")
        if not lock_path.is_file():
            continue
        key = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
        if best is None or key > best[0]:
            best = (key, zip_path, lock_path)
    if best is None:
        return None
    return ".".join(str(n) for n in best[0]), best[1], best[2]

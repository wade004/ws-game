#!/usr/bin/env python3
"""资产导入工具入口。

职责与用法见 ``toolchain/asset_import/`` 包与 ``toolchain/README.md``"资产导入工具
（import_assets.py）"一节；本文件只是薄入口，实际实现都在 ``toolchain/asset_import/``。

```
python toolchain/import_assets.py <子命令> ...
```

子命令：``sprite``、``icon``、``vfx``、``sfx``、``check``（各自 ``--help`` 查看参数）。
"""

from __future__ import annotations

import sys
from pathlib import Path

# 允许直接以 "python toolchain/import_assets.py" 方式运行（不依赖 PYTHONPATH/包安装）。
sys.path.insert(0, str(Path(__file__).resolve().parent))

from asset_import.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())

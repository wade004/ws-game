"""`build.ps1 -Release` 端到端测试用的"最小仓库骨架 + 外部命令桩"（发布续跑，2026-10-04）。

为什么要骨架：续跑的接线（`build.ps1` 的参数校验、前置校验、从哪个阶段起跑、各阶段写完成标记、失败时的恢复提示）
只能通过真实的 `build.ps1` 验证。真实仓库里跑一次发布要 50 分钟全量门禁、要私服、要 GitHub，测试里既不能也不该这么做。
这里在临时目录里搭一个"刚好够 build.ps1 走完打包"的仓库骨架：

- 复制真实的 `build.ps1` 与它 dot-source 的 toolchain 脚本（`_hash.ps1`、`_release_resume.ps1`、`prune_dist.ps1` 等）；
- 其余输入（VERSION、CHANGELOG、两个 package.json、packages-lock.json、数据/资产目录、各程序集的构建产物、
  私服包清单 …）用最小占位内容；
- 外部命令全部用 PATH 上的 `.cmd` 桩替换：`dotnet`（只记日志）、`npm`（`pack`/`view`/`publish`，私服用一个 JSON 文件模拟）、
  `gh`（`release view/create/upload`，Release 用一个 JSON 文件模拟）；`check.ps1` 是骨架里的一个桩脚本（只记日志、
  打印"门禁通过"行）；`git push` 推到本地裸仓库 `origin.git`。绝不触及真实私服 / GitHub。
- 故障注入走环境变量（`NPM_STUB_FAIL_PACK`、`NPM_STUB_FAIL_PUBLISH_PKG`、`NPM_STUB_VIEW_ERROR`、`GH_STUB_FAIL_CREATE`、
  `CHECK_STUB_EXIT`、`DOTNET_STUB_EXIT`），所有桩的调用都追加到 `STUB_LOG`（每行一个 JSON）。

骨架里的 REGRESSION_LOG.md 带一行对基线提交的"含 Unity 全量通过"记录，使第 3b 步的回归记录守卫放行。
"""

from __future__ import annotations

import base64
import gzip
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tarfile
from dataclasses import dataclass, field
from pathlib import Path

from _git_env import git_env, init_temp_repo, run_git
from _ps_harness import REPO_ROOT, find_powershell
from _ps_subprocess_env import clean_powershell_env

PACKAGE_NAMES = (
    "com.gamefoundation.adapter.unity",
    "com.gamefoundation.framework-data",
    "com.gamefoundation.toolchain",
    "com.gamefoundation.adapter.headless",
)

# 骨架里要复制的真实脚本（相对仓库根）。
COPIED_FILES = (
    "build.ps1",
    "toolchain/_hash.ps1",
    "toolchain/_version_writeback.ps1",
    "toolchain/_lock_writeback.ps1",
    "toolchain/_dist_immutability_guard.ps1",
    "toolchain/_release_regression_guard.ps1",
    "toolchain/_release_notes.ps1",
    "toolchain/_precommit_tiering_guard.ps1",
    "toolchain/_release_resume.ps1",
    "toolchain/prune_dist.ps1",
    "toolchain/resource_layout_map.json",
)

CORE_ASSEMBLY_DIRS = (
    ("Core.Foundation", "core/foundation"),
    ("Core.Numbers", "core/numbers"),
    ("Core.Rules", "core/rules"),
    ("Core.Carriers", "core/carriers"),
    ("Core.Gameplay", "core/gameplay"),
    ("Presentation.Common", "presentation"),
    ("Lab.Kernel", "lab"),
    ("Core.Sim", "core/sim"),
    ("Adapters.Stub", "adapters/stub"),
)

STUB_SCRIPT = r'''
import base64, gzip, hashlib, io, json, os, sys, tarfile

tool = sys.argv[1]
args = sys.argv[2:]
log_path = os.environ.get("STUB_LOG")
if log_path:
    with open(log_path, "a", encoding="utf-8") as f:
        f.write(json.dumps({"tool": tool, "args": args}, ensure_ascii=False) + "\n")


def load(path, default):
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    return default


def save(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def make_tgz(src_dir, dest_path):
    raw = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as gz:
        with tarfile.open(fileobj=gz, mode="w", format=tarfile.USTAR_FORMAT) as tar:
            entries = []
            for base, dirs, files in os.walk(src_dir):
                dirs.sort()
                for name in sorted(files):
                    full = os.path.join(base, name)
                    entries.append((os.path.relpath(full, src_dir).replace("\\", "/"), full))
            for rel, full in entries:
                data = open(full, "rb").read()
                info = tarfile.TarInfo("package/" + rel)
                info.size = len(data)
                info.mtime = 0
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                info.mode = 0o644
                tar.addfile(info, io.BytesIO(data))
    with open(dest_path, "wb") as f:
        f.write(raw.getvalue())


def digests(path):
    data = open(path, "rb").read()
    return ("sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode("ascii"),
            hashlib.sha1(data).hexdigest())


if tool == "dotnet":
    code = int(os.environ.get("DOTNET_STUB_EXIT", "0")) if (args and args[0] == "build") else 0
    sys.exit(code)

if tool == "npm":
    reg_path = os.environ.get("NPM_STUB_REGISTRY")
    cmd = args[0] if args else ""
    if cmd == "pack":
        if os.environ.get("NPM_STUB_FAIL_PACK"):
            sys.stderr.write("stub: npm pack 注入失败\n")
            sys.exit(1)
        src = args[1]
        dest = args[args.index("--pack-destination") + 1]
        pkg = json.load(open(os.path.join(src, "package.json"), encoding="utf-8-sig"))
        name = pkg["name"] + "-" + pkg["version"] + ".tgz"
        make_tgz(src, os.path.join(dest, name))
        sys.exit(0)
    if cmd == "view":
        spec = args[1]
        if os.environ.get("NPM_STUB_VIEW_ERROR"):
            print(json.dumps({"error": {"code": "ECONNREFUSED", "summary": "stub: 私服连不上"}}))
            sys.exit(1)
        store = load(reg_path, {})
        if spec in store:
            print(json.dumps({"integrity": store[spec]["integrity"], "shasum": store[spec]["shasum"]}))
            sys.exit(0)
        print(json.dumps({"error": {"code": "E404", "summary": "stub: not found"}}))
        sys.exit(1)
    if cmd == "publish":
        src = args[1]
        pkg = json.load(open(os.path.join(src, "package.json"), encoding="utf-8-sig"))
        if os.environ.get("NPM_STUB_FAIL_PUBLISH_PKG") == pkg["name"]:
            sys.stderr.write("stub: npm publish 注入失败\n")
            sys.exit(1)
        tgz = os.path.join(os.path.dirname(src.rstrip("\\/")), pkg["name"] + "-" + pkg["version"] + ".tgz")
        integrity, shasum = digests(tgz)
        store = load(reg_path, {})
        key = pkg["name"] + "@" + pkg["version"]
        if key in store:
            sys.stderr.write("stub: 版本已存在，禁止覆盖发布\n")
            sys.exit(1)
        store[key] = {"integrity": integrity, "shasum": shasum}
        save(reg_path, store)
        sys.exit(0)
    sys.exit(0)

if tool == "gh":
    gh_path = os.environ.get("GH_STUB_STORE")
    store = load(gh_path, {})
    if args[:2] == ["release", "view"]:
        tag = args[2]
        if tag not in store:
            sys.stderr.write("release not found\n")
            sys.exit(1)
        assets = [{"name": n, "size": s} for n, s in store[tag].items()]
        print(json.dumps({"assets": assets}))
        sys.exit(0)
    if args[:2] == ["release", "create"]:
        if os.environ.get("GH_STUB_FAIL_CREATE"):
            sys.stderr.write("stub: gh release create 注入失败\n")
            sys.exit(1)
        tag = args[2]
        files = []
        for a in args[3:]:
            if a.startswith("--"):
                break
            files.append(a)
        store[tag] = {os.path.basename(f): os.path.getsize(f) for f in files}
        save(gh_path, store)
        sys.exit(0)
    if args[:2] == ["release", "upload"]:
        tag = args[2]
        files = [a for a in args[3:] if not a.startswith("--")]
        store.setdefault(tag, {})
        for f in files:
            store[tag][os.path.basename(f)] = os.path.getsize(f)
        save(gh_path, store)
        sys.exit(0)
    sys.exit(0)

sys.exit(0)
'''

CHECK_STUB = """\
$ErrorActionPreference = "Stop"
if ($env:STUB_LOG) {
    $entry = @{ tool = "check.ps1"; args = @($args) } | ConvertTo-Json -Compress
    [System.IO.File]::AppendAllText($env:STUB_LOG, $entry + "`n", (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host "门禁通过：桩 check.ps1（骨架仓库，不跑真实检查）"
if ($env:CHECK_STUB_EXIT) { exit [int]$env:CHECK_STUB_EXIT }
exit 0
"""


@dataclass
class Skeleton:
    root: Path          # 骨架仓库根（build.ps1 所在）
    origin: Path        # 本地裸仓库（git push 的目标）
    stub_dir: Path      # 放 .cmd 桩的目录（加在 PATH 最前）
    work: Path          # 骨架之外的工作目录（日志、私服/GitHub 模拟存储）
    base_version: str
    release_version: str
    base_commit: str = ""
    extra_env: dict = field(default_factory=dict)

    @property
    def log_path(self) -> Path:
        return self.work / "stub_calls.jsonl"

    @property
    def npm_store_path(self) -> Path:
        return self.work / "npm_registry.json"

    @property
    def gh_store_path(self) -> Path:
        return self.work / "gh_releases.json"

    @property
    def state_path(self) -> Path:
        return self.root / "dist" / f"release-{self.release_version}.state.json"

    @property
    def tag(self) -> str:
        return "v" + self.release_version

    # ----- 读取 -----
    def calls(self) -> list[dict]:
        if not self.log_path.exists():
            return []
        out = []
        for line in self.log_path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line:
                out.append(json.loads(line))
        return out

    def calls_since(self, count: int) -> list[dict]:
        return self.calls()[count:]

    def call_labels(self, calls: list[dict] | None = None) -> list[str]:
        """每次桩调用的可读标签：`dotnet build`、`npm pack`、`npm publish <包名>`、`gh release create`、`check.ps1` …"""
        labels = []
        for c in self.calls() if calls is None else calls:
            tool, args = c["tool"], c["args"]
            if tool == "check.ps1":
                labels.append("check.ps1")
            elif tool == "npm" and args and args[0] in ("publish", "pack"):
                labels.append(f"npm {args[0]} {Path(args[1]).name}")
            elif tool == "npm" and args and args[0] == "view":
                labels.append(f"npm view {args[1]}")
            elif tool == "dotnet":
                labels.append(f"dotnet {args[0]}")
            elif tool == "gh":
                labels.append(f"gh {' '.join(args[:2])}")
            else:
                labels.append(tool)
        return labels

    def read_state(self) -> dict:
        return json.loads(self.state_path.read_text(encoding="utf-8-sig"))

    def edit_state(self, mutate) -> dict:
        state = self.read_state()
        mutate(state)
        self.state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        return state

    def git(self, *args: str, check: bool = True) -> str:
        return run_git(self.root, *args, check=check).stdout.strip()

    def head(self) -> str:
        return self.git("rev-parse", "HEAD")

    def stage_done(self, stage: str) -> bool:
        return bool(self.read_state()["stages"][stage]["done"])

    def tgz_integrity(self, package: str) -> tuple[str, str]:
        path = self.root / "dist" / self.release_version / "packages" / f"{package}-{self.release_version}.tgz"
        data = path.read_bytes()
        return ("sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode("ascii"),
                hashlib.sha1(data).hexdigest())

    # ----- 运行 -----
    def env(self, extra: dict | None = None) -> dict:
        exe = find_powershell()
        env = clean_powershell_env(exe)
        env.update({k: v for k, v in git_env().items() if k.startswith("GIT_")})
        env["PATH"] = str(self.stub_dir) + os.pathsep + env.get("PATH", os.environ.get("PATH", ""))
        env["STUB_LOG"] = str(self.log_path)
        env["NPM_STUB_REGISTRY"] = str(self.npm_store_path)
        env["GH_STUB_STORE"] = str(self.gh_store_path)
        for key in ("NPM_STUB_FAIL_PACK", "NPM_STUB_FAIL_PUBLISH_PKG", "NPM_STUB_VIEW_ERROR",
                    "GH_STUB_FAIL_CREATE", "CHECK_STUB_EXIT", "DOTNET_STUB_EXIT"):
            env.pop(key, None)
        env.update(self.extra_env)
        if extra:
            env.update(extra)
        return env

    def run_build(self, *args: str, env_extra: dict | None = None, timeout: int = 900) -> subprocess.CompletedProcess:
        exe = find_powershell()
        proc = subprocess.run(
            [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(self.root / "build.ps1"), *args],
            cwd=str(self.root),
            capture_output=True,
            timeout=timeout,
            env=self.env(env_extra),
        )
        # 控制台输出的编码随宿主/代码页而变（中文可能是 GBK）：只用 replace 解码，断言只依赖 ASCII 片段。
        proc.stdout_text = _decode(proc.stdout)  # type: ignore[attr-defined]
        proc.stderr_text = _decode(proc.stderr)  # type: ignore[attr-defined]
        return proc


def _decode(data: bytes) -> str:
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return data.decode("mbcs", errors="replace")


def _write(path: Path, text: str, *, bom: bool = False) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    data = text.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    path.write_bytes(data)


def make_stub_dir(stub_dir: Path) -> None:
    """在 stub_dir 里写 `dotnet.cmd`/`npm.cmd`/`gh.cmd` 三个桩（放进 PATH 最前即可替换真实命令）。"""
    stub_dir.mkdir(parents=True, exist_ok=True)
    stub_py = stub_dir / "stub_main.py"
    stub_py.write_text(STUB_SCRIPT, encoding="utf-8")
    for tool in ("dotnet", "npm", "gh"):
        (stub_dir / f"{tool}.cmd").write_text(
            f'@echo off\r\n"{sys.executable}" "{stub_py}" {tool} %*\r\n', encoding="ascii"
        )


def _json_text(obj: dict) -> str:
    return json.dumps(obj, ensure_ascii=False, indent=2) + "\n"


def build_skeleton(tmp_path: Path, *, base_version: str = "1.2.0", release_version: str = "1.2.1") -> Skeleton:
    work = tmp_path / "work"
    work.mkdir(parents=True, exist_ok=True)
    root = tmp_path / "repo"
    origin = tmp_path / "origin.git"
    stub_dir = tmp_path / "stubs"
    stub_dir.mkdir(parents=True, exist_ok=True)

    make_stub_dir(stub_dir)

    # --- 仓库骨架 ---
    init_temp_repo(root, branch="main")
    run_git(root, "config", "core.autocrlf", "false")
    run_git(root, "config", "core.safecrlf", "false")

    for rel in COPIED_FILES:
        dest = root / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(REPO_ROOT / rel, dest)

    _write(root / ".gitignore", "\n".join([
        "dist/", "bin/", "obj/",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Lab/",
        "adapters/unity/Assets/StreamingAssets/",
        "toolchain/registry/.npmrc",
        "",
    ]))
    _write(root / "VERSION", base_version)
    _write(root / "CHANGELOG.md",
           f"# 变更记录\n\n## [{release_version}] - 2026-10-04\n\n### 工具链\n\n- 骨架测试条目。\n\n"
           f"## [{base_version}] - 2026-10-01\n\n- 上一个版本。\n")
    adapter_pkg = "adapters/unity/Packages/com.gamefoundation.adapter.unity"
    _write(root / adapter_pkg / "package.json",
           _json_text({"name": "com.gamefoundation.adapter.unity", "version": base_version, "description": "适配层（骨架）"}))
    _write(root / adapter_pkg / "Runtime" / "readme.txt", "adapter runtime\n")
    _write(root / "adapters/unity/Packages/packages-lock.json",
           '{\n  "dependencies": {\n    "com.gamefoundation.game-template": {\n      "version": "file:../../../games/_template",\n'
           '      "depth": 0,\n      "source": "local",\n      "dependencies": {\n'
           f'        "com.gamefoundation.adapter.unity": "{base_version}"\n      }}\n    }}\n  }}\n}}\n')
    _write(root / "games/_template/package.json",
           _json_text({"name": "com.gamefoundation.game-template", "version": base_version,
                       "dependencies": {"com.gamefoundation.adapter.unity": base_version}}))
    _write(root / "games/_template/data/game/world.json", "{}\n")

    for rel in (
        "assets/_placeholder/a.txt", "assets/_sample/a.txt",
        "data/_framework/t.json", "data/_feel/t.json", "data/_lab/t.json", "data/_lab_action/t.json",
        "data/_equip/t.json", "data/_sample/t.json", "lab/fixtures/f.json",
        "adapters/unity/Assets/TextMesh Pro/a.txt",
        "adapters/unity/Assets/Resources/GameFoundation/models/a.txt",
        "adapters/unity/Assets/Resources/GameFoundation/anim_clips/a.txt",
        "adapters/unity/Assets/Resources/GameFoundation/materials/a.txt",
        "adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs",
        "adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs.meta",
        "adapters/unity/Assets/Editor/GeneratePlaceholderVfxAssets.cs",
        "adapters/unity/Assets/Editor/GeneratePlaceholderVfxAssets.cs.meta",
        "adapters/unity/Assets/Shaders/s.shader",
        "adapters/headless/README.md",
        "toolchain/get_framework.ps1",
    ):
        _write(root / rel, f"skeleton {rel}\n")

    # 私服：包清单 + 令牌文件（.gitignore 忽略）+ 三个包的清单目录
    _write(root / "toolchain/registry/registry.json",
           _json_text({"url": "http://registry.invalid:4873/", "packages": list(PACKAGE_NAMES)}))
    _write(root / "toolchain/registry/.npmrc", "//registry.invalid:4873/:_authToken=stub\n")
    for manifest_dir, pkg_name in (("framework-data", "com.gamefoundation.framework-data"),
                                   ("toolchain", "com.gamefoundation.toolchain"),
                                   ("adapter-headless", "com.gamefoundation.adapter.headless")):
        _write(root / "toolchain/registry/manifests" / manifest_dir / "package.json",
               _json_text({"name": pkg_name, "version": base_version, "description": "骨架包"}))
        _write(root / "toolchain/registry/manifests" / manifest_dir / "README.md", f"{pkg_name}\n")

    _write(root / "check.ps1", CHECK_STUB, bom=True)

    run_git(root, "add", "-A")
    run_git(root, "commit", "-q", "-m", "skeleton base")
    base = run_git(root, "rev-parse", "HEAD").stdout.strip()

    # 第 3b 步的回归记录守卫：REGRESSION_LOG.md 里有一行对基线提交的"含 Unity 全量通过"记录（之后只追加文档，守卫放行）。
    _write(root / "REGRESSION_LOG.md",
           "| run_id | 结果 | 提交 sha | 日期 |\n| --- | --- | --- | --- |\n"
           f"| full-20261004-01 | 通过（含 Unity：骨架测试） | {base[:8]} | 2026-10-04 |\n")
    run_git(root, "add", "REGRESSION_LOG.md")
    run_git(root, "commit", "-q", "-m", "skeleton regression log")
    base = run_git(root, "rev-parse", "HEAD").stdout.strip()

    # --- 构建产物（被 .gitignore 忽略，build.ps1 同步/打包时要读）---
    for name, rel_dir in CORE_ASSEMBLY_DIRS:
        dll = root / rel_dir / "bin" / "Release" / "netstandard2.1" / f"{name}.dll"
        _write(dll, f"fake dll {name}\n")
    for exe_dir, exe_name in (("toolchain/validator", "Validator.dll"), ("toolchain/simrunner", "SimRunner.dll"),
                              ("toolchain/feellab", "FeelLab.dll")):
        _write(root / exe_dir / "bin" / "Release" / "net8.0" / exe_name, f"fake {exe_name}\n")
    _write(root / "toolchain/feellab/bin/Release/net8.0/Lab.Kernel.dll", "fake Lab.Kernel.dll\n")

    # --- 本地裸仓库作为 origin ---
    origin.mkdir(parents=True, exist_ok=True)
    run_git(origin, "init", "-q", "--bare", "-b", "main")
    run_git(root, "remote", "add", "origin", str(origin))
    run_git(root, "push", "-q", "origin", "main")

    status = run_git(root, "status", "--porcelain").stdout.strip()
    assert status == "", f"骨架搭好后工作树应干净，实际：{status}"

    return Skeleton(root=root, origin=origin, stub_dir=stub_dir, work=work,
                    base_version=base_version, release_version=release_version, base_commit=base)

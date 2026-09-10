import hashlib
import json
import re
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path

repo = Path(r"D:\workespace\ws-game-audit-d6fda65-20260911")
formal = Path(r"D:\workespace\ws-game\dist\ws-game-1.18.0.zip")
formal_lock = Path(r"D:\workespace\ws-game\dist\ws-game-1.18.0.lock")
formal_samples = Path(r"D:\workespace\ws-game\dist\ws-game-1.18.0-samples.zip")
out = Path(r"D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\validation")

def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for b in iter(lambda: f.read(1024 * 1024), b""):
            h.update(b)
    return h.hexdigest()

lock = json.loads(formal_lock.read_text(encoding="utf-8"))
with zipfile.ZipFile(formal) as z:
    names = z.namelist()
    manifest_name = "ws-game-1.18.0/MANIFEST.txt"
    manifest = z.read(manifest_name).decode("utf-8")
    core_names = ["Core.Foundation.dll", "Core.Numbers.dll", "Core.Rules.dll", "Core.Carriers.dll", "Core.Gameplay.dll", "Presentation.Common.dll"]
    core_hashes = {}
    for dll in core_names:
        suffix = f"/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/{dll}"
        match = next(n for n in names if n.endswith(suffix))
        core_hashes[dll] = hashlib.sha256(z.read(match)).hexdigest()
    headless_path = "ws-game-1.18.0/adapters/headless/Adapters.Stub.dll"
    validator_path = "ws-game-1.18.0/toolchain/validator/bin/Validator.dll"
    headless_hash = hashlib.sha256(z.read(headless_path)).hexdigest()
    validator_hash = hashlib.sha256(z.read(validator_path)).hexdigest()
    package_versions = {}
    for n in names:
        if n.endswith("/package.json"):
            try:
                package_versions[n] = json.loads(z.read(n).decode("utf-8-sig")).get("version")
            except Exception:
                pass

identity = {
    "captured_utc": datetime.now(timezone.utc).isoformat(),
    "frozen_worktree": str(repo),
    "frozen_commit": subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True, encoding="ascii").strip(),
    "source_repository_readonly": r"D:\workespace\ws-game",
    "source_repository_commit": subprocess.check_output(["git", "-C", r"D:\workespace\ws-game", "rev-parse", "HEAD"], text=True, encoding="ascii").strip(),
    "formal_release": {
        "zip": str(formal), "zip_bytes": formal.stat().st_size, "zip_sha256": sha(formal),
        "lock": str(formal_lock), "lock_bytes": formal_lock.stat().st_size, "lock_sha256": sha(formal_lock),
        "samples_zip": str(formal_samples), "samples_bytes": formal_samples.stat().st_size, "samples_sha256": sha(formal_samples),
        "lock_fields": list(lock.keys()), "lock_version": lock.get("version"), "lock_git_commit": lock.get("git_commit"),
        "core_hashes_in_zip": core_hashes, "core_hashes_in_lock": lock.get("dlls", {}),
        "headless_hash_in_zip": headless_hash, "headless_hash_in_lock": lock.get("headless_dlls", {}).get("Adapters.Stub.dll"),
        "validator_hash_in_zip": validator_hash, "validator_hash_in_lock": lock.get("validator_dlls", {}).get("Validator.dll"),
        "manifest_sha256": hashlib.sha256(manifest.encode("utf-8")).hexdigest(),
        "manifest_git_commit_line": next((x for x in manifest.splitlines() if x.startswith("git_commit:")), None),
        "manifest_version_line": next((x for x in manifest.splitlines() if x.startswith("version:")), None),
        "package_versions": package_versions,
    },
    "validation_boundary": {"dotnet": "PASS", "python": "check-step FAIL; independent pytest 186 passed/4 skipped", "unity": "NOT_RUN (-SkipUnity)", "standalone": "NOT_RUN (-SkipUnity)", "il2cpp": "NOT_RUN (-SkipUnity)", "consumer_smoke": "NOT_RUN (-SkipUnity)", "abi_probe_gate": "SKIP (baseline dist/ws-game-1.12.0.zip absent in frozen worktree)", "abi_probe_formal_rerun": "PASS (old consumer against formal 1.12 and 1.18 exit 0; assembly hashes match; ABI surface breaks 0, additions 275; evidence supplied by independent audit)"},
}
(out / "identity.json").write_text(json.dumps(identity, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

tracked = subprocess.check_output(["git", "-c", "core.quotePath=false", "-C", str(repo), "ls-files"], text=True, encoding="utf-8").splitlines()
module_counts = {}
for p in tracked:
    top = p.split("/", 1)[0]
    module_counts[top] = module_counts.get(top, 0) + 1
(out / "tracked-modules.md").write_text("# Tracked file inventory\n\n" + f"Total tracked files: **{len(tracked)}**\n\n" + "| Top-level module | Files |\n|---|---:|\n" + "".join(f"| `{k}` | {module_counts[k]} |\n" for k in sorted(module_counts)) + "\n", encoding="utf-8")

link_re = re.compile(r"!?(?:\[[^\]]*\])\(([^)\n]+)\)")
checked = []
broken = []
for rel in tracked:
    if not rel.lower().endswith(".md") or any(part.lower().startswith("audit") for part in Path(rel).parts):
        continue
    src = repo / rel
    text = src.read_text(encoding="utf-8", errors="replace")
    for raw in link_re.findall(text):
        target = raw.strip().strip("<>").split()[0] if raw.strip() else ""
        if not target or target.startswith("#") or re.match(r"(?i)^(?:https?|mailto|ftp):", target):
            continue
        target_path = target.split("#", 1)[0]
        if not target_path:
            continue
        if any(part.lower().startswith("audit") for part in Path(target_path.replace("/", "\\")).parts):
            continue
        resolved = (src.parent / target_path).resolve()
        item = f"{rel} -> {target}"
        checked.append(item)
        if not resolved.exists():
            broken.append(item)
(out / "markdown-links.md").write_text("# Markdown relative link inventory\n\n" + f"Checked links: **{len(checked)}**\nBroken links: **{len(broken)}**\n\n" + ("\n".join(f"- `{x}`" for x in broken) if broken else "No broken relative file links found.") + "\n", encoding="utf-8")
print(json.dumps({"tracked_files": len(tracked), "links_checked": len(checked), "broken_links": len(broken), "identity": str(out / "identity.json")}, ensure_ascii=False))

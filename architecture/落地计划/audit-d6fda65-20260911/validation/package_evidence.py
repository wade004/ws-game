import hashlib
import zipfile
from pathlib import Path

root = Path(r"D:\workespace\ws-game-artifacts\audit-d6fda65-20260911")
zip_path = root / "evidence.zip"
manifest_path = root / "evidence.manifest.txt"
excluded_dirs = {"bin", "obj", "Library", "node_modules", "target", "UnityCopy", "build"}
excluded_suffixes = {".dll", ".exe", ".pdb"}
excluded_names = {"evidence.zip", "evidence.manifest.txt", "evidence.sha256"}
files = []
for p in root.rglob("*"):
    if not p.is_file():
        continue
    rel = p.relative_to(root)
    if p.name in excluded_names or any(part in excluded_dirs for part in rel.parts):
        continue
    if p.suffix.lower() in excluded_suffixes:
        continue
    if p.suffix.lower() == ".zip":
        continue
    files.append((rel.as_posix(), p))
files.sort()
with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for rel, p in files:
        z.write(p, rel)
lines = ["evidence.zip manifest", f"root={root}", f"file_count={len(files)}"]
for rel, p in files:
    lines.append(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {rel}")
manifest_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
zip_sha = hashlib.sha256(zip_path.read_bytes()).hexdigest()
(root / "evidence.sha256").write_text(f"{zip_sha}  evidence.zip\n", encoding="ascii")
print(f"file_count={len(files)}")
print(f"zip_bytes={zip_path.stat().st_size}")
print(f"zip_sha256={zip_sha}")

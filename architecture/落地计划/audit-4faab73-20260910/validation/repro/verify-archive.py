#!/usr/bin/env python3
"""独立校验 archive-audit.ps1 生成的证据归档。

只读输入：AuditRoot、ZipPath，以及 ZipPath 旁的 manifest.sha256。
输出：stdout 上的 JSON 机器摘要；退出码 0 表示所有校验通过，1 表示校验失败。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import posixpath
import re
import sys
import zipfile
from pathlib import Path
from urllib.parse import unquote


MANIFEST_NAME = "manifest.sha256"
FORBIDDEN_DIRS = {"bin", "obj", "build", "library", "temp", "unity-copy"}
FORBIDDEN_SUFFIXES = (".dll", ".pdb", ".exe", ".zip", ".tgz", ".nupkg")
LINK_RE = re.compile(r"\[[^\]]+\]\(([^)\r\n]+)\)")
MANIFEST_RE = re.compile(r"^(?P<sha>[0-9a-fA-F]{64})  (?P<path>.+)$")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_member(raw_name: str) -> tuple[str, list[str]]:
    """Return slash-normalized member name and structural errors."""
    name = raw_name.replace("\\", "/")
    errors: list[str] = []
    if not name or name.startswith("/") or re.match(r"^[A-Za-z]:/", name):
        errors.append("absolute-or-empty-path")
    normalized = posixpath.normpath(name)
    if normalized in (".", "") or normalized == ".." or normalized.startswith("../"):
        errors.append("path-escapes-root")
    if normalized != name:
        errors.append("non-canonical-path")
    return normalized, errors


def forbidden_member(name: str) -> list[str]:
    pieces = name.split("/")
    reasons: list[str] = []
    if any(piece.casefold() in FORBIDDEN_DIRS for piece in pieces[:-1]):
        reasons.append("forbidden-directory")
    if name.casefold().endswith(FORBIDDEN_SUFFIXES):
        reasons.append("forbidden-binary-or-nested-archive")
    return reasons


def parse_manifest(data: bytes) -> tuple[dict[str, str], list[str]]:
    records: dict[str, str] = {}
    errors: list[str] = []
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as exc:
        return {}, [f"manifest-not-utf8:{exc}"]
    for line_number, line in enumerate(text.splitlines(), 1):
        if not line.strip() or line.startswith("#"):
            continue
        match = MANIFEST_RE.fullmatch(line)
        if not match:
            errors.append(f"manifest-format:line-{line_number}")
            continue
        path, path_errors = canonical_member(match.group("path"))
        errors.extend(f"manifest:{path}:{reason}" for reason in path_errors)
        if path in records:
            errors.append(f"manifest-duplicate:{path}")
        records[path] = match.group("sha").lower()
    return records, errors


def check_markdown_links(member_name: str, data: bytes, member_names: set[str]) -> list[str]:
    failures: list[str] = []
    text = data.decode("utf-8", errors="replace")
    for match in LINK_RE.finditer(text):
        target = match.group(1).strip().strip("<>")
        if not target or target.startswith("#"):
            continue
        target = re.split(r"\s+", target, maxsplit=1)[0]
        # URLs and absolute paths are outside the relative-link contract.
        if re.match(r"^[A-Za-z][A-Za-z0-9+.-]*:", target) or target.startswith(("/", "\\", "//")):
            continue
        target = re.split(r"[#?]", target, maxsplit=1)[0]
        if not target:
            continue
        target = unquote(target.replace("\\", "/"))
        resolved = posixpath.normpath(posixpath.join(posixpath.dirname(member_name), target))
        if resolved == ".." or resolved.startswith("../") or resolved.startswith("/"):
            failures.append(f"{member_name} -> external-relative:{target}")
        elif resolved not in member_names:
            failures.append(f"{member_name} -> {resolved}")
    return failures


def run(audit_root: Path, zip_path: Path) -> dict[str, object]:
    result: dict[str, object] = {
        "passed": False,
        "auditRoot": str(audit_root),
        "zipPath": str(zip_path),
        "zipSha256": None,
        "memberCount": 0,
        "manifestListedCount": 0,
        "manifestInnerOuterSameBytes": False,
        "diskHashFailures": [],
        "manifestFailures": [],
        "duplicateMembers": [],
        "forbiddenMembers": [],
        "markdownLinkFailures": [],
        "errors": [],
    }
    disk_failures: list[str] = result["diskHashFailures"]  # type: ignore[assignment]
    manifest_failures: list[str] = result["manifestFailures"]  # type: ignore[assignment]
    duplicate_members: list[str] = result["duplicateMembers"]  # type: ignore[assignment]
    forbidden_members: list[str] = result["forbiddenMembers"]  # type: ignore[assignment]
    link_failures: list[str] = result["markdownLinkFailures"]  # type: ignore[assignment]
    errors: list[str] = result["errors"]  # type: ignore[assignment]

    if not audit_root.is_dir():
        errors.append(f"missing-audit-root:{audit_root}")
        return result
    if not zip_path.is_file():
        errors.append(f"missing-zip:{zip_path}")
        return result
    external_manifest = zip_path.parent / MANIFEST_NAME
    if not external_manifest.is_file():
        errors.append(f"missing-external-manifest:{external_manifest}")
        return result

    outer_manifest = external_manifest.read_bytes()
    try:
        with zipfile.ZipFile(zip_path, "r") as archive:
            infos = archive.infolist()
            result["memberCount"] = len(infos)
            raw_names = [info.filename for info in infos]
            canonical_names: list[str] = []
            info_by_name: dict[str, zipfile.ZipInfo] = {}
            for raw_name, info in zip(raw_names, infos):
                name, path_errors = canonical_member(raw_name)
                if path_errors:
                    errors.extend(f"member:{raw_name}:{reason}" for reason in path_errors)
                if name in info_by_name:
                    duplicate_members.append(name)
                else:
                    info_by_name[name] = info
                canonical_names.append(name)
                reasons = forbidden_member(name)
                if info.is_dir() or name.endswith("/"):
                    reasons.append("directory-entry")
                if reasons:
                    forbidden_members.append(f"{name}:{','.join(reasons)}")

            if MANIFEST_NAME not in info_by_name:
                errors.append("manifest-missing-inside-zip")
                inner_manifest = b""
            else:
                inner_manifest = archive.read(info_by_name[MANIFEST_NAME])
                if inner_manifest != outer_manifest:
                    errors.append("manifest-inner-outer-byte-mismatch")
                else:
                    result["manifestInnerOuterSameBytes"] = True

            manifest_records, parse_errors = parse_manifest(inner_manifest)
            manifest_failures.extend(parse_errors)
            result["manifestListedCount"] = len(manifest_records)
            member_names = set(canonical_names)
            non_manifest_names = member_names - {MANIFEST_NAME}
            if set(manifest_records) != non_manifest_names:
                missing = sorted(non_manifest_names - set(manifest_records))
                extra = sorted(set(manifest_records) - non_manifest_names)
                if missing:
                    manifest_failures.append("manifest-missing-members:" + ",".join(missing))
                if extra:
                    manifest_failures.append("manifest-extra-members:" + ",".join(extra))

            for name, info in info_by_name.items():
                if name == MANIFEST_NAME:
                    continue
                if name not in manifest_records:
                    continue
                entry_data = archive.read(info)
                entry_hash = sha256_bytes(entry_data)
                expected_hash = manifest_records[name]
                if entry_hash != expected_hash:
                    manifest_failures.append(f"zip-hash:{name}")
                source_path = audit_root.joinpath(*name.split("/"))
                try:
                    source_resolved = source_path.resolve()
                    root_resolved = audit_root.resolve()
                    source_resolved.relative_to(root_resolved)
                except (OSError, ValueError):
                    disk_failures.append(f"source-outside-root:{name}")
                    continue
                if not source_resolved.is_file():
                    disk_failures.append(f"missing-source:{name}")
                elif sha256_file(source_resolved) != expected_hash:
                    disk_failures.append(f"disk-hash:{name}")

            for name, info in info_by_name.items():
                if name.casefold().endswith(".md"):
                    link_failures.extend(check_markdown_links(name, archive.read(info), member_names))
    except (OSError, zipfile.BadZipFile, RuntimeError) as exc:
        errors.append(f"zip-read-error:{type(exc).__name__}:{exc}")

    result["zipSha256"] = sha256_file(zip_path)
    result["passed"] = not any(
        (
            errors,
            disk_failures,
            manifest_failures,
            duplicate_members,
            forbidden_members,
            link_failures,
        )
    )
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="独立校验 audit archive 与 manifest")
    parser.add_argument("--AuditRoot", required=True, type=Path)
    parser.add_argument("--ZipPath", required=True, type=Path)
    args = parser.parse_args()
    try:
        result = run(args.AuditRoot.resolve(), args.ZipPath.resolve())
    except Exception as exc:  # 保证异常也输出机器可读摘要，不吞掉失败。
        result = {
            "passed": False,
            "auditRoot": str(args.AuditRoot),
            "zipPath": str(args.ZipPath),
            "errors": [f"unexpected:{type(exc).__name__}:{exc}"],
        }
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0 if result.get("passed") else 1


if __name__ == "__main__":
    sys.exit(main())

"""Check public Git content and release containers before upload."""
from __future__ import annotations
import argparse
import hashlib
import io
import importlib.util
import json
import marshal
import pathlib
import re
import struct
import subprocess
import sys
import types
import zipfile
import zlib

EMAIL = re.compile(r"(?i)[A-Z0-9._%+\-]+@([A-Z0-9.\-]+\.[A-Z]{2,})")
USER_PATH = re.compile(r"(?i)(?:[A-Z]:[\\/]+Users[\\/]+[^\s\x00\"<>]{1,180}|/(?:home|Users)/[A-Za-z0-9_.\-]+[/\\])")
INTERNAL_LINK = re.compile(r"(?i)https?://[^\s\"<>]{0,120}(?:feishu\.cn|kdocs\.cn|wps\.cn)/[^\s\"<>]{1,240}")
SECRET = re.compile(r"(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_\-]{24,}|-----BEGIN (?:RSA |OPENSSH |EC |DSA )?PRIVATE KEY-----|eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,})")
PRIVATE_IP = re.compile(r"(?<![\d.])(?:10(?:\.\d{1,3}){3}|192\.168(?:\.\d{1,3}){2}|172\.(?:1[6-9]|2\d|3[01])(?:\.\d{1,3}){2})(?![\d.])")
BUNDLE = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")
IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".ico", ".webp", ".gif"}
LOCAL_SUFFIXES = {".log", ".sqlite", ".sqlite3", ".db", ".xlsx", ".xls", ".docx", ".pem", ".key", ".pfx", ".p12", ".dmp", ".etl"}
LOCAL_PARTS = {"outputs", "work", "runtime", "results", "uploads", ".codex", ".agents", "rollout_summaries", "memories_v2", "superpowers"}
MAX_ENTRY = 256 * 1024 * 1024

def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args])

def allowed_email(domain):
    return domain.lower() in {"users.noreply.github.com", "github.com", "example.com", "example.org", "example.net"} or domain.lower().endswith((".test", ".invalid"))

def native_dependency_name(name):
    name = re.sub(r"python3\d+t?\.dll$", "python3.dll", name)
    return re.sub(r"\.cp3\d+t?-win_amd64", ".cpython-win_amd64", name)

class Guard:
    def __init__(self, root):
        self.root = pathlib.Path(root)
        self.policy = json.loads((self.root / ".privacy-policy.json").read_text(encoding="utf-8"))
        self.errors = set()
        self.seen_assets = set()

    def fail(self, where, kind):
        # Do not print matched secrets or personal values into public CI logs.
        self.errors.add(f"{where}: {kind}")

    def text(self, data, where, emails=True, strict_utf8=True):
        if strict_utf8:
            try:
                values = [data.decode("utf-8-sig")]
            except UnicodeDecodeError:
                self.fail(where, "unexpected binary content")
                return
        else:
            values = [data.decode("utf-8", errors="replace"), data.decode("utf-16-le", errors="replace")]
        for value in values:
            if USER_PATH.search(value):
                self.fail(where, "absolute user directory")
            if INTERNAL_LINK.search(value):
                self.fail(where, "private document link")
            if SECRET.search(value):
                self.fail(where, "credential-like value")
            address_value = value
            if not strict_utf8:
                address_value = re.sub(r"PresentationBuildTasks[\x00-\x20]{0,4}\d+\.\d+\.\d+\.\d+", "PresentationBuildTasks", address_value)
            if PRIVATE_IP.search(address_value):
                self.fail(where, "private network address")
            if emails and any(not allowed_email(m.group(1)) for m in EMAIL.finditer(value)):
                self.fail(where, "non-public author or contact email")

    def source(self, name, data):
        path = pathlib.PurePosixPath(name)
        if len(path.parts) == 2 and path.parts[0] == "app" and path.suffix == ".py" and path.stem not in self.policy["owned_python_modules"]:
            self.fail(name, "first-party Python module needs registration")
        if path.suffix.lower() in LOCAL_SUFFIXES or any(p.lower() in LOCAL_PARTS for p in path.parts) or name in self.policy["private_files"]:
            self.fail(name, "local data or internal draft")
            return
        if path.suffix.lower() in {".exe", ".dll", ".zip", ".tar", ".gz", ".7z", ".pdb"}:
            self.fail(name, "generated release file in Git")
            return
        if path.suffix.lower() in IMAGE_SUFFIXES:
            approved = self.policy["reviewed_images"].get(name, [])
            if hashlib.sha256(data).hexdigest() not in approved:
                self.fail(name, "image needs visual review and an approved SHA-256")
            return
        license_file = "license" in name.lower() or "licence" in name.lower() or name.endswith("NOTICE.md")
        self.text(data, name, emails=not license_file)
        if path.suffix.lower() == ".csproj":
            for company in re.findall(rb"<Company>([^<]+)</Company>", data):
                if company.decode("utf-8") not in self.policy["public_companies"]:
                    self.fail(name, "company metadata must use a public project identifier")

    def tracked(self, staged=False):
        args = ("ls-files", "-z") if not staged else ("diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z")
        for name in filter(None, git(self.root, *args).decode("utf-8").split("\0")):
            data = git(self.root, "show", ":" + name) if staged else (self.root / name).read_bytes()
            self.source(name, data)

    def bundle_inputs(self):
        tracked = set(filter(None, git(self.root, "ls-files", "-z").decode("utf-8").split("\0")))
        inputs = set(self.policy["public_data_files"])
        inputs.update(name[4:] for name in self.policy["reviewed_images"] if name.startswith("app/"))
        for folder in self.policy.get("packaged_data_directories", []):
            inputs.update(path.relative_to(self.root / "app").as_posix() for path in (self.root / "app" / folder).rglob("*") if path.is_file())
        for name in inputs:
            source = "app/" + name
            if source not in tracked:
                self.fail(source, "untracked packaged input")
                continue
            if name not in self.policy["public_data_files"] and source not in self.policy["reviewed_images"]:
                self.fail(source, "unregistered packaged input")
                continue
            self.source(source, (self.root / source).read_bytes())

    def identity(self, name, email, where):
        if name not in self.policy["public_git_names"]:
            self.fail(where, "Git identity needs an approved public name")
        domain = email.rsplit("@", 1)[-1]
        if not allowed_email(domain):
            self.fail(where, "Git identity must use a public email")

    def history(self, revisions=None):
        revisions = revisions or ["--all"]
        log = git(self.root, "log", *revisions, "--format=%H%n%an <%ae>%n%cn <%ce>%n%B")
        self.text(log, "Git commit metadata")
        identities = git(self.root, "log", *revisions, "--format=%an%x00%ae%x00%cn%x00%ce").decode("utf-8").splitlines()
        for row in identities:
            author, author_email, committer, committer_email = row.split("\0")
            self.identity(author, author_email, "Git commit author")
            self.identity(committer, committer_email, "Git commit committer")
        tag_metadata = git(self.root, "for-each-ref", "refs/tags", "--format=%(refname)%0a%(taggername) <%(taggeremail)>%0a%(contents)")
        self.text(tag_metadata, "Git tag metadata")
        for row in git(self.root, "for-each-ref", "refs/tags", "--format=%(objecttype)%00%(taggername)%00%(taggeremail)").decode("utf-8").splitlines():
            kind, name, email = row.split("\0")
            if kind == "tag":
                self.identity(name, email.strip("<>"), "Git tag author")
        objects = git(self.root, "rev-list", "--objects", *revisions).decode("utf-8").splitlines()
        proc = subprocess.Popen(["git", "-C", str(self.root), "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        try:
            for row in objects:
                oid, _, name = row.partition(" ")
                proc.stdin.write((oid + "\n").encode("ascii")); proc.stdin.flush()
                header = proc.stdout.readline().decode("ascii").split()
                if len(header) != 3:
                    raise ValueError("Cannot read reachable Git object")
                size = int(header[2]); data = proc.stdout.read(size); proc.stdout.read(1)
                if header[1] == "blob":
                    self.source(name, data)
        finally:
            proc.stdin.close()
            proc.stdout.close()
            proc.wait()

    def company(self, data, where):
        resources = pe_version_resources(data)
        if not resources:
            raise ValueError("Missing executable version metadata")
        data = b"".join(resources)
        self.text(data, where + "!version", strict_utf8=False)
        key = "CompanyName\0".encode("utf-16-le")
        start = 0
        while True:
            pos = data.find(key, start)
            if pos < 0:
                return
            start = pos + len(key)
            if pos < 6:
                continue
            length, words, kind = struct.unpack_from("<HHH", data, pos - 6)
            value_start = (pos + len(key) + 3) & ~3
            if kind != 1 or length < len(key) + 6 or words > 256:
                continue
            company = data[value_start:value_start + words * 2].decode("utf-16-le", errors="replace").rstrip("\0")
            if company and company not in self.policy["public_companies"]:
                self.fail(where, "company metadata must use a public project identifier")

    def asset(self, data, where, depth=0):
        if depth > 5 or len(data) > MAX_ENTRY:
            raise ValueError("Release container exceeds inspection limits")
        digest = hashlib.sha256(data).digest()
        if digest in self.seen_assets:
            return
        self.seen_assets.add(digest)
        suffix = pathlib.PurePosixPath(where.replace("\\", "/")).suffix.lower()
        if data.startswith(b"PK\x03\x04"):
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                for entry in archive.infolist():
                    if entry.is_dir():
                        continue
                    parts = pathlib.PurePosixPath(entry.filename.replace("\\", "/")).parts
                    if entry.file_size > MAX_ENTRY or ".." in parts or any(p.lower() in LOCAL_PARTS for p in parts) or pathlib.PurePosixPath(entry.filename).suffix.lower() in LOCAL_SUFFIXES:
                        self.fail(where + "!" + entry.filename, "unexpected local file in package")
                        continue
                    self.asset(archive.read(entry), where + "!" + entry.filename, depth + 1)
            return
        owned_binary = pathlib.PurePosixPath(where.rsplit("!", 1)[-1].replace("\\", "/")).name in self.policy["owned_binaries"]
        if data.startswith(b"MZ"):
            if not owned_binary:
                raise ValueError("Unregistered release executable")
            self.company(data, where)
            if self.policy["binary_format"] == "dotnet":
                self.dotnet(data, where)
            elif self.policy["binary_format"] == "pyinstaller":
                self.pyinstaller(data, where)
            else:
                raise ValueError("Unknown release binary format")
            return
        license_file = "license" in where.lower() or "licence" in where.lower() or "authors" in where.lower() or "copying" in where.lower()
        self.text(data, where, emails=not license_file)

    def dotnet(self, data, where):
        pos = data.find(BUNDLE)
        if pos < 8:
            raise ValueError("Cannot inspect .NET single-file manifest")
        offset = struct.unpack_from("<q", data, pos - 8)[0]
        f = io.BytesIO(data); f.seek(offset)
        major, minor, count = struct.unpack("<III", f.read(12))
        if major not in (2, 6) or not 0 < count < 10000:
            raise ValueError("Unsupported .NET single-file manifest")
        f.read(read7(f))
        f.read(40)
        owned = 0
        for _ in range(count):
            offset, size = struct.unpack("<qq", f.read(16))
            compressed = struct.unpack("<q", f.read(8))[0] if major >= 6 else 0
            f.read(1)
            name = f.read(read7(f)).decode("utf-8")
            if name not in self.policy["owned_binaries"]:
                continue
            owned += 1
            part = data[offset:offset + (compressed or size)]
            if compressed:
                part = zlib.decompress(part, -15)
            self.text(part, where + "!" + name, strict_utf8=False)
            for hit in re.finditer(b"MPDB", part):
                pdb = zlib.decompress(part[hit.start() + 8:], -15)
                for document in pdb_documents(pdb):
                    # Release source paths must be mapped, including generated WPF sources.
                    if not document.startswith("/_/"):
                        self.fail(where + "!" + name, "unmapped source path in embedded debug symbols")
                self.text(pdb, where + "!" + name + "!symbols", strict_utf8=False)
        if not owned:
            raise ValueError("Missing application assembly in .NET bundle")

    def pyinstaller(self, data, where):
        cookie = data.rfind(b"MEI\x0c\x0b\x0a\x0b\x0e")
        if cookie < 0:
            raise ValueError("Cannot inspect PyInstaller archive")
        _, length, toc_offset, toc_length, version, library = struct.unpack_from("!8sIIII64s", data, cookie)
        start = cookie + 88 - length
        toc = data[start + toc_offset:start + toc_offset + toc_length]
        pos = 0
        owned = set(self.policy["owned_python_modules"])
        while pos < len(toc):
            entry_length, offset, length, uncompressed, compressed, kind = struct.unpack_from("!IIIIBc", toc, pos)
            if entry_length < 18 or uncompressed > MAX_ENTRY:
                raise ValueError("Invalid PyInstaller entry")
            name = toc[pos + 18:pos + entry_length].rstrip(b"\0").decode("utf-8")
            pos += entry_length
            part = data[start + offset:start + offset + length]
            if compressed:
                part = zlib.decompress(part)
            if kind == b"s" and name in owned:
                self.compiled_code(part, where + "!" + name)
            if kind == b"z":
                if part[:4] != b"PYZ\0":
                    raise ValueError("Unsupported PYZ header")
                if part[4:8] != importlib.util.MAGIC_NUMBER:
                    raise ValueError("Inspect the executable with its build Python minor version")
                toc_pos = int.from_bytes(part[8:12], "big")
                items = marshal.loads(part[toc_pos:])
                items = items.items() if isinstance(items, dict) else items
                for module, (_, mod_offset, mod_size) in items:
                    if mod_size and module in owned:
                        code = zlib.decompress(part[mod_offset:mod_offset + mod_size])
                        self.compiled_code(code, where + "!PYZ!" + module)
            if kind in (b"b", b"x"):
                normalized = name.replace("\\", "/")
                location = where + "!" + name
                if normalized in self.policy["public_data_files"]:
                    self.text(part, location)
                elif "app/" + normalized in self.policy["reviewed_images"]:
                    self.source("app/" + normalized, part)
                elif normalized in self.policy.get("dependency_opaque_files", {}):
                    if hashlib.sha256(part).hexdigest() != self.policy["dependency_opaque_files"][normalized]:
                        self.fail(location, "registered dependency resource has changed")
                elif native_dependency_name(normalized) in {native_dependency_name(item) for item in self.policy.get("dependency_root_files", [])}:
                    if normalized != "base_library.zip" and not part.startswith(b"MZ"):
                        raise ValueError("Expected a registered native dependency")
                elif any(normalized.startswith(prefix) for prefix in self.policy.get("dependency_data_prefixes", [])):
                    if part.startswith(b"MZ"):
                        pe_version_resources(part)
                    elif not normalized.lower().endswith((".dll", ".pyd")):
                        if normalized.endswith(".deps.json"):
                            metadata = part.decode("utf-8")
                            metadata = re.sub(r'("(?:assemblyVersion|fileVersion)"\s*:\s*")\d+\.\d+\.\d+\.\d+', r'\g<1>version', metadata)
                            part = metadata.encode("utf-8")
                        self.text(part, location, emails=False)
                    else:
                        raise ValueError("Cannot inspect a native dependency")
                elif normalized == "LICENSE":
                    self.text(part, location, emails=False)
                else:
                    self.fail(location, "unregistered packaged data")
            elif kind == b"o":
                self.text(name.encode("utf-8"), where + "!runtime-option")
                if name not in self.policy.get("runtime_options", []):
                    self.fail(where, "unregistered runtime option")
            elif kind in (b"m", b"s") and name not in owned and name not in self.policy.get("bootstrap_modules", []):
                self.fail(where + "!" + name, "unregistered startup module")
            elif kind not in (b"m", b"s", b"z", b"o"):
                raise ValueError("Unsupported PyInstaller entry type")

    def compiled_code(self, data, where):
        code = marshal.loads(data)
        if not isinstance(code, types.CodeType):
            raise ValueError("Expected a Python code object")
        pending = [code]
        while pending:
            item = pending.pop()
            name = item.co_filename
            if pathlib.PureWindowsPath(name).is_absolute() or (name.startswith("/") and not name.startswith("/_/")):
                self.fail(where, "absolute source filename in Python code")
            pending.extend(value for value in item.co_consts if isinstance(value, types.CodeType))
        self.text(data, where, strict_utf8=False)

def pe_version_resources(data):
    if data[:2] != b"MZ" or len(data) < 64:
        raise ValueError("Invalid PE header")
    pe = int.from_bytes(data[60:64], "little")
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("Invalid PE signature")
    sections = int.from_bytes(data[pe + 6:pe + 8], "little")
    optional_size = int.from_bytes(data[pe + 20:pe + 22], "little")
    optional = pe + 24
    magic = int.from_bytes(data[optional:optional + 2], "little")
    directories = optional + (112 if magic == 0x20B else 96 if magic == 0x10B else 0)
    if directories == optional:
        raise ValueError("Unsupported PE optional header")
    resource_rva, resource_size = struct.unpack_from("<II", data, directories + 16)
    if not resource_rva:
        return []
    def offset(rva):
        table = optional + optional_size
        for index in range(sections):
            row = table + index * 40
            virtual_size, virtual_address, raw_size, raw_address = struct.unpack_from("<IIII", data, row + 8)
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                return raw_address + rva - virtual_address
        raise ValueError("Invalid PE resource RVA")
    base = offset(resource_rva)
    values = []
    def walk(relative, level=0):
        if level > 3:
            raise ValueError("Invalid PE resource nesting")
        directory = base + relative
        named, ids = struct.unpack_from("<HH", data, directory + 12)
        for index in range(named + ids):
            name, target = struct.unpack_from("<II", data, directory + 16 + index * 8)
            if level == 0 and name != 16:
                continue
            if target & 0x80000000:
                walk(target & 0x7FFFFFFF, level + 1)
            else:
                rva, size = struct.unpack_from("<II", data, base + target)
                start = offset(rva)
                values.append(data[start:start + size])
    walk(0)
    return values

def read7(stream):
    result = shift = 0
    for _ in range(5):
        byte = stream.read(1)[0]
        result |= (byte & 127) << shift
        if byte < 128:
            return result
        shift += 7
    raise ValueError("Invalid encoded length")

def compressed(blob, pos):
    byte = blob[pos]
    if byte < 128:
        return byte, pos + 1
    if byte < 192:
        return ((byte & 63) << 8) | blob[pos + 1], pos + 2
    return ((byte & 31) << 24) | int.from_bytes(blob[pos + 1:pos + 4], "big"), pos + 4

def pdb_documents(data):
    if data[:4] != b"BSJB":
        raise ValueError("Unsupported portable debug symbols")
    version_length = int.from_bytes(data[12:16], "little")
    pos = (16 + version_length + 3) & ~3
    count = int.from_bytes(data[pos + 2:pos + 4], "little")
    pos += 4
    streams = {}
    for _ in range(count):
        offset, size = struct.unpack_from("<II", data, pos); pos += 8
        end = data.index(0, pos); name = data[pos:end].decode("ascii"); pos = (end + 4) & ~3
        streams[name] = data[offset:offset + size]
    tables = streams.get("#~", streams.get("#-", b"")); blobs = streams.get("#Blob", b"")
    heap_sizes = tables[6]; valid = int.from_bytes(tables[8:16], "little"); pos = 24
    rows = {}
    for table in range(64):
        if valid >> table & 1:
            rows[table] = int.from_bytes(tables[pos:pos + 4], "little"); pos += 4
    if any(table < 48 for table in rows):
        raise ValueError("Unsupported debug symbol tables")
    blob_size = 4 if heap_sizes & 4 else 2
    guid_size = 4 if heap_sizes & 2 else 2
    def get_blob(index):
        size, first = compressed(blobs, index)
        return blobs[first:first + size]
    documents = []
    for _ in range(rows.get(48, 0)):
        index = int.from_bytes(tables[pos:pos + blob_size], "little"); pos += 2 * blob_size + 2 * guid_size
        value = get_blob(index); sep = chr(value[0]); parts = []; cursor = 1
        while cursor < len(value):
            index, cursor = compressed(value, cursor); parts.append(get_blob(index).decode("utf-8"))
        documents.append(sep.join(parts))
    return documents

def install_hooks(root, script):
    hook_dir = pathlib.Path(git(root, "rev-parse", "--git-path", "hooks").decode().strip())
    if not hook_dir.is_absolute():
        hook_dir = root / hook_dir
    hook_dir.mkdir(parents=True, exist_ok=True)
    for name, option in [("pre-commit", "--staged"), ("pre-push", "--history")]:
        path = hook_dir / name
        marker = "# public-content-privacy-guard"
        if path.exists() and marker not in path.read_text(encoding="utf-8"):
            raise ValueError("Existing Git hook must be integrated manually: " + name)
        relative = script.relative_to(root).as_posix()
        path.write_text('#!/bin/sh\n' + marker + '\nroot=$(git rev-parse --show-toplevel) || exit 1\n'
                        + f'test -f "$root/{relative}" || {{ echo "Privacy checker is missing; update this checkout before uploading." >&2; exit 1; }}\n'
                        + f'exec python "$root/{relative}" {option}\n', encoding="utf-8")
        path.chmod(0o755)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--history", action="store_true")
    parser.add_argument("--staged", action="store_true")
    parser.add_argument("--asset", type=pathlib.Path, action="append", default=[])
    parser.add_argument("--install-hooks", action="store_true")
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parent.parent
    guard = Guard(root)
    try:
        if args.install_hooks:
            install_hooks(root, pathlib.Path(__file__).resolve())
        if args.history:
            guard.history()
        elif not args.asset and not args.install_hooks:
            if args.staged:
                for role in ("GIT_AUTHOR_IDENT", "GIT_COMMITTER_IDENT"):
                    value = git(root, "var", role).decode("utf-8")
                    match = re.match(r"(.*) <([^>]+)>", value)
                    if not match:
                        raise ValueError("Cannot read Git identity")
                    guard.identity(match.group(1), match.group(2), "Pending commit")
            guard.tracked(staged=args.staged)
        for file in args.asset:
            guard.asset(file.read_bytes(), file.name)
    except (OSError, ValueError, EOFError, IndexError, KeyError, struct.error, zlib.error, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        raise SystemExit("Privacy inspection could not finish: " + type(error).__name__) from error
    if guard.errors:
        raise SystemExit("\n".join(sorted(guard.errors)[:30]))
    print("Public content privacy check passed")

if __name__ == "__main__":
    main()

import hashlib
import importlib.util
import io
import json
import marshal
import pathlib
import struct
import subprocess
import tempfile
import unittest
import zipfile
import zlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRIPT = next(path for path in (ROOT / "scripts/privacy_guard.py", ROOT / "tools/privacy_guard.py") if path.exists())
spec = importlib.util.spec_from_file_location("public_privacy_guard", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def user_path():
    return "C:" + "\\" + "Users" + "\\" + "Example" + "\\" + "project" + "\\" + "main.py"

def encoded_int(value):
    if value < 128:
        return bytes([value])
    return bytes([0x80 | value >> 8, value & 255])

def symbols(document):
    blobs = bytearray(b"\0")
    def add(value):
        index = len(blobs)
        blobs.extend(encoded_int(len(value)) + value)
        return index
    parts = document.split("/")
    indexes = [add(p.encode()) for p in parts]
    name = add(b"/" + b"".join(encoded_int(i) for i in indexes))
    tables = struct.pack("<IBBBBQQI", 0, 2, 0, 0, 1, 1 << 48, 0, 1) + struct.pack("<HHHH", name, 0, 0, 0)
    header = b"BSJB" + struct.pack("<HHII", 1, 1, 0, 4) + b"v1\0\0" + struct.pack("<HH", 0, 2)
    header_size = len(header) + 12 + 16
    return header + struct.pack("<II", header_size, len(tables)) + b"#~\0\0" + struct.pack("<II", header_size + len(tables), len(blobs)) + b"#Blob\0\0\0" + tables + blobs

def dotnet_bundle(document):
    pdb = symbols(document)
    compress = zlib.compressobj(wbits=-15)
    assembly = b"application\0MPDB" + struct.pack("<I", len(pdb)) + compress.compress(pdb) + compress.flush()
    data = bytearray(b"MZ" + b"\0" * 6 + b"\0" * 8 + module.BUNDLE)
    offset = len(data)
    data.extend(assembly)
    manifest = len(data)
    data.extend(struct.pack("<III", 6, 0, 1) + b"\1x" + b"\0" * 40)
    name = b"PublicProject.dll"
    data.extend(struct.pack("<qqqB", offset, len(assembly), 0, 1) + bytes([len(name)]) + name)
    struct.pack_into("<q", data, 8, manifest)
    return bytes(data)

def pyinstaller_archive(document, namespace=True, extra=()):
    code = marshal.dumps(compile("value = 1", document, "exec"))
    part = zlib.compress(code)
    pyz = b"PYZ\0" + importlib.util.MAGIC_NUMBER + struct.pack("!I", 12 + len(part)) + part
    entries = [("sample", (0, 12, len(part)))]
    if namespace:
        entries.insert(0, ("namespace", (3, 0, 0)))
    pyz += marshal.dumps(entries)
    compressed = zlib.compress(pyz)
    name = b"PYZ.pyz\0"
    entry = struct.pack("!IIIIBc", 18 + len(name), 0, len(compressed), len(pyz), 1, b"z") + name
    payload = bytearray(compressed)
    toc = bytearray(entry)
    for extra_name, content, kind in extra:
        packed = zlib.compress(content)
        encoded = extra_name.encode() + b"\0"
        toc.extend(struct.pack("!IIIIBc", 18 + len(encoded), len(payload), len(packed), len(content), 1, kind) + encoded)
        payload.extend(packed)
    package = bytes(payload + toc)
    return b"MZ" + package + struct.pack("!8sIIII64s", b"MEI\x0c\x0b\x0a\x0b\x0e", len(package) + 88, len(payload), len(toc), 313, b"python")


def pe_with_company(company, overlay=b""):
    image = bytearray(1024)
    image[:2] = b"MZ"
    struct.pack_into("<I", image, 60, 128)
    image[128:132] = b"PE\0\0"
    struct.pack_into("<H", image, 134, 1)
    struct.pack_into("<H", image, 148, 240)
    struct.pack_into("<H", image, 152, 0x20B)
    struct.pack_into("<II", image, 152 + 112 + 16, 0x1000, 512)
    struct.pack_into("<IIII", image, 152 + 240 + 8, 512, 0x1000, 512, 512)
    for relative, name, target in [(0, 16, 0x80000020), (32, 1, 0x80000040), (64, 1033, 96)]:
        struct.pack_into("<H", image, 512 + relative + 14, 1)
        struct.pack_into("<II", image, 512 + relative + 16, name, target)
    key = "CompanyName\0".encode("utf-16-le")
    value = (company + "\0").encode("utf-16-le")
    block = struct.pack("<HHH", 32 + len(value), len(value) // 2, 1) + key + b"\0\0" + value
    struct.pack_into("<II", image, 512 + 96, 0x1080, len(block))
    image[640:640 + len(block)] = block
    return bytes(image) + overlay

class PrivacyGuardTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.policy = {
            "public_companies": ["PublicProject"],
            "public_git_names": ["PublicAuthor"],
            "binary_format": "dotnet",
            "owned_binaries": ["PublicProject.exe", "PublicProject.dll"],
            "private_files": ["docs/MVP.md"],
            "reviewed_images": {},
            "owned_python_modules": ["sample"],
            "public_data_files": ["index.html"],
            "packaged_data_directories": ["knowledge", "reader"],
        }
        self.save_policy()

    def save_policy(self):
        (self.root / ".privacy-policy.json").write_text(json.dumps(self.policy), encoding="utf-8")
        self.guard = module.Guard(self.root)

    def test_rejects_plain_and_utf16_user_paths(self):
        for encoding in ("utf-8", "utf-16-le"):
            with self.subTest(encoding=encoding):
                self.guard.text(user_path().encode(encoding), "fixture", strict_utf8=False)
                self.assertTrue(self.guard.errors)

    def test_rejects_email_without_printing_its_value(self):
        value = "person" + "@" + "mail.example"
        self.guard.text(value.encode(), "fixture")
        self.assertTrue(self.guard.errors)
        self.assertNotIn(value, str(self.guard.errors))

    def test_rejects_unapproved_git_name_with_noreply_email(self):
        email = "123+public" + "@" + "users.noreply.github.com"
        self.guard.identity("PrivateAuthor", email, "fixture")
        self.assertTrue(self.guard.errors)
        self.guard = module.Guard(self.root)
        self.guard.identity("PublicAuthor", email, "fixture")
        self.assertFalse(self.guard.errors)

    def test_accepts_public_email_and_loopback(self):
        value = "123+project" + "@" + "users.noreply.github.com"
        self.guard.text((value + "\nhttp://127.0.0.1:8317\n").encode(), "fixture")
        self.assertFalse(self.guard.errors)

    def test_rejects_document_links_secrets_and_private_ips(self):
        values = ["https://" + "team.feishu.cn" + "/docx/example",
                  "ghp_" + "A" * 36, "10" + ".23.45.67"]
        for value in values:
            with self.subTest(value=value[:10]):
                guard = module.Guard(self.root)
                guard.text(value.encode(), "fixture")
                self.assertTrue(guard.errors)

    def test_checks_new_images_by_reviewed_hash(self):
        data = b"reviewed-image-fixture"
        self.guard.source("docs/example.png", data)
        self.assertTrue(self.guard.errors)
        self.policy["reviewed_images"]["docs/example.png"] = [hashlib.sha256(data).hexdigest()]
        self.save_policy()
        self.guard.source("docs/example.png", data)
        self.assertFalse(self.guard.errors)

    def test_rejects_internal_drafts_and_runtime_files(self):
        for name in ("docs/MVP.md", "docs/superpowers/spec.md", "runtime/data.json", "results/test.log"):
            guard = module.Guard(self.root)
            guard.source(name, b"content")
            self.assertTrue(guard.errors)

    def test_rejects_non_project_company(self):
        self.guard.source("app.csproj", b"<Company>PrivateAuthor</Company>")
        self.assertTrue(self.guard.errors)
        self.guard = module.Guard(self.root)
        self.guard.source("app.csproj", b"<Company>PublicProject</Company>")
        self.assertFalse(self.guard.errors)

    def test_inspects_text_inside_zip(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("readme.txt", user_path())
        self.guard.asset(stream.getvalue(), "release.zip")
        self.assertTrue(self.guard.errors)

    def test_rejects_archive_traversal_and_runtime_data(self):
        for name in ("../readme.txt", "runtime/data.json"):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, "w") as archive:
                archive.writestr(name, "content")
            guard = module.Guard(self.root)
            guard.asset(stream.getvalue(), "release.zip")
            self.assertTrue(guard.errors)

    def test_inspects_registered_executable_inside_zip(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("PublicProject.exe", pe_with_company("PublicProject"))
        self.guard.dotnet = lambda data, where: None
        self.guard.asset(stream.getvalue(), "release.zip")
        self.assertFalse(self.guard.errors)

    def test_reads_portable_pdb_document_table(self):
        path = "/_/src/main.cs"
        self.assertEqual(module.pdb_documents(symbols(path)), [path])

    def test_inspects_compressed_embedded_debug_symbols(self):
        path = "/home" + "/example/private/main.cs"
        self.guard.dotnet(dotnet_bundle(path), "fixture")
        self.assertTrue(self.guard.errors)
        self.guard = module.Guard(self.root)
        self.guard.dotnet(dotnet_bundle("/_/src/main.cs"), "fixture")
        self.assertFalse(self.guard.errors)

    def test_inspects_pyz_code_filename_and_empty_namespace(self):
        self.guard.pyinstaller(pyinstaller_archive(user_path()), "fixture")
        self.assertTrue(self.guard.errors)
        self.guard = module.Guard(self.root)
        self.guard.pyinstaller(pyinstaller_archive("sample.py"), "fixture")
        self.assertFalse(self.guard.errors)

    def test_rejects_unreadable_executable_instead_of_skipping_it(self):
        with self.assertRaises(ValueError):
            self.guard.asset(b"MZ" + b"\0" * 100, "PublicProject.exe")

    def test_rejects_absolute_python_source_filename_without_user_folder(self):
        path = "D:" + "\\" + "build" + "\\" + "sample.py"
        self.guard.pyinstaller(pyinstaller_archive(path), "fixture")
        self.assertTrue(self.guard.errors)

    def test_rejects_unregistered_compressed_data_for_binary_and_data_entry_types(self):
        for kind in (b"b", b"x"):
            for name, content in (("knowledge/notes.txt", user_path().encode()),
                                  ("knowledge/unknown.zip", b"unsupported archive")):
                guard = module.Guard(self.root)
                guard.pyinstaller(pyinstaller_archive("sample.py", extra=[(name, content, kind)]), "fixture")
                self.assertTrue(guard.errors)

    def test_inspects_registered_data_in_binary_entry(self):
        self.guard.pyinstaller(pyinstaller_archive("sample.py", extra=[("index.html", user_path().encode(), b"b")]), "fixture")
        self.assertTrue(self.guard.errors)

    def test_rejects_untracked_input_in_recursive_data_directory(self):
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        (self.root / "app/knowledge").mkdir(parents=True)
        (self.root / "app/knowledge/notes.txt").write_text("private fixture", encoding="utf-8")
        self.guard.bundle_inputs()
        self.assertTrue(any("untracked packaged input" in error for error in self.guard.errors))

    def test_registered_dependency_resource_requires_unchanged_digest(self):
        content = b"reviewed third-party resource"
        self.policy["dependency_opaque_files"] = {"webview/example.jar": hashlib.sha256(content).hexdigest()}
        self.save_policy()
        self.guard.pyinstaller(pyinstaller_archive("sample.py", extra=[("webview/example.jar", content, b"b")]), "fixture")
        self.assertFalse(self.guard.errors)
        self.guard = module.Guard(self.root)
        self.guard.pyinstaller(pyinstaller_archive("sample.py", extra=[("webview/example.jar", b"changed", b"b")]), "fixture")
        self.assertTrue(self.guard.errors)

    def test_scans_staged_bytes_instead_of_worktree_replacement(self):
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        file = self.root / "sample.txt"
        file.write_text(user_path(), encoding="utf-8")
        subprocess.run(["git", "-C", str(self.root), "add", "sample.txt"], check=True)
        file.write_text("clean", encoding="utf-8")
        self.guard.tracked(staged=True)
        self.assertTrue(self.guard.errors)

    def test_checks_author_metadata_from_history(self):
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        (self.root / "sample.txt").write_text("clean", encoding="utf-8")
        subprocess.run(["git", "-C", str(self.root), "add", "sample.txt"], check=True)
        email = "person" + "@" + "mail.example"
        subprocess.run(["git", "-C", str(self.root), "-c", "user.name=PublicAuthor",
                        "-c", "user.email=" + email, "commit", "-qm", "fixture"], check=True)
        self.guard.history()
        self.assertTrue(self.guard.errors)


    def test_reads_company_from_pe_resources_not_dependency_overlay(self):
        dependency = ("CompanyName\0" + "ThirdParty").encode("utf-16-le")
        data = pe_with_company("PublicProject", dependency)
        self.guard.company(data, "fixture")
        self.assertFalse(self.guard.errors)
        self.guard.company(pe_with_company("PrivateAuthor"), "fixture")
        self.assertTrue(self.guard.errors)

    def test_binary_build_tool_version_is_not_a_private_address(self):
        value = b"PresentationBuildTasks\t" + b"10" + b".0.13.0\0"
        self.guard.text(value, "fixture", strict_utf8=False)
        self.assertFalse(self.guard.errors)
        self.guard.text(value + b"http://" + b"10" + b".20.30.40", "fixture", strict_utf8=False)
        self.assertTrue(self.guard.errors)

if __name__ == "__main__":
    unittest.main()

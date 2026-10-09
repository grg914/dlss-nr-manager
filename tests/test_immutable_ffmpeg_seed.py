import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PUBLISHER = ROOT / "tools/publish-immutable-ffmpeg-seed.sh"

FAKE_GH = r'''#!/usr/bin/env python3
import hashlib, json, os, pathlib, sys
state = pathlib.Path(os.environ["FFMPEG_FAKE_STATE"])
assets = json.loads(state.read_text(encoding="utf-8"))
args = sys.argv[1:]
if args[0] == "api":
    if os.environ.get("FFMPEG_FAKE_API_FAIL") == "1":
        sys.exit(6)
    if os.environ.get("FFMPEG_FAKE_HIDE_ASSET") == "1":
        print(json.dumps({"draft": False, "assets": []}))
    else:
        print(json.dumps({"draft": False, "assets": assets}))
elif args[:2] == ["release", "upload"]:
    if os.environ.get("FFMPEG_FAKE_UPLOAD_FAIL") == "1":
        sys.exit(7)
    path = pathlib.Path(args[3])
    name = path.name
    if any(x["name"] == name for x in assets):
        print("duplicate asset", file=sys.stderr)
        sys.exit(8)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if os.environ.get("FFMPEG_FAKE_CORRUPT_DIGEST") == "1":
        digest = "f" * 64
    assets.append({"name": name, "digest": "sha256:" + digest, "size": path.stat().st_size})
    state.write_text(json.dumps(assets), encoding="utf-8")
else:
    print("unsupported fake gh command", args, file=sys.stderr)
    sys.exit(9)
'''


@unittest.skipUnless(shutil.which("bash") and shutil.which("unzip") and shutil.which("jq"),
                     "Requires bash, unzip, jq")
class ImmutableFfmpegSeedTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.fake_bin = self.root / "bin"
        self.fake_bin.mkdir()
        fake = self.fake_bin / "gh"
        fake.write_text(FAKE_GH, encoding="utf-8")
        fake.chmod(0o755)
        self.state = self.root / "assets.json"
        self.state.write_text("[]", encoding="utf-8")
        self.archive = self.root / "ffmpeg-dlssnr-win-x64.zip"
        self.make_zip("first")

    def make_zip(self, version):
        with zipfile.ZipFile(self.archive, "w") as z:
            z.writestr("ffmpeg.exe", f"ffmpeg-{version}")
            z.writestr("ffprobe.exe", f"ffprobe-{version}")
            z.writestr("LICENSE.txt", "FFmpeg")
        return hashlib.sha256(self.archive.read_bytes()).hexdigest()

    def run_publisher(self, **env_vars):
        env = dict(os.environ)
        env["PATH"] = str(self.fake_bin) + os.pathsep + env.get("PATH", "")
        env["FFMPEG_FAKE_STATE"] = str(self.state)
        env.update(env_vars)
        return subprocess.run(
            ["bash", str(PUBLISHER), "grg914/dlss-nr-manager", "runtime-seed-v1", str(self.archive)],
            capture_output=True, text=True, env=env, check=False,
        )

    def assets(self):
        return json.loads(self.state.read_text(encoding="utf-8"))

    def test_upload_idempotence_and_immutable_staging(self):
        initial = self.make_zip("initial")
        self.assertEqual(0, self.run_publisher().returncode)
        self.assertEqual(1, len(self.assets()))
        self.assertEqual("nu" if False else "ffmpeg-dlssnr-win-x64.zip", self.assets()[0]["name"])
        self.assertEqual("sha256:" + initial, self.assets()[0]["digest"])
        self.assertEqual(0, self.run_publisher().returncode)
        self.assertEqual(1, len(self.assets()))
        new_sha = self.make_zip("updated")
        staged = self.run_publisher()
        self.assertEqual(0, staged.returncode, staged.stderr)
        self.assertEqual(2, len(self.assets()))
        self.assertEqual("ffmpeg-dlssnr-win-x64.sha256-" + new_sha + ".zip", self.assets()[1]["name"])
        self.assertEqual("sha256:" + initial, self.assets()[0]["digest"])
        self.assertEqual(0, self.run_publisher().returncode)
        self.assertEqual(2, len(self.assets()))

    def test_digest_mismatch_rejected_without_overwrite(self):
        self.assertEqual(0, self.run_publisher().returncode)
        self.make_zip("second")
        self.assertEqual(0, self.run_publisher().returncode)
        assets = self.assets()
        assets[1]["digest"] = "sha256:" + "f" * 64
        self.state.write_text(json.dumps(assets), encoding="utf-8")
        result = self.run_publisher()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Refusing to replace conflicting", result.stderr)
        self.assertEqual(assets, self.assets())

    def test_archive_corruption_rejected_before_publish(self):
        self.archive.write_bytes(b"not a ZIP")
        result = self.run_publisher()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("integrity check failed", result.stderr)
        self.assertEqual([], self.assets())

    def test_failed_upload_leaves_old_canonical_asset(self):
        self.assertEqual(0, self.run_publisher().returncode)
        first = self.assets()
        self.make_zip("second")
        result = self.run_publisher(FFMPEG_FAKE_UPLOAD_FAIL="1")
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(first, self.assets())

    def test_missing_post_upload_asset_fails_closed(self):
        result = self.run_publisher(FFMPEG_FAKE_HIDE_ASSET="1")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("failed release SHA-256/size verification", result.stderr)


if __name__ == "__main__":
    unittest.main()

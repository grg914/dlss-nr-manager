import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class RuntimeSeedWriterPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.refresh = (ROOT / ".github/workflows/runtime-refresh.yml").read_text(encoding="utf-8")
        cls.nuget = (ROOT / ".github/workflows/nuget-seed.yml").read_text(encoding="utf-8")
        cls.publisher = (ROOT / "tools/publish-immutable-nuget-seed.ps1").read_text(encoding="utf-8")
        cls.prepare = (ROOT / "tools/prepare-offline-dotnet.ps1").read_text(encoding="utf-8")
        cls.pins = json.loads((ROOT / "manifests/runtime-seed-assets.json").read_text(encoding="utf-8"))

    def test_shared_non_cancelling_concurrency(self):
        for content in (self.refresh, self.nuget):
            self.assertIn("group: runtime-seed-v1-writers", content)
            self.assertIn("cancel-in-progress: false", content)
        self.assertNotIn("group: nuget-offline-seed", self.nuget)
        self.assertNotIn("cancel-in-progress: true", self.refresh)

    def test_nuget_writers_use_single_append_only_publisher(self):
        invocation = "tools/publish-immutable-nuget-seed.ps1"
        self.assertIn(invocation, self.nuget)
        self.assertIn(invocation, self.refresh)
        self.assertNotIn("--clobber", self.nuget)
        nuget_job = self.refresh.split("  nuget-runtime:", 1)[1].split("  java-runtime:", 1)[0]
        self.assertNotIn("gh release upload", nuget_job)
        self.assertNotIn("gh release upload $env:RUNTIME_SEED_TAG $path", self.nuget)

    def test_publisher_verifies_content_and_published_asset(self):
        self.assertIn("Get-FileHash -LiteralPath $source -Algorithm SHA256", self.publisher)
        self.assertIn('Assert-AssetIntegrity -Asset $canonical', self.publisher)
        self.assertIn('Assert-AssetIntegrity -Asset $verified', self.publisher)
        self.assertIn('New NuGet release asset failed SHA-256/size verification', self.publisher)
        self.assertIn("gh release upload $ReleaseTag $uploadPath --repo $Repository", self.publisher)
        self.assertNotIn("--clobber", self.publisher)

    def test_existing_nuget_asset_remains_pinned(self):
        self.assertIn('nuget-offline.sha256-$localSha.zip', self.publisher)
        self.assertIn("Canonical nuget-offline.zip remains pinned", self.publisher)
        self.assertIn('SKIP unchanged NuGet seed', self.publisher)
        self.assertIn("Immutable release asset", self.publisher)

    def test_reviewed_manifest_atomically_pins_nuget_consumer(self):
        self.assertEqual(1, self.pins["schema"])
        self.assertEqual("runtime-seed-v1", self.pins["release_tag"])
        pin = self.pins["nuget_offline"]
        self.assertEqual("nuget-offline.zip", pin["asset_name"])
        self.assertEqual(64, len(pin["sha256"]))
        self.assertGreater(pin["size"], 0)
        self.assertIn('manifests/runtime-seed-assets.json', self.prepare)
        self.assertIn('Pinned offline NuGet seed size/SHA-256 mismatch.', self.prepare)
        self.assertIn('Content-addressed NuGet asset name does not match', self.prepare)

    def test_remaining_runtime_seed_publishers_never_clobber(self):
        self.assertNotIn("gh release upload $env:RUNTIME_SEED_TAG $zip", self.refresh)
        self.assertIn("publish-append-only-runtime-seed.ps1", self.refresh)
        for name in ("publish-vlc-runtime.ps1", "publish-temurin25-runtime.ps1"):
            source = (ROOT / "tools" / name).read_text(encoding="utf-8")
            self.assertIn("publish-append-only-runtime-seed.ps1", source)
            self.assertNotIn("gh release upload", source)
        streamline = (ROOT / "tools/publish-streamline-runtime.ps1").read_text(encoding="utf-8")
        self.assertIn("create-deterministic-flat-zip.ps1", streamline)
        self.assertIn("publish-append-only-release-zip.ps1", streamline)
        self.assertNotIn("gh release upload", streamline)
        helper = (ROOT / "tools/publish-append-only-runtime-seed.ps1").read_text(encoding="utf-8")
        self.assertIn("Get-FileHash -LiteralPath $source -Algorithm SHA256", helper)
        self.assertIn("Find-ExactAsset -Release (Read-Release)", helper)
        self.assertIn("Published runtime seed SHA-256/size mismatch", helper)
        self.assertNotIn("gh release upload $ReleaseTag $upload --repo $Repository --clobber", helper)
        self.assertIn("-OnChanged Reject", (ROOT / "tools/publish-vlc-runtime.ps1").read_text(encoding="utf-8"))
        self.assertIn("-OnChanged StageImmutable", (ROOT / "tools/publish-temurin25-runtime.ps1").read_text(encoding="utf-8"))

    def test_openmp_publication_requires_license_attestation(self):
        self.assertIn("env.DLSSNR_OPENMP_REDIST_APPROVED == '1'", self.refresh)
        self.assertIn("Skipping Real-ESRGAN publication", self.refresh)


if __name__ == "__main__":
    unittest.main()

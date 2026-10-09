import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class RuntimeSeedWriterPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.refresh = (ROOT / ".github/workflows/runtime-refresh.yml").read_text(encoding="utf-8")
        cls.nuget = (ROOT / ".github/workflows/nuget-seed.yml").read_text(encoding="utf-8")
        cls.publisher = (ROOT / "tools/publish-immutable-nuget-seed.ps1").read_text(encoding="utf-8")

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
        self.assertNotIn("gh release upload $env:RUNTIME_SEED_TAG $zip", self.refresh)
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

    def test_openmp_publication_requires_license_attestation(self):
        self.assertIn("env.DLSSNR_OPENMP_REDIST_APPROVED == '1'", self.refresh)
        self.assertIn("Skipping Real-ESRGAN publication", self.refresh)


if __name__ == "__main__":
    unittest.main()

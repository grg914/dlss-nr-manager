import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
class RuntimeSeedWriterPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.refresh = (ROOT / ".github/workflows/runtime-refresh.yml").read_text(encoding="utf-8")
        cls.nuget = (ROOT / ".github/workflows/nuget-seed.yml").read_text(encoding="utf-8")

    def test_shared_non_cancelling_concurrency(self):
        for content in (self.refresh, self.nuget):
            self.assertIn("group: runtime-seed-v1-writers", content)
            self.assertIn("cancel-in-progress: false", content)
        self.assertNotIn("group: nuget-offline-seed", self.nuget)
        self.assertNotIn("cancel-in-progress: true", self.refresh)

    def test_release_asset_digest_and_size_guard(self):
        self.assertIn('SKIP unchanged NuGet seed', self.nuget)
        self.assertIn('Get-FileHash -LiteralPath $path -Algorithm SHA256', self.nuget)
        self.assertIn('uploaded.digest', self.nuget)
        self.assertIn('uploaded.size', self.nuget)
        self.assertIn('Existing NuGet asset has no GitHub digest', self.nuget)

    def test_openmp_publication_requires_license_attestation(self):
        self.assertIn("env.DLSSNR_OPENMP_REDIST_APPROVED == '1'", self.refresh)
        self.assertIn("Skipping Real-ESRGAN publication", self.refresh)

    def test_provenance_gate_before_legacy_clobber(self):
        old = self.nuget.index('gh release upload $env:RUNTIME_SEED_TAG $path')
        self.assertLess(self.nuget.index('Get-FileHash -LiteralPath $path'), old)
        self.assertLess(self.nuget.index('if ($asset -and [long]$asset.size'), old)
        self.assertLess(self.nuget.index('throw "Existing NuGet asset has no GitHub digest'), old)

if __name__ == "__main__":
    unittest.main()

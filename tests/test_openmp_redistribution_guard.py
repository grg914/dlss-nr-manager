import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]

class OpenMpRedistributionGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = (ROOT / ".github/workflows/runtime-refresh.yml").read_text("utf-8")
        cls.helper = (ROOT / "tools/package-approved-openmp.ps1").read_text("utf-8")

    def test_no_system32_dll_publishing(self):
        self.assertNotIn('$env:WINDIR\\System32\\vcomp140.dll', self.workflow)
        self.assertIn("DLSSNR_OPENMP_REDIST_APPROVED", self.workflow)
        self.assertIn("redistribution approval is missing", self.workflow)

    def test_helper_fail_closed_on_licensing(self):
        self.assertIn("if (-not $ApprovedForRedistribution)", self.helper)
        self.assertIn("VC/Redist/MSVC", self.helper)
        self.assertIn("debug_nonredist", self.helper)
        self.assertIn("Get-AuthenticodeSignature", self.helper)
        self.assertIn("Get-DescendantRelativePath", self.helper)
        self.assertNotIn("[IO.Path]::GetRelativePath(", self.helper)

    def test_digest_and_provenance_required(self):
        self.assertIn("Get-FileHash", self.helper)
        self.assertIn("MICROSOFT_OPENMP_PROVENANCE.json", self.helper)
        self.assertIn("publisher_subject", self.helper)
        self.assertIn("license_review_attested", self.helper)
        self.assertIn("file_version", self.helper)

if __name__ == "__main__":
    unittest.main()

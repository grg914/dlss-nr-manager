import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class RealEsrganNoOpenMpPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = (ROOT / ".github/workflows/runtime-refresh.yml").read_text("utf-8")
        cls.builder = (ROOT / "tools/build-realesrgan.ps1").read_text("utf-8")
        cls.audit = (ROOT / "tools/audit-realesrgan-package-openmp.ps1").read_text("utf-8")
        cls.qualifier = (
            ROOT / ".github/workflows/realesrgan-openmp-free-qualification.yml"
        ).read_text("utf-8")

    def test_openmp_disabled_for_every_realesrgan_build(self):
        self.assertIn("-DNCNN_OPENMP=OFF", self.builder)
        self.assertIn("-DCMAKE_DISABLE_FIND_PACKAGE_OpenMP=TRUE", self.builder)
        self.assertNotIn("if ($DisableOpenMp)", self.builder)
        self.assertNotIn("[switch]$DisableOpenMp", self.builder)
        self.assertIn("-BuildPath build-realesrgan-no-openmp -Clean", self.qualifier)

    def test_no_vcomp_redistribution_in_production_workflow(self):
        self.assertNotIn("package-approved-openmp.ps1", self.workflow)
        self.assertNotIn("DLSSNR_OPENMP_REDIST_APPROVED", self.workflow)
        self.assertNotIn("System32\\vcomp140.dll", self.workflow)
        self.assertIn("DLSSNR_REALESRGAN_RUNTIME_APPROVED", self.workflow)
        self.assertIn("audit-realesrgan-package-openmp.ps1", self.workflow)
        self.assertIn("audit-realesrgan-package-openmp.ps1", self.qualifier)
        release = (ROOT / ".github/workflows/release.yml").read_text("utf-8")
        self.assertIn("Enforce OpenMP-free Real-ESRGAN archive before V4 release", release)
        self.assertIn("audit-realesrgan-package-openmp.ps1", release)
        self.assertIn("Promote a verified OpenMP-free immutable asset first", release)
        self.assertIn("Get-ChildItem -LiteralPath $packageDir -File -Recurse", self.workflow)
        self.assertIn("NO_SHIPPED_PE_OPENMP_IMPORT_FOUND", self.qualifier)

    def test_audit_is_fail_closed_for_all_shipped_pe_files(self):
        self.assertIn("Get-ChildItem -LiteralPath $package -File -Recurse", self.audit)
        self.assertIn("dumpbin.exe", self.audit)
        self.assertIn("VC\\Tools\\MSVC\\*", self.audit)
        self.assertIn("if ($openMpImports.Count -gt 0)", self.audit)
        self.assertIn("No PE imports identified", self.audit)


if __name__ == "__main__":
    unittest.main()

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET


SCRIPT_DIR = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("legacy_select", SCRIPT_DIR / "select.py")
legacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(legacy)


class LegacySelectionTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="fluentdl-selection-test-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def test_templates_preserve_current_dependencies_including_new_logging_packages(self):
        target = self.root / "current.csproj"
        target.write_text(
            '<Project><ItemGroup><PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0"/>'
            '<PackageReference Include="YoutubeExplode" Version="6.6.2"/></ItemGroup></Project>'
        )
        for template in ("csproj_local.txt", "csproj_store.txt"):
            with self.subTest(template=template):
                output = self.root / "selected.csproj"
                legacy.update_csproj_safely(target, SCRIPT_DIR / template, output)
                references = ET.parse(output).getroot().findall("./ItemGroup/PackageReference")
                self.assertEqual(
                    [(item.get("Include"), item.get("Version")) for item in references],
                    [("Serilog.Extensions.Logging", "8.0.0"), ("YoutubeExplode", "6.6.2")],
                )

    def test_python_and_powershell_entrypoints_support_help(self):
        for command in (
            [sys.executable, str(SCRIPT_DIR / "select.py"), "--help"],
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
             "-File", str(SCRIPT_DIR / "select.ps1"), "-?"],
        ):
            result = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 0, result.stderr)
            if command[0] == sys.executable:
                self.assertIn("--mode", result.stdout)


if __name__ == "__main__":
    unittest.main()

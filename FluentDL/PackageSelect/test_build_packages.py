import argparse
from contextlib import redirect_stdout
import io
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

import build_packages as packaging


class PackagingTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(packaging.os.environ, {
            "FLUENTDL_PACKAGE_OUTPUT": "",
            "FLUENTDL_SANDBOX_SHARE": "",
            "FLUENTDL_MSBUILD": "",
        })
        environment.start()
        self.addCleanup(environment.stop)
        self.temporary = tempfile.TemporaryDirectory(prefix="fluentdl-packaging-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_unconfigured_paths_are_portable_and_disable_sandbox(self):
        args = packaging.parse_args(["--version", "3.8.0.0"])
        self.assertEqual(args.output, packaging.PROJECT_DIR / "artifacts")
        self.assertIsNone(args.sandbox_share)
        self.assertIsNone(args.msbuild)

    def test_environment_paths_are_used_and_expand_variables(self):
        with patch.dict(packaging.os.environ, {
            "FLUENTDL_PACKAGE_OUTPUT": str(self.root / "output"),
            "FLUENTDL_SANDBOX_SHARE": str(self.root / "sandbox"),
            "FLUENTDL_MSBUILD": "%FLUENTDL_TEST_TOOLS%\\MSBuild.exe",
            "FLUENTDL_TEST_TOOLS": str(self.root / "tools"),
        }):
            args = packaging.parse_args(["--version", "3.8.0.0"])
        self.assertEqual(args.output, self.root / "output")
        self.assertEqual(args.sandbox_share, self.root / "sandbox")
        self.assertEqual(args.msbuild, self.root / "tools" / "MSBuild.exe")

    def test_explicit_options_override_environment_paths(self):
        with patch.dict(packaging.os.environ, {
            key: str(self.root / "not-used")
            for key in ("FLUENTDL_PACKAGE_OUTPUT", "FLUENTDL_SANDBOX_SHARE", "FLUENTDL_MSBUILD")
        }):
            args = packaging.parse_args([
                "--version", "3.8.0.0", "--output", str(self.root / "explicit-output"),
                "--sandbox-share", str(self.root / "explicit-share"),
                "--msbuild", str(self.root / "explicit-msbuild"),
            ])
        self.assertEqual(args.output, self.root / "explicit-output")
        self.assertEqual(args.sandbox_share, self.root / "explicit-share")
        self.assertEqual(args.msbuild, self.root / "explicit-msbuild")

    def test_blank_environment_paths_are_unset(self):
        with patch.dict(packaging.os.environ, {"FLUENTDL_SANDBOX_SHARE": " \t "}):
            self.assertIsNone(packaging.environment_path("FLUENTDL_SANDBOX_SHARE"))
        self.assertIsNone(packaging.environment_path("FLUENTDL_MSBUILD"))

    def test_environment_paths_are_validated_before_build(self):
        with patch.dict(packaging.os.environ, {"FLUENTDL_SANDBOX_SHARE": str(self.root / "missing")}):
            args = packaging.parse_args(["--version", "3.8.0.0", "--channel", "Sideload"])
        with patch.object(packaging.subprocess, "run") as build, self.assertRaises(FileNotFoundError):
            packaging.build_packages(args)
        build.assert_not_called()

    def test_no_sandbox_overrides_environment_and_unset_share_needs_no_opt_out(self):
        for configured in (False, True):
            with self.subTest(configured=configured):
                with patch.dict(packaging.os.environ, {
                    "FLUENTDL_SANDBOX_SHARE": str(self.root / "nonexistent") if configured else "",
                }):
                    args = packaging.parse_args([
                        "--version", "3.8.0.0", "--channel", "Sideload", "--architecture", "x64",
                        "--output", str(self.root / str(configured)),
                        *(["--no-sandbox"] if configured else []),
                    ])
                with patch.object(packaging, "output_paths", return_value=[]) as paths, \
                        patch.object(packaging, "find_msbuild", side_effect=FileNotFoundError("test stop")), \
                        self.assertRaisesRegex(FileNotFoundError, "test stop"):
                    packaging.build_packages(args)
                self.assertIsNone(paths.call_args.args[-1])

    def fake_package(self, path, manifest, architecture, channel, signed=False):
        root = ET.parse(manifest).getroot()
        root.find(f"{packaging.PACKAGE_NS}Identity").set("ProcessorArchitecture", architecture)
        dependencies = root.find(f"{packaging.PACKAGE_NS}Dependencies")
        if channel == "Store":
            ET.SubElement(
                dependencies, f"{packaging.PACKAGE_NS}PackageDependency",
                Name="Microsoft.WindowsAppRuntime.1.7",
            )
        path.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("AppxManifest.xml", ET.tostring(root))
            if signed:
                archive.writestr("AppxSignature.p7x", b"signature")

    def test_versions_accept_valid_boundaries(self):
        for value in ("1.0.0.0", "3.8.0.0", "65535.65535.65535.65535"):
            with self.subTest(value=value):
                self.assertEqual(packaging.package_version(value), value)

    def test_versions_reject_invalid_input(self):
        for value in ("0.1.0.0", "3.8", "3.08.0.0", "3.8.0.-1", "3.8.0.65536", "..\\file", "3.8.0.0\n"):
            with self.subTest(value=value), self.assertRaises(argparse.ArgumentTypeError):
                packaging.package_version(value)

    def test_versioned_manifest_preserves_source_and_identity(self):
        source = packaging.SCRIPT_DIR / "sideload.appxmanifest.txt"
        before = source.read_bytes()
        destination = self.root / "manifest.xml"
        packaging.write_versioned_manifest(source, destination, "4.0.0.0", "Identity", "Version")
        identity = ET.parse(destination).getroot().find(f"{packaging.PACKAGE_NS}Identity")
        self.assertEqual(identity.get("Version"), "4.0.0.0")
        self.assertIn("OID.2.25", identity.get("Publisher"))
        self.assertEqual(source.read_bytes(), before)

    def test_versioned_manifest_rejects_missing_and_duplicate_identity(self):
        source = self.root / "source.xml"
        for content in ('<Package />', '<Package><Identity Version="1"/><Identity Version="2"/></Package>'):
            source.write_text(content)
            with self.assertRaises(ValueError):
                packaging.write_versioned_manifest(source, self.root / "out.xml", "4.0.0.0", "Identity", "Version")

    def test_commands_select_all_four_variants_and_raw_unsigned_msix(self):
        for channel in packaging.CHANNELS:
            for architecture in packaging.ARCHITECTURES:
                with self.subTest(channel=channel, architecture=architecture):
                    command = packaging.msbuild_command(
                        Path("MSBuild.exe"), channel, architecture,
                        self.root / "manifest", self.root / "app", self.root / "packages", self.root / "build.log",
                    )
                    self.assertIn(f"-p:RuntimeIdentifier=win-{architecture}", command)
                    self.assertIn(f"-p:PlatformTarget={architecture}", command)
                    self.assertIn("-p:WindowsAppSdkIncludeVersionInfo=true", command)
                    self.assertIn(f"-p:WindowsAppSDKSelfContained={str(channel == 'Sideload').lower()}", command)
                    self.assertIn(f"-p:AppxPackageIsForStore={str(channel == 'Store').lower()}", command)
                    self.assertIn("-p:BuildAppxUploadPackageForUap=false", command)
                    self.assertIn("-p:AppxPackageSigningEnabled=false", command)
                    self.assertIn("-p:AppxBundle=Never", command)

    def test_explicit_msbuild_path_is_validated(self):
        executable = self.root / "MSBuild.exe"
        executable.touch()
        self.assertEqual(packaging.find_msbuild(executable), executable)
        with self.assertRaises(FileNotFoundError):
            packaging.find_msbuild(self.root / "missing.exe")

    def test_package_validation_checks_identity_architecture_and_channel(self):
        manifest = packaging.SCRIPT_DIR / "store.appxmanifest.txt"
        package = self.root / "test.msix"
        self.fake_package(package, manifest, "arm64", "Store")
        packaging.validate_package(package, manifest, "arm64", "Store")
        for architecture, channel in (("x64", "Store"), ("arm64", "Sideload")):
            with self.assertRaises(ValueError):
                packaging.validate_package(package, manifest, architecture, channel)
        with self.assertRaises(ValueError):
            packaging.validate_package(package, packaging.SCRIPT_DIR / "sideload.appxmanifest.txt", "arm64", "Store")

    def test_package_validation_rejects_signed_output(self):
        manifest = packaging.SCRIPT_DIR / "sideload.appxmanifest.txt"
        package = self.root / "test.msix"
        self.fake_package(package, manifest, "x64", "Sideload", signed=True)
        with self.assertRaises(ValueError):
            packaging.validate_package(package, manifest, "x64", "Sideload")

    def test_zip_contains_versioned_folder_and_exact_payload(self):
        payload = self.root / "FluentDL_3.8.0.0_x64"
        payload.mkdir()
        (payload / "app.msix").write_bytes(b"package")
        archive = self.root / "release.zip"
        packaging.create_release_zip(payload, archive)
        with zipfile.ZipFile(archive) as result:
            self.assertEqual(result.namelist(), [f"{payload.name}/app.msix"])
            self.assertEqual(result.read(result.namelist()[0]), b"package")
        (payload / "unexpected").mkdir()
        with self.assertRaises(ValueError):
            packaging.create_release_zip(payload, archive)

    def test_output_paths_keep_store_out_of_sandbox(self):
        output, sandbox = self.root / "artifacts", self.root / "sandbox"
        paths = packaging.output_paths("3.8.0.0", packaging.CHANNELS, packaging.ARCHITECTURES, output, sandbox)
        self.assertEqual(len(paths), 10)
        self.assertEqual(len([path for path in paths if path.suffix == ".zip"]), 2)
        self.assertFalse(any("Store" in path.parts for path in paths if path.is_relative_to(sandbox)))
        folder = sandbox / "FluentDL_3.8.0.0_x64"
        folder.mkdir(parents=True)
        (folder / "old.msix").touch()
        with self.assertRaises(FileExistsError):
            packaging.output_paths("3.8.0.0", ("Sideload",), ("x64",), output, sandbox)

    def test_publish_preserves_unrelated_files_and_requires_overwrite(self):
        source, destination = self.root / "source", self.root / "destination"
        source.write_bytes(b"new")
        destination.write_bytes(b"old")
        unrelated = self.root / "unrelated"
        unrelated.write_bytes(b"keep")
        with self.assertRaises(FileExistsError):
            packaging.publish_files([(source, destination)], False)
        self.assertEqual(destination.read_bytes(), b"old")
        with redirect_stdout(io.StringIO()):
            packaging.publish_files([(source, destination)], True)
        self.assertEqual(destination.read_bytes(), b"new")
        self.assertEqual(unrelated.read_bytes(), b"keep")
        self.assertFalse(list(self.root.glob(".packaging-*")))

    def test_all_variants_build_and_stage_without_changing_sources(self):
        args = packaging.parse_args([
            "--version", "3.8.0.0", "--output", str(self.root / "artifacts"),
            "--sandbox-share", str(self.root),
        ])
        originals = {
            path: path.read_bytes()
            for path in (packaging.PROJECT_DIR / "FluentDL.csproj", packaging.PROJECT_DIR / "Package.appxmanifest",
                         packaging.PROJECT_DIR / "app.manifest")
        }

        def fake_build(command, **kwargs):
            properties = dict(argument[3:].split("=", 1) for argument in command if argument.startswith("-p:"))
            package_dir = Path(properties["AppxPackageDir"])
            name = f"FluentDL_{args.version}_{properties['Platform']}.msix"
            self.fake_package(
                package_dir / name,
                Path(properties["FluentDLPackageManifest"]),
                properties["Platform"], properties["FluentDLPackageChannel"],
            )
            dependency = package_dir / "Dependencies" / "runtime.msix"
            dependency.parent.mkdir()
            dependency.write_bytes(b"dependency must not be mistaken for the app")
            return subprocess.CompletedProcess(command, 0)

        with patch.object(packaging, "find_msbuild", return_value=Path("MSBuild.exe")), \
                patch.object(packaging.subprocess, "run", side_effect=fake_build) as build, \
                redirect_stdout(io.StringIO()):
            packaging.build_packages(args)
        self.assertEqual(build.call_count, 4)
        for architecture in packaging.ARCHITECTURES:
            name = f"FluentDL_3.8.0.0_{architecture}"
            self.assertTrue((args.output / args.version / "Store" / architecture / f"{name}.msix").is_file())
            with zipfile.ZipFile(args.output / args.version / "Sideload" / f"{name}.zip") as archive:
                self.assertEqual(len(archive.namelist()), 3)
                for file in (f"{name}.msix", *packaging.INSTALLERS):
                    self.assertEqual(archive.read(f"{name}/{file}"), (self.root / name / file).read_bytes())
        for path, original in originals.items():
            self.assertEqual(path.read_bytes(), original)
        self.assertFalse(list(args.output.glob(".packaging-*")))

    def test_failed_build_does_not_publish(self):
        args = packaging.parse_args(["--version", "3.8.0.0", "--output", str(self.root), "--no-sandbox"])
        with patch.object(packaging, "find_msbuild", return_value=Path("MSBuild.exe")), \
                patch.object(packaging.subprocess, "run", return_value=subprocess.CompletedProcess([], 1)), \
                redirect_stdout(io.StringIO()), self.assertRaisesRegex(RuntimeError, "Store/x64 build failed"):
            packaging.build_packages(args)
        self.assertFalse(list(self.root.rglob("*.msix")))
        self.assertFalse(list(self.root.rglob("*.zip")))
        self.assertFalse(list(self.root.glob(".packaging-*")))

    def test_invalid_store_revision_and_missing_sandbox_fail_before_build(self):
        for arguments in (
            ["--version", "3.8.0.1", "--no-sandbox"],
            ["--version", "3.8.0.0", "--sandbox-share", str(self.root / "missing")],
        ):
            with patch.object(packaging.subprocess, "run") as build, self.assertRaises((ValueError, FileNotFoundError)):
                packaging.build_packages(packaging.parse_args(arguments))
            build.assert_not_called()


if __name__ == "__main__":
    unittest.main()

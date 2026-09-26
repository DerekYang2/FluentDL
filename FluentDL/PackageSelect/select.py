import os
import re
import shutil
import argparse
import xml.etree.ElementTree as ET

def replace_version_in_file(filepath, new_version, pattern):
    """Replaces version strings in manifest files using Regex to preserve exact XML formatting."""
    if not os.path.exists(filepath):
        print(f"Warning: File not found: {filepath}")
        return False

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Replaces the version string inside the specified group pattern
    new_content, count = re.subn(
        pattern,
        r'\g<1>' + new_version + r'\g<2>',
        content,
        flags=re.IGNORECASE
    )

    if count > 0:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(new_content)
        return True
    return False

def update_csproj_safely(target_path, template_path, output_path):
    """
    Overwrites the csproj with the template, but preserves all <ItemGroup>
    blocks containing <PackageReference> from the original target.
    """
    if not os.path.exists(target_path) or not os.path.exists(template_path):
        print(f"Error: Missing csproj files. Target: {target_path}, Template: {template_path}")
        return

    # 1. Extract PackageReference groups from the current target
    target_tree = ET.parse(target_path)
    target_root = target_tree.getroot()
    pkg_ref_groups = []

    for item_group in target_root.findall('ItemGroup'):
        if item_group.find('PackageReference') is not None:
            pkg_ref_groups.append(item_group)

    # 2. Load the template we want to apply
    template_tree = ET.parse(template_path)
    template_root = template_tree.getroot()

    # 3. Strip any existing PackageReferences from the template to prevent duplication
    for item_group in template_root.findall('ItemGroup'):
        if item_group.find('PackageReference') is not None:
            template_root.remove(item_group)

    # 4. Inject the preserved PackageReferences into the new template
    for pr_group in pkg_ref_groups:
        template_root.append(pr_group)

    # 5. Format and save
    if hasattr(ET, 'indent'):
        ET.indent(template_tree, space="  ", level=0)

    template_tree.write(output_path, encoding='utf-8', xml_declaration=False)

def prompt_choice():
    print("Select Mode:")
    print("1. Store")
    print("2. Sideload")
    print("3. Restore")
    while True:
        choice = input("Choose one of the following modes (1-3 or name): ").strip().lower()
        if choice in ['1', 'store']: return 'Store'
        if choice in ['2', 'sideload']: return 'Sideload'
        if choice in ['3', 'restore']: return 'Restore'
        print("Invalid choice.")

def prompt_version():
    while True:
        version = input("Enter package Version (e.g. 3.4.0.0): ").strip()
        if version: return version
        print("Version cannot be empty.")

def main():
    parser = argparse.ArgumentParser(description="Switch FluentDL configurations.")
    parser.add_argument('-m', '--mode', choices=['Store', 'Sideload', 'Restore'], type=str.capitalize, help="Configuration mode")
    parser.add_argument('-v', '--version', type=str, help="Package version (e.g. 3.4.0.0)")
    args = parser.parse_args()

    mode = args.mode or prompt_choice()

    # Define Paths
    root = os.path.dirname(os.path.abspath(__file__))

    # Inputs
    manifests = {
        'Store': os.path.join(root, "store.appxmanifest.txt"),
        'Sideload': os.path.join(root, "sideload.appxmanifest.txt")
    }
    csprojs = {
        'Store': os.path.join(root, "csproj_store.txt"),
        'Sideload': os.path.join(root, "csproj_local.txt")
    }

    # Outputs
    canonical_dir = os.path.abspath(os.path.join(root, ".."))
    os.makedirs(canonical_dir, exist_ok=True)

    canonical_manifest = os.path.join(canonical_dir, "Package.appxmanifest")
    manifest_backup = os.path.join(root, "Package.appxmanifest.bak")

    canonical_app_manifest = os.path.join(canonical_dir, "app.manifest")
    app_manifest_backup = os.path.join(root, "app.manifest.bak")

    canonical_csproj = os.path.join(canonical_dir, "FluentDL.csproj")
    csproj_backup = os.path.join(root, "FluentDL.csproj.bak")

    # Regex Patterns
    pkg_manifest_pattern = r'(<Identity[^>]*?Version=")[^"]+(")'
    app_manifest_pattern = r'(<assemblyIdentity\b[^>]*\bversion=")[^"]+(")'

    if mode in ['Store', 'Sideload']:
        version = args.version or prompt_version()

        # 1. Handle Package.appxmanifest
        if os.path.exists(canonical_manifest):
            shutil.copy2(canonical_manifest, manifest_backup)

        temp_manifest = os.path.join(root, f"{mode.lower()}.appxmanifest.tmp")
        shutil.copy2(manifests[mode], temp_manifest)

        if not replace_version_in_file(temp_manifest, version, pkg_manifest_pattern):
            print(f"Warning: No Identity Version attribute found in {manifests[mode]}")

        shutil.move(temp_manifest, canonical_manifest)
        print(f"Selected {mode} manifest -> {canonical_manifest} (Version: {version})")

        # 2. Handle FluentDL.csproj
        if os.path.exists(canonical_csproj):
            shutil.copy2(canonical_csproj, csproj_backup)
            update_csproj_safely(canonical_csproj, csprojs[mode], canonical_csproj)
            print(f"Wrote {mode} csproj snippet (preserved NuGet packages) -> {canonical_csproj}")
        else:
            print(f"Warning: Target csproj not found at {canonical_csproj}. Cannot preserve packages.")

        # 3. Handle app.manifest
        if os.path.exists(canonical_app_manifest):
            shutil.copy2(canonical_app_manifest, app_manifest_backup)
            if replace_version_in_file(canonical_app_manifest, version, app_manifest_pattern):
                print(f"Updated app.manifest version -> {canonical_app_manifest} (Version: {version})")
            else:
                print(f"Warning: No assemblyIdentity version attribute found in {canonical_app_manifest}")
        else:
            print(f"Warning: app.manifest not found at {canonical_app_manifest}")

    elif mode == 'Restore':
        for backup, original, name in [
            (manifest_backup, canonical_manifest, "Package.appxmanifest"),
            (csproj_backup, canonical_csproj, "FluentDL.csproj"),
            (app_manifest_backup, canonical_app_manifest, "app.manifest")
        ]:
            if os.path.exists(backup):
                shutil.move(backup, original)
                print(f"Restored original {name} from backup.")
            else:
                print(f"No backup found to restore for {name}.")

if __name__ == "__main__":
    main()

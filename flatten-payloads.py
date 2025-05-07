import os
import shutil
import argparse
from pathlib import Path

def extract_uuid_chars(filename):
    """
    Extract the last two characters from the UUID part of the filename.
    The UUID is the first part of the filename before the first '-'.
    Example: For '000c4819c0b0e6000a35ce48f1b5668a-dflt_key-...', returns '8' and 'a'.
    """
    try:
        uuid_part = filename.split('-')[0]
        if len(uuid_part) >= 2:
            return uuid_part[-2], uuid_part[-1]
        return None, None
    except IndexError:
        return None, None

def transform_directory_structure(source_dir, target_dir, move_files=False):
    """
    Transform the directory structure from source to target.
    If move_files is True, files are moved; otherwise, they are copied.
    Starts by looping over tenant directories directly in the source directory.
    """
    source_path = Path(source_dir)
    target_path = Path(target_dir)

    # Ensure source directory exists
    if not source_path.exists():
        print(f"Error: Source directory '{source_dir}' does not exist.")
        return

    # Create target directory if it doesn't exist
    target_path.mkdir(parents=True, exist_ok=True)

    # Loop through tenant directories directly in source
    for tenant_dir in source_path.iterdir():
        if not tenant_dir.is_dir():
            continue
        print(f"Processing tenant: {tenant_dir.name}")

        # Create tenant directory in target
        target_tenant_dir = target_path / tenant_dir.name
        target_tenant_dir.mkdir(parents=True, exist_ok=True)

        # Loop through drives under tenant/drives
        drives_dir = tenant_dir / "drives"
        if not drives_dir.exists():
            continue

        for drive_dir in drives_dir.iterdir():
            if not drive_dir.is_dir():
                continue
            print(f"  Processing drive: {drive_dir.name}")

            # Create drive directory in target
            target_drive_dir = target_tenant_dir / "drives" / drive_dir.name
            target_drive_dir.mkdir(parents=True, exist_ok=True)

            # Loop through files in the drive's files directory
            files_dir = drive_dir / "files"
            if not files_dir.exists():
                continue

            for file_path in files_dir.rglob("*"):
                if file_path.is_file():
                    filename = file_path.name
                    char1, char2 = extract_uuid_chars(filename)

                    if char1 and char2:
                        # Create new 2-level subdirectory structure
                        target_file_dir = target_drive_dir / "files" / char1 / char2
                        target_file_dir.mkdir(parents=True, exist_ok=True)

                        # Define target file path
                        target_file_path = target_file_dir / filename

                        # Copy or move the file
                        if move_files:
                            print(f"    Moving {file_path} to {target_file_path}")
                            shutil.move(file_path, target_file_path)
                        else:
                            print(f"    Copying {file_path} to {target_file_path}")
                            shutil.copy2(file_path, target_file_path)
                    else:
                        print(f"    Skipping {filename}: Invalid UUID format")

def main():
    parser = argparse.ArgumentParser(description="Transform directory structure.")
    parser.add_argument("source_dir", help="Source directory path (containing tenant directories)")
    parser.add_argument("target_dir", help="Target directory path for the transformed structure")
    parser.add_argument("--move", action="store_true", help="Move files instead of copying")
    args = parser.parse_args()

    transform_directory_structure(args.source_dir, args.target_dir, move_files=args.move)

if __name__ == "__main__":
    main()
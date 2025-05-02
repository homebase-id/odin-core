import os
import sys
import shutil
import time
from pathlib import Path
from datetime import timedelta

DRY_RUN = False  # Set to False to actually move files

# Source and destination roots
SOURCE_ROOT = Path("/home/seb/tmp/dot-temp")
DESTINATION_ROOT = Path("/home/seb/tmp/dot-temp-restructured")  # Change this to your desired destination

def collect_file_paths():
    """
    Collect all relative file paths from the source directory.
    
    Returns:
        list: A list of Path objects representing all files in SOURCE_ROOT (relative paths).
    """
    relative_paths = []
    
    # Walk through all files in the source directory
    for root, _, files in os.walk(SOURCE_ROOT):
        for file in files:
            absolute_path = Path(root) / file
            # Create relative path from SOURCE_ROOT
            relative_path = absolute_path.relative_to(SOURCE_ROOT)
            relative_paths.append(relative_path)
    
    return relative_paths

def analyze_paths(file_paths):
    """
    Analyze the collected paths to verify the structure.
    
    Args:
        file_paths (list): List of relative Path objects to analyze.
        
    Returns:
        bool: True if all files match expected patterns, False otherwise.
    """
    print(f"Found {len(file_paths)} files in total.")
    
    # Check how many files match our expected patterns
    uploads_pattern = 0
    inbox_pattern = 0
    other_pattern = 0
    
    for path in file_paths:
        path_parts = list(path.parts)
        
        if len(path_parts) >= 6:  # Ensure minimum path depth
            if 'uploads' in path_parts:
                uploads_pattern += 1
            elif 'inbox' in path_parts:
                inbox_pattern += 1
            else:
                other_pattern += 1
        else:
            other_pattern += 1
    
    print(f"Files matching 'uploads' pattern: {uploads_pattern}")
    print(f"Files matching 'inbox' pattern: {inbox_pattern}")
    print(f"Files not matching expected patterns: {other_pattern}")
    
    # Return False if any files don't match expected patterns
    if other_pattern > 0:
        print("\nWarning: Some files don't match the expected patterns!")
        return False
    else:
        return True

def move_files(file_paths):
    """
    Move files to their new structure, preserving timestamps.
    Original: temp/tenant/drives/drive/uploads|inbox/a/b/c/d/filename
    New: registrations/tenant/temp/drives/drive/uploads|inbox/filename
    
    Args:
        file_paths (list): List of relative Path objects to move.
    """
    total_files = len(file_paths)
    print(f"\nMoving {total_files} files to new structure...")
    
    # Counter for successful moves
    moved_count = 0
    skipped_count = 0
    
    # Progress reporting threshold
    progress_interval = 1000
    
    for rel_path in file_paths:
        # Get absolute source path
        source_path = SOURCE_ROOT / rel_path
        
        path_parts = list(rel_path.parts)
        if len(path_parts) != 9:
            skipped_count += 1
            continue
        
        # Extract the components - path_parts[0] should be 'temp'
        tenant = path_parts[0]
        drive = path_parts[2]
        category = path_parts[3]  # 'uploads' or 'inbox'
        filename = path_parts[8]  # The last part is the filename
               
        # Construct new path: registrations/tenant/temp/drives/drive/uploads|inbox/filename
        destination_path = DESTINATION_ROOT / tenant / 'temp' / 'drives' / drive / category / filename

        try:
            if DRY_RUN: print(f"DRY RUN: Moving {source_path} to {destination_path}")

            # Create parent directories if they don't exist
            if not DRY_RUN: destination_path.parent.mkdir(parents=True, exist_ok=True)

            # Copy the file, preserving metadata (timestamps, permissions, etc.)
            if not DRY_RUN: shutil.copy2(source_path, destination_path)
            
            # Remove the original after successful copy
            if not DRY_RUN: os.remove(source_path)
            
            # Increment counter
            moved_count += 1
            
            # Report progress every 1000 files
            if moved_count % progress_interval == 0:
                print(f"Moved {moved_count} out of {total_files} files...")
                
        except Exception as e:
            print(f"Error processing {source_path}: {str(e)}")
            skipped_count += 1
    
    print(f"Operation complete. Moved {moved_count} files, skipped {skipped_count} files.")

if __name__ == "__main__":
    overall_start_time = time.time()

    print(f"Collecting files from {SOURCE_ROOT}")
    files = collect_file_paths()
    
    if not analyze_paths(files):
        print("Exiting without processing files due to unexpected patterns.")
        sys.exit(1)
    
    print("\nAll files follow expected patterns. Proceeding with file moves.")
    
    # Ensure destination root exists
    DESTINATION_ROOT.mkdir(parents=True, exist_ok=True)
    
    # Move the files
    move_files(files)

    # Calculate and display total elapsed time
    overall_time = time.time() - overall_start_time
    print(f"\nTotal script execution time: {timedelta(seconds=int(overall_time))}")
#!/bin/bash

# Set source and destination root directories
SOURCE_ROOT="/home/seb/tmp/dot-temp"
DEST_ROOT="/home/seb/tmp/dot-re-temp"

# Initialize counters
file_counter=0
total_moved=0

process_file() {
    local file="$1"

    # Use regex to extract components
    # Pattern: /path/to/TENANT/drives/DRIVE/LOCATION/DIR1/DIR2/DIR3/DIR4/FILENAME
    if [[ "$file" =~ $SOURCE_ROOT/([^/]+)/drives/([^/]+)/([^/]+)/[^/]+/[^/]+/[^/]+/[^/]+/([^/]+)$ ]]; then
        local tenant="${BASH_REMATCH[1]}"
        local drive="${BASH_REMATCH[2]}"
        local location="${BASH_REMATCH[3]}"
        local filename="${BASH_REMATCH[4]}"

        # Create new destination path
        local dest_path="$DEST_ROOT/registrations/$tenant/temp/drives/$drive/$location/$filename"

        # Create destination directory if it doesn't exist
        mkdir -p "$(dirname "$dest_path")"

        # Move the file
        mv "$file" "$dest_path"

        # Increment the counter for successfully moved files
        ((total_moved++))
        return 0
    else
        echo "Warning: File $file doesn't match expected path structure, skipping."
        echo "Path: $file"
        return 1
    fi
}

echo "Starting file restructuring..."
echo "Finding files to process..."
start_time=$(date +%s)

# Collect all matching files into an array
mapfile -t files < <(find "$SOURCE_ROOT" -path "*/drives/*/uploads/*/*/*/*/*" -type f -o \
    -path "*/drives/*/inbox/*/*/*/*/*" -type f)

total_files=${#files[@]}
echo "Found $total_files files to process."

# Process each file in the array
for file in "${files[@]}"; do
    process_file "$file"
    ((file_counter++))

    # Report progress every 1000 files
    if ((file_counter % 1000 == 0)); then
        echo "Progress: Processed $file_counter of $total_files files, moved $total_moved files"
    fi
done

# Report final statistics
end_time=$(date +%s)
duration=$((end_time - start_time))
echo "File restructuring complete!"
echo "Total files processed: $file_counter"
echo "Total files moved: $total_moved"
echo "Time taken: $duration seconds"

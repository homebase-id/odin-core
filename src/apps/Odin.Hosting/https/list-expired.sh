#!/bin/bash

# Directory to start searching from (default is current directory)
SEARCH_DIR=${1:-.}

echo "Searching for expired certificates in $SEARCH_DIR"
echo "Current date: $(date)"
echo "----------------------------------------"

# Counter for expired certificates
expired_count=0

# Find all .crt files and check their expiration dates
find "$SEARCH_DIR" -type f -name "*.crt" | while read -r cert_file; do
    # Get the end date of the certificate
    end_date=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)

    # Skip if openssl couldn't read the certificate
    if [ -z "$end_date" ]; then
        echo "Warning: Could not read certificate: $cert_file"
        continue
    fi

    # Convert dates to seconds since epoch for comparison
    end_date_sec=$(date -d "$end_date" +%s 2>/dev/null)
    current_date_sec=$(date +%s)

    # Check if conversion was successful
    if [ -z "$end_date_sec" ]; then
        echo "Warning: Could not parse date in certificate: $cert_file"
        continue
    fi

    # Compare dates
    if [ "$end_date_sec" -lt "$current_date_sec" ]; then
        echo "Expired certificate found:"
        echo "File: $cert_file"
        echo "Expired on: $end_date"
        echo "----------------------------------------"
    fi
done

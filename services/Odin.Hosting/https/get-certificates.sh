#!/usr/bin/env bash
set -e

#  copy certificates from certbot location
rsync -v -r \
    --copy-links \
    --include '*/' \
    --include 'fullchain.pem' \
    --include 'privkey.pem' \
    --exclude '*' \
    dns.id.pub:/letsencrypt-certificates/* ./
    
# rename files
find . -type f -name "fullchain.pem" -exec sh -c 'mv "$0" "${0%/*}/certificate.crt"' {} \;
find . -type f -name "privkey.pem" -exec sh -c 'mv "$0" "${0%/*}/private.key"' {} \;


#!/usr/bin/env bash
set -e

#  copy certificates from certbot location
rsync -v -r \
    --copy-links \
    --include '*/' \
    --include 'fullchain-with-root.pem' \
    --include 'privkey.pem' \
    --exclude '*' \
    dns.id.pub:/letsencrypt-certificates/* ./
    
# rename files
find . -type f -name "fullchain-with-root.pem" -exec sh -c 'mv "$0" "${0%/*}/certificate.crt"' {} \;
find . -type f -name "privkey.pem" -exec sh -c 'mv "$0" "${0%/*}/private.key"' {} \;


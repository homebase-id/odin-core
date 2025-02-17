#!/usr/bin/env bash
set -e

#
# copy certificates from certbot location
#
rsync -v -r \
    --copy-links \
    --include '*/' \
    --include 'fullchain.pem' \
    --include 'privkey.pem' \
    --exclude '*' \
    dns.id.pub:/letsencrypt-certificates/* ./

#
# rename files
#
find . -type f -name "fullchain.pem" -exec sh -c 'mv "$0" "${0%/*}/certificate.crt"' {} \;
find . -type f -name "privkey.pem" -exec sh -c 'mv "$0" "${0%/*}/private.key"' {} \;

#
# copy to front end
#
TARGET_DIR="../../../../../odin-js"

if [ -d "$TARGET_DIR" ]; then
    cp ./dev.dotyou.cloud/certificate.crt "$TARGET_DIR/dev-dotyou-cloud.crt"
    cp ./dev.dotyou.cloud/private.key "$TARGET_DIR/dev-dotyou-cloud.key"

    if [ -d "$TARGET_DIR/packages/apps/login-app" ]; then
        cp -r ./anon.dotyou.cloud "$TARGET_DIR/packages/apps/login-app"
    fi
else
    echo "Front end directory not found: $TARGET_DIR. You must copy the certificates manually."
fi

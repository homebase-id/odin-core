#  copy certificates from certbot location
rsync -v -r \
    --copy-links \
    --checksum \
    --include '*/' \
    --include 'fullchain.pem' \
    --include 'privkey.pem' \
    --exclude '*' \
    root@d0d9ad6.online-server.cloud:/root/odin-certbot/out/etc/letsencrypt/live/* ./
    
# rename files
find . -type f -name "fullchain.pem" -exec sh -c 'mv "$0" "${0%/*}/certificate.crt"' {} \;
find . -type f -name "privkey.pem" -exec sh -c 'mv "$0" "${0%/*}/private.key"' {} \;


rsync -v -r \
    --copy-links \
    --checksum \
    --include '*/' \
    --include 'fullchain.pem' \
    --include 'privkey.pem' \
    --exclude '*' \
    root@d0d9ad6.online-server.cloud:/root/odin-certbot/out/etc/letsencrypt/live/* 127.0.0.1/
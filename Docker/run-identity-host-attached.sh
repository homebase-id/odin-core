docker run \
  --rm \
  --name identity-host \
  --env ASPNETCORE_ENVIRONMENT=development \
  -p 80:80 \
  -p 443:443 \
  dotyou:local

# docker exec identity-host du -h
# docker exec --interactive --tty identity-host bash

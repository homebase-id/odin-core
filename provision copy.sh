#!/bin/bash
set -eou pipefail

docker run \
    --name identity-host \
    --rm \
    --env CertificateRenewal__CertificateAuthorityAssociatedEmail="noreply@homebase.id" \
    --env CertificateRenewal__UseCertificateAuthorityProductionServers="True" \
    --env Host__CacheSlidingExpirationSeconds="15" \
    --env Host__IPAddressListenList__0__HttpPort="80" \
    --env Host__IPAddressListenList__0__HttpsPort="443" \
    --env Host__IPAddressListenList__0__Ip="*" \
    --env Host__SystemDataRootPath="$HOME/tmp/dotyou/system" \
    --env Host__TenantDataRootPath="$HOME/tmp/dotyou/tenants" \
    --env Job__EnsureCertificateProcessorIntervalSeconds="3600" \
    --env Job__InboxOutboxReconciliationIntervalSeconds="43200" \
    --env Job__JobCleanUpIntervalSeconds="14400" \
    --env Logging__LogFilePath="$HOME/tmp/dotyou/logs" \
    --env Registry__DnsRecordValues__ApexAliasRecord="your-apex-alias-record.com" \
    --env Registry__DnsRecordValues__ApexARecords__0="123.123.123.123" \
    --env Registry__InvitationCodes__0="secret!" \
    --env Registry__ProvisioningDomain="your-provisioning-domain-here.com" \
    --env Registry__ProvisioningEnabled="True" \
    --env Serilog__MinimumLevel__Default="Debug" \
    --env Serilog__MinimumLevel__Override__Microsoft="Information" \
    --env Serilog__MinimumLevel__Override__Microsoft.AspNetCore="Warning" \
    --env Serilog__MinimumLevel__Override__Microsoft.AspNetCore.Authentication="Error" \
    --publish 80:80 \
    --publish 443:443 \
    --publish 4444:4444 \
    --volume /opt/homebase:/homebase \
    --pull always \
    ghcr.io/homebase-id/odin-core:latest

#!/bin/bash
set -eou pipefail

###############################################################################
#
# main
#
###############################################################################

main() {
  echo 
  echo "Homebase Installer (Docker)"
  echo 
  echo "This is a very basic first-time installer for Homebase."
  echo "We're using Docker and once installed, you will need to use"
  echo "your normal Docker tools to start/stop/update etc."
  echo

  #
  # Check that Docker is installed
  #
  if ! command -v docker &> /dev/null; then
      echo "Error: Docker is not installed. Please install Docker and try again."
      exit 1
  fi

  #
  # Check we have sufficent privileges to run Docker commands
  #
  if ! docker info > /dev/null 2>&1; then
      echo "Error: You do not have sufficient access to interact with Docker."
      exit 1
  fi

  # We can only run on port 80 and 443 for the time being
  ODIN_HTTP_PORT=80
  ODIN_HTTPS_PORT=443
  ODIN_ADMIN_PORT=4444

  # Docker image
  if [[ -z "${ODIN_DOCKER_IMAGE:-}" ]]; then
      ODIN_DOCKER_IMAGE="ghcr.io/homebase-id/odin-core:latest"
      read -p "Homebas Docker image [${ODIN_DOCKER_IMAGE}]: " input
      ODIN_DOCKER_IMAGE="${input:-$ODIN_DOCKER_IMAGE}"
  fi

  # Input container name
  if [[ -z "${ODIN_CONTAINER_NAME:-}" ]]; then
      ODIN_CONTAINER_NAME="identity-host"
      read -p "Docker container name [${ODIN_CONTAINER_NAME}]: " input
      ODIN_CONTAINER_NAME="${input:-$ODIN_CONTAINER_NAME}"
  fi

  # Input root path
  if [[ -z "${ODIN_ROOT_PATH:-}" ]]; then
      ODIN_ROOT_PATH="/srv/homebase/${ODIN_CONTAINER_NAME}"
      read -p "Root dirctory [${ODIN_ROOT_PATH}]: " input
      ODIN_ROOT_PATH="${input:-$ODIN_ROOT_PATH}"
  fi

  echo
  echo --------------------------------------------------------------------------
  echo
  echo "Docker image:             ${ODIN_DOCKER_IMAGE}"
  echo "Docker container name:    ${ODIN_CONTAINER_NAME}"
  echo "Root direcotory:          ${ODIN_ROOT_PATH}"
  echo
  echo --------------------------------------------------------------------------
  echo

  continue_install=$(prompt_choice "Start Docker container now? (y/n):" "y" "yes" "n" "no")
  if [[ "$continue_install" != "y" && "$continue_install" != "yes" ]]; then
      exit 1
  fi

  ###############################################################################

  docker run \
      --name "${ODIN_CONTAINER_NAME}" \
      --detach \
      --restart always \
      --env Admin__ApiEnabled='False' \
      --env Admin__ApiKey='your-secret-api-key-here' \
      --env Admin__ApiKeyHttpHeaderName='Odin-Admin-Api-Key' \
      --env Admin__ApiPort="${ODIN_ADMIN_PORT}" \
      --env Admin__Domain='your-admin-domain-here.example.com' \
      --env Admin__ExportTargetPath='/tmp/odin-export' \
      --env CertificateRenewal__CertificateAuthorityAssociatedEmail='your-certificate-email-here@example.com' \
      --env CertificateRenewal__UseCertificateAuthorityProductionServers='True' \
      --env Host__CacheSlidingExpirationSeconds='15' \
      --env Host__Http1Only='False' \
      --env Host__IPAddressListenList__0__HttpPort="${ODIN_HTTP_PORT}" \
      --env Host__IPAddressListenList__0__HttpsPort="${ODIN_HTTPS_PORT}" \
      --env Host__IPAddressListenList__0__Ip='*' \
      --env Host__ReportContentUrl='your-report-content-url-here' \
      --env Host__ShutdownTimeoutSeconds='30' \
      --env Host__SystemDataRootPath='/homebase/system' \
      --env Host__TenantDataRootPath='/homebase/tenants' \
      --env Job__EnsureCertificateProcessorIntervalSeconds='3600' \
      --env Job__InboxOutboxReconciliationIntervalSeconds='43200' \
      --env Job__JobCleanUpIntervalSeconds='14400' \
      --env Logging__LogFilePath='/homebase/logs' \
      --env Mailgun__ApiKey='your-mailgun-api-key-here' \
      --env Mailgun__DefaultFromEmail='your-default-email-from-address-here@example.com' \
      --env Mailgun__EmailDomain='your-mailgun-email-domain-here' \
      --env Mailgun__Enabled='False' \
      --env PushNotification__BaseUrl='https://push.homebase.id' \
      --env Registry__DnsRecordValues__ApexAliasRecord='identity-host.sebbarg.net' \
      --env Registry__DnsRecordValues__ApexARecords__0='131.164.170.62' \
      --env Registry__DnsResolvers__0='1.1.1.1' \
      --env Registry__DnsResolvers__1='8.8.8.8' \
      --env Registry__DnsResolvers__2='9.9.9.9' \
      --env Registry__DnsResolvers__3='208.67.222.222' \
      --env Registry__InvitationCodes__0='anabolicfrolic' \
      --env Registry__ManagedDomainApexes__0__Apex='demo.rocks' \
      --env Registry__ManagedDomainApexes__0__PrefixLabels__0='First' \
      --env Registry__ManagedDomainApexes__0__PrefixLabels__1='Surname' \
      --env Registry__ManagedDomainApexes__1__Apex='id.pub' \
      --env Registry__ManagedDomainApexes__1__PrefixLabels__0='First' \
      --env Registry__ManagedDomainApexes__1__PrefixLabels__1='Surname' \
      --env Registry__ManagedDomainApexes__2__Apex='indie.social' \
      --env Registry__ManagedDomainApexes__2__PrefixLabels__0='Name' \
      --env Registry__PowerDnsApiKey='replace_with_top_secret_powerdns_api_key' \
      --env Registry__PowerDnsHostAddress='dns.id.pub' \
      --env Registry__ProvisioningDomain='provisioning.dotyou.cloud' \
      --env Registry__ProvisioningEmailLogoHref='https://homebase.id/' \
      --env Registry__ProvisioningEmailLogoImage='https://homebase.id/logo-email.png' \
      --env Serilog__MinimumLevel__Default='Debug' \
      --env Serilog__MinimumLevel__Override__Microsoft.AspNetCore.Authentication='Error' \
      --env Serilog__MinimumLevel__Override__Microsoft.AspNetCore='Warning' \
      --env Serilog__MinimumLevel__Override__Microsoft='Information' \
      --publish ${ODIN_HTTP_PORT}:${ODIN_HTTP_PORT} \
      --publish ${ODIN_HTTPS_PORT}:${ODIN_HTTPS_PORT} \
      --publish ${ODIN_ADMIN_PORT}:${ODIN_ADMIN_PORT} \
      --volume ${ODIN_ROOT_PATH}:/homebase \
      --pull always \
      ${ODIN_DOCKER_IMAGE}


  if [ $? -eq 0 ]; then
      echo
      echo "Docker container started."
      echo
      echo "Show log:"
      echo
      echo "  docker logs -f ${ODIN_CONTAINER_NAME}"
      echo
  fi
}

###############################################################################
#
# prompt_choice
#
###############################################################################

prompt_choice() {
  local prompt_message="$1"
  shift
  local valid_responses=("$@")

  while true; do
    # Prompt the user
    read -p "$prompt_message " choice

    # Convert the choice to lowercase using tr
    choice=$(echo "$choice" | tr '[:upper:]' '[:lower:]')

    # Check if the entered choice is in the list of valid responses
    for valid in "${valid_responses[@]}"; do
      if [[ "$choice" == "$valid" ]]; then
        echo "$choice"
        return 0
      fi
    done

    # If we reach here, the input was invalid
    echo "Invalid selection, please enter one of: ${valid_responses[*]}"
  done
}

###############################################################################

# Run main entrypoint
main


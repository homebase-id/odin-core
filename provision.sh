#!/bin/bash
set -eou pipefail

###############################################################################
#
# main
#
###############################################################################

main() {

    check_prerequisites

    # Input container name
    if [[ -z "${ODIN_CONTAINER_NAME:-}" ]]; then
        ODIN_CONTAINER_NAME="identity-host"
        read -r -p "Docker container name [${ODIN_CONTAINER_NAME}]: " input
        ODIN_CONTAINER_NAME="${input:-$ODIN_CONTAINER_NAME}"
    fi

    echo

    # Check if the container is running
    if [ "$(docker inspect -f '{{.State.Running}}' "$ODIN_CONTAINER_NAME" 2>/dev/null)" == "true" ]; then
        echo "✅ container $ODIN_CONTAINER_NAME is running"
    else
        echo "⛔️ container $ODIN_CONTAINER_NAME is not running"
        exit 1
    fi

    # Get public IP address
    response=$(curl -s -w "%{http_code}" https://api.ipify.org)
    http_code=$(echo "$response" | awk '{print substr($0, length($0) - 2)}')
    ip=$(echo "$response" | awk '{print substr($0, 1, length($0) - 3)}')
    if [ "$http_code" -eq 200 ] && [ -n "$ip" ]; then
        echo "✅ your public IP is: $ip"
    else
        echo "⛔️ failed to retrieve your public IP. HTTP Status: $response"
        exit 1
    fi

    # Input provisioning domain
    if [[ -z "${ODIN_PROVISIONING_DOMAIN:-}" ]]; then
        echo
        echo "Enter your provisioning domain:"
        echo "DNS A record or CNAME record must resolve to your ip address $ip"
        read -r -p "Provisioning domain (e.g. provisioning.example.com): " ODIN_PROVISIONING_DOMAIN
        echo
    fi

    if [ -z "$ODIN_PROVISIONING_DOMAIN" ]; then
        echo "⛔️ missing provisioning domain."
        exit 1
    fi

    echo "✅ provisioning domain: $ODIN_PROVISIONING_DOMAIN"

    # dig $provisioning_domain

}

###############################################################################
#
# check_prerequisites
#
###############################################################################

check_prerequisites() {
    if ! command -v curl &>/dev/null; then
        echo "Error: curl is not installed. Please install curl and try again."
        exit 1
    fi

    if ! command -v jq &>/dev/null; then
        echo "Error: jq is not installed. Please install jq and try again."
        exit 1
    fi

}

###############################################################################
#
# prompt_choice
#
###############################################################################

prompt_choice() {
    local prompt_message="$1"
    local default_value="$2"
    shift 2
    local valid_responses=("$@")

    while true; do
        # Prompt the user, showing the default value
        read -r -p "$prompt_message " choice

        # If the user presses Enter without typing, use the default value
        if [[ -z "$choice" ]]; then
            choice="$default_value"
        fi

        # Convert the choice to lowercase using tr
        choice=$(echo "$choice" | tr '[:upper:]' '[:lower:]')

        # Check if the entered choice is in the list of valid responses
        for valid in "${valid_responses[@]}"; do
            if [[ "$choice" == "$valid" ]]; then
                echo "$choice"
                return 0
            fi
        done
    done
}

###############################################################################

# Run main entrypoint
main

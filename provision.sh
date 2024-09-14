#!/bin/bash
set -eou pipefail

###############################################################################
#
# main
#
###############################################################################

main() {

  check_prerequisites

  response=$(curl -s -o /tmp/homebase-public-ip.txt -w "%{http_code}" https://api.ipify.org)
  ip=$(cat /tmp/homebase-public-ip.txt)

  if [ "$response" -eq 200 ]; then
      echo "Your public IP is: $ip"
  else
      echo "Failed to retrieve your public IP. HTTP Status: $response"
      exit 1
  fi  

  echo
  read -r -p "Provisioning domain (e.g. provisioning.example.com): " provisioning_domain

  dig $provisioning_domain

}

###############################################################################
#
# check_prerequisites
#
###############################################################################

check_prerequisites() {
  if ! command -v curl &> /dev/null; then
      echo "Error: curl is not installed. Please install curl and try again."
      exit 1
  fi

  if ! command -v jq &> /dev/null; then
      echo "Error: jq is not installed. Please install jq and try again."
      exit 1
  fi

  if ! command -v dig &> /dev/null; then
      echo "Error: dig is not installed. Please install dig and try again."
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


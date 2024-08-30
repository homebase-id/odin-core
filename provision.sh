#!/bin/bash
set -eou pipefail

###############################################################################
#
# main
#
###############################################################################

main() {
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


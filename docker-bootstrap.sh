#!/bin/bash
set -eou pipefail

#
# Linter: https://www.shellcheck.net/
#

###############################################################################
#
# Globals
#
###############################################################################

os_name=$(uname)
echo "OS: $os_name"

###############################################################################
#
# main
#
###############################################################################

main() {
  echo
  echo "🚀 Homebase Installer (Docker editon) 🚀"
  echo
  echo "This is a very basic first-time installer for Homebase."
  echo "We're using Docker and once installed, you will need to use"
  echo "your normal Docker tools to start/stop/update etc."
  echo

  check_prerequisites

  # We can only run on port 80 and 443 for the time being
  ODIN_HTTP_PORT=80
  ODIN_HTTPS_PORT=443

  ODIN_DOCKER_IMAGE="ghcr.io/homebase-id/odin-core:install__main"

  docker_run_script=$(mktemp /tmp/homebase/docker-run-script.sh)
  chmod +x "$docker_run_script"

  docker run \
    --interactive \
    --tty \
    --publish "${ODIN_HTTP_PORT}":"${ODIN_HTTP_PORT}" \
    --publish "${ODIN_HTTPS_PORT}":"${ODIN_HTTPS_PORT}" \
    --volume "${docker_run_script}":"${docker_run_script}" \
    --pull always \
    "${ODIN_DOCKER_IMAGE}" --docker-setup

}

###############################################################################
#
# is_macos
#
###############################################################################
is_macos() {
  [[ "$os_name" == "Darwin" ]]
}

###############################################################################
#
# is_linux
#
###############################################################################
is_linux() {
  [[ "$os_name" == "Linux" ]]
}

###############################################################################
#
# check_prerequisites
#
###############################################################################

check_prerequisites() {
  #
  # Check that Docker is installed
  #
  echo "Checking docker is installed..."
  if ! command -v docker &>/dev/null; then
    echo "⛔️ docker is not installed. Please install Docker and try again."
    exit 1
  fi

  #
  # Check we have sufficent privileges to run Docker commands
  #
  echo "Checking Docker permissions..."
  if ! docker info >/dev/null 2>&1; then
    echo "⛔️ you do not have sufficient access to interact with Docker."
    exit 1
  fi

  echo
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

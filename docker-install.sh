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

  ODIN_DOCKER_IMAGE="ghcr.io/homebase-id/odin-core:latest"

  setup_dir="/tmp/odin/setup"
  mkdir -p "$setup_dir"

  docker_run_script="$setup_dir/docker-run-script.sh"
  touch "$docker_run_script"
  chmod +x "$docker_run_script"

  # --entrypoint /bin/bash \

  docker run \
    --interactive \
    --tty \
    --publish "${ODIN_HTTP_PORT}":"${ODIN_HTTP_PORT}" \
    --publish "${ODIN_HTTPS_PORT}":"${ODIN_HTTPS_PORT}" \
    --volume "${setup_dir}":"${setup_dir}:rw" \
    --pull always \
    "${ODIN_DOCKER_IMAGE}" \
    --docker-setup \
    output-docker-run-script="${docker_run_script}" \
    docker-image-name="${ODIN_DOCKER_IMAGE}" \
    docker-root-data-mount=/tmp/homebase

  exit_code=$?

  if [[ $exit_code -ne 0 ]]; then
    echo "⛔️ Something went wrong. Please check the logs above."
    exit 1
  fi

  echo
  echo "✅ Docker container start-up script: $docker_run_script"
  echo

  view_script=$(prompt_choice "Do you want to view the script? [y/N]:" "n" "y" "n")
  if [[ "$view_script" == "y" ]]; then
    echo
    cat "$docker_run_script"
    echo
  fi

  execute_script=$(prompt_choice "Do you want to execute the script? [Y/n]:" "y" "y" "n")
  if [[ "$execute_script" == "y" ]]; then
    echo
    echo "Once the Docker container is running, you can provision your new identity"
    echo "by pointing your browser to your selected provisioning domain."
    echo
    "$docker_run_script"
  fi

  exit_code=$?

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
  echo "Checking Docker is installed..."
  if ! command -v docker &>/dev/null; then
    echo "⛔️ Docker is not installed. Please install Docker and try again."
    exit 1
  fi

  #
  # Check if Docker daemon is up and that we are privileged to run Docker commands
  #
  echo "Checking Docker daemon..."
  if ! docker info >/dev/null 2>&1; then
    echo "⛔️ there was a problem communicating with the Docker daemon."
    echo "Try running 'docker run hello-world' to check your installation."
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

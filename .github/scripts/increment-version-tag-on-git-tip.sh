#!/bin/bash

# Exit immediately if a command exits with a non-zero status
set -e

# Configure Git user.email if it's not already set
gitEmail=$(git config --global --get user.email)
if [ -z "$gitEmail" ]; then
  git config --global user.email "actions@github.com"
fi

# Configure Git user.name if it's not already set
gitName=$(git config --global --get user.name)
if [ -z "$gitName" ]; then
  git config --global user.name "GitHub Action"
fi

# Fetch all tags from remote repository
git fetch --tags

# Get the latest version tag (ignoring other tags)
latest_tag=$(git tag | grep '^v[0-9]\+\.[0-9]\+\.[0-9]\+$' | sort -V | tail -n 1)

# Check if a version tag exists
if [ -z "$latest_tag" ]; then
    new_tag="v0.0.1"
else
    # Extract version components and increment the patch version
    IFS='.' read -ra version_parts <<< "${latest_tag#v}"
    major="${version_parts[0]}"
    minor="${version_parts[1]}"
    patch="${version_parts[2]}"

    # Increment the patch version by 1
    new_patch=$((patch + 1))

    # Create new version string
    new_tag="v${major}.${minor}.${new_patch}"
fi

# Create new tag
git tag -a "${new_tag}" -m "Auto-incremented to ${new_tag}"

# Push new tag to remote repository
git push origin "${new_tag}"

echo "${new_tag}"

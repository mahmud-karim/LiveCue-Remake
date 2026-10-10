#!/usr/bin/env bash
# Generates the Xcode project from ios/project.yml and resolves its Swift packages.
#
#   bash scripts/ci/generate_project.sh [derived data path]
#
# XcodeGen is installed with Homebrew when it is missing. Package resolution talks to GitHub, so it
# is tried up to three times. Pass the derived data path that later xcodebuild calls will use, so
# the packages are fetched once.
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="${PROJECT:-ios/LiveCueRemake.xcodeproj}"
scheme="${SCHEME:-LiveCueRemake}"
derived_data="${1:-}"

cd "$repository"

echo "::group::Generate $project with XcodeGen"
if ! command -v xcodegen >/dev/null 2>&1; then
  HOMEBREW_NO_AUTO_UPDATE=1 HOMEBREW_NO_INSTALL_CLEANUP=1 HOMEBREW_NO_ANALYTICS=1 brew install xcodegen
fi
xcodegen --version
(cd "$(dirname "$project")" && xcodegen generate)
echo "::endgroup::"

arguments=(-resolvePackageDependencies -project "$project" -scheme "$scheme")
if [[ -n "$derived_data" ]]; then
  arguments+=(-derivedDataPath "$derived_data")
fi

attempts=3
for (( attempt = 1; attempt <= attempts; attempt++ )); do
  echo "::group::Resolve Swift packages (attempt $attempt of $attempts)"
  if xcodebuild "${arguments[@]}"; then
    echo "::endgroup::"
    exit 0
  fi
  echo "::endgroup::"
  if (( attempt < attempts )); then
    sleep 15
  fi
done

echo "::error title=Swift packages::Package resolution failed $attempts times."
exit 1

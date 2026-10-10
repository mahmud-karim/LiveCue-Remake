#!/usr/bin/env bash
# Fails fast when the pinned Xcode is not on this runner image, then prints what will be used.
#
#   DEVELOPER_DIR=/Applications/Xcode_26.3.app/Contents/Developer bash scripts/ci/require_xcode.sh
#
# The app names iOS 26 symbols behind availability checks, so it needs an iOS 26 or later SDK
# even when it is tested on an iOS 18 simulator.
set -euo pipefail

minimum_sdk_major=26

if [[ -z "${DEVELOPER_DIR:-}" ]]; then
  echo "::error title=Xcode not pinned::DEVELOPER_DIR is not set."
  exit 1
fi

if [[ ! -d "$DEVELOPER_DIR" ]]; then
  echo "::error title=Pinned Xcode is missing::$DEVELOPER_DIR does not exist on this runner image (${ImageOS:-unknown} ${ImageVersion:-})."
  echo "Installed Xcode versions:"
  ls -d /Applications/Xcode*.app 2>/dev/null || echo "  none"
  exit 1
fi

echo "Runner image: ${ImageOS:-unknown} ${ImageVersion:-}"
echo "DEVELOPER_DIR: $DEVELOPER_DIR"
xcodebuild -version
xcrun swift --version 2>&1 | sed -n '1p'

sdk_version="$(xcrun --sdk iphoneos --show-sdk-version)"
echo "iOS SDK: $sdk_version"
if (( ${sdk_version%%.*} < minimum_sdk_major )); then
  echo "::error title=iOS SDK too old::The pinned Xcode ships the iOS $sdk_version SDK; iOS $minimum_sdk_major or later is required."
  exit 1
fi

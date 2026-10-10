#!/usr/bin/env bash
# Runs xcodebuild, saves its complete output to a file and shows only the lines worth reading.
#
#   bash scripts/ci/xcodebuild_logged.sh <log file> <xcodebuild arguments...>
#
# The console gets errors, warnings, package resolution, link steps and test results. When
# xcodebuild fails, every error is printed again with its surrounding lines, so the cause can be
# read from the step log without downloading the full log. Exits with xcodebuild's own status.
set -uo pipefail

if (( $# < 2 )); then
  echo "usage: $0 <log file> <xcodebuild arguments...>" >&2
  exit 2
fi

log_file="$1"
shift
mkdir -p "$(dirname "$log_file")"

shown='(error|warning): '
shown+='|^(\*\* |Test Suite |Test [Cc]ase |Testing |Executed |Resolve Package Graph|Resolved source packages|Ld |note: )'
shown+='|^[[:space:]]*[A-Za-z0-9_.-]+: (https?://|/)[^ ]* @ '
shown+='|Test run (started|with)|(Suite|Test) .* (started|passed|failed|skipped)|recorded an issue'
shown+='|xcodebuild: error|Undefined symbols?|ld: '

NSUnbufferedIO=YES xcodebuild "$@" 2>&1 | tee "$log_file" | grep --line-buffered -E "$shown"
status="${PIPESTATUS[0]}"

if (( status != 0 )); then
  echo
  echo "xcodebuild exited with status $status. Errors, with context, from $log_file:"
  grep -n -E -B 3 -A 12 'error: |xcodebuild: error|Undefined symbols?|ld: |\*\* [A-Z ]+ FAILED \*\*|Testing failed|: failed' "$log_file" | tail -n 600
  echo
  echo "Last lines of $log_file:"
  tail -n 40 "$log_file"
fi

exit "$status"

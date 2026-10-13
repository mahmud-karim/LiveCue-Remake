#!/usr/bin/env bash
# Text checks on the Swift sources. Needs only bash, find and grep, so it runs in the Swift container.
#
#   bash scripts/ci/lint_swift.sh [repository root]
#
# 1. iOS 26 Liquid Glass symbols may appear in one file only, the wrapper. Everything else calls the
#    wrapper, which keeps the iOS 18 fallback in one place. Comments count: this is a text search.
# 2. The core package imports Foundation and nothing else, so it keeps building on Linux.
# 3. No Swift file of the app builds an attributed string from Markdown. Text that came from a model or
#    from the relay is drawn as it is, so that it can never turn into a tappable link. Comments count.
set -euo pipefail

root="${1:-.}"
wrapper="ios/App/DesignSystem/LiquidGlass.swift"
core_sources="ios/LiveCueCore/Sources"
app_sources="ios/App"

# `.glass` and `.glassProminent` are the button styles; a longer name such as `.glassButton` or a
# member path such as `theme.glass.sheen` is something else.
markdown_symbol='AttributedString\(markdown'
glass_symbols='glassEffect|GlassEffectContainer|glassEffectID|safeAreaBar|scrollEdgeEffectStyle|tabBarMinimizeBehavior|ToolbarSpacer|\.glass(Prominent)?([^A-Za-z0-9_.]|$)'

cd "$root"
failures=0

if [[ ! -f "$wrapper" ]]; then
  echo "::error title=Glass lint::$wrapper does not exist."
  failures=1
fi
if [[ ! -d "$core_sources" ]]; then
  echo "::error title=Core import lint::$core_sources does not exist."
  failures=1
fi
if [[ ! -d "$app_sources" ]]; then
  echo "::error title=Markdown lint::$app_sources does not exist."
  failures=1
fi

checked=0
while IFS= read -r file; do
  checked=$((checked + 1))
  if [[ "$file" == "$wrapper" ]]; then
    continue
  fi
  if matches="$(grep -n -E "$glass_symbols" "$file")"; then
    while IFS= read -r match; do
      echo "::error file=$file,line=${match%%:*},title=Glass lint::An iOS 26 glass symbol appears outside $wrapper: ${match#*:}"
    done <<< "$matches"
    failures=1
  fi
done < <(find ios -name '*.swift' -not -path '*/.build/*' | LC_ALL=C sort)

if [[ -d "$core_sources" ]]; then
  while IFS= read -r file; do
    # Any import line, with or without attributes, that is not exactly `import Foundation`.
    if matches="$(grep -n -E '^[[:space:]]*(@[A-Za-z_]+[[:space:]]+)*import[[:space:]]' "$file" | grep -v -E '^[0-9]+:[[:space:]]*import[[:space:]]+Foundation[[:space:]]*(//.*)?$')"; then
      while IFS= read -r match; do
        echo "::error file=$file,line=${match%%:*},title=Core import lint::The core package may import only Foundation: ${match#*:}"
      done <<< "$matches"
      failures=1
    fi
  done < <(find "$core_sources" -name '*.swift' | LC_ALL=C sort)
fi

if [[ -d "$app_sources" ]]; then
  while IFS= read -r file; do
    if matches="$(grep -n -E "$markdown_symbol" "$file")"; then
      while IFS= read -r match; do
        echo "::error file=$file,line=${match%%:*},title=Markdown lint::The app may not build an attributed string from Markdown: ${match#*:}"
      done <<< "$matches"
      failures=1
    fi
  done < <(find "$app_sources" -name '*.swift' | LC_ALL=C sort)
fi

if (( failures != 0 )); then
  exit 1
fi
echo "Swift lint passed: $checked Swift file(s) checked; glass symbols only in $wrapper; core imports only Foundation; no Markdown attributed strings in $app_sources."

#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: sign-and-notarize.sh <app-bundle> <output-zip>" >&2
  exit 64
fi

app_bundle="$1"
output_zip="$2"
sign_identity="${APPLE_SIGN_IDENTITY:-}"
notary_profile="${APPLE_NOTARY_PROFILE:-}"

if [[ ! -d "$app_bundle/Contents/MacOS" ]]; then
  echo "invalid app bundle: $app_bundle" >&2
  exit 66
fi
if [[ -z "$sign_identity" || -z "$notary_profile" ]]; then
  echo "APPLE_SIGN_IDENTITY and APPLE_NOTARY_PROFILE are required" >&2
  exit 78
fi

while IFS= read -r -d '' binary; do
  codesign --force --options runtime --timestamp --sign "$sign_identity" "$binary"
done < <(find "$app_bundle/Contents/MacOS" -type f \( -perm -111 -o -name '*.dylib' \) -print0)

codesign --force --options runtime --timestamp --sign "$sign_identity" "$app_bundle"
codesign --verify --deep --strict --verbose=2 "$app_bundle"
spctl --assess --type execute --verbose=2 "$app_bundle"

rm -f "$output_zip"
ditto -c -k --keepParent "$app_bundle" "$output_zip"
xcrun notarytool submit "$output_zip" --keychain-profile "$notary_profile" --wait
xcrun stapler staple "$app_bundle"
xcrun stapler validate "$app_bundle"
spctl --assess --type execute --verbose=2 "$app_bundle"

rm -f "$output_zip"
ditto -c -k --keepParent "$app_bundle" "$output_zip"
shasum -a 256 "$output_zip" > "$output_zip.sha256"

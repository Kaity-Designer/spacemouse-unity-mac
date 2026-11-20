#!/bin/zsh
# Builds SpaceMouseBridge.bundle (Apple Silicon + Intel) into ../Plugins/macOS
set -euo pipefail
cd "${0:A:h}"

OUT="../Plugins/macOS/SpaceMouseBridge.bundle"
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS"

clang -bundle -O2 -Wall -Wextra \
  -arch arm64 -arch x86_64 \
  -mmacosx-version-min=11.0 \
  -fvisibility=hidden \
  -o "$OUT/Contents/MacOS/SpaceMouseBridge" \
  SpaceMouseBridge.c

cat > "$OUT/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleExecutable</key><string>SpaceMouseBridge</string>
  <key>CFBundleIdentifier</key><string>com.kah.spacemousebridge</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>SpaceMouseBridge</string>
  <key>CFBundlePackageType</key><string>BNDL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
</dict>
</plist>
PLIST

codesign --force --sign - "$OUT"
echo "Built $OUT"
lipo -archs "$OUT/Contents/MacOS/SpaceMouseBridge"

# Test harness (not shipped to Unity)
clang -O2 -Wall -o test_harness test_harness.c

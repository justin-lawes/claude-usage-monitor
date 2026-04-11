#!/bin/bash
set -e

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$PROJECT_DIR/build"
APP_NAME="ClaudeUsageMonitor"
APP_BUNDLE="$BUILD_DIR/$APP_NAME.app"

echo "Building $APP_NAME..."

mkdir -p "$BUILD_DIR"

# Compile all Swift sources
swiftc \
  "$PROJECT_DIR/Sources/UsageData.swift" \
  "$PROJECT_DIR/Sources/ClaudeService.swift" \
  "$PROJECT_DIR/Sources/ContentView.swift" \
  "$PROJECT_DIR/Sources/AppDelegate.swift" \
  "$PROJECT_DIR/Sources/main.swift" \
  -framework AppKit \
  -framework WebKit \
  -framework SwiftUI \
  -framework UserNotifications \
  -framework Combine \
  -framework ServiceManagement \
  -target arm64-apple-macosx13.0 \
  -O \
  -o "$BUILD_DIR/$APP_NAME"

echo "Compilation successful. Creating .app bundle..."

# Build .app bundle structure
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

cp "$BUILD_DIR/$APP_NAME"              "$APP_BUNDLE/Contents/MacOS/$APP_NAME"
cp "$PROJECT_DIR/Resources/Info.plist" "$APP_BUNDLE/Contents/Info.plist"
cp "$PROJECT_DIR/Resources/AppIcon.icns" "$APP_BUNDLE/Contents/Resources/AppIcon.icns"

echo "Installing to /Applications (replacing stale bundle if present)..."
rm -rf "/Applications/$APP_NAME.app"
cp -R "$APP_BUNDLE" "/Applications/$APP_NAME.app"

# Remove quarantine attribute (avoids Gatekeeper block for local builds)
xattr -cr "$APP_BUNDLE" 2>/dev/null || true

# Ad-hoc sign so UNUserNotificationCenter works without a developer certificate
codesign --sign - --force --deep "$APP_BUNDLE" 2>/dev/null || true
codesign --sign - --force --deep "/Applications/$APP_NAME.app" 2>/dev/null || true

echo ""
echo "Done: /Applications/$APP_NAME.app"
echo "To run: open \"/Applications/$APP_NAME.app\""

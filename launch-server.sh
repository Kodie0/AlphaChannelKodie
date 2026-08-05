#!/usr/bin/env bash
# One-click launcher for the AetherChannel relay server (see AetherChannel/Desktop shortcut).
set -e
cd "$(dirname "$0")"
echo "Starting AetherChannel relay server..."
dotnet run --project src/AetherChannel.Server

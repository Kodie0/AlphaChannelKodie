#!/usr/bin/env bash
# One-click launcher for the AlphaChannel relay server (see AlphaChannel/Desktop shortcut).
set -e
cd "$(dirname "$0")"
echo "Starting AlphaChannel relay server..."
dotnet run --project src/AlphaChannel.Server

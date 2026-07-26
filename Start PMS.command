#!/bin/bash
# Double-click this file in Finder to start the Performance Management System locally.
# Starts the local Postgres container, restores tooling, and runs the web app.
# Stop the app with Ctrl+C in this window (or just close the window).

# cd to the directory this script lives in, regardless of where it was double-clicked from.
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1

echo "==> Starting local Postgres (docker compose)..."
docker compose up -d

echo "==> Restoring .NET tools..."
dotnet tool restore

echo "==> Starting the web app..."
echo "    (Ctrl+C in this window stops the app; the database keeps running.)"
dotnet run --project src/PerformanceManagement.Web/PerformanceManagement.Web.csproj

# Keep the Terminal window open after the app exits/crashes so any error is visible.
echo ""
echo "App stopped. Press Enter to close this window."
read -r

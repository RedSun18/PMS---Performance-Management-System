#!/bin/bash
# Double-click this file in Finder to start the Performance Management System in the
# public Demo environment (fictional "Apex Corporation" data — safe to show anyone).
# Starts the Demo Postgres container, seeds it if needed, and runs the web app over HTTPS.
# Stop the app with Ctrl+C in this window (or just close the window).

# cd to the directory this script lives in, regardless of where it was double-clicked from.
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1

echo "==> Trusting the local HTTPS dev certificate (Demo requires HTTPS; safe to re-run)..."
dotnet dev-certs https --trust

echo "==> Starting the Demo Postgres container (docker compose)..."
docker compose up -d postgres-demo

echo "==> Restoring .NET tools..."
dotnet tool restore

echo "==> Seeding the Demo database if it isn't already seeded..."
dotnet run --project src/PerformanceManagement.DemoSeeder

echo "==> Starting the web app in the Demo environment..."
echo "    Open https://localhost:5275 once it's up."
echo "    Login: admin / Admin@123  (or manager / Demo@123, or employee / Demo@123)"
echo "    (Ctrl+C in this window stops the app; the database keeps running.)"
ASPNETCORE_ENVIRONMENT=Demo dotnet run --project src/PerformanceManagement.Web/PerformanceManagement.Web.csproj --no-launch-profile --urls https://localhost:5275

# Keep the Terminal window open after the app exits/crashes so any error is visible.
echo ""
echo "App stopped. Press Enter to close this window."
read -r

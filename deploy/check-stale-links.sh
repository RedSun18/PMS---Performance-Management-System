#!/usr/bin/env bash
# Fails (non-zero exit) if the Demo database has any Notification or EmailLog row whose
# stored link/body still references a dev-only "http://localhost:PORT" address instead of
# the real public origin (https://pms.aryanb.dev).
#
# WHY THIS EXISTS: Notification.Link and EmailLog.Body are both plain text columns — the
# absolute URL is rendered once, at the moment the row is created, using whatever
# SystemSettings.ApplicationBaseUrl held at that instant (see FormLinkService.
# BuildFormUrlAsync). Fixing that setting — in code (DemoSeeder.cs), in config
# (appsettings.Demo.json), or by hand through the Settings page — only changes what NEW rows
# get; it can never rewrite rows that already exist. That's exactly how a stale
# "http://localhost:5274" address survived in the live Demo database for weeks after the
# code and the setting had both already been corrected (see the FixLocalhostNotificationAnd
# EmailLinks migration, which does the one-time cleanup this check verifies actually landed).
# This check exists so that gap can never again go unnoticed after a deploy.
#
# Called by deploy/healthcheck.sh (which is itself called by publish.sh after every deploy).
# Read-only. Safe to run any time: `deploy/check-stale-links.sh`.
set -uo pipefail

NOTIF_COUNT="$(docker exec pms-demo-postgres psql -U pms_demo -d pms_demo -tAc \
  "SELECT COUNT(*) FROM \"Notifications\" WHERE \"Link\" LIKE 'http://localhost:%';" 2>/dev/null)"
EMAIL_COUNT="$(docker exec pms-demo-postgres psql -U pms_demo -d pms_demo -tAc \
  "SELECT COUNT(*) FROM \"EmailLogs\" WHERE \"Body\" LIKE '%http://localhost:%';" 2>/dev/null)"

if [ -z "$NOTIF_COUNT" ] || [ -z "$EMAIL_COUNT" ]; then
  echo "  FAIL  Could not query the Demo database (is pms-demo-postgres running?)" >&2
  exit 1
fi

if [ "$NOTIF_COUNT" != "0" ] || [ "$EMAIL_COUNT" != "0" ]; then
  echo "  FAIL  $NOTIF_COUNT stale Notification link(s) and $EMAIL_COUNT stale EmailLog body(ies) still reference http://localhost:*" >&2
  echo "        Run: docker exec pms-demo-postgres psql -U pms_demo -d pms_demo -c \"SELECT \\\"Id\\\", \\\"Link\\\" FROM \\\"Notifications\\\" WHERE \\\"Link\\\" LIKE 'http://localhost:%';\"" >&2
  exit 1
fi

exit 0

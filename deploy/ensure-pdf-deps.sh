#!/usr/bin/env bash
set -uo pipefail

# Ensures the apt packages PDF export (PuppeteerSharp/headless Chromium — see
# src/PerformanceManagement.Core/Services/PdfRenderer.cs and README.md "Reports") needs at
# runtime are installed. Idempotent and non-fatal — `apt-get install` on an already-installed
# package is a fast no-op, and a failure here must never block the rest of a deploy (PDF
# export is one optional feature, not core app health; see the health-check-not-gate
# reasoning in deploy/healthcheck.sh's PDF section).
#
# WHY THIS EXISTS AS ITS OWN SCRIPT, CALLED ON EVERY DEPLOY (not just in bootstrap-server.sh):
# the current production VPS was bootstrapped before this exact package list was correct, and
# bootstrap-server.sh only ever runs once — nothing in the routine deploy path
# (deploy/update.sh -> deploy/publish.sh) re-ran it, so a fix added here after the fact would
# otherwise sit in git forever without reaching the live server (the same class of bug fixed
# for Nginx config drift by deploy/sync-nginx.sh and for missing certs by
# deploy/ensure-certs.sh). Confirmed via deploy/healthcheck.sh's `ldd` diagnostic against the
# actual downloaded Chromium binary on the live VPS: libXfixes.so.3 (libxfixes3) and
# libcairo.so.2 (libcairo2) were missing and PDF export was failing to launch Chromium as a
# direct result — not a network/download/permissions issue (those were separate, already-
# fixed bugs; see PdfRenderer.cs's WarmupAsync comment for the cache-directory one).
#
# Called by both bootstrap-server.sh (fresh servers) and deploy/publish.sh (every deploy of
# an already-bootstrapped server, so a future Chromium dependency gap self-heals on the next
# push instead of requiring another manual one-off SSH session).
apt-get update -qq || true
if ! apt-get install -y \
  libnss3 libatk-bridge2.0-0 libcups2 libxcomposite1 libxdamage1 libxrandr2 libgbm1 \
  libpango-1.0-0 libasound2t64 libxfixes3 libcairo2; then
  echo "!! Failed to install one or more Chromium/PDF-export dependencies — continuing anyway."
  echo "!! PDF export (Reports page) will not work until this is resolved manually; nothing"
  echo "!! else in this deployment depends on these packages. See README.md 'Reports' for the"
  echo "!! exact package list, or check apt output above for the specific failing name, or"
  echo "!! run: sudo deploy/healthcheck.sh   to see exactly which shared library is missing."
fi

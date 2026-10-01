#!/usr/bin/env bash
# Render every storyboard frame (f01 … f11) of frames.html to frames/fNN.png at 1920×1080.
# Uses the Playwright already installed for the web e2e tests; nothing else is required.
#   docs/demo/h3/render.sh            # all frames
#   docs/demo/h3/render.sh f04 f05    # just these
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
web="$here/../../../src/MesXray.Web"
mkdir -p "$here/frames"
frames=("$@")
if [ ${#frames[@]} -eq 0 ]; then frames=(f01 f02 f03 f04 f05 f06 f07 f08 f09 f10 f11); fi
for f in "${frames[@]}"; do
  # --channel chrome reuses the installed Google Chrome (the e2e suite runs the same way); drop it if you have `npx playwright install chromium`
  (cd "$web" && npx playwright screenshot --browser chromium ${XRAY_PW_CHANNEL:+--channel "$XRAY_PW_CHANNEL"} --viewport-size=1920,1080 --wait-for-timeout=300 \
      "file://$here/frames.html?state=$f" "$here/frames/$f.png" >/dev/null)
  echo "frames/$f.png"
done

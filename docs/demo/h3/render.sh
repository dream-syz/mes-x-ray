#!/usr/bin/env bash
# Render every storyboard frame (f01 … f11) of frames.html to frames/fNN.png at 1920×1080.
# Uses the Playwright already installed for the web e2e tests and, like the e2e config, the installed Google Chrome
# (XRAY_PW_CHANNEL=chrome by default; set XRAY_PW_CHANNEL= (empty) to use a bundled Chromium from `npx playwright install chromium`).
# Render on the machine whose fonts you want in the frames (the cards and frames fall back to PingFang SC on macOS).
#   docs/demo/h3/render.sh            # all frames
#   docs/demo/h3/render.sh f04 f05    # just these
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
web="$here/../../../src/MesXray.Web"
channel="${XRAY_PW_CHANNEL-chrome}"
mkdir -p "$here/frames"
frames=("$@")
if [ ${#frames[@]} -eq 0 ]; then frames=(f01 f02 f03 f04 f05 f06 f07 f08 f09 f10 f11); fi
for f in "${frames[@]}"; do
  (cd "$web" && npx playwright screenshot --browser chromium ${channel:+--channel "$channel"} --viewport-size=1920,1080 --wait-for-timeout=300 \
      "file://$here/frames.html?state=$f" "$here/frames/$f.png" >/dev/null)
  echo "frames/$f.png"
done

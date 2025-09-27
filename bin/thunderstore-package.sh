#!/usr/bin/env bash

set -euo pipefail

SS_VERSION=$(grep -oP '(?<=\[BepInPlugin\("MVP\.Valheim_Serverside_Simulations", "Serverside Simulations", ")\d\.\d\.\d(?="\)\]$)' src/Valheim_Serverside/ServersidePlugin.cs)

cat > manifest.json <<- EOM
{
  "name": "Serverside Simulations",
  "description": "Run world and monster simulations on a dedicated server.",
  "version_number": "$SS_VERSION",
  "dependencies": ["denikson-BepInExPack_Valheim-5.4.2202"],
  "website_url": "https://github.com/ddormer/valheim-serverside"
}
EOM

zip -e thunderstore-package.zip Serverside_Simulations.dll icon.png manifest.json README.md CHANGELOG.md

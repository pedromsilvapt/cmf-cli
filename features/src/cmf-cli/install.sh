#!/bin/sh
set -e

echo "Activating feature 'cmf-cli'"
echo "The provided @criticalmanufacturing/cli version is: ${VERSION}"

npm install --global @criticalmanufacturing/cli@$VERSION
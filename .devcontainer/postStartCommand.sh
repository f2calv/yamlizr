#!/usr/bin/env bash

# Reports the .NET SDK version whenever the development container starts.

set -euo pipefail

echo "postStartCommand.sh"
echo "-------------------"

dotnet --version

echo "Done"

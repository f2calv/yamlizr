#!/bin/sh

echo "postStartCommand.sh"
echo "-------------------"

dotnet --version
pre-commit autoupdate

echo "Done"

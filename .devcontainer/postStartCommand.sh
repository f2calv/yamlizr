#!/bin/sh

echo "postStartCommand.sh"
echo "-------------------"

sudo apt-get update
sudo apt-get upgrade -y

dotnet --version
pre-commit autoupdate

echo "Done"

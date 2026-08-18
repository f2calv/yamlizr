#!/bin/sh

echo "postCreateCommand.sh"
echo "--------------------"

sudo apt-get update
sudo apt-get upgrade -y

sudo chmod +x .devcontainer/postStartCommand.sh

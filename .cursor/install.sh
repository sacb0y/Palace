#!/usr/bin/env bash
# Cloud Agent bootstrap for Palace.
#
# Palace itself is a packaged WinUI 3 / Windows App SDK app and can only be
# built and run on Windows (see AGENTS.md). On this Linux Cloud Agent the
# buildable, runnable surface is Palace.Tests — a net10.0 xUnit project that
# links Helpers/CloudFile.cs and stays off WinUI/WinRT. This script installs
# the .NET 10 SDK and warms a restore/build of that test project.
#
# Idempotent: safe to re-run against cached state.
set -euo pipefail

DOTNET_DIR="$HOME/.dotnet"
DOTNET_CHANNEL="10.0"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

if ! "$DOTNET_DIR/dotnet" --version >/dev/null 2>&1; then
    echo "Installing .NET SDK (channel $DOTNET_CHANNEL) into $DOTNET_DIR ..."
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR"
else
    echo ".NET SDK already present: $("$DOTNET_DIR/dotnet" --version)"
fi

export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$PATH"

# Persist the toolchain on PATH for interactive agent shells.
BASHRC="$HOME/.bashrc"
if ! grep -q '# .NET SDK (Palace)' "$BASHRC" 2>/dev/null; then
    {
        echo ''
        echo '# .NET SDK (Palace)'
        echo 'export DOTNET_ROOT="$HOME/.dotnet"'
        echo 'export PATH="$HOME/.dotnet:$PATH"'
        echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
        echo 'export DOTNET_NOLOGO=1'
    } >> "$BASHRC"
fi

echo "Restoring and building Palace.Tests ..."
dotnet build ./Palace.Tests/Palace.Tests.csproj --nologo

echo "Palace Cloud Agent bootstrap complete."

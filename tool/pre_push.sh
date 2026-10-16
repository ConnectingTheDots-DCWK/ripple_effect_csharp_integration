#!/usr/bin/env bash
# The pre-push hook: the formatting and the analyzers, then the suite in both
# variants, when there is an engine to test against.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet format RippleFx.sln --verify-no-changes --exclude src/RippleFx.Engine/Interop/
if [ -z "${RIPPLE_ENGINE_TREE:-}" ] && [ "$(jq -r '.archives.conformance.sha256' engine.json)" = null ]; then
  echo "pre-push: engine.json pins no release and RIPPLE_ENGINE_TREE is unset; skipped the tests." >&2
  exit 0
fi
dotnet build src/RippleFx.Engine -c Release
for variant in python no-python; do
  dotnet test tests/RippleFx.Engine.Tests -c Release -p:RippleVariant="$variant"
done

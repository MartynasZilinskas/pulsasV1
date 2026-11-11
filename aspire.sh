#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

if (( $# == 0 )); then
    set -- run
fi

# Use the project toolchain and matching CLI, not an unrelated global install.
exec mise exec -- dotnet dnx aspire.cli@13.6.5 -- "$@"

#!/usr/bin/env bash
# Regenerates the P/Invoke declarations from the pinned engine's ripple.h, and
# the record of how a C compiler lays every struct out, which the tests hold
# .NET's layout to. Both are committed; CI runs this and fails on a diff, so
# the bindings cannot drift from the header they bind.
#
#   scripts/generate.sh            from the pinned release's header
#   scripts/generate.sh <header>   from another one (a RIPPLE_ENGINE_TREE's)
#
# Needs the .NET SDK (the generator is a local tool: .config/dotnet-tools.json),
# clang, for its builtin headers and its record layouts, and python3.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
die() { echo "generate: $*" >&2; exit 1; }
for tool in dotnet clang python3; do
  command -v "$tool" >/dev/null || die "$tool is not installed"
done

if [ $# -gt 0 ]; then
  header="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
else
  dotnet run --project tool/Engine -c Release -- fetch --rid linux-x64 >/dev/null
  header="$root/out/engine/python/linux-x64/include/ripple/ripple.h"
fi
[ -f "$header" ] || die "no header at $header"
include="$(dirname "$(dirname "$header")")"
resources="$(clang -print-resource-dir)"

dotnet tool restore >/dev/null
out="src/RippleFx.Engine/Interop/Native.g.cs"
dotnet tool run ClangSharpPInvokeGenerator \
  --file "$header" \
  --namespace RippleFx.Engine.Interop \
  --method-class-name Native \
  --library-path ripple_effect_engine \
  --output "$out" \
  --language c --std c11 \
  --resource-directory "$resources" \
  --header-file scripts/generated-header.txt \
  --config codegen=compatible types=unix file=single \
  --generate helper-types macro-bindings funcs-with-body=false \
  --with-type RIPPLE_CHOICE_NONE=uint \
  | grep -vE '^(Processing|Diagnostics for|    Info .* has a size of 0)' || true
[ -z "$(tail -c1 "$out")" ] || echo >> "$out"

# Every struct and union, as clang lays it out on a 64-bit target: its size,
# and each field's offset. A test reads .NET's layout of the same types and
# compares, on every platform CI runs.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
{
  echo '#include <ripple/ripple.h>'
  grep -oE '^typedef (struct|union) Ripple[A-Za-z0-9]+ \{' "$header" | awk '{print $2, $3}' | sort -u |
    while read -r kind name; do echo "int s_$name = sizeof($kind $name);"; done
} > "$work/layout.c"
clang -I"$include" -Xclang -fdump-record-layouts -fsyntax-only "$work/layout.c" > "$work/layout.txt"
python3 - "$work/layout.txt" tests/RippleFx.Engine.Tests/layout.json <<'PY'
import json, re, sys
records, current = {}, None
for line in open(sys.argv[1]):
    head = re.match(r'\s*0 \| (?:struct|union) (Ripple\w+)$', line)
    if head:
        current = {"size": None, "fields": {}}
        records[head.group(1)] = current
        continue
    if current is None:
        continue
    field = re.match(r'\s*(\d+) \|   (?! )(.*\S)$', line)
    if field:
        # The last word is the field's name; what comes before it is its type.
        current["fields"][field.group(2).split()[-1]] = int(field.group(1))
        continue
    size = re.search(r'\[sizeof=(\d+), align=\d+\]', line)
    if size:
        current["size"] = int(size.group(1))
        current = None
with open(sys.argv[2], "w") as out:
    json.dump(dict(sorted(records.items())), out, indent=2)
    out.write("\n")
print(f"generate: {len(records)} records laid out")
PY

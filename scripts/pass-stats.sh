#!/usr/bin/env bash
# Prints the Code-Lines trailer for a SIMPLIFY or REVIEW commit: real code changed in the staged
# changes, or in the commit given as $1. Tests, docs, blank lines and comment-only lines don't count.
#   scripts/pass-stats.sh            # before committing
#   scripts/pass-stats.sh <commit>   # an existing commit
set -euo pipefail

if [ $# -gt 0 ]; then
  git show --format= --unified=0 --no-color "$1"
else
  git diff --cached --unified=0 --no-color
fi | python3 -c '
import re, sys

CODE = {"kt", "kts", "swift", "java", "cs", "ts", "tsx", "js", "jsx", "mjs", "py", "sh", "m", "mm", "c", "h", "cpp", "go", "rs"}
HASH_COMMENTS = {"py", "sh"}

def is_test(path):
    parts = path.split("/")
    if any(p.lower() in ("test", "tests", "test-fixtures") or re.search(r"Tests?([A-Z.]|$)", p) for p in parts[:-1]):
        return True
    return re.search(r"(Tests?|Spec)\.[a-z]+$|\.(test|spec)\.[a-z]+$", parts[-1]) is not None

def counts(path):
    ext = path.rsplit(".", 1)[-1].lower() if "." in path else ""
    return ext in CODE and not is_test(path) and not path.startswith("docs/"), ext

added = removed = 0
path, code, ext, header = None, False, "", False
for line in sys.stdin:
    if line.startswith("diff --git "):
        header, code = True, False
        continue
    if header and line.startswith("@@"):
        header = False
    if header and line.startswith("--- "):
        old = line[4:].strip()
        path = None if old == "/dev/null" else old[2:]
        continue
    if header and line.startswith("+++ "):
        new = line[4:].strip()
        path = path if new == "/dev/null" else new[2:]
        code, ext = counts(path or "")
        continue
    if header or not code or line.startswith("@@") or line[:1] not in "+-":
        continue
    text = line[1:].strip()
    if not text or text.startswith(("//", "/*", "*", "*/")) or (ext in HASH_COMMENTS and text.startswith("#")):
        continue
    if line[0] == "+":
        added += 1
    else:
        removed += 1
print(f"Code-Lines: +{added} -{removed}")
'

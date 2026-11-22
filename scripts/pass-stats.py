#!/usr/bin/env python3
"""The Code-Lines trailer of a SIMPLIFY or REVIEW commit (CLAUDE.md, "Simplify, then code review").

Counts the real code the staged changes, or the commit given, add and remove: no tests, docs, vendored
code, generated or binary files, blank lines or comment-only lines. The weekly team data runs this same
file from each commit's tree, so its rules are the only definition of "real code".

    scripts/pass-stats.py            # before committing
    scripts/pass-stats.py <commit>   # an existing commit
"""
import re
import subprocess
import sys

CODE = {"kt", "kts", "swift", "java", "cs", "ts", "tsx", "js", "jsx", "mjs", "py", "sh", "m", "mm", "c", "h", "cpp", "go", "rs"}
HASH_COMMENTS = {"py", "sh"}
NOT_CODE = re.compile(
    r"(^|/)(libs|vendor|third_party|gradle/local-repo|node_modules|docs)/|\.xcframework/|stb_image|/blake3/|/fixtures?/"
    r"|(^|/)(tests?|__tests__|[A-Za-z]*Test|iosAppTests|test-utils|test-fixtures)(/|$)"
    r"|Tests?\.(kt|cs|swift|rs|ts|tsx)$|\.(test|spec)\.(ts|tsx|js)$|_test\.rs$|Tests?\.[A-Za-z]+\.cs$|/tests\.rs$"
)


def diff(commit):
    args = ["show", "--format=", commit] if commit else ["diff", "--cached"]
    return subprocess.run(["git", *args, "--unified=0", "--no-prefix", "--no-color"], check=True, capture_output=True, text=True).stdout


def code_lines(text):
    added = removed = 0
    ext, code, header, old = "", False, False, None
    for line in text.splitlines():
        if line.startswith("diff --git "):
            header = True
        elif header:
            # A removed "-- x" or added "++ x" line is only ever after the header's @@.
            if line.startswith("--- "):
                old = line[4:]
            elif line.startswith("+++ "):
                path = old if line[4:] == "/dev/null" else line[4:]
                ext = path.rsplit(".", 1)[-1].lower() if "." in path.rsplit("/", 1)[-1] else ""
                code = ext in CODE and not NOT_CODE.search(path)
            elif line.startswith("@@"):
                header = False
        elif code and line[:1] in ("+", "-"):
            body = line[1:].strip()
            if body and not body.startswith(("//", "/*", "*")) and not (ext in HASH_COMMENTS and body.startswith("#")):
                if line[0] == "+":
                    added += 1
                else:
                    removed += 1
    return added, removed


if __name__ == "__main__":
    added, removed = code_lines(diff(sys.argv[1] if len(sys.argv) > 1 else None))
    print(f"Code-Lines: +{added} -{removed}")

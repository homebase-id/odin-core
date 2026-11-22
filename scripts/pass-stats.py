#!/usr/bin/env python3
"""Prints the Code-Lines trailer for the staged changes, a commit, or a branch (`origin/main...HEAD`). The
weekly team data runs this file from each commit's tree, so its rules are what "real code" means there."""
import re
import subprocess
import sys

CODE = {"kt", "kts", "swift", "java", "cs", "ts", "tsx", "js", "jsx", "mjs", "py", "sh", "m", "mm", "c", "h", "cpp", "go", "rs", "sq", "sqm", "sql"}
HASH_COMMENTS = {"py", "sh"}
DASH_COMMENTS = {"sq", "sqm", "sql"}
NOT_CODE = re.compile(
    r"(^|/)(libs|vendor|third_party|gradle/local-repo|node_modules|docs|resources|fixtures?)/|\.xcframework/|stb_image|/cpp/blake3/|\.min\.js$"
    r"|(^|/)src/[A-Za-z]*[Tt]est[A-Za-z]*/|^tests?/|(^|/)(__tests__|test-utils|test-fixtures|iosAppTests)/"
    r"|\.(test|spec)\.(ts|tsx|js)$|_test\.rs$"
)
# Plumbing-level settings, so the count doesn't depend on whoever runs it.
GIT = ["git", "-c", "core.quotePath=false", "-c", "diff.renames=true"]
DIFF = ["--no-ext-diff", "--no-textconv", "-M", "--diff-algorithm=myers", "--unified=0", "--no-color", "--no-prefix"]
HUNK = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")


def git(*args):
    return subprocess.run([*GIT, *args], check=True, capture_output=True).stdout.decode("utf-8", "replace")


def ext(path):
    name = path.rsplit("/", 1)[-1]
    return name.rsplit(".", 1)[-1].lower() if "." in name else ""


def is_code(path):
    return path != "/dev/null" and ext(path) in CODE and not NOT_CODE.search(path)


def code_line_numbers(source, ext):
    """The 1-based numbers of the lines that hold code, not only blanks or comments."""
    numbers, in_block = set(), False
    for number, line in enumerate(source.splitlines(), 1):
        if ext in HASH_COMMENTS or ext in DASH_COMMENTS:
            text = line.strip()
            if text and not text.startswith("#" if ext in HASH_COMMENTS else "--"):
                numbers.add(number)
            continue
        code, i = "", 0
        while i < len(line):
            if in_block:
                end = line.find("*/", i)
                if end < 0:
                    break
                in_block, i = False, end + 2
            elif line.startswith("//", i):
                break
            elif line.startswith("/*", i):
                in_block, i = True, i + 2
            else:
                code, i = code + line[i], i + 1
        if code.strip():
            numbers.add(number)
    return numbers


def path_of(header):
    path = header[4:].rstrip("\t")
    return path[1:-1] if path.startswith('"') and path.endswith('"') else path


def main(rev):
    if not rev:
        old_rev, new_rev, sides = "HEAD", "", ["--cached"]
    elif "..." in rev:
        base, head = rev.split("...", 1)
        new_rev = head or "HEAD"
        old_rev = git("merge-base", base, new_rev).strip()
        sides = [old_rev, new_rev]
    else:
        old_rev, new_rev = f"{rev}^1", rev
        sides = [old_rev, new_rev]
    text = git("diff", *DIFF, *sides)
    added = removed = 0
    old_lines = new_lines = set()
    old_path, header = None, False
    for line in text.splitlines():
        # A removed "-- x" or added "++ x" line only ever comes after the header's first @@.
        if line.startswith("diff --git "):
            header = True
        elif header and line.startswith("--- "):
            old_path = path_of(line)
        elif header and line.startswith("+++ ") and old_path is not None:
            new_path = path_of(line)
            old_lines = code_line_numbers(git("show", f"{old_rev}:{old_path}"), ext(old_path)) if is_code(old_path) else set()
            new_lines = code_line_numbers(git("show", f"{new_rev}:{new_path}"), ext(new_path)) if is_code(new_path) else set()
            old_path = None
        elif match := HUNK.match(line):
            header = False
            old_start, old_count, new_start, new_count = (int(g) if g is not None else 1 for g in match.groups())
            removed += sum(1 for n in range(old_start, old_start + old_count) if n in old_lines)
            added += sum(1 for n in range(new_start, new_start + new_count) if n in new_lines)
    print(f"Code-Lines: +{added} -{removed}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else None)

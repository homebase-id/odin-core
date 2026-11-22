#!/usr/bin/env python3
"""Prints the Code-Lines trailer for the staged changes, a commit, or a range such as `origin/main...HEAD`. The
weekly team data runs this file from each commit's tree, so its rules are what "real code" means there."""
import re
import subprocess
import sys

# Source, build and config files; a line in these counts unless it is blank or only a comment.
C_LIKE = {"kt", "kts", "swift", "java", "cs", "ts", "tsx", "js", "jsx", "mjs", "m", "mm", "c", "h", "cpp", "go", "rs", "gradle"}
LINE_COMMENT = {"py": "#", "sh": "#", "toml": "#", "yml": "#", "yaml": "#", "properties": "#", "pro": "#",
                "sq": "--", "sqm": "--", "sql": "--", "xml": "<!--", "plist": "<!--", "csproj": "<!--",
                "props": "<!--", "targets": "<!--", "json": None}
NOT_CODE = re.compile(
    r"(^|/)(libs|vendor|third_party|gradle/local-repo|node_modules|docs|resources|fixtures?|\.idea)/|\.xcframework/"
    r"|stb_image|/cpp/blake3/|\.min\.js$|\.lock$|package-lock\.json$|(^|/)(res|composeResources)/values[^/]*/"
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
    return path != "/dev/null" and (ext(path) in C_LIKE or ext(path) in LINE_COMMENT) and not NOT_CODE.search(path)


def code_line_numbers(source, kind):
    """The 1-based numbers of the lines that hold code, not only blanks or comments."""
    if kind in LINE_COMMENT:
        marker = LINE_COMMENT[kind]
        return {n for n, line in enumerate(source.splitlines(), 1) if line.strip() and not (marker and line.strip().startswith(marker))}
    numbers, depth, raw = set(), 0, False  # block comments nest in Kotlin and Swift; raw: inside a """ string
    for number, line in enumerate(source.splitlines(), 1):
        code, i = raw, 0
        while i < len(line):
            if raw:
                end = line.find('"""', i)
                if end < 0:
                    break
                raw, i = False, end + 3
            elif depth:
                if line.startswith("/*", i):
                    depth, i = depth + 1, i + 2
                elif line.startswith("*/", i):
                    depth, i = depth - 1, i + 2
                else:
                    i += 1
            elif line.startswith("//", i):
                break
            elif line.startswith("/*", i):
                depth, i = 1, i + 2
            elif line.startswith('"""', i):
                raw, code, i = True, True, i + 3
            elif line[i] in "\"'":
                quote, code, i = line[i], True, i + 1
                while i < len(line) and line[i] != quote:
                    i += 2 if line[i] == "\\" else 1
                i += 1
            else:
                code, i = code or not line[i].isspace(), i + 1
        if code:
            numbers.add(number)
    return numbers


def path_of(header):
    path = header[4:].rstrip("\t")
    return path[1:-1] if path.startswith('"') and path.endswith('"') else path


def sides(rev):
    """The two revisions to compare; "" is the index."""
    if not rev:
        return "HEAD", ""
    if "..." in rev:
        base, head = rev.split("...", 1)
        head = head or "HEAD"
        return git("merge-base", base or "HEAD", head).strip(), head
    if ".." in rev:
        base, head = rev.split("..", 1)
        return base or "HEAD", head or "HEAD"
    parent = subprocess.run([*GIT, "rev-parse", "--verify", "-q", f"{rev}^1"], capture_output=True, text=True).stdout.strip()
    # A root commit is compared with the empty tree.
    return parent or git("hash-object", "-t", "tree", "/dev/null").strip(), rev


def main(rev):
    old_rev, new_rev = sides(rev)
    text = git("diff", *DIFF, old_rev, new_rev) if new_rev else git("diff", "--cached", *DIFF)
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

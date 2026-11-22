#!/usr/bin/env python3
"""Prints the Code-Lines trailer for the staged changes, a commit, or a range such as `origin/main...HEAD`. The
weekly team data runs this file from each commit's tree, so its rules are what "real code" means there.
Tests: python3 scripts/pass_stats_test.py"""
import re
import subprocess
import sys

# Source, build and config files: a line in these counts unless it is blank or only a comment.
C_LIKE = {"kt", "kts", "swift", "java", "cs", "ts", "tsx", "js", "jsx", "mjs", "m", "mm", "c", "h", "cpp", "go", "rs",
          "gradle", "xcconfig", "pbxproj"}
NESTING = {"kt", "kts", "swift", "rs"}
BACKTICK = {"ts", "tsx", "js", "jsx", "mjs", "go"}
LINE_COMMENT = {"py": "#", "sh": "#", "rb": "#", "toml": "#", "yml": "#", "yaml": "#", "properties": "#", "pro": "#",
                "conf": "#", "def": "#", "cmake": "#", "sq": "--", "sqm": "--", "sql": "--", "json": None, "bat": None}
XML = {"xml", "plist", "entitlements", "csproj", "props", "targets", "html"}
NAMES = {"Fastfile": "rb", "Podfile": "rb", "Gemfile": "rb", "CMakeLists.txt": "cmake"}
NOT_CODE = re.compile(
    r"(^|/)(libs|vendor|third_party|gradle/local-repo|node_modules|docs|fixtures?|\.idea|ffmpeg-wasm/core)/"
    r"|\.xcframework/|stb_image|/cpp/blake3/|\.min\.js$|(^|/)sql-wasm\.js$|\.lock$|package-lock\.json$"
    r"|(^|/)composeResources/|(^|/)res/(?!xml/)|(^|/)resources/(?!.*\.(js|mjs|html|json)$)"
    r"|(^|/)src/[A-Za-z]*[Tt]est[A-Za-z]*/|^tests?/|(^|/)(__tests__|test-utils|test-fixtures|iosAppTests)/"
    r"|\.(test|spec)\.(ts|tsx|js)$|_test\.(rs|py)$"
)
# Plumbing-level settings, so the count doesn't depend on whoever runs it, or where.
GIT = ["git", "-c", "core.quotePath=false", "-c", "diff.renames=true"]
DIFF = ["--no-ext-diff", "--no-textconv", "--no-relative", "-M", "--diff-algorithm=myers", "--no-indent-heuristic",
        "--unified=0", "--no-color", "--no-prefix"]
HUNK = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")


def git(*args):
    return subprocess.run([*GIT, *args], check=True, capture_output=True).stdout.decode("utf-8", "replace")


def kind(path):
    name = path.rsplit("/", 1)[-1]
    return NAMES.get(name) or (name.rsplit(".", 1)[-1].lower() if "." in name else "")


def is_code(path):
    k = kind(path)
    return path != "/dev/null" and (k in C_LIKE or k in LINE_COMMENT or k in XML) and not NOT_CODE.search(path)


def lines_of(text):
    # git numbers lines by \n alone; str.splitlines() also splits on \r, \x0c, U+2028 and others.
    return text.split("\n")


def code_line_numbers(source, k):
    """The 1-based numbers of the lines that hold code, not only blanks or comments."""
    if k in LINE_COMMENT:
        marker = LINE_COMMENT[k]
        return {n for n, line in enumerate(lines_of(source), 1) if line.strip() and not (marker and line.strip().startswith(marker))}
    if k in XML:
        return markup_code_lines(source)
    quotes = "\"`" if k in BACKTICK else "\"" if k == "rs" else "\"'"
    numbers, depth, open_quote = set(), 0, None  # open_quote: a string that runs past the end of its line
    for number, line in enumerate(lines_of(source), 1):
        code, i = open_quote is not None, 0
        while i < len(line):
            if open_quote:
                end = line.find(open_quote, i)
                if end < 0:
                    break
                open_quote, i = None, end + len(open_quote)
            elif depth:
                if line.startswith("*/", i):
                    depth, i = depth - 1, i + 2
                elif k in NESTING and line.startswith("/*", i):
                    depth, i = depth + 1, i + 2
                else:
                    i += 1
            elif line.startswith("//", i):
                break
            elif line.startswith("/*", i):
                depth, i = 1, i + 2
            elif line.startswith('"""', i) or line[i] == "`" and "`" in quotes:
                quote = '"""' if line[i] == '"' else "`"
                code, i = True, i + len(quote)
                end = line.find(quote, i)
                if end < 0:
                    open_quote = quote
                    break
                i = end + len(quote)
            elif line[i] in quotes:
                quote, code, i = line[i], True, i + 1
                while i < len(line) and line[i] != quote:
                    i += 2 if line[i] == "\\" else 1
                i += 1
            else:
                code, i = code or not line[i].isspace(), i + 1
        if code:
            numbers.add(number)
    return numbers


def markup_code_lines(source):
    numbers, in_comment = set(), False
    for number, line in enumerate(lines_of(source), 1):
        rest, code = line, False
        while rest:
            if in_comment:
                end = rest.find("-->")
                if end < 0:
                    break
                in_comment, rest = False, rest[end + 3:]
            else:
                start = rest.find("<!--")
                code = code or bool((rest if start < 0 else rest[:start]).strip())
                if start < 0:
                    break
                in_comment, rest = True, rest[start + 4:]
        if code:
            numbers.add(number)
    return numbers


def path_of(header):
    path = header[4:].rstrip("\t")
    if not (path.startswith('"') and path.endswith('"')):
        return path
    # git C-quotes a path holding a quote, a backslash or a control character; octal escapes are UTF-8 bytes.
    return path[1:-1].encode("latin-1", "backslashreplace").decode("unicode_escape").encode("latin-1").decode("utf-8")


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
    return parent or empty_tree(), rev


def empty_tree():
    return git("hash-object", "-t", "tree", "/dev/null").strip()


def code_lines(rev):
    old_rev, new_rev = sides(rev)
    text = git("diff", *DIFF, old_rev, new_rev) if new_rev else git("diff", "--cached", *DIFF)
    added = removed = 0
    old_lines = new_lines = set()
    old_path, header = None, False
    for line in lines_of(text):
        # A removed "-- x" or added "++ x" line only ever comes after the header's first @@.
        if line.startswith("diff --git "):
            header = True
        elif header and line.startswith("--- "):
            old_path = path_of(line)
        elif header and line.startswith("+++ ") and old_path is not None:
            new_path = path_of(line)
            old_lines = code_line_numbers(git("show", f"{old_rev}:{old_path}"), kind(old_path)) if is_code(old_path) else set()
            new_lines = code_line_numbers(git("show", f"{new_rev}:{new_path}"), kind(new_path)) if is_code(new_path) else set()
            old_path = None
        elif match := HUNK.match(line):
            header = False
            old_start, old_count, new_start, new_count = (int(g) if g is not None else 1 for g in match.groups())
            removed += sum(1 for n in range(old_start, old_start + old_count) if n in old_lines)
            added += sum(1 for n in range(new_start, new_start + new_count) if n in new_lines)
    return added, removed


if __name__ == "__main__":
    added, removed = code_lines(sys.argv[1] if len(sys.argv) > 1 else None)
    print(f"Code-Lines: +{added} -{removed}")

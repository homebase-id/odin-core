#!/usr/bin/env python3
"""Prints the Code-Lines trailer for the staged changes, a commit, or a range such as `origin/main...HEAD`. The
weekly team data runs this file from each commit's tree, so its rules are what "real code" means there.
Tests: python3 scripts/pass_stats_test.py"""
import codecs
import re
import subprocess
import sys

# Source, build and config files: a line in these counts unless it is blank or only a comment.
C_LIKE = {"kt", "kts", "swift", "java", "cs", "cshtml", "ts", "tsx", "mts", "cts", "js", "jsx", "mjs", "cjs", "css",
          "m", "mm", "c", "h", "cpp", "go", "rs", "gradle", "xcconfig", "pbxproj"}
LINE_COMMENT = {"py": "#", "sh": "#", "rb": "#", "toml": "#", "yml": "#", "yaml": "#", "properties": "#", "pro": "#",
                "conf": "#", "cmake": "#", "docker": "#", "make": "#", "spec": "#", "lines": None, "sq": "--", "sqm": "--", "sql": "--", "json": None, "bat": None}
XML = {"xml", "plist", "entitlements", "csproj", "props", "targets", "html", "policy", "xcscheme", "storyboard"}
SPECIAL = {"def"}  # a cinterop .def: # comments above its --- line, C below it
NAMES = {"Fastfile": "rb", "Podfile": "rb", "Gemfile": "rb", "CMakeLists.txt": "cmake", "Dockerfile": "docker",
         "Makefile": "make", "objc-entry-points.txt": "lines"}
NOT_CODE = re.compile(
    r"(^|/)(libs|vendor|third_party|gradle/local-repo|node_modules|docs|fixtures?|\.idea|ffmpeg-wasm/core)/"
    r"|\.xcframework/|stb_image|/cpp/blake3/|\.min\.js$|(^|/)sql-wasm\.js$|\.lock$|package-lock\.json$"
    r"|(^|/)composeResources/|(^|/)res/(?!xml/)|(^|/)resources/(?!META-INF/|.*\.(js|mjs|html|json)$)"
    r"|(^|/)src/[A-Za-z]*[Tt]est[A-Za-z]*/|^tests?/|(^|/)(__tests__|test-utils|test-fixtures|iosAppTests)/"
    r"|\.(test|spec)\.(ts|tsx|js)$|_test\.(rs|py)$"
)
# Plumbing-level settings, so the count doesn't depend on whoever runs it, or where.
GIT = ["git", "-c", "core.quotePath=false", "-c", "diff.renames=true"]
DIFF = ["--no-ext-diff", "--no-textconv", "--no-relative", "--text", "-M", "-l0", "--diff-algorithm=myers",
        "--no-indent-heuristic", "--inter-hunk-context=0", "--unified=0", "--no-color", "--no-prefix"]
HUNK = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")


def git(*args):
    done = subprocess.run([*GIT, *args], capture_output=True)
    if done.returncode:
        sys.exit(f"git {' '.join(args)}: {done.stderr.decode('utf-8', 'replace').strip()}")
    return done.stdout.decode("utf-8", "replace")


def kind(path):
    name = path.rsplit("/", 1)[-1]
    if "/META-INF/services/" in path:
        return "lines"
    return NAMES.get(name) or (name.rsplit(".", 1)[-1].lower() if "." in name else "")


def is_code(path):
    k = kind(path)
    return path != "/dev/null" and (k in C_LIKE or k in LINE_COMMENT or k in XML or k in SPECIAL) and not NOT_CODE.search(path)


def lines_of(text):
    # git numbers lines by \n alone; str.splitlines() also splits on \r, \x0c, U+2028 and others.
    return text.split("\n")


def code_line_numbers(source, k):
    """Only a line that starts with a comment marker, or continues a block comment opened at the start of a line (it
    starts with * or holds the closer), is a comment; anything else counts, and ends the block. A misjudged line
    counts a comment as code; the one way to miss code is a line in a multi-line string that looks like a comment."""
    lines = lines_of(source)
    if k == "def":
        cut = next((i for i, line in enumerate(lines) if line.strip() == "---"), len(lines))
        return {n for n, line in enumerate(lines, 1) if line.strip() and (n > cut + 1 or not line.strip().startswith("#"))} - {cut + 1}
    if k in LINE_COMMENT:
        marker = LINE_COMMENT[k]
        return {n for n, line in enumerate(lines, 1) if line.strip() and not (marker and line.strip().startswith(marker))}
    opener, closer, marker = ("<!--", "-->", None) if k in XML else ("/*", "*/", "//")
    numbers, in_block = set(), False
    for n, line in enumerate(lines, 1):
        text = line.strip()
        if in_block and (closer in text or not text or k in XML or text.startswith("*")):
            in_block = closer not in text
            if in_block or not text.split(closer, 1)[1].strip():
                continue
        elif in_block:
            in_block = False
        elif not text or marker and text.startswith(marker):
            continue
        elif text.startswith(opener):
            after = text[len(opener):]
            in_block = closer not in after
            if in_block or not after.split(closer, 1)[1].strip():
                continue
        numbers.add(n)
    return numbers


def path_of(header):
    path = header[4:].rstrip("\t")
    if not (path.startswith('"') and path.endswith('"')):
        return path
    # git C-quotes a path holding a quote, a backslash or a control character, and writes UTF-8 inside as it is.
    return codecs.escape_decode(path[1:-1].encode("utf-8"))[0].decode("utf-8")


def sides(rev):
    """The two revisions to compare; "" is the index."""
    if not rev:
        return "HEAD", ""
    if "..." in rev:
        base, head = rev.split("...", 1)
        head = head or "HEAD"
        found = subprocess.run([*GIT, "merge-base", base or "HEAD", head], capture_output=True, text=True)
        if found.returncode:
            sys.exit(found.stderr.strip() or f"{base or 'HEAD'} and {head} have no common ancestor")
        return found.stdout.strip(), head
    if ".." in rev:
        base, head = rev.split("..", 1)
        return base or "HEAD", head or "HEAD"
    # The commit object names its parents even where a shallow clone cut them off.
    parents = [line.split()[1] for line in lines_of(git("cat-file", "-p", rev)) if line.startswith("parent ")]
    if not parents:
        return empty_tree(), rev
    if subprocess.run([*GIT, "cat-file", "-e", parents[0]], capture_output=True).returncode:
        sys.exit(f"{rev}'s parent isn't in this shallow clone: fetch more history")
    return parents[0], rev


def empty_tree():
    return git("hash-object", "-t", "tree", "/dev/null").strip()


def code_lines(rev):
    old_rev, new_rev = sides(rev)
    text = git("diff", *DIFF, old_rev, new_rev) if new_rev else git("diff", "--cached", *DIFF)
    added = removed = 0
    old_lines = new_lines = set()
    old_path = renamed = None
    header = False

    def code_of(rev_, path):
        return code_line_numbers(git("show", f"{rev_}:{path}"), kind(path)) if is_code(path) else set()

    def moved_whole(old, new):
        # Moved between code and tests or docs: all of the code side changed, not only the edited lines.
        nonlocal added, removed
        added, removed = added + len(code_of(new_rev, new)), removed + len(code_of(old_rev, old))

    for line in lines_of(text):
        # A removed "-- x" or added "++ x" line only ever comes after the header's first @@.
        if line.startswith("diff --git "):
            if renamed:
                moved_whole(*renamed)
            header, renamed = True, None
        elif header and line.startswith("rename from "):
            renamed = (path_of("    " + line[len("rename from "):]), None)
        elif header and line.startswith("rename to ") and renamed:
            old, new = renamed[0], path_of("    " + line[len("rename to "):])
            renamed = (old, new) if is_code(old) != is_code(new) else None
        elif header and line.startswith("--- "):
            old_path = path_of(line)
        elif header and line.startswith("+++ ") and old_path is not None:
            new_path = path_of(line)
            if renamed:
                moved_whole(*renamed)
                old_lines = new_lines = set()
            else:
                old_lines, new_lines = code_of(old_rev, old_path), code_of(new_rev, new_path)
            old_path = renamed = None
        elif match := HUNK.match(line):
            header = False
            old_start, old_count, new_start, new_count = (int(g) if g is not None else 1 for g in match.groups())
            removed += sum(1 for n in range(old_start, old_start + old_count) if n in old_lines)
            added += sum(1 for n in range(new_start, new_start + new_count) if n in new_lines)
    if renamed:
        moved_whole(*renamed)
    return added, removed


if __name__ == "__main__":
    added, removed = code_lines(sys.argv[1] if len(sys.argv) > 1 else None)
    print(f"Code-Lines: +{added} -{removed}")

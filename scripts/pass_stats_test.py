#!/usr/bin/env python3
"""Tests for pass-stats.py: python3 scripts/pass_stats_test.py"""
import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("pass_stats", Path(__file__).with_name("pass-stats.py"))
ps = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ps)


def code(source, kind="kt"):
    return ps.code_line_numbers(source, kind)


class Scanner(unittest.TestCase):
    def test_comments_and_blanks_dont_count(self):
        self.assertEqual({4}, code("/**\n * Doc.\n */\nfun g() = 1 // tail\n\n// note\n"))

    def test_a_star_continuation_is_code(self):
        self.assertEqual({1, 2, 3, 4}, code("f(\n    a\n    * b\n)\n"))

    def test_a_comment_opener_in_a_string_is_code(self):
        self.assertEqual({1, 2}, code('val t = "image/*"\nval b = 1\n'))
        self.assertEqual({1, 2}, code("val c = '/'\nval d = '*'\n"))

    def test_raw_strings_span_lines(self):
        self.assertEqual({1, 2, 3, 4}, code('val s = """\n// not a comment\n/* nor this\n"""\n'))

    def test_block_comments_nest_only_where_the_language_nests_them(self):
        self.assertEqual({2}, code("/* a /* nested */ still */\nx\n", "kt"))
        self.assertEqual({2, 3}, code("/** Accepts image/* types. */\nfunction g() {}\nconst x = 1;\n", "js"))

    def test_template_literals_are_strings(self):
        self.assertEqual({1, 2}, code("const t = `image/*`;\nconst y = 2;\n", "ts"))
        self.assertEqual({1, 2, 3}, code("const t = `\n/* text\n`;\n", "ts"))

    def test_a_rust_lifetime_isnt_a_quote(self):
        self.assertEqual({1}, code("fn f<'a>(x: &'a str) {} /* c\n*/\n", "rs"))

    def test_lines_are_numbered_as_git_numbers_them(self):
        self.assertEqual({1, 2, 3}, code('val s = "a\x0cb"\nval b = 2\nval c = 3\n'))

    def test_line_comment_and_markup_kinds(self):
        self.assertEqual({2}, code("# c\nkey = 1\n\n", "toml"))
        self.assertEqual({1, 2, 3}, code('{\n  "a": 1\n}\n', "json"))
        self.assertEqual({4}, code('<!--\n<uses-permission android:name="x"/>\n-->\n<a/>\n', "xml"))


class Paths(unittest.TestCase):
    def test_what_counts_as_code(self):
        counts = ["a/src/commonMain/kotlin/A.kt", "webApp/src/wasmJsMain/resources/sw.js", "iosApp/fastlane/Fastfile",
                  "homebase-blake3/src/main/cpp/CMakeLists.txt", "iosApp/iosApp/iosApp.entitlements", "gradle/libs.versions.toml",
                  "androidApp/src/main/res/xml/network_security_config.xml", "core/test/TestHelpers.kt", "src/Odin.Core/Drive.cs"]
        skips = ["a/src/jvmTest/kotlin/ATest.kt", "tests/apps/DriveTests.cs", "docs/x.kt", "README.md",
                 "webApp/src/wasmJsMain/resources/sql-wasm.js", "webApp/src/wasmJsMain/resources/mp4box.all.min.js",
                 "webApp/src/wasmJsMain/resources/ffmpeg-wasm/core/esm/ffmpeg-core.js", "androidApp/src/main/res/drawable/x.xml",
                 "androidApp/src/main/res/values-da/strings.xml", "a/src/commonMain/composeResources/files/emoji_data.json",
                 "homebase-blake3/src/main/cpp/blake3/blake3.c", "yarn.lock", "scripts/pass_stats_test.py"]
        self.assertEqual([], [p for p in counts if not ps.is_code(p)])
        self.assertEqual([], [p for p in skips if ps.is_code(p)])

    def test_c_quoted_paths_are_unescaped(self):
        self.assertEqual('a"b.kt', ps.path_of('+++ "a\\"b.kt"'))
        self.assertEqual("é.kt", ps.path_of('+++ "\\303\\251.kt"'))
        self.assertEqual("a b/Foo.kt", ps.path_of("+++ a b/Foo.kt\t"))


class Repository(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.root = Path(self.dir.name)
        self.cwd = os.getcwd()
        os.chdir(self.root)
        self.git("init", "-q")

    def tearDown(self):
        os.chdir(self.cwd)
        self.dir.cleanup()

    def git(self, *args):
        env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")
        return subprocess.run(["git", *args], check=True, capture_output=True, text=True, env=env).stdout.strip()

    def write(self, path, text):
        (self.root / path).parent.mkdir(parents=True, exist_ok=True)
        (self.root / path).write_text(text)

    def commit(self, message="c"):
        self.git("add", "-A")
        self.git("commit", "-q", "-m", message)
        return self.git("rev-parse", "HEAD")

    def test_staged_commit_root_and_ranges(self):
        self.write("A.kt", "val a = 1\n")
        root = self.commit()
        self.assertEqual((1, 0), ps.code_lines(root), "a root commit is compared with the empty tree")
        self.write("a b/Foo.kt", "val x = 1\n// c\n")
        self.write('q"uote.kt', "val y = 2\n")
        self.write("src/jvmTest/T.kt", "fun t() = 1\n")
        self.write("README.md", "text\n")
        self.git("add", "-A")
        self.assertEqual((2, 0), ps.code_lines(None), "staged: odd paths count, tests and docs don't")
        second = self.commit()
        self.assertEqual((2, 0), ps.code_lines(second))
        self.assertEqual((2, 0), ps.code_lines(f"{root}..{second}"))
        self.assertEqual((2, 0), ps.code_lines(f"{root}...{second}"))

    def test_a_rename_counts_each_side_by_its_own_path(self):
        self.write("src/commonMain/Foo.kt", "val a = 1\nval b = 2\n")
        self.commit()
        os.makedirs("src/jvmTest", exist_ok=True)
        self.git("mv", "src/commonMain/Foo.kt", "src/jvmTest/Foo.kt")
        self.write("src/jvmTest/Foo.kt", "val a = 1\n")
        self.assertEqual((0, 1), ps.code_lines(self.commit()), "the removed production line counts; the test side doesn't")

    def test_the_runners_git_config_and_folder_dont_change_the_count(self):
        self.write("A.kt", "val a = 1\n")
        self.write("sub/B.kt", "val b = 1\n")
        self.git("add", "-A")
        self.git("config", "diff.relative", "true")
        os.chdir(self.root / "sub")
        self.assertEqual((2, 0), ps.code_lines(None))


if __name__ == "__main__":
    unittest.main()

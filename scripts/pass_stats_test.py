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

    def test_a_comment_opener_inside_a_line_never_hides_code(self):
        self.assertEqual({1, 2, 3}, code("const a = 'image/*';\nconst b = 1;\nconst c = 2;\n", "js"))
        self.assertEqual({1, 3, 4}, code("const a = `x\\`y`;\n// note\nconst b = `see /* here`;\nconst c = 1;\n", "js"))
        self.assertEqual({1, 2, 3}, code("const r = /\\/*$/;\nconst c = 1;\nconst d = 2;\n", "js"))
        self.assertEqual({2, 3}, code("/** Accepts image/* types. */\nfunction g() {}\nconst x = 1;\n", "js"))

    def test_a_misjudged_line_only_ever_counts_a_comment_as_code(self):
        self.assertEqual({1, 2}, code("/* a /* nested */ still */\nx\n"), "code after a closer counts")
        self.assertEqual({1, 2}, code("fn f<'a>(x: &'a str) {} /* c\n*/\n", "rs"), "a comment opened mid-line isn't followed")

    def test_the_one_way_it_can_miss_code_is_one_line(self):
        # A line inside a multi-line string that looks like a comment reads as one, and no more than that line.
        self.assertEqual({1, 3}, code('val s = """\n// not a comment\n"""\n'))
        self.assertEqual({1, 3, 4, 5}, code('val s = """\n/* glob\n"""\nval y = 2\nval z = 3\n'))

    def test_a_block_comment_without_stars_counts_as_code_after_its_first_line(self):
        self.assertEqual({2, 3, 4}, code("/* note\n   more\n*/\nx\n"), "counted as code: one more pass, never one skipped")

    def test_a_cinterop_def_has_comments_above_its_separator_and_c_below(self):
        self.assertEqual({2, 5, 6}, code("# about\nheaders = blake3.h\n---\n\n#include \"blake3.c\"\n#define X 1\n", "def"))

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
                  "androidApp/src/main/res/xml/network_security_config.xml", "core/test/TestHelpers.kt", "src/Odin.Core/Drive.cs",
                  "homebase-core/objc-entry-points.txt", "desktopApp/linux/id.homebase.feed.vault-unlock.policy",
                  "androidApp/src/main/resources/META-INF/services/kotlinx.coroutines.CoroutineExceptionHandler",
                  "iosApp/iosApp.xcodeproj/xcshareddata/xcschemes/iosApp.xcscheme", "iosApp/ShareExtension/Base.lproj/MainInterface.storyboard",
                  "src/Odin.Oidc.Login/Pages/Consent.cshtml", "Dockerfile", "Makefile", "web/app.css", "web/vite.config.cjs",
                  "packaging/linux/homebase-filesync.spec"]
        skips = ["a/src/jvmTest/kotlin/ATest.kt", "tests/apps/DriveTests.cs", "docs/x.kt", "README.md",
                 "webApp/src/wasmJsMain/resources/sql-wasm.js", "webApp/src/wasmJsMain/resources/mp4box.all.min.js",
                 "webApp/src/wasmJsMain/resources/ffmpeg-wasm/core/esm/ffmpeg-core.js", "androidApp/src/main/res/drawable/x.xml",
                 "androidApp/src/main/res/values-da/strings.xml", "a/src/commonMain/composeResources/files/emoji_data.json",
                 "homebase-blake3/src/main/cpp/blake3/blake3.c", "yarn.lock", "scripts/pass_stats_test.py"]
        self.assertEqual([], [p for p in counts if not ps.is_code(p)])
        self.assertEqual([], [p for p in skips if ps.is_code(p)])

    def test_c_quoted_paths_are_unescaped(self):
        self.assertEqual('a"b.kt', ps.path_of('+++ "a\\"b.kt"'))
        self.assertEqual('café"x.kt', ps.path_of('+++ "café\\"x.kt"'))
        self.assertEqual("a\tb.kt", ps.path_of('+++ "a\\tb.kt"'))
        self.assertEqual("a b/Foo.kt", ps.path_of("+++ a b/Foo.kt\t"))


class Repository(unittest.TestCase):
    ISOLATED = dict(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1", GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t",
                    GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")

    def setUp(self):
        # No signing, hooks or settings of whoever runs the tests, for the tests' git and the script's alike.
        self.saved_env = dict(os.environ)
        os.environ.update(self.ISOLATED)
        self.dir = tempfile.TemporaryDirectory()
        self.root = Path(self.dir.name)
        self.cwd = os.getcwd()
        os.chdir(self.root)
        self.git("init", "-q")

    def tearDown(self):
        os.chdir(self.cwd)
        os.environ.clear()
        os.environ.update(self.saved_env)
        self.dir.cleanup()

    def git(self, *args):
        return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout.strip()

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

    def test_a_move_between_code_and_tests_counts_the_whole_code_side(self):
        self.write("src/commonMain/Foo.kt", "val a = 1\nval b = 2\n")
        self.commit()
        os.makedirs("src/jvmTest", exist_ok=True)
        self.git("mv", "src/commonMain/Foo.kt", "src/jvmTest/Foo.kt")
        self.write("src/jvmTest/Foo.kt", "val a = 1\n")
        self.assertEqual((0, 2), ps.code_lines(self.commit()), "both production lines left the code")
        self.git("mv", "src/jvmTest/Foo.kt", "src/commonMain/Foo.kt")
        self.assertEqual((1, 0), ps.code_lines(self.commit()), "an unedited move into code adds its lines")

    def test_the_runners_git_config_and_folder_dont_change_the_count(self):
        self.write("A.kt", "val a = 1\nval b = 2\nval c = 3\nval d = 4\n")
        self.write("sub/B.kt", "val b = 1\n")
        self.commit()
        self.write("A.kt", "val a = 9\nval b = 2\nval c = 3\nval d = 9\n")
        self.write("sub/B.kt", "val b = 9\n")
        self.git("add", "-A")
        for key, value in [("diff.relative", "true"), ("diff.interHunkContext", "5"), ("diff.renames", "false")]:
            self.git("config", key, value)
        (self.root / ".git/info/attributes").write_text("sub/B.kt -diff\n")
        os.chdir(self.root / "sub")
        self.assertEqual((3, 3), ps.code_lines(None))

    def test_a_shallow_clones_missing_parent_is_an_error_not_a_root(self):
        self.write("A.kt", "val a = 1\n")
        self.commit()
        self.write("A.kt", "val a = 2\n")
        self.commit()
        shallow = self.root.parent / (self.root.name + "-shallow")
        subprocess.run(["git", "clone", "-q", "--depth", "1", f"file://{self.root}", str(shallow)], check=True, capture_output=True)
        os.chdir(shallow)
        try:
            with self.assertRaises(SystemExit) as error:
                ps.code_lines("HEAD")
            self.assertIn("shallow", str(error.exception))
        finally:
            subprocess.run(["rm", "-rf", str(shallow)])

    def test_a_root_commit_in_a_shallow_clone_is_still_a_root(self):
        self.write("A.kt", "val a = 1\n")
        root = self.commit()
        shallow = self.root.parent / (self.root.name + "-root")
        subprocess.run(["git", "clone", "-q", "--depth", "1", f"file://{self.root}", str(shallow)], check=True, capture_output=True)
        os.chdir(shallow)
        try:
            self.assertEqual((1, 0), ps.code_lines(root))
        finally:
            subprocess.run(["rm", "-rf", str(shallow)])

    def test_unrelated_histories_say_so(self):
        self.write("A.kt", "val a = 1\n")
        self.commit()
        self.git("checkout", "-q", "--orphan", "other")
        self.write("B.kt", "val b = 1\n")
        self.commit()
        with self.assertRaises(SystemExit) as error:
            ps.code_lines("master...HEAD" if "master" in self.git("branch") else "main...HEAD")
        self.assertIn("no common ancestor", str(error.exception))

    def test_gits_own_message_is_shown_when_it_fails(self):
        self.write("A.kt", "val a = 1\n")
        self.commit()
        with self.assertRaises(SystemExit) as error:
            ps.code_lines("no-such-branch...HEAD")
        self.assertIn("no-such-branch", str(error.exception))


if __name__ == "__main__":
    unittest.main()

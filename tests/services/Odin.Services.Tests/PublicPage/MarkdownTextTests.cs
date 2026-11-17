using NUnit.Framework;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class MarkdownTextTests
{
    [TestCase("**bold** and *italic* and ~~gone~~", "bold and italic and gone")]
    [TestCase("__bold__ and _italic_", "bold and italic")]
    [TestCase("# Title", "Title")]
    [TestCase("### Third", "Third")]
    [TestCase("> quoted", "quoted")]
    [TestCase("- one\n* two\n+ three", "one\ntwo\nthree")]
    [TestCase("1. one\n2) two", "one\ntwo")]
    [TestCase("see [the docs](https://x.test/a_b) now", "see the docs now")]
    [TestCase("run `npm i` first", "run npm i first")]
    [TestCase("```js\nlet a = 1 * 2;\n```", "let a = 1 * 2;")]
    [TestCase("above\n\n---\n\nbelow", "above\nbelow")]
    [TestCase("  spaced   out  ", "spaced out")]
    [TestCase("5 * 3", "5 * 3")]
    [TestCase("snake_case and my_var_name", "snake_case and my_var_name")]
    [TestCase("2024.", "2024.")]
    [TestCase("#hashtag", "#hashtag")]
    [TestCase("</h1><script>alert(1)</script>", "</h1><script>alert(1)</script>")]
    [TestCase(null, "")]
    public void ToPlain(string input, string expected) =>
        Assert.That(MarkdownText.ToPlain(input), Is.EqualTo(expected));

    [Test]
    public void ToPlain_SingleLineJoinsWithSpaces() =>
        Assert.That(MarkdownText.ToPlain("# Hi\n- a\n- b", singleLine: true), Is.EqualTo("Hi a b"));
}

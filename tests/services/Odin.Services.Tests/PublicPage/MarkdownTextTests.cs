using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class MarkdownTextTests
{
    [TestCase("**bold** and *italic* and ~~gone~~", "bold and italic and gone")]
    [TestCase("__bold__ and _italic_", "bold and italic")]
    [TestCase("# Title", "Title")]
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
    [TestCase(null, "")]
    public void ToPlain(string input, string expected) =>
        Assert.That(MarkdownText.ToPlain(input), Is.EqualTo(expected));

    [Test]
    public void ToPlain_SingleLineJoinsWithSpaces() =>
        Assert.That(MarkdownText.ToTitle("# Hi\n- a\n- b"), Is.EqualTo("Hi a b"));

    [TestCase("100. Things", "100. Things")]
    [TestCase("7) Reasons", "7) Reasons")]
    [TestCase(">=5 apps", ">=5 apps")]
    [TestCase(">_< wow", ">_< wow")]
    [TestCase("> quoted", "quoted")]
    public void ToTitle_KeepsNumbersAndAngles(string input, string expected) =>
        Assert.That(MarkdownText.ToTitle(input), Is.EqualTo(expected));

    [Test]
    public void AdversarialInputFinishesFast()
    {
        var input = string.Concat(Enumerable.Repeat("**a ", 100_000));
        var sw = Stopwatch.StartNew();
        MarkdownText.ToTitle(input);
        MarkdownText.ToPlain(input);
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(3000));
    }
}

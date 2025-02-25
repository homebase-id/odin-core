using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Core.Cryptography.Tests.BIP39
{
	public class TestAutoLanguageDetect
	{
		[Test]
		public void TestKnownEnglish()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.English, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "about" }));
		}

		[Test]
		public void TestKnownJapenese()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.Japanese, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "あいこくしん", "あいさつ", "あいだ", "あおぞら", "あかちゃん", "あきる", "あけがた", "あける", "あこがれる", "あさい", "あさひ", "あしあと", "あじわう", "あずかる", "あずき", "あそぶ", "あたえる", "あたためる", "あたりまえ", "あたる", "あつい", "あつかう", "あっしゅく", "あつまり", "あつめる", "あてな", "あてはまる", "あひる", "あぶら", "あぶる", "あふれる", "あまい", "あまど", "あまやかす", "あまり", "あみもの", "あめりか" }));
		}

		[Test]
		public void TestKnownSpanish()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.Spanish, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "yoga", "yogur", "zafiro", "zanja", "zapato", "zarza", "zona", "zorro", "zumo", "zurdo" }));
		}

		[Test]
		public void TestKnownChineseSimplified()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.ChineseSimplified, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "的", "一", "是", "在", "不", "了", "有", "和", "人", "这" }));
		}

		[Test]
		public void TestKnownChineseTraditional()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.ChineseTraditional, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "的", "一", "是", "在", "不", "了", "有", "和", "載" }));
		}

		[Test]
		public void TestKnownFrench()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.French, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "abaisser", "brutal", "bulletin", "circuler", "citoyen", "impact", "joyeux", "massif", "nébuleux" }));
		}

		[Test]
		public void TestKnownUnknown()
		{
			ClassicAssert.AreEqual(Bitcoin.BIP39.BIP39.Language.Unknown, Bitcoin.BIP39.BIP39.AutoDetectLanguageOfWords(new string[] { "gffgfg", "khjkjk", "kjkkj" }));
		}
	}
}

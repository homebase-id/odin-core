using System;
using System.Diagnostics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Util;

namespace Odin.Core.Tests
{
    public class DomainNameValidatorTests
    {

        [SetUp]
        public void Setup()
        {
        }

        [Test(Description = "Label cannot be empty")]
        public void LabelEmptyTest()
        {
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain(".aaa"));
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain("aaa."));
        }

        [Test(Description = "Test 63 character label is OK")]
        public void LabelLengthOKTest()
        {
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("012345678901234567890123456789012345678901234567890123456789012.aaa"));
        }

        [Test(Description = "Test 64 character label fails")]
        public void LabelLengthFailTest()
        {
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain("0123456789012345678901234567890123456789012345678901234567890123.aaa"));
        }

        [Test(Description = "Test first char isn't a dash")]
        public void LabelStartDashFailTest()
        {
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain("-a.aaa"));
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain("aa.-aaa"));
        }


        [Test(Description = "Test last char isn't a dash")]
        public void LabelLastDashFailTest()
        {
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain("a-.aaa"));
            ClassicAssert.IsFalse(AsciiDomainNameValidator.TryValidateDomain("aa.aaa-"));
        }

        [Test(Description = "Test 'a' is OK as a label")]
        public void LabelPassTest()
        {
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("a.a"));
        }

        [Test(Description = "Test shortest valid domain")]
        public void DomainShortestPassTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("a.b");
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }
            Assert.Pass();
        }

        [Test(Description = "Test one label domain")]
        public void DomainOneLabelPostFailTest()
        {

            // Assert.Throws<Exception>(c=> DomainNameValidator.TryValidateDomain(".com"), "domain test failed"),
            try
            {
                AsciiDomainNameValidator.AssertValidDomain(".com");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test(Description = "Test one label domain")]
        public void DomainOneLabelPreFailTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("com.");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }


        [Test(Description = "Test invalid starting char domain")]
        public void DomainStartDashFailTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("-a.com");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test(Description = "Test invalid end char domain")]
        public void DomainEndDashFailTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("a.com-");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test(Description = "Invalid domain name with =")]
        public void DomainOddTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("=xyyaa.com");
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
        }

        [Test(Description = "Puny code international domain name")]
        public void DomainInternationalTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("xn--hxajbheg2az3al.xn--jxalpdlp");
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }


        [Test(Description = "Test period invalid")]
        public void DomainDotFailTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain(".");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test(Description = "Test .. invalid")]
        public void DomainDotDotFailTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("..");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test(Description = "Test ... invalid")]
        public void DomainDotDotDotFailTest()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("...");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }


        [Test]
        public void NameTooShortFails()
        {
            try
            {
                AsciiDomainNameValidator.AssertValidDomain("a.");
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }
            try
            {
                AsciiDomainNameValidator.AssertValidDomain(".a");
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }
        }

        [Test]
        public void NameTooLongFails()
        {
            var lbl = new string('a', 9) + ".";
            string dom = "";

            for (int i = 0; i < 25; i++)
                dom += lbl;

            // Now we have a 250 character long dom.
            // 
            ClassicAssert.IsTrue(dom.Length == 250);

            dom = "a" + dom + "como";
            ClassicAssert.IsTrue(dom.Length == 255);
            AsciiDomainNameValidator.AssertValidDomain(dom);

            dom = "a" + dom;
            ClassicAssert.IsTrue(dom.Length == 256);

            try
            {
                AsciiDomainNameValidator.AssertValidDomain(dom);
                Assert.Fail();
            }
            catch (Exception)
            {
            }
            Assert.Pass();
        }


        [Test]
        public void MiscTestsMovedFromOtherCode()
        {
            // Test valid labels
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("") == false, "Empty name error");
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("012345678901234567890123456789012345678901234567890123456789012.aa") == true,
                "63 chars not allowed");
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("0123456789012345678901234567890123456789012345678901234567890123.aa") == false,
                "64 chars allowed");
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("-a.aa") == false, "Allowed to start with -");
            ClassicAssert.IsTrue(AsciiDomainNameValidator.TryValidateDomain("a-.aa") == false, "Allowed to end with -");

            AsciiDomainNameValidator.AssertValidDomain("a.com");

            try { AsciiDomainNameValidator.AssertValidDomain(".com"); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain("a."); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain("-a.com"); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain("a-.com"); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain("a.com-"); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain("."); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain(".."); Assert.Fail(); } catch { }
            try { AsciiDomainNameValidator.AssertValidDomain("..."); Assert.Fail(); } catch { }

            Assert.Pass();
        }

        // [Test]
        // [Ignore("Not yet used...")]
        // public void CNameLookupTest()
        // {
        //     string s;
        //
        //     s = AsciiDomainNameValidator.CNameLookup("alias.id.pub");
        //     ClassicAssert.IsTrue(s == "odin.earth.");
        //
        //     s = AsciiDomainNameValidator.CNameLookup("corleone.com");
        //     ClassicAssert.IsTrue(s == null);
        //
        //     Assert.Pass();
        // }
    }
}


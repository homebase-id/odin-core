using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core.Trie;
using Youverse.Core.Util;

namespace Youverse.Core.Tests
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
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain(".aaa"));
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain("aaa."));
        }

        [Test(Description = "Test 63 character label is OK")]
        public void LabelLengthOKTest()
        {
            Assert.IsTrue(PunyDomainNameValidator.TryValidateDomain("012345678901234567890123456789012345678901234567890123456789012.aaa"));
        }

        [Test(Description = "Test 64 character label fails")]
        public void LabelLengthFailTest()
        {
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain("0123456789012345678901234567890123456789012345678901234567890123.aaa"));
        }

        [Test(Description = "Test first char isn't a dash")]
        public void LabelStartDashFailTest()
        {
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain("-a.aaa"));
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain("aa.-aaa"));
        }


        [Test(Description = "Test last char isn't a dash")]
        public void LabelLastDashFailTest()
        {
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain("a-.aaa"));
            Assert.IsFalse(PunyDomainNameValidator.TryValidateDomain("aa.aaa-"));
        }

        [Test(Description = "Test 'a' is OK as a label")]
        public void LabelPassTest()
        {
            Assert.IsTrue(PunyDomainNameValidator.TryValidateDomain("a.a"));
        }

        [Test(Description = "Test shortest valid domain")]
        public void DomainShortestPassTest()
        {
            try
            {
                PunyDomainNameValidator.AssertValidDomain("a.b");
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
                PunyDomainNameValidator.AssertValidDomain(".com");
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
                PunyDomainNameValidator.AssertValidDomain("com.");
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
                PunyDomainNameValidator.AssertValidDomain("-a.com");
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
                PunyDomainNameValidator.AssertValidDomain("a.com-");
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
                PunyDomainNameValidator.AssertValidDomain("=xyyaa.com");
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
                PunyDomainNameValidator.AssertValidDomain("xn--hxajbheg2az3al.xn--jxalpdlp");
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
                PunyDomainNameValidator.AssertValidDomain(".");
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
                PunyDomainNameValidator.AssertValidDomain("..");
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
                PunyDomainNameValidator.AssertValidDomain("...");
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
                PunyDomainNameValidator.AssertValidDomain("a.");
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }
            try
            {
                PunyDomainNameValidator.AssertValidDomain(".a");
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
            Assert.IsTrue(dom.Length == 250);

            dom = "a" + dom + "como";
            Assert.IsTrue(dom.Length == 255);
            PunyDomainNameValidator.AssertValidDomain(dom);

            dom = "a" + dom;
            Assert.IsTrue(dom.Length == 256);

            try
            {
                PunyDomainNameValidator.AssertValidDomain(dom);
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
            Debug.Assert(PunyDomainNameValidator.TryValidateDomain("") == false, "Empty name error");
            Debug.Assert(PunyDomainNameValidator.TryValidateDomain("012345678901234567890123456789012345678901234567890123456789012.aa") == true,
                "63 chars not allowed");
            Debug.Assert(PunyDomainNameValidator.TryValidateDomain("0123456789012345678901234567890123456789012345678901234567890123.aa") == false,
                "64 chars allowed");
            Debug.Assert(PunyDomainNameValidator.TryValidateDomain("-a.aa") == false, "Allowed to start with -");
            Debug.Assert(PunyDomainNameValidator.TryValidateDomain("a-.aa") == false, "Allowed to end with -");

            PunyDomainNameValidator.AssertValidDomain("a.com");

            try { PunyDomainNameValidator.AssertValidDomain(".com"); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain("a."); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain("-a.com"); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain("a-.com"); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain("a.com-"); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain("."); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain(".."); Assert.Fail(); } catch { }
            try { PunyDomainNameValidator.AssertValidDomain("..."); Assert.Fail(); } catch { }

            Assert.Pass();
        }

        [Test]
        [Ignore("Not yet used...")]
        public void CNameLookupTest()
        {
            string s;

            s = PunyDomainNameValidator.CNameLookup("alias.id.pub");
            Debug.Assert(s == "odin.earth.");

            s = PunyDomainNameValidator.CNameLookup("corleone.com");
            Debug.Assert(s == null);

            Assert.Pass();
        }

        [Test, Explicit]
        public void IdentityDNSValidate()
        {
            try
            {
                PunyDomainNameValidator.TryIdentityDNSValidate("michael.seifert.uno");
            }
            catch
            {
                Assert.Fail();
            }

            try
            {
                PunyDomainNameValidator.TryIdentityDNSValidate("michael.seifert.kin.pub");
            }
            catch
            {
                Assert.Fail();
            }
        }
    }
}


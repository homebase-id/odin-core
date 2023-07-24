using System;
using System.Diagnostics;
using NUnit.Framework;
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
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain(".aaa"));
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain("aaa."));
        }

        [Test(Description = "Test 63 character label is OK")]
        public void LabelLengthOKTest()
        {
            Assert.IsTrue(SimpleDomainNameValidator.TryValidateDomain("012345678901234567890123456789012345678901234567890123456789012.aaa"));
        }

        [Test(Description = "Test 64 character label fails")]
        public void LabelLengthFailTest()
        {
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain("0123456789012345678901234567890123456789012345678901234567890123.aaa"));
        }

        [Test(Description = "Test first char isn't a dash")]
        public void LabelStartDashFailTest()
        {
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain("-a.aaa"));
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain("aa.-aaa"));
        }


        [Test(Description = "Test last char isn't a dash")]
        public void LabelLastDashFailTest()
        {
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain("a-.aaa"));
            Assert.IsFalse(SimpleDomainNameValidator.TryValidateDomain("aa.aaa-"));
        }

        [Test(Description = "Test 'a' is OK as a label")]
        public void LabelPassTest()
        {
            Assert.IsTrue(SimpleDomainNameValidator.TryValidateDomain("a.a"));
        }

        [Test(Description = "Test shortest valid domain")]
        public void DomainShortestPassTest()
        {
            try
            {
                SimpleDomainNameValidator.AssertValidDomain("a.b");
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
                SimpleDomainNameValidator.AssertValidDomain(".com");
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
                SimpleDomainNameValidator.AssertValidDomain("com.");
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
                SimpleDomainNameValidator.AssertValidDomain("-a.com");
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
                SimpleDomainNameValidator.AssertValidDomain("a.com-");
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
                SimpleDomainNameValidator.AssertValidDomain("=xyyaa.com");
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
                SimpleDomainNameValidator.AssertValidDomain("xn--hxajbheg2az3al.xn--jxalpdlp");
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
                SimpleDomainNameValidator.AssertValidDomain(".");
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
                SimpleDomainNameValidator.AssertValidDomain("..");
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
                SimpleDomainNameValidator.AssertValidDomain("...");
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
                SimpleDomainNameValidator.AssertValidDomain("a.");
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }
            try
            {
                SimpleDomainNameValidator.AssertValidDomain(".a");
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
            SimpleDomainNameValidator.AssertValidDomain(dom);

            dom = "a" + dom;
            Assert.IsTrue(dom.Length == 256);

            try
            {
                SimpleDomainNameValidator.AssertValidDomain(dom);
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
            Debug.Assert(SimpleDomainNameValidator.TryValidateDomain("") == false, "Empty name error");
            Debug.Assert(SimpleDomainNameValidator.TryValidateDomain("012345678901234567890123456789012345678901234567890123456789012.aa") == true,
                "63 chars not allowed");
            Debug.Assert(SimpleDomainNameValidator.TryValidateDomain("0123456789012345678901234567890123456789012345678901234567890123.aa") == false,
                "64 chars allowed");
            Debug.Assert(SimpleDomainNameValidator.TryValidateDomain("-a.aa") == false, "Allowed to start with -");
            Debug.Assert(SimpleDomainNameValidator.TryValidateDomain("a-.aa") == false, "Allowed to end with -");

            SimpleDomainNameValidator.AssertValidDomain("a.com");

            try { SimpleDomainNameValidator.AssertValidDomain(".com"); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain("a."); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain("-a.com"); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain("a-.com"); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain("a.com-"); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain("."); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain(".."); Assert.Fail(); } catch { }
            try { SimpleDomainNameValidator.AssertValidDomain("..."); Assert.Fail(); } catch { }

            Assert.Pass();
        }

        [Test]
        [Ignore("Not yet used...")]
        public void CNameLookupTest()
        {
            string s;

            s = SimpleDomainNameValidator.CNameLookup("alias.id.pub");
            Debug.Assert(s == "odin.earth.");

            s = SimpleDomainNameValidator.CNameLookup("corleone.com");
            Debug.Assert(s == null);

            Assert.Pass();
        }
    }
}


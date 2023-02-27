using System;
using System.Diagnostics;
using NUnit.Framework;
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
            Assert.IsFalse(DomainNameValidator.ValidLabel(""));
        }

        [Test(Description = "Test 63 character label is OK")]
        public void LabelLengthOKTest()
        {
            Assert.IsTrue(DomainNameValidator.ValidLabel("012345678901234567890123456789012345678901234567890123456789012"));
        }

        [Test(Description = "Test 64 character label fails")]
        public void LabelLengthFailTest()
        {
            Assert.IsFalse(DomainNameValidator.ValidLabel("0123456789012345678901234567890123456789012345678901234567890123"));
        }

        [Test(Description = "Test first char isn't a dash")]
        public void LabelStartDashFailTest()
        {
            Assert.IsFalse(DomainNameValidator.ValidLabel("-a"));
        }


        [Test(Description = "Test last char isn't a dash")]
        public void LabelLastDashFailTest()
        {
            Assert.IsFalse(DomainNameValidator.ValidLabel("a-"));
        }

        [Test(Description = "Test 'a' is OK as a label")]
        public void LabelPassTest()
        {
            Assert.IsTrue(DomainNameValidator.ValidLabel("a"));
        }

        [Test(Description = "Test shortest valid domain")]
        public void DomainShortestPassTest()
        {
            try
            {
                DomainNameValidator.AssertValidDomain("a.b");
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

            // Assert.Throws<Exception>(c=> DomainNameValidator.ValidateDomain(".com"), "domain test failed"),
            try
            {
                DomainNameValidator.AssertValidDomain(".com");
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
                DomainNameValidator.AssertValidDomain("com.");
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
                DomainNameValidator.AssertValidDomain("-a.com");
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
                DomainNameValidator.AssertValidDomain("a.com-");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test(Description = "Test period invalid")]
        public void DomainDotFailTest()
        {
            try
            {
                DomainNameValidator.AssertValidDomain(".");
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
                DomainNameValidator.AssertValidDomain("..");
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
                DomainNameValidator.AssertValidDomain("...");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }

        [Test]
        public void MiscTestsMovedFromOtherCode()
        {
            // Test valid labels
            Debug.Assert(DomainNameValidator.ValidLabel("") == false, "Empty name error");
            Debug.Assert(DomainNameValidator.ValidLabel("012345678901234567890123456789012345678901234567890123456789012") == true,
                "63 chars not allowed");
            Debug.Assert(DomainNameValidator.ValidLabel("0123456789012345678901234567890123456789012345678901234567890123") == false,
                "64 chars allowed");
            Debug.Assert(DomainNameValidator.ValidLabel("-a") == false, "Allowed to start with -");
            Debug.Assert(DomainNameValidator.ValidLabel("a-") == false, "Allowed to end with -");
            Debug.Assert(DomainNameValidator.ValidLabel("a") == true, "one char not allowed");

            DomainNameValidator.AssertValidDomain("a.com");

            try { DomainNameValidator.AssertValidDomain(".com"); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain("a."); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain("-a.com"); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain("a-.com"); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain("a.com-"); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain("."); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain(".."); Assert.Fail(); } catch { }
            try { DomainNameValidator.AssertValidDomain("..."); Assert.Fail(); } catch { }

            Assert.Pass();
        }

        [Test]
        public void CNameLookupTest()
        {
            string s;

            s = DomainNameValidator.CNameLookup("alias.id.pub");
            Debug.Assert(s == "odin.earth.");

            s = DomainNameValidator.CNameLookup("corleone.com");
            Debug.Assert(s == null);

            Assert.Pass();
        }

        [Test]
        public void IdentityDNSValidate()
        {
            try
            {
                DomainNameValidator.TryIdentityDNSValidate("michael.seifert.uno");
            }
            catch
            {
                Assert.Fail();
            }

            try
            {
                DomainNameValidator.TryIdentityDNSValidate("michael.seifert.kin.pub");
            }
            catch
            {
                Assert.Fail();
            }
        }
    }
}


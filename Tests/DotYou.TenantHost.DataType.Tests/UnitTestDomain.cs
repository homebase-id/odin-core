using NUnit.Framework;
using System;
using System.Diagnostics;
using Youverse.Core.Trie;


namespace DomainUnitTest
{
    public class Tests
    {

        [SetUp]
        public void Setup()
        {
        }

        [Test(Description = "Label cannot be empty")]
        public void LabelEmptyTest()
        {
            Assert.IsFalse(DomainName.ValidLabel(""));
        }

        [Test(Description = "Test 63 character label is OK")]
        public void LabelLengthOKTest()
        {
            Assert.IsTrue(DomainName.ValidLabel("012345678901234567890123456789012345678901234567890123456789012"));
        }

        [Test(Description = "Test 64 character label fails")]
        public void LabelLengthFailTest()
        {
            Assert.IsFalse(DomainName.ValidLabel("0123456789012345678901234567890123456789012345678901234567890123"));
        }

        [Test(Description = "Test first char isn't a dash")]
        public void LabelStartDashFailTest()
        {
            Assert.IsFalse(DomainName.ValidLabel("-a"));
        }

        
        [Test(Description = "Test last char isn't a dash")]
        public void LabelLastDashFailTest()
        {
            Assert.IsFalse(DomainName.ValidLabel("a-"));            
        }

        [Test(Description = "Test 'a' is OK as a label")]
        public void LabelPassTest()
        {
            Assert.IsTrue(DomainName.ValidLabel("a"));
        }

        [Test(Description = "Test shortest valid domain")]
        public void DomainShortestPassTest()
        {
            try
            {
                DomainName.ValidateDomain("a.b");
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

            // Assert.Throws<Exception>(c=> DomainName.ValidateDomain(".com"), "domain test failed"),
            try
            {
                DomainName.ValidateDomain(".com");
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
                DomainName.ValidateDomain("com.");
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
                DomainName.ValidateDomain("-a.com");
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
                DomainName.ValidateDomain("a.com-");
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
                DomainName.ValidateDomain(".");
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
                DomainName.ValidateDomain("..");
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
                DomainName.ValidateDomain("...");
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }
            Assert.Fail();
        }
    }
}

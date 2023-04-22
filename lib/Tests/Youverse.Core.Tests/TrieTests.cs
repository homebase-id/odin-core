using System;
using NUnit.Framework;
using Youverse.Core.Trie;
using Youverse.Core.Util;

namespace Youverse.Core.Tests
{
    public class TrieTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void EmptyGuidFails()
        {
            var t = new Trie<Guid>();
            var e = Guid.Empty;

            try
            {
                t.AddDomain("local.youfoundation.com", e);
            }
            catch (EmptyKeyNotAllowedException)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void EmptyNameFails()
        {
            var t = new Trie<Guid>();

            try
            {
                t.AddDomain("", Guid.NewGuid());
            }
            catch (DomainTooShortException)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void LegalCharactersPass()
        {
            var t = new Trie<Guid>();

            try
            {
                t.AddDomain("abcdefghijklmnopqrstuvwxyz.com", Guid.NewGuid());
                t.AddDomain("0123456789.com", Guid.NewGuid());
                t.AddDomain("a-b.com", Guid.NewGuid());
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }


        [Test]
        public void IllegalCharactersFail()
        {
            var t = new Trie<Guid>();

            try
            {
                t.AddDomain(";.com", Guid.NewGuid());
            }
            catch (DomainIllegalCharacterException)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void NameTooLongFails()
        {
            var t = new Trie<Guid>();

            try
            {
                t.AddDomain(new string('a', 256), Guid.NewGuid());
            }
            catch (DomainTooLongException)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void DuplicateNameFails()
        {
            var t = new Trie<Guid>();

            try
            {
                t.AddDomain("local.youfoundation.com", Guid.NewGuid());
                t.AddDomain("local.youfoundation.com", Guid.NewGuid());
            }
            catch (DomainHierarchyNotUniqueException)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }



        [Test]
        public void LookupEmptyReturnsEmpty()
        {
            var t = new Trie<Guid>();

            try
            {
                Guid g;

                g = t.LookupName("");
                if (g != Guid.Empty)
                    throw new Exception();

                g = t.LookupName("q");
                if (g != Guid.Empty)
                    throw new Exception();

                g = t.LookupName("ymer");
                if (g != Guid.Empty)
                    throw new Exception();
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }


        [Test]
        public void SubNamePass()
        {
            var t = new Trie<Guid>();

            t.AddDomain("aa.com", Guid.NewGuid());

            try
            {
                var g = t.LookupName("aa.com");
                if (g == Guid.Empty)
                    Assert.Fail();
                g = t.LookupName("a.com");
                if (g != Guid.Empty)
                    Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }


        [Test]
        public void LookupNameNode1Fail()
        {
            var t = new Trie<Guid>();

            t.AddDomain("odin.valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("valhalla.com"))
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void LookupNameNode2Fail()
        {
            var t = new Trie<Guid>();

            t.AddDomain("valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("odin.valhalla.com"))
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void LookupNameNode3Fail()
        {
            var t = new Trie<Guid>();

            t.AddDomain("odin.valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("big.odin.valhalla.com"))
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void LookupNameNode4Pass()
        {
            var t = new Trie<Guid>();

            t.AddDomain("odin.valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("thor.valhalla.com"))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void LookupNameNode5Pass()
        {
            var t = new Trie<Guid>();

            t.AddDomain("odin.valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("din.valhalla.com"))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void LookupNameNode6Pass()
        {
            var t = new Trie<Guid>();

            t.AddDomain("odin.valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("bodin.valhalla.com"))
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void LookupNameNodeDuplicateFail()
        {
            var t = new Trie<Guid>();

            t.AddDomain("valhalla.com", Guid.NewGuid());

            if (t.IsDomainUniqueInHierarchy("valhalla.com"))
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void RemoveNameTest()
        {
            var t = new Trie<Guid>();

            var g = Guid.NewGuid();

            if (t.IsDomainUniqueInHierarchy("valhalla.com") == false)
                Assert.Fail();

            t.AddDomain("valhalla.com", g);

            if (t.IsDomainUniqueInHierarchy("valhalla.com") == true)
                Assert.Fail();
            
            t.RemoveDomain("valhalla.com");

            if (t.IsDomainUniqueInHierarchy("valhalla.com") == false)
                Assert.Fail();

            t.AddDomain("valhalla.com", g);

            if (t.IsDomainUniqueInHierarchy("valhalla.com") == true)
                Assert.Fail();

            t.RemoveDomain("valhalla.com");

            if (t.IsDomainUniqueInHierarchy("valhalla.com") == false)
                Assert.Fail();

            Assert.Pass();
        }


        [Test]
        public void ComplexAddLookup2Pass()
        {
            var t = new Trie<Guid>();

            Guid g1 = Guid.NewGuid();
            Guid g2 = Guid.NewGuid();
            Guid g3 = Guid.NewGuid();
            Guid g4 = Guid.NewGuid();
            Guid g5 = Guid.NewGuid();

            try
            {
                t.AddDomain("aaa.c", g4);
                t.AddDomain("a.c", g1);
                t.AddDomain("aa.c", g2);
                t.AddDomain("ab.c", g3);
                t.AddDomain("b.c", g5);
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Guid g;

            try
            {
                g = t.LookupName("a.c");
                if (g != g1)
                    throw new Exception();

                g = t.LookupName("aa.c");
                if (g != g2)
                    throw new Exception();

                g = t.LookupName("ab.c");
                if (g != g3)
                    throw new Exception();

                g = t.LookupName("aaa.c");
                if (g != g4)
                    throw new Exception();

                g = t.LookupName("b.c");
                if (g != g5)
                    throw new Exception();
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }


        // Here we could have a test of the private method AddName()
        // but that's not possible...
        [Test]
        public void ComplexAddLookupPass()
        {
            var t = new Trie<Guid>();

            Guid g1 = Guid.NewGuid();
            Guid g2 = Guid.NewGuid();
            Guid g3 = Guid.NewGuid();
            Guid g4 = Guid.NewGuid();
            Guid g5 = Guid.NewGuid();

            try
            {
                t.AddDomain("aaa.com", g4);
                t.AddDomain("a.com", g1);
                t.AddDomain("aa.com", g2);
                t.AddDomain("ab.com", g3);
                t.AddDomain("b.com", g5);
            } 
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Guid g;

            try
            {
                g = t.LookupName("a.com");
                if (g != g1)
                    Assert.Fail();

                g = t.LookupName("aa.com");
                if (g != g2)
                    Assert.Fail();

                g = t.LookupName("ab.com");
                if (g != g3)
                    Assert.Fail();

                g = t.LookupName("aaa.com");
                if (g != g4)
                    Assert.Fail();

                g = t.LookupName("b.com");
                if (g != g5)
                    Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }
    }
}

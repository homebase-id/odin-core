using DotYou.TenantHost;
using NUnit.Framework;
using System;

namespace TrieUnitTest
{
    public class Tests
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
            catch (EmptyKeyNotAllowed)
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
            catch (DomainTooShort)
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
            catch (DomainIllegalCharacter)
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
                t.AddDomain(new string('a', 254), Guid.NewGuid());
            }
            catch (DomainTooLong)
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
            catch (DomainHierarchyNotUnique)
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
        [Ignore("Ignored until the AddName property is resolved.")]
        public void SubNamePass()
        {
            var t = new Trie<Guid>();

            t.AddDomain("aa", Guid.NewGuid());

            try
            {
                var g = t.LookupName("aa");
                if (g == Guid.Empty)
                    throw new Exception();
                g = t.LookupName("a");
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


        [Test]
        [Ignore("Ignored until the AddName property is resolved.")]

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
                t.AddDomain("aaa", g4);
                t.AddDomain("a", g1);
                t.AddDomain("aa", g2);
                t.AddDomain("ab", g3);
                t.AddDomain("b", g5);
            } 
            catch (Exception)
            {
                Assert.Fail();
                return;
            }

            Guid g;

            try
            {
                g = t.LookupName("a");
                if (g != g1)
                    throw new Exception();

                g = t.LookupName("aa");
                if (g != g2)
                    throw new Exception();

                g = t.LookupName("ab");
                if (g != g3)
                    throw new Exception();

                g = t.LookupName("aaa");
                if (g != g4)
                    throw new Exception();

                g = t.LookupName("b");
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


    }
}
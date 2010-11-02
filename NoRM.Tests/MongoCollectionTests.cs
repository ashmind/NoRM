using System;
using System.Collections.Generic;
using System.Linq;
using Norm;
using Norm.BSON;
using Norm.Collections;
using Norm.Configuration;
using Norm.Protocol.Messages;
using Norm.Linq;
using Xunit;

namespace Norm.Tests
{
    public class MongoCollectionTests
    {

        public MongoCollectionTests()
        {
            MongoConfiguration.RemoveMapFor<Address>();
            MongoConfiguration.RemoveMapFor<TestProduct>();
            MongoConfiguration.RemoveMapFor<IntId>();

            using (var mongo = Mongo.Create(TestHelper.ConnectionString("strict=false")))
            {
                mongo.Database.DropCollection("Fake");
                mongo.Database.DropCollection("Faker");
            }
        }

        [Fact]
        public void CollectionName_For_Deeply_Generic_Collection_Is_Legal_And_Reasonable()
        {
            using (var db = Mongo.Create(TestHelper.ConnectionString()))
            {
                var coll = db.GetCollection<GenericSuperClass<List<String>>>();
                Assert.Equal("NormTests.GenericSuperClass_List_String", coll.FullyQualifiedName);
            }
        }

        [Fact]
        public void Find_On_Unspecified_Type_Returns_Expando_When_No_Discriminator_Available()
        {
            using (var db = Mongo.Create(TestHelper.ConnectionString("strict=false")))
            {
                //db.Database.GetCollection("helloWorld").Insert(new { _id = 1 });
                db.Database.DropCollection("helloWorld");
                var coll = db.Database.GetCollection("helloWorld");
                coll.Insert(new IntId { Id = 5, Name = "hi there" },
                    new { Id = Guid.NewGuid(), Value = "22" },
                    new { _id = ObjectId.NewObjectId(), Key = 578 });

                var allObjs = coll.Find().ToArray();
                Assert.True(allObjs.All(y => y is Expando));
                Assert.Equal(3, allObjs.Length);
            }
        }

        [Fact(Skip = "This test is timing out")]
        public void Get_Collection_Statistics_Works()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var coll = mongo.GetCollection<IntId>("Fake");
                coll.Insert(new IntId { Id = 4, Name = "Test 1" });
                coll.Insert(new IntId { Id = 5, Name = "Test 2" });
                var stats = coll.GetCollectionStatistics();
                Assert.NotNull(stats);
                Assert.Equal(stats.Count, 2);
            }
        }

        [Fact]
        public void Find_On_Collection_Returning_More_Than_4MB_Of_Docs_Works()
        {
            //this tests Cursor management in the ReplyMessage<T>, 
            //we built NoRM so that the average user picking up the library
            //doesn't have to think about this.
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                List<TestProduct> junkInTheTrunk = new List<TestProduct>();
                for (int i = 0; i < 16000; i++)
                {
                    #region Initialize and add a product to the batch.
                    junkInTheTrunk.Add(new TestProduct()
                                {
                                    Available = DateTime.Now,
                                    Inventory = new List<InventoryChange> { 
                            new InventoryChange{ 
                               AmountChanged=5, CreatedOn=DateTime.Now
                            }
                    },
                                    Name = "Pogo Stick",
                                    Price = 42.0,
                                    Supplier = new Supplier()
                                    {
                                        Address = new Address
                                        {
                                            Zip = "27701",
                                            City = "Durham",
                                            State = "NC",
                                            Street = "Morgan St."
                                        },
                                        CreatedOn = DateTime.Now,
                                        Name = "ACME"
                                    }
                                });
                    #endregion
                }
                var bytes = junkInTheTrunk.SelectMany(y => Norm.BSON.BsonSerializer.Serialize(y)).Count();

                Assert.InRange(bytes, 4194304, Int32.MaxValue);
                mongo.GetCollection<TestProduct>("Fake").Insert(junkInTheTrunk);
                Assert.Equal(16000, mongo.GetCollection<TestProduct>("Fake").Find().Count());
            }
        }

        [Fact]
        public void Find_Subset_Returns_Appropriate_Subset()
        {
            using (var admin = new MongoAdmin(TestHelper.ConnectionString()))
            {
                admin.SetProfileLevel(2);
            }
            using (var db = Mongo.Create(TestHelper.ConnectionString()))
            {
                var coll = db.GetCollection<TestProduct>();
                coll.Delete(new { });
                var oid = ObjectId.NewObjectId();

                coll.Insert(new TestProduct
                {
                    _id = oid,
                    Price = 42.42f,
                    Supplier = new Supplier
                    {
                        Name = "Bob's house of pancakes",
                        RefNum = 12,
                        CreatedOn = DateTime.MinValue
                    }
                });


                var subset = db.GetCollection<TestProduct>().Find(new { }, new { }, Int32.MaxValue, 0,
                    j => new { SupplierName = j.Supplier.Name, Cost = j.Price, Id = j._id }).ToArray();

                Assert.Equal("Bob's house of pancakes", subset[0].SupplierName);
                Assert.Equal(42.42f, subset[0].Cost);
                Assert.Equal(oid, subset[0].Id);
            }
        }


        [Fact]
        public void SaveOrInsertThrowsExceptionIfTypeDoesntHaveAnId()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var ex = Assert.Throws<MongoException>(() => mongo.GetCollection<Address>("Fake").Insert(new Address()));
                Assert.Equal("This collection does not accept insertions/updates, this is due to the fact that the collection's type Norm.Tests.Address does not specify an identifier property", ex.Message);
            }
        }

        [Fact]
        public void InsertsNewEntityWithNonObjectIdKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                mongo.GetCollection<IntId>("Fake").Insert(new IntId { Id = 4, Name = "Test 1" });
                mongo.GetCollection<IntId>("Fake").Insert(new IntId { Id = 5, Name = "Test 2" });
                var found = mongo.GetCollection<IntId>("Fake").Find();
                Assert.Equal(2, found.Count());
                Assert.Equal(4, found.ElementAt(0).Id);
                Assert.Equal("Test 1", found.ElementAt(0).Name);
                Assert.Equal(5, found.ElementAt(1).Id);
                Assert.Equal("Test 2", found.ElementAt(1).Name);

            }
        }

        [Fact]
        public void InsertThrowsExcpetionOnDuplicateKeyAndStrictMode()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString("strict=true")))
            {
                mongo.GetCollection<IntId>("Fake").Insert(new IntId { Id = 4, Name = "Test 1" });
                var ex = Assert.Throws<MongoException>(() => mongo.GetCollection<IntId>("Fake").Insert(new IntId { Id = 4, Name = "Test 2" }));


            }
        }

        [Fact]
        public void MongoCollectionEnsuresDeleteIndices()
        {
            using (var session = new Session())
            {
                session.Drop<TestProduct>();
                session.Add(new TestProduct
                {
                    Name = "ExplainProduct",
                    Price = 10,
                    Supplier = new Supplier { Name = "Supplier", CreatedOn = DateTime.Now }
                });
                session.DB.GetCollection<TestProduct>()
                    .CreateIndex(p => p.Supplier.Name, "TestIndex", true, IndexOption.Ascending);

                int i;
                session.DB.GetCollection<TestProduct>().DeleteIndices(out i);

                //it's TWO because there's always an index on _id by default.
                Assert.Equal(2, i);

            }
        }
        [Fact]
        public void Collection_Creates_Complex_Index()
        {
            using (var db = Mongo.Create(TestHelper.ConnectionString()))
            {
                db.Database.DropCollection<TestProduct>();
                MongoConfiguration.Initialize(j => j.For<TestProduct>(k =>
                    k.ForProperty(x => x.Inventory).UseAlias("inv")));
                
                var prods = db.GetCollection<TestProduct>();
                prods.CreateIndex(j => new { j.Available, j.Inventory.Count }, "complexIndex", true, IndexOption.Ascending);
            }
        }


        [Fact]
        public void MongoCollectionEnsuresDeletIndexByName()
        {
            using (var session = new Session())
            {
                session.Drop<TestProduct>();
                session.Add(new TestProduct
                {
                    Name = "ExplainProduct",
                    Price = 10,
                    Supplier = new Supplier { Name = "Supplier", CreatedOn = DateTime.Now }
                });
                session.DB.GetCollection<TestProduct>().CreateIndex(p => p.Supplier.Name, "TestIndex", true, IndexOption.Ascending);
                session.DB.GetCollection<TestProduct>().CreateIndex(p => p.Available, "TestIndex1", false, IndexOption.Ascending);
                session.DB.GetCollection<TestProduct>().CreateIndex(p => p.Name, "TestIndex2", false, IndexOption.Ascending);

                int i, j;
                session.DB.GetCollection<TestProduct>().DeleteIndex("TestIndex1", out i);
                session.DB.GetCollection<TestProduct>().DeleteIndex("TestIndex2", out j);

                Assert.Equal(4, i);
                Assert.Equal(3, j);
            }
        }

        [Fact]
        public void UpdatesEntityWithNonObjectIdKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                mongo.GetCollection<IntId>("Fake").Insert(new IntId { Id = 4, Name = "Test" });
                mongo.GetCollection<IntId>("Fake").Update(new { Id = 4 }, new { Name = "Updated" }, false, false);
                var found = mongo.GetCollection<IntId>("Fake").Find();
                Assert.Equal(1, found.Count());
                Assert.Equal(4, found.ElementAt(0).Id);
                Assert.Equal("Updated", found.ElementAt(0).Name);
            }
        }

        [Fact]
        public void InsertsNewEntityWithObjectIdKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var id1 = ObjectId.NewObjectId();
                var id2 = ObjectId.NewObjectId();
                mongo.GetCollection<TestProduct>("Fake").Insert(new TestProduct { _id = id1, Name = "Prod1", Price=3 });
                mongo.GetCollection<TestProduct>("Fake").Insert(new TestProduct { _id = id2, Name = "Prod2" , Price=4});
                var found = mongo.GetCollection<TestProduct>("Fake").Find();
                Assert.Equal(2, found.Count());
                Assert.Equal(id1, found.ElementAt(0)._id);
                Assert.Equal("Prod1", found.ElementAt(0).Name);
                Assert.Equal(id2, found.ElementAt(1)._id);
                Assert.Equal("Prod2", found.ElementAt(1).Name);

            }
        }

        [Fact]
        public void UpdatesEntityWithObjectIdKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var id = ObjectId.NewObjectId();
                mongo.GetCollection<TestProduct>("Fake").Insert(new TestProduct { _id = id, Name = "Prod" });
                mongo.GetCollection<TestProduct>("Fake").Update(new { _id = id }, new { Name = "Updated Prod" }, false, false);
                var found = mongo.GetCollection<TestProduct>("Fake").Find();
                Assert.Equal(1, found.Count());
                Assert.Equal(id, found.ElementAt(0)._id);
                Assert.Equal("Updated Prod", found.ElementAt(0).Name);
            }
        }

        [Fact]
        public void SavingANewEntityWithObjectIdKeyGeneratesAKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var product = new TestProduct { _id = null };
                mongo.GetCollection<TestProduct>("Fake").Insert(product);
                Assert.NotNull(product._id);
                Assert.NotEqual(ObjectId.Empty, product._id);
            }
        }
        [Fact]
        public void InsertingANewEntityWithObjectIdKeyGeneratesAKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var product = new TestProduct { _id = null };
                mongo.GetCollection<TestProduct>("Fake").Insert(product);
                Assert.NotNull(product._id);
                Assert.NotEqual(ObjectId.Empty, product._id);
            }
        }

        [Fact]
        public void InsertingMultipleNewEntityWithObjectIdKeyGeneratesAKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var product1 = new TestProduct { _id = null };
                var product2 = new TestProduct { _id = null };
                mongo.GetCollection<TestProduct>("Fake").Insert(new[] { product1, product2 });
                Assert.NotNull(product1._id);
                Assert.NotEqual(ObjectId.Empty, product1._id);
                Assert.NotNull(product2._id);
                Assert.NotEqual(ObjectId.Empty, product2._id);
            }
        }

        [Fact]
        public void InsertingANewEntityGeneratingTheIntKeyFirst()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var collection = mongo.GetCollection<TestIntGeneration>("Fake");

                var identity = (int)collection.GenerateId();
                var testint = new TestIntGeneration { _id = identity, Name = "TestMe" };
                collection.Insert(testint);

                var result = collection.FindOne(new { _id = testint._id });

                Assert.NotNull(testint._id);
                Assert.Equal(result.Name, "TestMe");
            }
        }

        [Fact]
        public void InsertingANewEntityWithNullableIntGeneratesAKey()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var testint = new TestIntGeneration { _id = null };
                mongo.GetCollection<TestIntGeneration>("Fake").Insert(testint);

                Assert.NotNull(testint._id);
                Assert.NotEqual(0, testint._id.Value);
            }
        }

        [Fact]
        public void InsertingANewEntityWithNullableIntGeneratesAKeyComplex()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var idents = new List<int>();
                for (int i = 0; i < 15; i++)
                {
                    var testint = new TestIntGeneration { _id = null };
                    mongo.GetCollection<TestIntGeneration>("Fake").Insert(testint);
                    idents.Add(testint._id.Value);
                }

                var list = mongo.GetCollection<TestIntGeneration>("Fake").Find(new { _id = Q.In(idents.ToArray()) });

                foreach (var item in list)
                {
                    Assert.True(idents.Contains(item._id.Value));
                }

                Assert.Equal(idents.Distinct().Count(), list.Select(x => x._id.Value).Distinct().Count());
            }
        }

        [Fact]
        public void InsertingANewEntityWithNullableIntGeneratesAKeyComplexWith2Collections()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var idents = new List<int>();
                for (int i = 0; i < 15; i++)
                {
                    var testint = new TestIntGeneration { _id = null };
                    mongo.GetCollection<TestIntGeneration>("Fake").Insert(testint);
                    idents.Add(testint._id.Value);
                }

                var idents2 = new List<int>();
                for (int i = 0; i < 15; i++)
                {
                    var testint = new TestIntGeneration { _id = null };
                    mongo.GetCollection<TestIntGeneration>("Faker").Insert(testint);
                    idents2.Add(testint._id.Value);
                }

                var list = mongo.GetCollection<TestIntGeneration>("Fake").Find(new { _id = Q.In(idents.ToArray()) });
                var list2 = mongo.GetCollection<TestIntGeneration>("Faker").Find(new { _id = Q.In(idents2.ToArray()) });

                foreach (var item in list)
                {
                    Assert.True(idents.Contains(item._id.Value));
                }

                foreach (var item in list2)
                {
                    Assert.True(idents2.Contains(item._id.Value));
                }

                Assert.Equal(idents.Distinct().Count(), list.Select(x => x._id.Value).Distinct().Count());
                Assert.Equal(idents2.Distinct().Count(), list2.Select(x => x._id.Value).Distinct().Count());
            }
        }

        [Fact]
        public void DeletesObjectsBasedOnTemplate()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var collection = mongo.GetCollection<TestProduct>("Fake");
                collection.Insert(new[] { new TestProduct { Price = 10 }, new TestProduct { Price = 5 }, new TestProduct { Price = 1 } });
                Assert.Equal(3, collection.Count());
                collection.Delete(new { Price = 1 });
                Assert.Equal(2, collection.Count());
                Assert.Equal(0, collection.Count(new { Price = 1 }));
            }
        }

        [Fact]
        public void ThrowsExceptionWhenAttemptingToDeleteIdLessEntity()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var ex = Assert.Throws<MongoException>(() => mongo.GetCollection<Address>("Fake").Delete(new Address()));
                Assert.Equal("Cannot delete Norm.Tests.Address since it has no id property", ex.Message);
            }
        }
        [Fact]
        public void DeletesEntityBasedOnItsId()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString()))
            {
                var collection = mongo.GetCollection<TestProduct>("Fake");
                var product1 = new TestProduct();
                var product2 = new TestProduct();
                collection.Insert(new[] { product1, product2 });
                Assert.Equal(2, collection.Count());
                collection.Delete(product1);
                Assert.Equal(1, collection.Count());
                Assert.Equal(1, collection.Count(new { Id = product2._id }));
            }
        }

        [Fact]
        public void MapReduceIsSuccessful()
        {
            var _map = "function(){emit(0, this.Price);}";
            var _reduce = "function(key, values){var sumPrice = 0;for(var i = 0; i < values.length; ++i){sumPrice += values[i];} return sumPrice;}";

            using (var mongo = Mongo.Create(TestHelper.ConnectionString("pooling=false&strict=false")))
            {
                mongo.Database.DropCollection("ReduceProduct");
                IMongoCollection<ReduceProduct> collection = mongo.GetCollection<ReduceProduct>();
                collection.Insert(new ReduceProduct { Price = 1.5f }, new ReduceProduct { Price = 2.5f });
                var r = collection.MapReduce<ProductSum>(_map, _reduce).FirstOrDefault();

                Assert.Equal(4, r.Value);
            }
        }


        [Fact]
        public void StringAsIdentifierDoesTranslation()
        {
            using (var mongo = Mongo.Create(TestHelper.ConnectionString("strict=false")))
            {
                
                var collection = mongo.GetCollection<StringIdentifier>();
                mongo.Database.DropCollection("StringIdentifier");
                collection.Insert(new StringIdentifier { CollectionName = "test", ServerHi = 2 });
                
                var result = collection.AsQueryable().Where(x => x.CollectionName == "test").SingleOrDefault();
                
                Assert.Equal(2, result.ServerHi);
            }
        }

        private class StringIdentifier
        {
            [MongoIdentifier]
            public string CollectionName { get; set; }
            public long ServerHi { get; set; }
        }

        private class IntId
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
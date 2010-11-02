using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

using Norm.BSON;
using Norm.Collections;
using Norm.Configuration;

namespace Norm.Tests
{
    public class UpdateTests : IDisposable
    {
        private readonly IMongo _server;
        private readonly IMongoCollection<CheeseClubContact> _collection;
        public UpdateTests()
        {
            _server = Mongo.Create("mongodb://localhost/NormTests?pooling=false");
            _collection = _server.GetCollection<CheeseClubContact>("CheeseClubContacts");
        }
        public void Dispose()
        {
            try
            {
                _server.Database.DropCollection("CheeseClubContacts");
            }
            catch(MongoException e)
            {
                if (e.Message != "ns not found")
                {
                    throw;
                }
            }

            using (var admin = new MongoAdmin("mongodb://localhost/NormTests?pooling=false"))
            {
                admin.DropDatabase();
            }
            _server.Dispose();
        }

        [Fact]
        public void Save_Inserts_Or_Updates()
        {
            var c = new CheeseClubContact();
            c.Id = ObjectId.NewObjectId();
            _collection.Save(c);
            var a = _collection.FindOne(new { c.Id });
            //prove it was inserted.
            Assert.Equal(c.Id, a.Id);

            c.Name = "hello";
            _collection.Save(c);
            var b = _collection.FindOne(new { c.Id });
            //prove that it was updated.
            Assert.Equal(c.Name, b.Name);
        }

        [Fact]
        public void Save_should_insert_and_then_update_when_id_is_nullable_int()
        {
            var collection = _server.GetCollection<CheeseClubContactWithNullableIntId>();
            var subject = new CheeseClubContactWithNullableIntId();
            collection.Save(subject);

            var a = collection.FindOne(new { subject.Id });
            //prove it was inserted.
            Assert.Equal(subject.Id, a.Id);

            subject.Name = "hello";
            collection.Save(subject);

            var b = collection.FindOne(new { subject.Id });
            //prove that it was updated.
            Assert.Equal(subject.Name, b.Name);

            _server.Database.DropCollection(typeof(CheeseClubContactWithNullableIntId).Name);
        }

        [Fact]
        public void Update_Multiple_With_Lambda_Works()
        {
            _collection.Insert(new CheeseClubContact { Name = "Hello" }, new CheeseClubContact { Name = "World" });
            _collection.Update(new { Name = Q.NotEqual("") }, h => h.SetValue(y => y.Name, "Cheese"), true, false);
            Assert.Equal(2, _collection.Find(new { Name = "Cheese" }).Count());
        }

        [Fact]
        public void BasicUsageOfUpdateOne()
        {
            var aPerson = new CheeseClubContact { Name = "Joe", FavoriteCheese = "Cheddar" };
            _collection.Insert(aPerson);

            var count = _collection.Find();
            Assert.Equal(1, count.Count());

            var matchDocument = new { Name = "Joe" };
            aPerson.FavoriteCheese = "Gouda";

            _collection.UpdateOne(matchDocument, aPerson);

            var query = new Expando();
            query["Name"] = Q.Equals<string>("Joe");

            var retreivedPerson = _collection.FindOne(query);

            Assert.Equal("Gouda", retreivedPerson.FavoriteCheese);
        }

        [Fact]
        public void BasicUsageOfUpdateOneUsingObjectId()
        {
            var aPerson = new CheeseClubContact { Name = "Joe", FavoriteCheese = "American" };
            _collection.Insert(aPerson);

            var results = _collection.Find();
            Assert.Equal(1, results.Count());

            var matchDocument = new { _id = aPerson.Id };
            aPerson.FavoriteCheese = "Velveeta";

            _collection.UpdateOne(matchDocument, aPerson);

            var query = new Expando();
            query["_id"] = Q.Equals<ObjectId>(aPerson.Id);

            var retreivedPerson = _collection.FindOne(query);

            Assert.Equal("Velveeta", retreivedPerson.FavoriteCheese);
        }

        [Fact]
        public void UpdateMustSpecifyEverything()
        {
            //Note, when doing an update, MongoDB replaces everything in your document except the id. So, if you don't re-specify a property, it'll disappear.
            //In this example, the cheese is gone.

            var aPerson = new CheeseClubContact { Name = "Joe", FavoriteCheese = "Cheddar" };

            Assert.NotNull(aPerson.FavoriteCheese);

            _collection.Insert(aPerson);

            var matchDocument = new { Name = "Joe" };
            var updatesToApply = new { Name = "Joseph" };

            _collection.UpdateOne(matchDocument, updatesToApply);

            var query = new Expando();
            query["Name"] = Q.Equals<string>("Joseph");

            var retreivedPerson = _collection.FindOne(query);

            Assert.Null(retreivedPerson.FavoriteCheese);
        }

        [Fact]
        public void UsingUpsertToInsertOnUpdate()
        {
            //Upsert will update an existing document if it can find a match, otherwise it will insert.
            //In this scenario, we're updating our collection without inserting first.

            var aPerson = new CheeseClubContact { Name = "Joe", FavoriteCheese = "American" };

            var matchDocument = new { Name = aPerson.Name };
            _collection.Update(matchDocument, aPerson, false, true);

            var results = _collection.Find();
            Assert.Equal(1, results.Count());
        }

        [Fact]
        public void UpdateShouldWorkWhenIdUsesCustomAndGetterSetterMap()
        {
            var idMap = new Dictionary<object, ObjectId>();
            MongoConfiguration.Initialize(config => config.For<CheeseClubContactWithoutId>(typeConfig => typeConfig.IdIs(
                contact => idMap.ContainsKey(contact) ? idMap[contact] : null,
                (contact, id) => idMap[contact] = id
            )));

            var collection = _server.GetCollection<CheeseClubContactWithoutId>();
            var subject = new CheeseClubContactWithoutId();
            collection.Save(subject);

            Assert.True(idMap.ContainsKey(subject));
            Assert.NotNull(idMap[subject]);

            subject.Name = "hello";
            collection.Save(subject);

            var latest = collection.FindOne(new { Id = idMap[subject] });

            Assert.Equal(subject.Name, latest.Name);

            _server.Database.DropCollection(typeof(CheeseClubContactWithoutId).Name);
        }
    }
}
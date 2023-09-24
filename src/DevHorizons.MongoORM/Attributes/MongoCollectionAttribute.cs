namespace DevHorizons.MongoORM.Attributes
{
    using System;
using DevHorizons.MongoORM.Attributes;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false)]
    public class MongoCollectionAttribute : Attribute
    {
        #region Constructors

        /// <summary>
        ///    Initializes a new instance of the <see cref="MongoCollectionAttribute"/> class.
        /// </summary>
        /// <Created>
        ///    <Author>Ahmad Gad (ahmad.gad@DevHorizons.com)</Author>
        ///    <DateTime>10/02/2020 11:59 PM</DateTime>
        /// </Created>
        public MongoCollectionAttribute()
        {
        }

        public MongoCollectionAttribute(string collection)
        {
           this.Collection = collection;
        }

        public MongoCollectionAttribute(string collection, string database)
            :this(collection)
        {
            this.Database = database;
        }
        #endregion Constructors

        #region Properties
        public string Database { get; set; }

        public string Collection { get; set; }
        #endregion Properties
    }
}

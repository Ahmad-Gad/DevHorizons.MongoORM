namespace DevHorizons.MongoORM.Provisioning.Index
{
    using System.ComponentModel;

    [DefaultValue(Asc)]
    public enum IndexFieldType
    {
        /// <summary>
        /// 
        /// </summary>
        [Description("asc")]
        Asc,

        [Description("desc")]
        Desc,

        /// <summary>
        ///    A 2dsphere index supports queries that calculate geometries on an earth-like sphere. 2dsphere index supports all MongoDB geospatial queries: queries for inclusion, intersection and proximity.
        /// </summary>
        /// <remarks>
        ///    The 2dsphere index supports data stored as GeoJSON objects and legacy coordinate pairs. For legacy coordinate pairs, the index converts the data to GeoJSON Point.
        ///    <para><see href="https://www.mongodb.com/docs/manual/core/2dsphere/"/></para>
        ///    <para><see href="https://www.mongodb.com/docs/manual/geospatial-queries/#std-label-geospatial-geojson"/></para>
        ///    <para><see href="https://www.mongodb.com/docs/manual/core/2dsphere/#std-label-2dsphere-data-restrictions"/></para>
        /// </remarks>
        [Description("2dsphere")]
        Geo2DSphere,

        /// <summary>
        ///  Use a 2d index for data stored as points on a two-dimensional plane. The 2d index is intended for legacy coordinate pairs used in MongoDB 2.2 and earlier.
        /// </summary>
        /// <remarks>
        ///  Use a 2d index if:
        ///  <para>your database has legacy legacy coordinate pairs from MongoDB 2.2 or earlier, and</para>
        ///  <para>you do not intend to store any location data as GeoJSON objects.</para>
        ///  <para><see href="https://www.mongodb.com/docs/manual/core/2d/"/></para>
        /// </remarks>
        [Description("2d")]
        Geo2D,

        /// <summary>
        ///    To run legacy text search queries, you must have a text index on your collection. MongoDB provides text indexes to support text search queries on string content. 'text' indexes can include any field whose value is a string or an array of string elements. A collection can only have one text search index, but that index can cover multiple fields.
        /// </summary>
        /// <remarks>
        ///    A collection can have at most one text index. 
        ///    <para><see href="https://www.mongodb.com/docs/manual/core/index-text/"/></para>
        ///    <para>Atlas Search (available in MongoDB Atlas) supports multiple full-text search indexes on a single collection.</para>
        ///    <para><see href="https://www.mongodb.com/docs/atlas/atlas-search/"/></para>
        /// </remarks>
        [Description("text")]
        Text
    }
}

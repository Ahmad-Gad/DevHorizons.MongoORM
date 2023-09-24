namespace DevHorizons.MongoORM.Attributes
{
    using Internal;

    using MongoDB.Bson.Serialization;
    using MongoDB.Bson.Serialization.IdGenerators;

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    [BsonMemberMapAttributeUsage(AllowMultipleMembers = false)]
    public class DocumentIdAttribute : Attribute, IBsonMemberMapAttribute
    {
        public void Apply(BsonMemberMap memberMap)
        {
            memberMap.SetElementName(Constants.ID);
            memberMap.ClassMap.SetIdMember(memberMap);
            var idGenerator = (IIdGenerator)Activator.CreateInstance(typeof(GuidGenerator));
            memberMap.SetIdGenerator(idGenerator);
            memberMap.ClassMap.SetIdMember(memberMap);
        }
    }
}

namespace DevHorizons.MongoORM.Settings
{
    public interface IProvisioningSettings
    {
        ICollection<Provisioning.Collection> Collections { get; set; }
    }
}

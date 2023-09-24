namespace DevHorizons.MongoORM.Settings
{
    public class ProvisioningSettings : IProvisioningSettings
    {
        public ICollection<Provisioning.Collection> Collections { get; set; }
    }
}

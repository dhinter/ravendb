using Newtonsoft.Json.Linq;
using Raven.Database;
using Raven.Database.Plugins;

namespace Raven.Bundles.Replication
{
    public class RemoveConflictOnPutTrigger : AbstractPutTrigger
    {
        public void OnPut(string key, JObject document, JObject metadata, TransactionInformation transactionInformation)
        {
            var oldVersion = Database.Get(key, transactionInformation);
            if (oldVersion == null)
                return;
            if (oldVersion.Metadata[ReplicationConstants.RavenReplicationConflict] == null)
                return;
            // this is a conflict document, holding document keys in the 
            // values of the properties
            foreach (JProperty prop in oldVersion.DataAsJson)
            {
                Database.Delete(prop.Value<string>(), null, transactionInformation);
            }
        }
    }
}
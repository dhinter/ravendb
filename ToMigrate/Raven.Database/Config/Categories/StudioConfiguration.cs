using System.ComponentModel;
using Raven.Database.Config.Attributes;

namespace Raven.Database.Config.Categories
{
    public class StudioConfiguration : ConfigurationCategory
    {
        [Description("Control whatever the Studio default indexes will be created or not. These default indexes are only used by the UI, and are not required for RavenDB to operate.")]
        [DefaultValue(false)]
        [ConfigurationEntry("Raven/Studio/SkipCreatingIndexes")]
        [ConfigurationEntry("Raven/SkipCreatingStudioIndexes")]
        public bool SkipCreatingIndexes { get; set; }
    }
}
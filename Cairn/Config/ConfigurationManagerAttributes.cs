namespace RavenIron.Cairn.Config
{
    /// <summary>
    /// Display hints for BepInEx's Configuration Manager (the F1 window), passed as a tag on a
    /// setting's ConfigDescription. The manager finds this class by its NAME and copies public
    /// fields by name, so neither the class name nor the field names may change. It changes only
    /// what the F1 window shows: the key in the .cfg file is untouched, so no install needs
    /// migrating. Without the manager installed it is simply ignored. (0.13.2)
    ///
    /// The manager sorts a section by Order, then by DispName, so a label's first words also
    /// decide where it sits in the list.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public string DispName;
    }
}

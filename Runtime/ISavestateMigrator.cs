namespace bnj.savestate_system.Runtime
{
    /// <summary>
    /// Interface for savestate migration. 
    /// Use SavestateMigratorBase for automatic migration chain handling.
    /// </summary>
    public interface ISavestateMigrator<T> where T : ISerializableSavestate
    {
        /// <summary>
        /// Current version the game expects.
        /// </summary>
        int CurrentVersion { get; }

        /// <summary>
        /// Attempts to migrate savestate from old version to current.
        /// </summary>
        /// <param name="oldSavestate">The savestate to migrate.</param>
        /// <param name="migratedSavestate">The migrated result at ExpectedVersion, or null if migration failed.</param>
        /// <returns>True if migration succeeded, false if fallback savestate should be used.</returns>
        bool TryMigrate(T oldSavestate, out T migratedSavestate);
    }
}

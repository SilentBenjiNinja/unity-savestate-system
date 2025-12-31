using System;
using UnityEngine;

namespace bnj.savestate_system.Runtime
{
    /// <summary>
    /// Abstract base class that handles migration chain logic automatically.
    /// Implement MigrateToNextVersion() for each version step.
    /// </summary>
    public abstract class SavestateMigrator<T> : ISavestateMigrator<T> where T : ISerializableSavestate
    {
        /// <summary>
        /// Override this with your game's current savestate version.
        /// </summary>
        public abstract int CurrentVersion { get; }

        /// <summary>
        /// Implement this to handle migration from any version to the NEXT version.
        /// Called repeatedly until savestate reaches CurrentVersion.
        /// </summary>
        /// <param name="savestate">Savestate at version N</param>
        /// <returns>Savestate at version N+1</returns>
        protected abstract T MigrateToNextVersion(T savestate);

        /// <summary>
        /// Automatically chains migrations from old version to CurrentVersion.
        /// </summary>
        public bool TryMigrate(T oldSavestate, out T migratedSavestate)
        {
            if (oldSavestate == null)
            {
                Debug.LogError("Cannot migrate null savestate.");
                migratedSavestate = default;
                return false;
            }

            if (oldSavestate.Version == CurrentVersion)
            {
                migratedSavestate = oldSavestate;
                return true;
            }

            if (oldSavestate.Version > CurrentVersion)
            {
                Debug.LogError($"Savestate version {oldSavestate.Version} is newer than CurrentVersion {CurrentVersion}. Cannot migrate.");
                migratedSavestate = default;
                return false;
            }

            try
            {
                var current = oldSavestate;
                var startVersion = current.Version;

                while (current.Version < CurrentVersion)
                {
                    var oldVersion = current.Version;
                    current = MigrateToNextVersion(current);

                    if (current.Version <= oldVersion)
                    {
                        throw new InvalidOperationException(
                            $"Migration from v{oldVersion} did not increment version. " +
                            $"Expected v{oldVersion + 1}, got v{current.Version}. " +
                            $"Ensure MigrateToNextVersion() updates the version field."
                        );
                    }

                    Debug.Log($"Migrated savestate: v{oldVersion} → v{current.Version}");
                }

                Debug.Log($"Migration complete: v{startVersion} → v{CurrentVersion} ({CurrentVersion - startVersion} steps)");
                migratedSavestate = current;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Migration failed: {ex.Message}\n{ex.StackTrace}");
                migratedSavestate = default;
                return false;
            }
        }
    }
}

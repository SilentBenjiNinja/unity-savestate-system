namespace bnj.savestate_system.Runtime
{
    /// <summary>
    /// Defines the contract for a serializable savestate with version tracking and dirty state management.
    /// </summary>
    public interface ISerializableSavestate
    {
        /// <summary>
        /// Gets the version number of this savestate's schema.
        /// Used for migration when loading older savestate files.
        /// </summary>
        int Version { get; }

        /// <summary>
        /// Gets whether this savestate has unsaved changes.
        /// When false, SaveToFile() will skip the write operation.
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// Clears the dirty flag after a successful save.
        /// Automatically called by SavestateFileIOStreamer after writing to disk.
        /// </summary>
        void RemoveDirtyFlag();
    }
}

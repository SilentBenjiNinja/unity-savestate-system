namespace bnj.savestate_system.Runtime
{
    /// <summary>
    /// Defines the contract for serializing and deserializing savestate objects.
    /// </summary>
    public interface ISerializer<T> where T : ISerializableSavestate
    {
        /// <summary>
        /// Serializes a savestate object into a byte array.
        /// </summary>
        byte[] Serialize(T savestate);

        /// <summary>
        /// Deserializes a byte array back into a savestate object.
        /// </summary>
        T Deserialize(byte[] data);

        /// <summary>
        /// Converts serialized byte data to JSON for debugging purposes.
        /// </summary>
        string ConvertToJson(byte[] data);
    }
}

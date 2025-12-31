using bnj.savestate_system.Runtime;
using MessagePack;

public class MessagePackSerializer<T> : ISerializer<T> where T : ISerializableSavestate
{
    public T Deserialize(byte[] byteArray)
    {
        return MessagePackSerializer.Deserialize<T>(byteArray);
    }

    public byte[] Serialize(T savestate)
    {
        return MessagePackSerializer.Serialize(savestate);
    }

    public string ConvertToJson(byte[] byteArray)
    {
        return MessagePackSerializer.ConvertToJson(byteArray);
    }
}

// Mocks MessagePack namespace to avoid conflicts
// To add MessagePack, follow https://github.com/MessagePack-CSharp/MessagePack-CSharp?tab=readme-ov-file#unity
// and remove this once MessagePack is added properly
namespace MessagePack
{
    public static class MessagePackSerializer
    {
        public static T Deserialize<T>(byte[] byteArray) => default;
        public static byte[] Serialize<T>(T obj) => default;
        public static string ConvertToJson(byte[] byteArray) => default;
    }
}

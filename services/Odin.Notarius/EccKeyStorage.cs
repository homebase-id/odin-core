using Odin.Core;
using Odin.Core.Cryptography.Data;
using System.Text.Json;

public static class EccKeyStorage
{
    const string path = "./key.chain.der";

    public static void SaveKey(EccFullKeyData key)
    {
        string jsonString = JsonSerializer.Serialize(key);
        File.WriteAllText(path, jsonString);
    }

    public static EccFullKeyData LoadKey(SensitiveByteArray pwd)
    {
        string jsonString = "";

        try
        {
            jsonString = File.ReadAllText(path);
        }
        catch
        {
            if (jsonString.Length <= 0)
            {
                // Hack to create a key if none is there
                var eccKey = new EccFullKeyData(pwd, 1);
                SaveKey(eccKey);
                jsonString = File.ReadAllText(path);
                if (jsonString == null)
                    throw new Exception("Unable to save / load ECC key");
            }
        }

        EccFullKeyData? key = JsonSerializer.Deserialize<EccFullKeyData>(jsonString);
        if (key == null)
            throw new Exception("Failed to deserialize the key.");

        return key;
    }
}

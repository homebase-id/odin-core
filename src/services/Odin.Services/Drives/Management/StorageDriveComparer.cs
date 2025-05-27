using System.Collections.Generic;
using System.Linq;

namespace Odin.Services.Drives.Management;

public static class StorageDriveComparer
{
    public static bool AreEqual(StorageDrive drive1, StorageDrive drive2, out string difference)
    {
        var differences = GetDifferences(drive1, drive2);
        difference = differences.FirstOrDefault();
        return difference == null;
    }

    public static List<string> GetDifferences(StorageDrive drive1, StorageDrive drive2)
    {
        var diffs = new List<string>();

        if (drive1 is null && drive2 is null) return diffs;
        if (drive1 is null || drive2 is null)
        {
            diffs.Add("One of the drives is null");
            return diffs;
        }

        if (drive1.Id != drive2.Id) diffs.Add("Id differs");

        if (drive1.Name != drive2.Name) diffs.Add("Name differs");
        if (!Equals(drive1.TargetDriveInfo, drive2.TargetDriveInfo)) diffs.Add("TargetDriveInfo differs");
        if (drive1.Metadata != drive2.Metadata) diffs.Add("Metadata differs");
        if (drive1.IsReadonly != drive2.IsReadonly) diffs.Add("IsReadonly differs");
        if (drive1.AllowSubscriptions != drive2.AllowSubscriptions) diffs.Add("AllowSubscriptions differs");
        
        if (!drive1.MasterKeyEncryptedStorageKey.KeyEncrypted.SequenceEqual(drive2.MasterKeyEncryptedStorageKey.KeyEncrypted)) diffs.Add("MasterKeyEncryptedStorageKey KeyEncrypted differs");
        if (!drive1.MasterKeyEncryptedStorageKey.KeyIV.SequenceEqual(drive2.MasterKeyEncryptedStorageKey.KeyIV)) diffs.Add("MasterKeyEncryptedStorageKey KeyIV differs");
        if (!drive1.MasterKeyEncryptedStorageKey.KeyHash.SequenceEqual(drive2.MasterKeyEncryptedStorageKey.KeyHash)) diffs.Add("MasterKeyEncryptedStorageKey KeyHash differs");

        if (!drive1.EncryptedIdIv.SequenceEqual(drive2.EncryptedIdIv)) diffs.Add("EncryptedIdIv differs");
        if (!drive1.EncryptedIdValue.SequenceEqual(drive2.EncryptedIdValue)) diffs.Add("EncryptedIdValue differs");
        
        if (drive1.AllowAnonymousReads != drive2.AllowAnonymousReads) diffs.Add("AllowAnonymousReads differs");
        if (drive1.OwnerOnly != drive2.OwnerOnly) diffs.Add("OwnerOnly differs");
        if (!DictionariesEqual(drive1.Attributes, drive2.Attributes)) diffs.Add("Attributes differ");

        return diffs;
    }

    public static (List<StorageDrive> OnlyInFirst, List<StorageDrive> OnlyInSecond, List<(StorageDrive Drive1, StorageDrive Drive2, List<string> Differences)> Mismatched)
        CompareLists(List<StorageDrive> list1, List<StorageDrive> list2)
    {
        var onlyInFirst = new List<StorageDrive>();
        var onlyInSecond = new List<StorageDrive>();
        var mismatched = new List<(StorageDrive, StorageDrive, List<string>)>();

        var lookup2 = list2.ToDictionary(d => d.Id);

        foreach (var d1 in list1)
        {
            if (lookup2.TryGetValue(d1.Id, out var d2))
            {
                var differences = GetDifferences(d1, d2);
                if (differences.Count > 0)
                {
                    mismatched.Add((d1, d2, differences));
                }

                lookup2.Remove(d1.Id); // Remove matched
            }
            else
            {
                onlyInFirst.Add(d1);
            }
        }

        // Remaining in list2 were not in list1
        onlyInSecond.AddRange(lookup2.Values);

        return (onlyInFirst, onlyInSecond, mismatched);
    }

    private static bool DictionariesEqual(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a == b) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var value)) return false;
            if (kvp.Value != value) return false;
        }

        return true;
    }
}

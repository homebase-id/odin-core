using System.Collections.Generic;
using System.Linq;

namespace Odin.Hosting.Tests._Universal;

public class TestPermissionKeyList
{
    private List<int> _permissionKeys;
    
    public TestPermissionKeyList(params int[] pk)
    {
        _permissionKeys = pk.ToList();
    }

    public List<int> PermissionKeys
    {
        get => _permissionKeys;
        set => _permissionKeys = value;
    }
}
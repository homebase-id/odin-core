#nullable enable
namespace Odin.Services.Tenant;

public class Tenant(string name)
{
    public string Name { get; } = name;

    public override string ToString()
    {
        return Name;
    }
}

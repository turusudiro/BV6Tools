using System.Text.Json;

namespace BV6Tools.Extensions;

internal static class ObjectExtensions
{
    public static T DeepClone<T>(this T self)
    {
        var json = JsonSerializer.Serialize(self);

        return JsonSerializer.Deserialize<T>(json) ??
               throw new InvalidOperationException($"DeepClone failed for type {typeof(T).FullName}");
    }
}
namespace BV6Tools.Services.Database.Models
{
    public readonly record struct FeaturedCacheResult(List<FeaturedCacheDb> Games, DateTime? CachedAt);
}

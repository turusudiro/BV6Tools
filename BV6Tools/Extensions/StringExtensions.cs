using System.Diagnostics.CodeAnalysis;

namespace BV6Tools.Extensions
{
    public static class StringExtensions
    {
        public static bool HasValue([NotNullWhen(true)] this string? value) => !string.IsNullOrEmpty(value);

        public static bool HasVisibleValue(this string? value) => !string.IsNullOrWhiteSpace(value);

        public static bool IsNullOrEmpty([NotNullWhen(false)] this string? value) => string.IsNullOrEmpty(value);

        public static bool IsNullOrWhiteSpace(this string? value) => string.IsNullOrWhiteSpace(value);
    }
}
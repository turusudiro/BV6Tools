namespace BV6Tools.Extensions;

public static class LongExtensions
{
    public static string ToSizeString(this long l)
    {
        long KB = 1024;
        var MB = KB * 1024;
        var GB = MB * 1024;
        var TB = GB * 1024;
        double size = l;
        if (l >= TB)
        {
            size = Math.Round((double)l / TB, 2);
            return $"{size} TB";
        }

        if (l >= GB)
        {
            size = Math.Round((double)l / GB, 2);
            return $"{size} GB";
        }

        if (l >= MB)
        {
            size = Math.Round((double)l / MB, 2);
            return $"{size} MB";
        }

        if (l >= KB)
        {
            size = Math.Round((double)l / KB, 2);
            return $"{size} KB";
        }

        return $"{size} Bytes";
    }
}
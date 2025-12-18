using System;
using System.Collections.Immutable;

static class Utils
{
    public static (int Start, int Length) FixRange(int totalLength, int? start, int? length)
    {
        int _start = Math.Clamp(start ?? 0, 0, totalLength - 1);
        int _length = Math.Clamp(length ?? totalLength, 0, totalLength - _start);
        return (_start, _length);
    }

    public static ImmutableArray<T> Slice<T>(this ImmutableArray<T> array, int? start, int? length) => array.Slice(FixRange(array.Length, start, length));
    public static ImmutableArray<T> Slice<T>(this ImmutableArray<T> array, (int Start, int Length) range) => array.Slice(range.Start, range.Length);
}

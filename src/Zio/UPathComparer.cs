// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;

namespace Zio;

public class UPathComparer : IComparer<UPath>, IEqualityComparer<UPath>
#if HAS_ALTERNATEEQUALITYCOMPARER
    , IAlternateEqualityComparer<ReadOnlySpan<char>, UPath>, IAlternateEqualityComparer<string, UPath>
#endif
{
    public static readonly UPathComparer Ordinal = new(StringComparer.Ordinal);
    public static readonly UPathComparer OrdinalIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    public static readonly UPathComparer CurrentCulture = new(StringComparer.CurrentCulture);
    public static readonly UPathComparer CurrentCultureIgnoreCase = new(StringComparer.CurrentCultureIgnoreCase);

    private readonly StringComparer _comparer;

    private UPathComparer(StringComparer comparer)
    {
        _comparer = comparer;

#if HAS_ALTERNATEEQUALITYCOMPARER
        Debug.Assert(_comparer is IAlternateEqualityComparer<ReadOnlySpan<char>, string>);
#endif
    }

    public int Compare(UPath x, UPath y)
    {
        return _comparer.Compare(x.FullName, y.FullName);
    }

    public bool Equals(UPath x, UPath y)
    {
        return _comparer.Equals(x.FullName, y.FullName);
    }

    public int GetHashCode(UPath obj)
    {
        return _comparer.GetHashCode(obj.FullName);
    }

#if HAS_ALTERNATEEQUALITYCOMPARER
    bool IAlternateEqualityComparer<ReadOnlySpan<char>, UPath>.Equals(ReadOnlySpan<char> alternate, UPath other)
    {
        return ((IAlternateEqualityComparer<ReadOnlySpan<char>, string>)_comparer).Equals(alternate, other.FullName);
    }

    int IAlternateEqualityComparer<ReadOnlySpan<char>, UPath>.GetHashCode(ReadOnlySpan<char> alternate)
    {
        return ((IAlternateEqualityComparer<ReadOnlySpan<char>, string>)_comparer).GetHashCode(alternate);
    }

    UPath IAlternateEqualityComparer<ReadOnlySpan<char>, UPath>.Create(ReadOnlySpan<char> alternate)
    {
        return ((IAlternateEqualityComparer<ReadOnlySpan<char>, string>)_comparer).Create(alternate);
    }

    bool IAlternateEqualityComparer<string, UPath>.Equals(string alternate, UPath other)
    {
        return _comparer.Equals(alternate, other.FullName);
    }

    int IAlternateEqualityComparer<string, UPath>.GetHashCode(string alternate)
    {
        return _comparer.GetHashCode(alternate);
    }

    UPath IAlternateEqualityComparer<string, UPath>.Create(string alternate)
    {
        return alternate;
    }
#endif
}

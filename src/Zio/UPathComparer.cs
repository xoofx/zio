// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

namespace Zio;

public class UPathComparer : IComparer<UPath>, IEqualityComparer<UPath>
{
    public static readonly UPathComparer Ordinal = new(StringComparer.Ordinal);
    public static readonly UPathComparer OrdinalIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    public static readonly UPathComparer CurrentCulture = new(StringComparer.CurrentCulture);
    public static readonly UPathComparer CurrentCultureIgnoreCase = new(StringComparer.CurrentCultureIgnoreCase);

    private readonly StringComparer _comparer;

    private UPathComparer(StringComparer comparer)
    {
        _comparer = comparer;
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
}

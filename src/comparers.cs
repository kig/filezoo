/*
    Filezoo - a small and fast file manager
    Copyright (C) 2008  Ilmari Heikkinen

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;

public class SortHandler {
  public string Name;
  public IComparer<FSEntry> Comparer;
  public SortHandler (string name, IComparer<FSEntry> comparer) {
    Name = name;
    Comparer = comparer;
  }
}

public class NullComparer : IComparer<FSEntry> {
  /** UNIMPORTANT */
  int IComparer<FSEntry>.Compare (FSEntry a, FSEntry b) { return 0; }
}

public class SizeComparer : IComparer<FSEntry> {
  /** BLOCKING */
  int IComparer<FSEntry>.Compare ( FSEntry a, FSEntry b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    long rv = a.Size - b.Size;
    if (rv == 0) rv = String.CompareOrdinal(a.LCName, b.LCName);
    return rv > 0 ? 1 : (rv < 0 ? -1 : 0);
  }
}

public class NameComparer : IComparer<FSEntry> {
  /** BLOCKING */
  int IComparer<FSEntry>.Compare ( FSEntry a, FSEntry b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    if (a.LCName[0] != b.LCName[0]) {
      if (a.LCName[0] == '.') return 1;
      if (b.LCName[0] == '.') return -1;
    }
    return String.CompareOrdinal(a.LCName, b.LCName);
  }
}

public class DateComparer : IComparer<FSEntry> {
  /** BLOCKING */
  int IComparer<FSEntry>.Compare ( FSEntry a, FSEntry b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    int rv = b.LastModified.CompareTo(a.LastModified);
    if (rv == 0) rv = String.CompareOrdinal(a.LCName, b.LCName);
    return rv;
  }
}

public class TypeComparer : IComparer<FSEntry> {
  /** BLOCKING */
  int IComparer<FSEntry>.Compare ( FSEntry a, FSEntry b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    } else if (a.IsDirectory) {
      return String.CompareOrdinal(a.LCName, b.LCName);
    }
    int rv = String.CompareOrdinal(a.Suffix, b.Suffix);
    if (rv == 0) rv = String.CompareOrdinal(a.LCName, b.LCName);
    return rv;
  }
}

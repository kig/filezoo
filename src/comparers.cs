using System;
using System.Collections.Generic;

class SortHandler {
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
    int rv = a.LastModified.CompareTo(b.LastModified);
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

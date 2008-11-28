using System;
using System.Collections.Generic;

class SortHandler {
  public string Name;
  public IComparer<DirStats> Comparer;
  public SortHandler (string name, IComparer<DirStats> comparer) {
    Name = name;
    Comparer = comparer;
  }
}

public class NullComparer : IComparer<DirStats> {
  /** UNIMPORTANT */
  int IComparer<DirStats>.Compare (DirStats a, DirStats b) { return 0; }
}

public class SizeComparer : IComparer<DirStats> {
  /** BLOCKING */
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    long rv = a.Length - b.Length;
    if (rv == 0) rv = String.CompareOrdinal(a.LCName, b.LCName);
    return rv > 0 ? 1 : (rv < 0 ? -1 : 0);
  }
}

public class NameComparer : IComparer<DirStats> {
  /** BLOCKING */
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    return String.CompareOrdinal(a.LCName, b.LCName);
  }
}

public class FSNameComparer : IComparer<FSEntry> {
  /** BLOCKING */
  int IComparer<FSEntry>.Compare ( FSEntry a, FSEntry b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    return String.CompareOrdinal(a.Name.ToLower(), b.Name.ToLower());
  }
}

public class DateComparer : IComparer<DirStats> {
  /** BLOCKING */
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    int rv = a.LastModified.CompareTo(b.LastModified);
    if (rv == 0) rv = String.CompareOrdinal(a.LCName, b.LCName);
    return rv;
  }
}

public class TypeComparer : IComparer<DirStats> {
  /** BLOCKING */
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
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

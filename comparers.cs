using System.Collections.Generic;

class SortHandler {
  public string Name;
  public IComparer<DirStats> Comparer;
  public SortHandler (string name, IComparer<DirStats> comparer) {
    Name = name;
    Comparer = comparer;
  }
}

public class SizeComparer : IComparer<DirStats> {
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    int rv = a.Length.CompareTo(b.Length);
    if (rv == 0) rv = a.Name.CompareTo(b.Name);
    return rv;
  }
}

public class NameComparer : IComparer<DirStats> {
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    return a.Name.CompareTo(b.Name);
  }
}

public class DateComparer : IComparer<DirStats> {
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    int rv = a.Info.LastWriteTime.CompareTo(b.Info.LastWriteTime);
    if (rv == 0) rv = a.Name.CompareTo(b.Name);
    return rv;
  }
}

public class TypeComparer : IComparer<DirStats> {
  int IComparer<DirStats>.Compare ( DirStats a, DirStats b ) {
    if (a.IsDirectory != b.IsDirectory) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    } else if (a.IsDirectory) {
      return a.Name.CompareTo(b.Name);
    }
    int rv = a.Suffix.CompareTo(b.Suffix);
    if (rv == 0) rv = a.Name.CompareTo(b.Name);
    return rv;
  }
}

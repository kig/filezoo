using System.Collections;

public class SizeComparer : IComparer {
  int IComparer.Compare ( object x, object y ) {
    DirStats a = (DirStats) x;
    DirStats b = (DirStats) y;
    if (a.Info.FileType != b.Info.FileType ) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    int rv = a.Info.Length.CompareTo(b.Info.Length);
    if (rv == 0) rv = a.Info.Name.CompareTo(b.Info.Name);
    return rv;
  }
}

public class NameComparer : IComparer {
  int IComparer.Compare ( object x, object y ) {
    DirStats a = (DirStats) x;
    DirStats b = (DirStats) y;
    if (a.Info.FileType != b.Info.FileType ) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    return a.Info.Name.CompareTo(b.Info.Name);
  }
}

public class DateComparer : IComparer {
  int IComparer.Compare ( object x, object y ) {
    DirStats a = (DirStats) x;
    DirStats b = (DirStats) y;
    if (a.Info.FileType != b.Info.FileType ) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    }
    int rv = a.Info.LastWriteTime.CompareTo(b.Info.LastWriteTime);
    if (rv == 0) rv = a.Info.Name.CompareTo(b.Info.Name);
    return rv;
  }
}

public class TypeComparer : IComparer {
  int IComparer.Compare ( object x, object y ) {
    DirStats a = (DirStats) x;
    DirStats b = (DirStats) y;
    if (a.Info.FileType != b.Info.FileType ) {
      if (a.IsDirectory) return -1;
      if (b.IsDirectory) return 1;
    } else if (a.IsDirectory) {
      return a.Info.Name.CompareTo(b.Info.Name);
    }
    int rv = a.Suffix.CompareTo(b.Suffix);
    if (rv == 0) rv = a.Info.Name.CompareTo(b.Info.Name);
    return rv;
  }
}

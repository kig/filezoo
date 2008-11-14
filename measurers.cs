using System;

public interface IMeasurer {
  double Measure (DirStats d);
}

public class SizeMeasurer : IMeasurer {
  public double Measure (DirStats d) {
    return Math.Max(1.0, d.Info.Length);
  }
}

public class TotalMeasurer : IMeasurer {
  public double Measure (DirStats d) {
    return Math.Max(1.0, d.GetRecursiveSize ());
  }
}

public class CountMeasurer : IMeasurer {
  public double Measure (DirStats d) {
    double mul = (d.Info.Name[0] == '.') ? 1.0 : 20.0;
    return (d.IsDirectory ? d.GetRecursiveCount() : 5.0) * mul;
  }
}

public class FlatMeasurer : IMeasurer {
  public double Measure (DirStats d) {
    bool isDotFile = d.Info.Name[0] == '.';
    if (isDotFile) return 0.05;
    return (d.IsDirectory ? 1.5 : 1.0);
  }
}

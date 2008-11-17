using System;

class SizeHandler {
  public string Name;
  public IMeasurer Measurer;
  public SizeHandler (string name, IMeasurer measurer) {
    Name = name;
    Measurer = measurer;
  }
}

public interface IMeasurer {
  double Measure (DirStats d);
  bool DependsOnTotals { get; }
}

public class SizeMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return false; } }
  public double Measure (DirStats d) {
    return Math.Max(1.0, d.Length);
  }
}

public class TotalMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return true; } }
  public double Measure (DirStats d) {
    return Math.Max(1.0, d.GetRecursiveSize ());
  }
}

public class CountMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return true; } }
  public double Measure (DirStats d) {
    double mul = (d.Info.Name[0] == '.') ? 1.0 : 20.0;
    return (d.IsDirectory ? d.GetRecursiveCount() : 5.0) * mul;
  }
}

public class FlatMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return false; } }
  public double Measure (DirStats d) {
    bool isDotFile = d.Info.Name[0] == '.';
    if (isDotFile) return 0.05;
    return (d.IsDirectory ? 1.5 : 1.0);
  }
}

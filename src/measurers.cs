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
  double Measure (FSEntry d);
  bool DependsOnTotals { get; }
}


public class NullMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return false; } }
  /** UNIMPORTANT */
  public double Measure (FSEntry d) { return 1.0; }
}

public class SizeMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return false; } }
  /** FAST */
  public double Measure (FSEntry d) {
    return Math.Max(1.0, d.Size);
  }
}

public class DateMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return false; } }
  /** FAST */
  public double Measure (FSEntry d) {
    long elapsed = (DateTime.Today.AddDays(1) - d.LastModified).Ticks;
    double mul = (d.Name[0] == '.') ? 1.0 : 20.0;
    return mul * (1 / Math.Max(1.0, (double)elapsed / (10000.0 * 300.0)));
  }
}

public class TotalMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return true; } }
  /** FAST */
  public double Measure (FSEntry d) {
    return Math.Max(1.0, d.TotalSize);
  }
}

public class CountMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return true; } }
  /** FAST */
  public double Measure (FSEntry d) {
    double mul = (d.Name[0] == '.') ? 1.0 : 20.0;
    return (d.IsDirectory ? Math.Max(1, d.TotalCount) : 1.0) * mul;
  }
}

public class FlatMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return false; } }
  /** FAST */
  public double Measure (FSEntry d) {
    bool isDotFile = d.Name[0] == '.';
    if (isDotFile) return 0.05;
    return (d.IsDirectory ? 1.5 : 1.0);
  }
}

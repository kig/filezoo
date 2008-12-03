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

public class SizeHandler {
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
    return Math.Max(1.0, d.SubTreeSize);
  }
}

public class CountMeasurer : IMeasurer {
  public bool DependsOnTotals { get { return true; } }
  /** FAST */
  public double Measure (FSEntry d) {
    double mul = (d.Name[0] == '.') ? 1.0 : 40.0;
    return (d.IsDirectory ? Math.Max(1, d.SubTreeCount) : 1.0) * mul;
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

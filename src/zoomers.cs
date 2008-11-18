using System;

public interface IZoomer {
  void SetZoom (double x, double y, double z);
  double X { get; set; }
  double Y { get; set; }
  double Z { get; set; }
  double GetZoomAt (double position);
  void ResetZoom ();
}

public class FlatZoomer : IZoomer {
  double xval = 0.0, yval = 0.0, zval = 1.0;
  public double X { get { return xval; } set { xval = value; } }
  public double Y {
    get { return yval; }
    set {
      double max = (1.0 / zval) - 1.0;
      yval = Math.Max(max, Math.Min(0.0, value));
    }
  }
  public double Z { get { return zval; } set { zval = value; } }
  public void SetZoom (double x, double y, double z) {
    Z = z;
    X = x; Y = y;
  }
  public void ResetZoom () { X = Y = 0.0; Z = 1.0; }
  public double GetZoomAt (double position) {
    return Z;
  }
}


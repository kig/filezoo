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
  public double Y { get { return yval; } set { yval = value; } }
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


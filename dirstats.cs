using System;
using System.Diagnostics;
using System.Collections;
using System.IO;
using Mono.Unix;
using Cairo;

public class DirStats
{

  public static Color directoryColor = new Color (0,0,1);
  public static Color blockDeviceColor = new Color (0.75,0.5,0);
  public static Color characterDeviceColor = new Color (0.75,0.5,0);
  public static Color fifoColor = new Color (0.75,0,0.82);
  public static Color socketColor = new Color (0.75,0,0);
  public static Color symlinkColor = new Color (0,0.75,0.93);
  public static Color executableColor = new Color (0,0.75,0);
  public static Color fileColor = new Color (0,0,0);

  public double Scale;
  public double Zoom;
  public double Height;
  public string Suffix;
  public UnixFileSystemInfo Info;

  public DirStats (UnixFileSystemInfo f)
  {
    Scale = Zoom = Height = 0.0;
    Info = f;
    string[] split = f.Name.Split('.');
    Suffix = (f.Name[0] == '.') ? "" : split[split.Length-1];
  }

  public double GetScaledHeight ()
  {
    return Height * Scale * Zoom;
  }

  public string GetSubTitle ()
  {
    string extras = "";
    if (Info.IsDirectory) {
      if (recursiveCountComputed)
        extras += String.Format(", {0} files", GetRecursiveCount().ToString("N0"));
      if (recursiveSizeComputed)
        extras += String.Format(", {0} total", FormatSize(GetRecursiveSize()));
    }
    return String.Format("{0}{1}", FormatSize(Info.Length), extras);
  }

  public static string FormatSize (double sz)
  {
    string suffix = "";
    if (sz >= 1e9) {
      suffix = "G";
      sz /= 1e9;
    } else if (sz >= 1e6) {
      suffix = "M";
      sz /= 1e6;
    } else if (sz >= 1e3) {
      suffix = "k";
      sz /= 1e3;
    } else {
      return String.Format("{0} B", sz.ToString("N0"));
    }
    return String.Format("{0} {1}B", sz.ToString("N1"), suffix);
  }

  public double BoxWidth = 0.1;

  public void Draw (Context cr)
  {
    double h = GetScaledHeight ();
    cr.Save ();
      cr.Rectangle (0.0, 0.0, BoxWidth, h*0.98);
      Color c = GetColor (Info.FileType, Info.FileAccessPermissions);
      cr.Color = c;
      cr.Fill ();
      double fs = GetFontSize(h);
      cr.SetFontSize (fs);
      cr.MoveTo (BoxWidth + 0.01, h / 2 + fs / 4);
      cr.ShowText (Info.Name);
      cr.SetFontSize(fs * 0.7);
      cr.ShowText ("  ");
      cr.ShowText (GetSubTitle ());
    cr.Restore ();
  }

  double MinFontSize = 0.0005;
  double MaxFontSize = 0.02;

  double GetFontSize(double h) {
    return Math.Max(MinFontSize, Quantize(Math.Min(MaxFontSize, 0.7 * h)));
  }

  double Quantize (double fs) {
    return (Math.Floor(fs / 0.001) * 0.001);
  }

  public bool[] Click (Context cr, double totalSize, double x, double y)
  {
    double h = GetScaledHeight ();
    bool[] retval = {false, false};
    double advance = 0.0;
    cr.Save ();
      cr.NewPath ();
      double fs = GetFontSize(h);
      cr.SetFontSize (fs);
      advance += cr.TextExtents(Info.Name).XAdvance;
      cr.SetFontSize(fs * 0.7);
      advance += cr.TextExtents("  ").XAdvance;
      advance += cr.TextExtents(GetSubTitle ()).XAdvance;
      cr.Rectangle (0.0, 0.0, BoxWidth + 0.01 + advance, h);
      cr.IdentityMatrix ();
      retval[0] = cr.InFill(x,y);
    cr.Restore ();
    if (retval[0])
      retval[1] = OpenFile ();
    return retval;
  }

  public string GetFullPath ()
  {
    return Info.FullName;
  }

  bool recursiveSizeComputed = false;
  double recursiveSize = 0.0;

  public double GetRecursiveSize ()
  {
    if (!recursiveSizeComputed) {
      recursiveSize = Info.IsDirectory ? dirSize(GetFullPath(), out recursiveCount) : Info.Length;
      recursiveCountComputed = true;
      recursiveSizeComputed = true;
    }
    return recursiveSize;
  }

  bool recursiveCountComputed = false;
  double recursiveCount = 1.0;

  public double GetRecursiveCount ()
  {
    if (!recursiveCountComputed) {
      GetRecursiveSize ();
      recursiveCountComputed = true;
    }
    return recursiveCount;
  }

  static double dirSize (string dirname, out double count)
  {
    UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
    UnixFileSystemInfo[] files = di.GetFileSystemEntries ();
    double size = 0.0;
    double subCount = 0.0;
    count = 0.0;
    foreach (UnixFileSystemInfo f in files) {
      subCount = 1.0;
      size += f.IsDirectory ? dirSize(f.FullName, out subCount) : (double)f.Length;
      count += subCount;
    }
    return size;
  }

  bool OpenFile ()
  {
    if (Info.IsDirectory) {
      return true;
    } else {
      Process proc = Process.Start ("gnome-open", GetFullPath ());
      proc.WaitForExit ();
      return false;
    }
  }

  static Color GetColor (FileTypes filetype, FileAccessPermissions perm)
  {
    switch (filetype) {
      case FileTypes.Directory: return directoryColor;
      case FileTypes.BlockDevice: return blockDeviceColor;
      case FileTypes.CharacterDevice: return characterDeviceColor;
      case FileTypes.Fifo: return fifoColor;
      case FileTypes.Socket: return socketColor;
      case FileTypes.SymbolicLink: return symlinkColor;
    }
    if ((perm & FileAccessPermissions.UserExecute) != 0)
      return executableColor;
    return fileColor;
  }
}
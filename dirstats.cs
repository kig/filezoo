using System;
using System.Diagnostics;
using System.Threading;
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
  public string Name;
  public string FullName;
  public double Length;
  public string Suffix;
  public bool IsDirectory = false;
  public UnixFileSystemInfo Info;
  public bool TraversalInProgress = false;
  public bool TraversalCancelled = false;

  public double BoxWidth = 100.0;

  public double MinFontSize = 0.5;
  public double MaxFontSize = 16.0;

  private bool recursiveSizeComputed = false;
  private double recursiveSize = 0.0;
  private double recursiveCount = 1.0;

  public DirStats (UnixFileSystemInfo f)
  {
    Scale = Zoom = Height = 0.0;
    Info = f;
    Name = f.Name;
    FullName = f.FullName;
    Length = f.Length;
    try { IsDirectory = Info.IsDirectory; } catch (System.InvalidOperationException) {}
    string[] split = Name.Split('.');
    Suffix = (Name[0] == '.') ? "" : split[split.Length-1];
  }

  public double GetScaledHeight ()
  {
    return Height * Scale * Zoom * 1000;
  }

  public string GetSubTitle () { return GetSubTitle (true); }

  public string GetSubTitle ( bool complexSubTitle )
  {
    if (IsDirectory) {
      string extras = "";
      extras += String.Format("{0} files", (complexSubTitle ? GetRecursiveCount() : 0).ToString("N0"));
      extras += String.Format(", {0} total", FormatSize(complexSubTitle ? GetRecursiveSize() : 0));
      return extras;
    } else {
      return String.Format("{0}", FormatSize(Length));
    }
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

  public void Draw (Context cr) { Draw (cr, true); }

  public void Draw (Context cr, bool complexSubTitle)
  {
    double h = GetScaledHeight ();
    cr.Save ();
      cr.Rectangle (0.0, 0.0, BoxWidth, h*0.98);
      Color c = GetColor (Info.FileType, Info.FileAccessPermissions);
      cr.Color = c;
      cr.Fill ();
      double fs = GetFontSize(h);
      cr.MoveTo (BoxWidth * 1.1, 0.0);
      cr.RelMoveTo(0, h*0.5 - fs);
      if (fs > 4) {
        Helpers.DrawText (cr, fs, Name);
        cr.RelMoveTo(0, fs*0.35);
        Helpers.DrawText (cr, fs * 0.7, "  " + GetSubTitle (complexSubTitle));
      } else if (fs > 1) {
        Helpers.DrawText (cr, fs, Name + "  " + GetSubTitle (complexSubTitle));
      } else {
        cr.Rectangle (BoxWidth * 1.1, h*0.5 - fs, fs / 2 * (Name.Length+15), fs/3);
        cr.Fill ();
      }
    cr.Restore ();
  }

  double GetFontSize(double h) {
    return Math.Max(MinFontSize, QuantizeFontSize(Math.Min(MaxFontSize, 0.5 * h)));
  }

  double QuantizeFontSize (double fs) {
    return Math.Floor(fs);
  }

  public bool[] Click (Context cr, double totalSize, double x, double y)
  {

    double h = GetScaledHeight ();
    double fs = GetFontSize(h);
    bool[] retval = {false, false, false};
    double advance = 0.0;
    cr.Save ();
      cr.NewPath ();
      if (fs < 8) {
        advance += BoxWidth;
      } else {
        advance += Helpers.GetTextExtents (cr, fs, Name).XAdvance;
        advance += Helpers.GetTextExtents (cr, fs*0.7, "  " + GetSubTitle ()).XAdvance;
      }
      cr.Rectangle (0.0, 0.0, BoxWidth * 1.1 + advance, h);
      cr.IdentityMatrix ();
      retval[0] = cr.InFill(x,y);
    cr.Restore ();
    if (retval[0]) {
      if (fs < 8)
        retval[2] = true;
      else
        retval[1] = OpenFile ();
    }
    return retval;
  }

  public string GetFullPath ()
  {
    return FullName;
  }

  public double GetRecursiveSize ()
  {
    if (!recursiveSizeComputed) {
      recursiveSizeComputed = true;
      recursiveSize = 0.0;
      recursiveCount = 1.0;
      if (IsDirectory) {
        TraversalInProgress = true;
        WaitCallback cb = new WaitCallback(DirSizeCallback);
        ThreadPool.QueueUserWorkItem(cb);
      } else {
        recursiveSize = Length;
        TraversalInProgress = false;
      }
    }
    return recursiveSize;
  }

  public double GetRecursiveCount ()
  {
    if (!recursiveSizeComputed)
      GetRecursiveSize ();
    return recursiveCount;
  }

  void DirSizeCallback (Object stateInfo)
  {
    DirSize(GetFullPath());
    TraversalInProgress = false;
  }

  void DirSize (string dirname)
  {
    if (TraversalCancelled) return;
    UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
    UnixFileSystemInfo[] files;
    try {
      files = di.GetFileSystemEntries ();
    } catch (System.UnauthorizedAccessException) {
      return;
    }
    foreach (UnixFileSystemInfo f in files) {
      if (TraversalCancelled) return;
      recursiveCount += 1.0;
      bool isDir = false;
      try { isDir = f.IsDirectory; } catch (System.InvalidOperationException) {}
      if (isDir)
        DirSize(f.FullName);
      else
        try { recursiveSize += (double)f.Length; } catch (System.InvalidOperationException) {}
    }
  }

  bool OpenFile ()
  {
    if (IsDirectory) {
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

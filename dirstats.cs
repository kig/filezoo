using System;
using System.Diagnostics;
using System.Collections;
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
  public string Suffix;
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
    string[] split = f.Name.Split('.');
    Suffix = (f.Name[0] == '.') ? "" : split[split.Length-1];
  }

  public double GetScaledHeight ()
  {
    return Height * Scale * Zoom * 1000;
  }

  public string GetSubTitle ()
  {
    if (IsDirectory) {
      string extras = "";
      extras += String.Format("{0} files", GetRecursiveCount().ToString("N0"));
      extras += String.Format(", {0} total", FormatSize(GetRecursiveSize()));
      return extras;
    } else {
      return String.Format("{0}", FormatSize(Info.Length));
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

  public void Draw (Context cr)
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
      Helpers.DrawText (cr, fs, Info.Name);
      cr.RelMoveTo(0, fs*0.35);
      Helpers.DrawText (cr, fs * 0.7, "  ");
      Helpers.DrawText (cr, fs * 0.7, GetSubTitle ());
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
    bool[] retval = {false, false};
    double advance = 0.0;
    cr.Save ();
      cr.NewPath ();
      double fs = GetFontSize(h);
      advance += Helpers.GetTextExtents (cr, fs, Info.Name).XAdvance;
      advance += Helpers.GetTextExtents (cr, fs*0.7, "  ").XAdvance;
      advance += Helpers.GetTextExtents (cr, fs*0.7, GetSubTitle ()).XAdvance;
      cr.Rectangle (0.0, 0.0, BoxWidth * 1.1 + advance, h);
      cr.IdentityMatrix ();
      retval[0] = cr.InFill(x,y);
    cr.Restore ();
    if (retval[0])
      retval[1] = OpenFile ();
    return retval;
  }

  public bool IsDirectory {
    get {
      bool isDir = false;
      try { isDir = Info.IsDirectory; } catch (System.InvalidOperationException) {}
      return isDir;
    }
  }

  public string GetFullPath ()
  {
    return Info.FullName;
  }

  class DirectoryEntry {
    public bool Complete;
    public string Path;
    public double TotalSize;

    public DirectoryEntry (string path) {
      Path = path;
      Complete = false;
      TotalSize = 0.0;
    }

    public void SetTotalSize (double sz) { TotalSize = sz; }
    public void SetComplete (bool c) { Complete = c; }
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
        recursiveSize = Info.Length;
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
      try { isDir = f.IsDirectory; }
      catch (System.InvalidOperationException) {}
      if (isDir)
        DirSize(f.FullName);
      else
        recursiveSize += (double)f.Length;
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
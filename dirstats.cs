using System;
using System.Diagnostics;
using System.Collections;
using System.IO;
using Mono.Unix;
using Cairo;

public class DirStats
{

  public class sizeComparer : IComparer {
    int IComparer.Compare ( object x, object y ) {
      DirStats a = (DirStats) x;
      DirStats b = (DirStats) y;
      if (a.Info.FileType != b.Info.FileType ) {
        if (a.Info.IsDirectory) return -1;
        if (b.Info.IsDirectory) return 1;
      }
      return -(a.GetRecursiveSize().CompareTo(b.GetRecursiveSize()));
    }
  }

  public class nameComparer : IComparer {
    int IComparer.Compare ( object x, object y ) {
      DirStats a = (DirStats) x;
      DirStats b = (DirStats) y;
      if (a.Info.FileType != b.Info.FileType ) {
        if (a.Info.IsDirectory) return -1;
        if (b.Info.IsDirectory) return 1;
      }
      return String.Compare(a.Info.Name, b.Info.Name);
    }
  }

  public double Scale;
  public double Zoom;
  public double Height;
  public bool Control;
  public UnixFileSystemInfo Info;

  public static Color directoryColor = new Color (0,0,1);
  public static Color blockDeviceColor = new Color (1,1,0);
  public static Color characterDeviceColor = new Color (1,1,0);
  public static Color fifoColor = new Color (1,0,1);
  public static Color socketColor = new Color (1,0,0);
  public static Color symlinkColor = new Color (0,1,1);
  public static Color executableColor = new Color (0,1,0);
  public static Color fileColor = new Color (0,0,0);

  public DirStats (UnixFileSystemInfo f)
  {
    Scale = Zoom = Height = 0.0;
    Control = false;
    Info = f;
  }

  public double GetScaledHeight ()
  {
    return Height * Scale * Zoom;
  }

  public string GetSubTitle ()
  {
    return String.Format("{0} bytes", Info.Length.ToString("N0"));
  }

  public void Draw (Context cr)
  {
    double h = GetScaledHeight ();
    cr.Save ();
      cr.Rectangle (0.0, 0.0, 0.2, h);
      Color c = GetColor (Info.FileType, Info.FileAccessPermissions);
      cr.Color = c;
      cr.FillPreserve ();
      cr.Color = new Color (1,1,1);
      cr.Stroke ();
      cr.Color = c;
      double fs = Math.Max(0.001, Math.Min(0.03, 0.8 * h));
      cr.SetFontSize (fs);
      cr.MoveTo (0.21, h / 2 + fs / 4);
      cr.ShowText (Info.Name);
      cr.SetFontSize(fs * 0.7);
      cr.ShowText ("  ");
      cr.ShowText (GetSubTitle ());
    cr.Restore ();
  }

  public bool[] Click (Context cr, double totalSize, double x, double y)
  {
    double h = GetScaledHeight ();
    bool[] retval = {false, false};
    cr.Save ();
      cr.NewPath ();
      cr.Rectangle (0.0, 0.0, 0.2, h);
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
      recursiveSize = Info.IsDirectory ? dirSize(GetFullPath()) : Info.Length;
      recursiveSizeComputed = true;
    }
    return recursiveSize;
  }

  public double GetRecursiveCount ()
  {
    return Info.IsDirectory ? dirCount(GetFullPath()) : 1.0;
  }

  static double dirSize (string dirname)
  {
    UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
    UnixFileSystemInfo[] files = di.GetFileSystemEntries ();
    double size = 0.0;
    foreach (UnixFileSystemInfo f in files)
      size += f.IsDirectory ? dirSize(f.FullName) : (double)f.Length;
    return size;
  }

  static double dirCount (string dirname)
  {
    UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
    UnixFileSystemInfo[] files = di.GetFileSystemEntries ();
    double size = 1.0;
    foreach (UnixFileSystemInfo f in files)
      size += f.IsDirectory ? dirCount(f.FullName) : 1.0;
    return size;
  }

  bool OpenFile ()
  {
    if (Info.IsDirectory) {
      Console.WriteLine("Navigating to {0}", GetFullPath ());
      return true;
    } else {
      Console.WriteLine("Opening {0}", GetFullPath ());
      Process proc = new Process ();
      proc.EnableRaisingEvents = false;
      proc.StartInfo.FileName = "gnome-open";
      proc.StartInfo.Arguments = GetFullPath ();
      proc.Start ();
      proc.WaitForExit ();
      return false;
    }
  }

  Color GetColor (FileTypes filetype, FileAccessPermissions perm)
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
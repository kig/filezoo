using System;
using System.Collections.Generic;
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

  double Scale;
  double Zoom;
  double Height;
  public string Name;
  public string FullName;
  public double Length;
  public string Suffix;
  public bool IsDirectory = false;
  public UnixFileSystemInfo Info;
  public bool TraversalCancelled = false;
  public IComparer<DirStats> Comparer;
  public IMeasurer Measurer;
  public IZoomer Zoomer;
  public SortingDirection SortDirection = SortingDirection.Ascending;

  DirStats[] _Entries = null;
  DirStats[] Entries {
    get {
      if (_Entries == null) {
        UnixFileSystemInfo[] files = new UnixDirectoryInfo(FullName).GetFileSystemEntries ();
        _Entries = new DirStats[files.Length];
        for (int i=0; i<files.Length; i++)
          _Entries[i] = new DirStats (files[i]);
      }
      return _Entries;
    }
  }

  bool travP;
  public virtual bool TraversalInProgress {
    get { return travP; }
    set { travP = value; }
  }

  public double BoxWidth = 100.0;

  public double MinFontSize = 0.5;
  public double MaxFontSize = 16.0;

  protected bool recursiveSizeComputed = false;
  private double recursiveSize = 0.0;
  private double recursiveCount = 1.0;

  public DirStats (UnixFileSystemInfo f)
  {
    Comparer = new NameComparer ();
    Scale = Zoom = Height = 0.0;
    Info = f;
    Name = f.Name;
    FullName = f.FullName;
    Length = f.Length;
    try { IsDirectory = Info.IsDirectory; } catch (System.InvalidOperationException) {}
    string[] split = Name.Split('.');
    Suffix = (Name[0] == '.') ? "" : split[split.Length-1];
  }

  public double GetScaledHeight () { return Height * Scale * Zoom * 1000; }

  public string GetSubTitle () { return GetSubTitle (true); }
  public string GetSubTitle ( bool complexSubTitle )
  {
    if (IsDirectory) {
      string extras = "";
      extras += String.Format("{0} files",
        (complexSubTitle ? GetRecursiveCount() : 0).ToString("N0"));
      extras += String.Format(", {0} total",
        Helpers.FormatSI(complexSubTitle ? GetRecursiveSize() : 0, "B"));
      return extras;
    } else {
      return String.Format("{0}", Helpers.FormatSI(Length, "B"));
    }
  }

  public void Sort () { Array.Sort (Entries, Comparer); }

  public void Relayout ()
  {
    double totalHeight = 0.0;
    foreach (DirStats f in Entries) {
      double height = Measurer.Measure(f);
      f.Height = height;
      totalHeight += height;
    }
    double position = 0.0;
    bool trav = false;
    foreach (DirStats f in Entries) {
      double zoom = Zoomer.GetZoomAt(position);
      f.Zoom = zoom;
      f.Scale = 1.0 / totalHeight;
      position += f.Height / totalHeight;
      trav = (trav || f.TraversalInProgress);
    }
  }

  public void Draw (Context cr, double y, double height, bool complexSubTitle)
  { Draw (cr, y, height, complexSubTitle, true); }

  public void Draw (Context cr, double y, double height, bool complexSubTitle, bool drawSubdirs)
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
    if (drawSubdirs && IsDirectory) {
      cr.Save ();
        cr.Translate (0, -h*0.01); // to account for the 0.02 bottom margin
        y += -h*0.01;
        DrawChildren(cr, y, FullName, h, Math.Min(1.0, Math.Max(0.8, h/1000.0)));
      cr.Restore ();
    }
  }

  public void DrawChildren (Context cr, double y, string path, double scale, double alpha)
  {
    if (scale < 1.0) return;
    cr.Save ();
      UnixDirectoryInfo info = new UnixDirectoryInfo(path);
      UnixFileSystemInfo[] files = info.GetFileSystemEntries();
      if (files.Length > 0) {
        cr.Translate(0, 0.05*scale);
        y += 0.05*scale;
        scale *= 0.9;
        cr.Rectangle (0, 0, BoxWidth*alpha, scale);
        cr.Color = new Color (1,1,1);
        cr.Fill ();
        scale /= files.Length;
        foreach (UnixFileSystemInfo f in files) {
          if (y > 1000.0) break;
          if (y + scale > 0.0) {
            cr.Rectangle (0,0, BoxWidth*alpha, scale*0.98);
            Color c = GetColor (f.FileType, f.FileAccessPermissions);
            cr.Color = c;
            cr.Fill();
            if (f.IsDirectory) {
              cr.Save ();
                cr.Translate (0, -scale*0.01); // to account for the 0.02 bottom margin
                DrawChildren(cr, y - scale*0.01, f.FullName, scale, alpha * 0.8);
              cr.Restore ();
            }
          }
          cr.Translate(0, scale);
          y += scale;
        }
      }
    cr.Restore ();
  }

  double GetFontSize(double h) {
    return Math.Max(MinFontSize, QuantizeFontSize(Math.Min(MaxFontSize, 0.5 * h)));
  }

  double QuantizeFontSize (double fs) {
    return Math.Floor(fs);
  }

  public DirAction Click (Context cr, double y, double height, double mouseX, double mouseY)
  {

    double h = GetScaledHeight ();
    double fs = GetFontSize(h);
    bool hit = false;
    DirAction retval = DirAction.None ();
    double advance = 0.0;
    cr.Save ();
      cr.NewPath ();
      if (fs < 10) {
        advance += BoxWidth;
      } else {
        advance += Helpers.GetTextExtents (cr, fs, Name).XAdvance;
        advance += Helpers.GetTextExtents (cr, fs*0.7, "  " + GetSubTitle ()).XAdvance;
      }
      cr.Rectangle (0.0, 0.0, BoxWidth * 1.1 + advance, h);
      cr.IdentityMatrix ();
      hit = cr.InFill(mouseX,mouseY);
      if (hit) retval = DirAction.Open(FullName);
    cr.Restore ();
    if (hit) {
      if (fs < 10)
        retval = DirAction.ZoomIn(h);
      else if (IsDirectory)
        retval = DirAction.Navigate(FullName);
    }
    return retval;
  }

  public virtual double GetRecursiveSize ()
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

  public virtual double GetRecursiveCount ()
  {
    if (!recursiveSizeComputed)
      GetRecursiveSize ();
    return recursiveCount;
  }

  void DirSizeCallback (Object stateInfo)
  {
    DirSize(FullName);
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


public class DirAction
{
  public Action Type;
  public string Path;
  public double Height;

  public static DirAction None ()
  { return new DirAction (Action.None, "", 0.0); }

  public static DirAction Open (string path)
  { return new DirAction (Action.Open, path, 0.0); }

  public static DirAction Navigate (string path)
  { return new DirAction (Action.Navigate, path, 0.0); }

  public static DirAction ZoomIn (double h)
  { return new DirAction (Action.ZoomIn, "", h); }

  DirAction (Action type, string path, double height)
  {
    Type = type;
    Path = path;
    Height = height;
  }

  public enum Action {
    None,
    Open,
    Navigate,
    ZoomIn
  }
}


public enum SortingDirection {
  Ascending,
  Descending
}

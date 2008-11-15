using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Mono.Unix;
using Cairo;

public class DirStats
{
  // Colors for the different file types, quite like ls
  public static Color directoryColor = new Color (0,0,1);
  public static Color blockDeviceColor = new Color (0.75,0.5,0);
  public static Color characterDeviceColor = new Color (0.75,0.5,0);
  public static Color fifoColor = new Color (0.75,0,0.82);
  public static Color socketColor = new Color (0.75,0,0);
  public static Color symlinkColor = new Color (0,0.75,0.93);
  public static Color executableColor = new Color (0,0.75,0);
  public static Color fileColor = new Color (0,0,0);

  // Style for the DirStats
  public double BoxWidth = 0.1;

  public double MinFontSize = 0.5;
  public double MaxFontSize = 16.0;

  // Public state of the DirStats
  public string Name;
  public string FullName;
  public double Length;
  public string Suffix;
  public bool IsDirectory = false;
  public UnixFileSystemInfo Info;

  // How to sort directories
  public IComparer<DirStats> Comparer;
  public SortingDirection SortDirection = SortingDirection.Ascending;

  // How to scale file sizes
  public IMeasurer Measurer;
  public IZoomer Zoomer;

  // Traversal progress flag, true if the
  // recursive traversal of the DirStats has not yet completed.
  public virtual bool TraversalInProgress
  { get { return travP; } set { travP = value; } }
  bool travP;

  // Should the recursive traversal be stopped?
  public bool TraversalCancelled = false;

  // State variables for computing the recursive traversal of the DirStats
  protected bool recursiveSizeComputed = false;
  private double recursiveSize = 0.0;
  private double recursiveCount = 1.0;

  // Drawing state variables
  double Scale;
  double Height;

  // DirStats objects for the children of a directory DirStats
  DirStats[] _Entries = null;
  DirStats[] Entries {
    get {
      if (_Entries == null) {
        try {
          UnixFileSystemInfo[] files = new UnixDirectoryInfo(FullName).GetFileSystemEntries ();
          _Entries = new DirStats[files.Length];
          for (int i=0; i<files.Length; i++)
            _Entries[i] = new DirStats (files[i]);
        } catch (System.UnauthorizedAccessException) {
          _Entries = new DirStats[0];
        }
      }
      return _Entries;
    }
  }


  /* Constructor */

  public DirStats (UnixFileSystemInfo f)
  {
    Comparer = new NameComparer ();
    Scale = Height = 1.0;
    Info = f;
    Name = f.Name;
    FullName = f.FullName;
    Length = f.Length;
    try { IsDirectory = Info.IsDirectory; } catch (System.InvalidOperationException) {}
    string[] split = Name.Split('.');
    Suffix = (Name[0] == '.') ? "" : split[split.Length-1];
  }


  /* Subtitles */

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


  /* Drawing helpers */

  public double GetScaledHeight ()
  {
    return Height * Scale;
  }

  double GetFontSize (double h)
  {
    return Math.Max(MinFontSize, QuantizeFontSize(Math.Min(MaxFontSize, 0.5 * h)));
  }

  double QuantizeFontSize (double fs) { return Math.Floor(fs); }

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


  /* Relayout */

  public void Sort ()
  {
    if (IsDirectory)
      Array.Sort (Entries, Comparer);
  }

  public void Relayout ()
  {
    if (!IsDirectory) return;
    double totalHeight = 0.0;
    foreach (DirStats f in Entries) {
      f.Height = Measurer.Measure(f);
      totalHeight += f.Height;
    }
    double scale = 1.0 / totalHeight;
    foreach (DirStats f in Entries) {
      f.Scale = scale;
    }
  }

  DirStats _ParentDir;
  public DirStats ParentDir {
    get {
      if (_ParentDir == null)
        _ParentDir = new DirStats (new UnixDirectoryInfo(System.IO.Path.GetDirectoryName(FullName)));
      return _ParentDir;
    }
  }
  public string GetParentDirPath () {
    return ParentDir.FullName;
  }
  public double GetYInParentDir () {
    UpdateChild (ParentDir);
    double position = 0.0;
    foreach (DirStats d in ParentDir.Entries) {
      if (d.Name == Name) return position;
      position += d.GetScaledHeight ();
    }
    return 0.0;
  }
  public double GetHeightInParentDir () {
    UpdateChild (ParentDir);
    foreach (DirStats d in ParentDir.Entries) {
      if (d.Name == Name) return d.GetScaledHeight ();
    }
    return 1.0;
  }


  /* Drawing */

  public bool IsVisible (Context cr, double targetTop, double targetHeight)
  {
    double h = cr.Matrix.Yy * GetScaledHeight ();
    if (h < 1) return false;
    double y = cr.Matrix.Y0 - targetTop;
    return ((y < targetHeight) && ((y+h) > 0.0));
  }

  public void Draw (Context cr, double targetTop, double targetHeight, bool complexSubTitle, uint depth)
  {
    if (!IsVisible(cr, targetTop, targetHeight)) {
      return;
    }
    double h = GetScaledHeight ();
    cr.Save ();
      cr.Scale (1, h);
      cr.Rectangle (0.0, -0.01, BoxWidth*1.02, 1.01);
      cr.Color = new Color (1,1,1);
      cr.Fill ();
      cr.Rectangle (0.0, 0.0, BoxWidth, 0.98);
      Color c = GetColor (Info.FileType, Info.FileAccessPermissions);
      cr.Color = c;
      cr.Fill ();
      DrawTitle (cr, complexSubTitle);
      if (IsDirectory) DrawChildren(cr, targetTop, targetHeight, complexSubTitle, depth);
    cr.Restore ();
  }

  void DrawChildren (Context cr, double targetTop, double targetHeight, bool complexSubTitle, uint depth)
  {
    cr.Save ();
      cr.Translate (0.1*BoxWidth, 0.04);
      cr.Scale (0.9, 0.93);
      foreach (DirStats d in Entries) {
        UpdateChild (d);
        d.Draw (cr, targetTop, targetHeight, complexSubTitle, depth+1);
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
  }

  void DrawTitle (Context cr, bool complexSubTitle)
  {
    double h = cr.Matrix.Yy;
    double fs = GetFontSize(h);
    cr.Save ();
      cr.Translate(BoxWidth * 1.1, 0);
      double x = cr.Matrix.X0;
      double y = cr.Matrix.Y0;
      cr.IdentityMatrix ();
      cr.Translate (x,y);
      cr.NewPath ();
      cr.MoveTo (0, 0);
      if (fs > 4) {
        Helpers.DrawText (cr, fs, Name);
        cr.RelMoveTo(0, fs*0.35);
        Helpers.DrawText (cr, fs * 0.7, "  " + GetSubTitle (complexSubTitle));
      } else if (fs > 1) {
        Helpers.DrawText (cr, fs, Name + "  " + GetSubTitle (complexSubTitle));
      } else {
        cr.Rectangle (0.0, 0.0, fs / 2 * (Name.Length+15), fs/3);
        cr.Fill ();
      }
    cr.Restore ();
  }

  void UpdateChild (DirStats d)
  {
    if (d.Comparer != Comparer || d.SortDirection != SortDirection) {
      d.Comparer = Comparer;
      d.SortDirection = SortDirection;
      d.Sort ();
    }
    d.Measurer = Measurer;
    d.Relayout ();
  }


  /* Click handler */

  public DirAction Click (Context cr, double y, double height, double mouseX, double mouseY)
  {
    DirAction retval = DirAction.None ();
/*    double h = GetScaledHeight ();
    double fs = GetFontSize(h);
    bool hit = false;
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
    }*/
    return retval;
  }


  /* Directory traversal */

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

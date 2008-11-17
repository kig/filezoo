using System;
using System.Collections.Generic;
using System.Collections;
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
  public double MaxFontSize = 12.0;

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
  // recursive traversal of the DirStats is completed.
  public virtual bool Complete
  { get { return recursiveInfo.Complete; } }

  // Should the recursive traversal be stopped?
  bool TraversalCancelled = false;

  // State variables for computing the recursive traversal of the DirStats
  public bool recursiveSizeComputed = false;
  public Dir recursiveInfo;

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
            _Entries[i] = Get (files[i]);
        } catch (System.UnauthorizedAccessException) {
          _Entries = new DirStats[0];
        }
      }
      return _Entries;
    }
  }



  /* Constructor */

  public static DirStats Get (UnixFileSystemInfo f) {
    DirStats d;
    if (f.IsDirectory)
      d = new DirStats(f, DirCache.GetCacheEntry(f.FullName));
    else
      d = new DirStats(f, new Dir(f.FullName));
    return d;
  }

  protected DirStats (UnixFileSystemInfo f, Dir r)
  {
    Comparer = new NameComparer ();
    recursiveInfo = r;
    Scale = Height = 1.0;
    Info = f;
    Name = f.Name;
    FullName = f.FullName;
    Length = f.Length;
    try { IsDirectory = Info.IsDirectory; } catch (System.InvalidOperationException) {}
    if (!IsDirectory) {
      recursiveInfo.InProgress = false;
      recursiveInfo.Complete = true;
      recursiveInfo.TotalSize = Length;
      recursiveInfo.TotalCount = 1.0;
    }
    string[] split = Name.Split('.');
    Suffix = (Name[0] == '.') ? "" : split[split.Length-1];
  }


  /* Subtitles */

  public string GetSubTitle ()
  {
    if (IsDirectory) {
      string extras = "";
      extras += String.Format("{0} files", GetRecursiveCount().ToString("N0"));
      extras += String.Format(", {0} total", Helpers.FormatSI(GetRecursiveSize(), "B"));
      if (recursiveInfo.Missing != recursiveInfo.Completed && recursiveInfo.InProgress) {
        extras += String.Format(", {0} missing", recursiveInfo.Missing-recursiveInfo.Completed);
      }
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
    double fs;
    fs = h * (IsDirectory ? 0.4 : 0.6);
    return Math.Max(MinFontSize, QuantizeFontSize(Math.Min(MaxFontSize, fs)));
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
        _ParentDir = Get (new UnixDirectoryInfo(System.IO.Path.GetDirectoryName(FullName)));
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
    double y = cr.Matrix.Y0 - targetTop;
    return ((y < targetHeight) && ((y+h) > 0.0));
  }

  static Color BG = new Color (1,1,1);
  public uint Draw (Context cr, double targetTop, double targetHeight, bool firstFrame, uint depth)
  {
    if (!IsVisible(cr, targetTop, targetHeight)) {
      return 0;
    }
    double h = GetScaledHeight ();
    uint c = 1;
    cr.Save ();
      cr.Scale (1, h);
      cr.Rectangle (-0.01*BoxWidth, 0.0, BoxWidth*1.02, 1.01);
      cr.Color = BG;
      cr.Fill ();
      Color co = GetColor (Info.FileType, Info.FileAccessPermissions);
      cr.Color = co;
      if (!recursiveInfo.Complete) cr.Color = new Color (0.5, 0, 1);
      if (depth > 0) {
        cr.Rectangle (0.0, 0.02, BoxWidth, 0.98);
        cr.Fill ();
      }
      if (cr.Matrix.Yy > 1) DrawTitle (cr, depth);
      if (IsDirectory) {
        bool childrenVisible = cr.Matrix.Yy > 2;
        bool shouldDrawChildren = !firstFrame && childrenVisible;
        if (depth == 0) shouldDrawChildren = true;
        if (shouldDrawChildren) {
          RequestInfo();
          c += DrawChildren(cr, targetTop, targetHeight, firstFrame, depth);
        }
      }
    cr.Restore ();
    return c;
  }

  void ChildTransform (Context cr, uint depth)
  {
    if (depth > 0) {
      cr.Translate (0.1*BoxWidth, 0.48);
      cr.Scale (0.9, 0.48);
    } else {
      cr.Translate (0.0, 0.05);
      cr.Scale (1.0, 0.93);
    }
  }

  uint DrawChildren (Context cr, double targetTop, double targetHeight, bool firstFrame, uint depth)
  {
    cr.Save ();
      ChildTransform (cr, depth);
      uint c = 0;
      foreach (DirStats d in Entries) {
        UpdateChild (d);
        c += d.Draw (cr, targetTop, targetHeight, firstFrame, depth+1);
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    return c;
  }

  void DrawTitle (Context cr, uint depth)
  {
    double h = cr.Matrix.Yy;
    double fs = GetFontSize(h);
    cr.Save ();
      cr.Translate(BoxWidth * 1.1, 0.02);
      double x = cr.Matrix.X0;
      double y = cr.Matrix.Y0;
      cr.IdentityMatrix ();
      cr.Translate (x, y);
      cr.NewPath ();
      cr.MoveTo (0, -fs*0.3);
      if (fs > 4) {
        Helpers.DrawText (cr, fs, Name);
        cr.RelMoveTo(0, fs*0.35);
        Helpers.DrawText (cr, fs * 0.7, "  " + GetSubTitle ());
      } else if (fs > 1) {
        Helpers.DrawText (cr, fs, Name + "  " + GetSubTitle ());
      } else {
        cr.Rectangle (0.0, 0.0, fs / 2 * (Name.Length+15), fs/3);
        cr.Fill ();
      }
    cr.Restore ();
  }

  void UpdateChild (DirStats d)
  {
    bool needRelayout = false;
    if (d.Comparer != Comparer || d.SortDirection != SortDirection) {
      d.Comparer = Comparer;
      d.SortDirection = SortDirection;
      d.Sort ();
      needRelayout = true;
    }
    if (d.Measurer != Measurer) {
      d.Measurer = Measurer;
      needRelayout = true;
    }
    if (Measurer.DependsOnTotals && !d.Complete)
      needRelayout = true;
    if (needRelayout)
      d.Relayout ();
  }


  /* Click handler */

  public DirAction Click (Context cr, double targetTop, double targetHeight, double mouseX, double mouseY, uint depth)
  {
    if (!IsVisible(cr, targetTop, targetHeight)) {
      return DirAction.None;
    }
    double h = GetScaledHeight ();
    DirAction retval = DirAction.None;
    double advance = 0.0;
    cr.Save ();
      cr.Scale (1, h);
      if (IsDirectory && (cr.Matrix.Yy > 2))
        retval = ClickChildren (cr, targetTop, targetHeight, mouseX, mouseY, depth);
      if (retval == DirAction.None || (retval.Type == DirAction.Action.ZoomIn && cr.Matrix.Yy < 10)) {
        cr.NewPath ();
        double fs = GetFontSize(cr.Matrix.Yy);
        if (fs < 10) {
          advance += fs / 2 * (Name.Length+15);
        } else {
          advance += Helpers.GetTextExtents (cr, fs, Name).XAdvance;
          advance += Helpers.GetTextExtents (cr, fs*0.7, "  " + GetSubTitle ()).XAdvance;
        }
        cr.Rectangle (0.0, 0.0, BoxWidth * 1.1 + advance, 1.0);
        double ys = cr.Matrix.Yy;
        cr.IdentityMatrix ();
        if (cr.InFill(mouseX,mouseY)) {
          if (ys < 20)
            retval = DirAction.ZoomIn(ys / 20);
          else if (IsDirectory)
            retval = DirAction.Navigate(FullName);
          else
            retval = DirAction.Open(FullName);
        }
      }
    cr.Restore ();
    return retval;
  }

  DirAction ClickChildren (Context cr, double targetTop, double targetHeight, double mouseX, double mouseY, uint depth)
  {
    DirAction retval = DirAction.None;
    cr.Save ();
      ChildTransform (cr, depth);
      foreach (DirStats d in Entries) {
        retval = d.Click (cr, targetTop, targetHeight, mouseX, mouseY, depth+1);
        if (retval != DirAction.None) break;
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    return retval;
  }




  /* Directory traversal */

  public virtual double GetRecursiveSize ()
  {
    return recursiveInfo.TotalSize;
  }

  public virtual double GetRecursiveCount ()
  {
    return recursiveInfo.TotalCount;
  }

  public void CancelTraversal () {
    TraversalCancelled = true;
    if (_Entries != null)
      foreach (DirStats e in _Entries) e.CancelTraversal ();
  }

  void RequestInfo () {
    if (!recursiveInfo.InProgress && !recursiveInfo.Complete) {
      WaitCallback cb = new WaitCallback(DirSizeCallback);
      ThreadPool.QueueUserWorkItem(cb);
    }
  }

  void DirSizeCallback (Object stateInfo)
  {
    TraversalCancelled = false;
    DirCache.Traverse(FullName, ref TraversalCancelled);
  }

}






public class DirAction
{
  public Action Type;
  public string Path;
  public double Height;

  public static DirAction None = GetNone ();

  public static DirAction GetNone ()
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

using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Mono.Unix;
using Cairo;


/**
  DirStats is the drawing model class for FileZoo.

  Every directory and file is presented by a DirStats instance.

  A DirStats draws itself and its children on a Cairo Context
  and and provides the Click-method to find out what to do when clicked.
*/
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

  public static Color unfinishedColor = new Color (0.5, 0, 1);
  public static Color BG = new Color (1,1,1);


  // Style for the DirStats
  public double BoxWidth = 0.1;

  public double MinFontSize = 0.5;
  public double MaxFontSize = 12.0;

  // Public state of the DirStats
  public string Name;
  public string FullName;
  public long Length;
  public string Suffix;
  public bool IsDirectory = false;
  public DateTime LastModified;

  UnixFileSystemInfo Info;

  FileAccessPermissions Permissions;
  FileTypes FileType;

  // How to sort directories
  public IComparer<DirStats> Comparer;
  public SortingDirection SortDirection = SortingDirection.Ascending;

  // How to scale file sizes
  public IMeasurer Measurer;
  public IZoomer Zoomer;

  // Traversal progress flag, true if the
  // recursive traversal of the DirStats is completed.
  /** FAST */
  public virtual bool Complete
  { get { return recursiveInfo.Complete; } }

  // State variables for computing the recursive traversal of the DirStats
  public Dir recursiveInfo;

  // Frame rate profiler to help with maintaining the frame rate when doing
  // lots of in-frame work.
  static Profiler FrameProfiler = new Profiler ();
  double MaxTimePerFrame = 50.0;

  // Drawing state variables
  double Scale;
  double Height;

  // DirStats objects for the children of a directory DirStats
  DirStats[] _Entries = null;
  /** BLOCKING */
  DirStats[] Entries {
    get {
      if (_Entries == null) {
        try {
          UnixFileSystemInfo[] files = Helpers.EntriesMaybe (FullName);
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

  /** BLOCKING */
  public static DirStats Get (UnixFileSystemInfo f) {
    DirStats d;
    if (Helpers.IsDir(f))
      d = new DirStats(f, DirCache.GetCacheEntry(f.FullName));
    else
      d = new DirStats(f, new Dir());
    return d;
  }

  /** BLOCKING */
  protected DirStats (UnixFileSystemInfo f, Dir r)
  {
    UpdateInfo (f, r);
    Comparer = new NameComparer ();
    Scale = Height = 1.0;
    string[] split = Name.Split('.');
    Suffix = (Name[0] == '.') ? "" : split[split.Length-1];
  }

  /** BLOCKING */
  void UpdateInfo (UnixFileSystemInfo f, Dir r) {
    recursiveInfo = r;
    Info = f;
    Name = f.Name;
    FullName = f.FullName;
    FileType = Helpers.FileType(f);
    Permissions = Helpers.FilePermissions(f);
    Length = Helpers.FileSize(f);
    LastModified = Helpers.LastModified(f);
    IsDirectory = Helpers.IsDir(f);
    if (!IsDirectory) {
      recursiveInfo.InProgress = false;
      recursiveInfo.Complete = true;
      recursiveInfo.TotalSize = Length;
      recursiveInfo.TotalCount = 1;
    }
  }


  /* Subtitles */

  /** FAST */
  public string GetSubTitle ()
  {
    if (IsDirectory) {
      string extras = "";
      extras += String.Format("{0} files", GetRecursiveCount().ToString("N0"));
      extras += String.Format(", {0} total", Helpers.FormatSI(GetRecursiveSize(), "B"));
      return extras;
    } else {
      return String.Format("{0}", Helpers.FormatSI(Length, "B"));
    }
  }


  /* Drawing helpers */

  /** FAST */
  public double GetScaledHeight ()
  {
    return Height * Scale;
  }

  /** FAST */
  double GetFontSize (double h)
  {
    double fs;
    fs = h * (IsDirectory ? 0.4 : 0.5);
    return Math.Max(MinFontSize, QuantizeFontSize(Math.Min(MaxFontSize, fs)));
  }

  /** FAST */
  double QuantizeFontSize (double fs) { return Math.Floor(fs); }

  /** FAST */
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

  /** BLOCKING */
  public void Sort ()
  {
    if (IsDirectory) {
      Array.Sort (Entries, Comparer);
      if (SortDirection == SortingDirection.Descending)
        Array.Reverse (Entries);
    }
  }

  /** BLOCKING */
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


  /* Drawing */

  /** FAST */
  public bool IsVisible (Context cr, double targetTop, double targetHeight)
  {
    double h = cr.Matrix.Yy * GetScaledHeight ();
    double y = cr.Matrix.Y0 - targetTop;
    // rectangle doesn't intersect any quarter-pixel midpoints
    if (h < 0.5 && (Math.Floor(y*4) == Math.Floor((y+h)*4)))
      return false;
    return ((y < targetHeight) && ((y+h) > 0.0));
  }

  /** BLOCKING */
  /**
    Draw draws the DirStats instance to the given Cairo Context, clipping it
    to the device-space Rectangle targetBox.

    If firstFrame is set, Draw skips drawing children beyond depth 1 to be able
    to draw the first frame of a directory fast.

    If the DirStats instance's on-screen presence is small, it won't draw its children.
    If the DirStats is hidden, it won't draw itself or its children.

    Draw uses the targetBox rectangle to determine its visibility (i.e. does the
    DirStats instance fall outside the draw area and what size to clip the drawn
    rectangles.)

    @param cr The Cairo.Context to draw on.
    @param targetBox The device-space clip box for determining object visibility.
    @param firstFrame Whether this frame should be drawn as fast as possible.
    @returns The count of DirStats instances drawn.
  */
  public uint Draw (Context cr, Rectangle targetBox, bool firstFrame) {
    return Draw (cr, targetBox, firstFrame, 0);
  }
  public uint Draw (Context cr, Rectangle targetBox, bool firstFrame, uint depth)
  {
    if (depth == 0) FrameProfiler.Restart ();
    if (!IsVisible(cr, targetBox.Y, targetBox.Height)) {
      return 0;
    }
    double h = GetScaledHeight ();
    uint c = 1;
    cr.Save ();
      cr.Scale (1, h);
      Helpers.DrawRectangle(cr, -0.01*BoxWidth, 0.0, BoxWidth*1.02, 1.02, targetBox);
      cr.Color = BG;
      cr.Fill ();
      Color co = GetColor (FileType, Permissions);
      cr.Color = co;
      if (!recursiveInfo.Complete) cr.Color = unfinishedColor;
      if (depth > 0) {
        Helpers.DrawRectangle (cr, 0.0, 0.02, BoxWidth, 0.98, targetBox);
        cr.Fill ();
        if (IsDirectory) {
          Helpers.DrawRectangle (cr, BoxWidth*0.1, 0.48, BoxWidth*0.9, 0.48, targetBox);
          cr.Color = BG;
          cr.Fill ();
          cr.Color = co;
        }
      }
      if (cr.Matrix.Yy > 1) DrawTitle (cr, depth);
      if (IsDirectory) {
        bool childrenVisible = cr.Matrix.Yy > 2;
        bool shouldDrawChildren = !firstFrame && childrenVisible;
        if (depth == 0) shouldDrawChildren = true;
        if (shouldDrawChildren) {
          RequestInfo();
          c += DrawChildren(cr, targetBox, firstFrame, depth);
        }
      }
    cr.Restore ();
    if (depth == 0) FrameProfiler.Stop ();
    return c;
  }

  /** FAST */
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

  /** BLOCKING */
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

  /** BLOCKING */
  uint DrawChildren (Context cr, Rectangle targetBox, bool firstFrame, uint depth)
  {
    if (FrameProfiler.Watch.ElapsedMilliseconds > MaxTimePerFrame && _Entries == null) return 0;
    cr.Save ();
      ChildTransform (cr, depth);
      uint c = 0;
      foreach (DirStats d in Entries) {
        UpdateChild (d);
        c += d.Draw (cr, targetBox, firstFrame, depth+1);
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    return c;
  }

  /** BLOCKING */
  void UpdateChild (DirStats d)
  {
    if (FrameProfiler.Watch.ElapsedMilliseconds > MaxTimePerFrame) return;
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

  /** BLOCKING */
  public DirAction Click
  (Context cr, Rectangle target, double mouseX, double mouseY)
  { return Click (cr, target, mouseX, mouseY, 0); }
  public DirAction Click
  (Context cr, Rectangle target, double mouseX, double mouseY, uint depth)
  {
    if (!IsVisible(cr, target.Y, target.Height)) {
      return DirAction.None;
    }
    double h = GetScaledHeight ();
    DirAction retval = DirAction.None;
    double advance = 0.0;
    cr.Save ();
      cr.Scale (1, h);
      if (IsDirectory && (cr.Matrix.Yy > 2))
        retval = ClickChildren (cr, target, mouseX, mouseY, depth);
      if (
        retval == DirAction.None ||
        (retval.Type == DirAction.Action.ZoomIn && cr.Matrix.Yy < 10)
      ) {
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
          if (ys < 16)
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

  /** BLOCKING */
  DirAction ClickChildren
  (Context cr, Rectangle target, double mouseX, double mouseY, uint depth)
  {
    DirAction retval = DirAction.None;
    cr.Save ();
      ChildTransform (cr, depth);
      foreach (DirStats d in Entries) {
        retval = d.Click (cr, target, mouseX, mouseY, depth+1);
        if (retval != DirAction.None) break;
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    return retval;
  }




  /* Directory traversal */

  /** FAST */
  public virtual double GetRecursiveSize () {
    return recursiveInfo.TotalSize;
  }

  /** FAST */
  public virtual double GetRecursiveCount () {
    return recursiveInfo.TotalCount;
  }

  /** FAST */
  public void CancelTraversal () {
    DirCache.CancelTraversal ();
  }

  /** ASYNC */
  void RequestInfo () {
    if (!recursiveInfo.InProgress && !recursiveInfo.Complete) {
      DirCache.RequestTraversal (FullName);
    }
  }

}


public class DirAction
{
  public Action Type;
  public string Path;
  public double Height;

  /** FAST */
  public static DirAction None = GetNone ();

  /** FAST */
  public static DirAction GetNone ()
  { return new DirAction (Action.None, "", 0.0); }

  /** FAST */
  public static DirAction Open (string path)
  { return new DirAction (Action.Open, path, 0.0); }

  /** FAST */
  public static DirAction Navigate (string path)
  { return new DirAction (Action.Navigate, path, 0.0); }

  /** FAST */
  public static DirAction ZoomIn (double h)
  { return new DirAction (Action.ZoomIn, "", h); }

  /** FAST */
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

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
  public static Color DirectoryColor = new Color (0,0,1);
  public static Color BlockDeviceColor = new Color (0.75,0.5,0);
  public static Color CharacterDeviceColor = new Color (0.5,0.25,0);
  public static Color FifoColor = new Color (0.75,0,0.22);
  public static Color SocketColor = new Color (0.75,0,0.82);
  public static Color SymlinkColor = new Color (0,0.75,0.93);
  public static Color ExecutableColor = new Color (0,0.75,0);
  public static Color RegularFileColor = new Color (0,0,0);
  public static Color ParentDirectoryColor = new Color (0,0,1);

  public static Color UnfinishedDirectoryColor = new Color (0.5, 0, 1);
  public static Color BackgroundColor = new Color (1,1,1);

  static Dictionary<string, string> _Prefixes = null;
  public static Dictionary<string, string> Prefixes
  { get {
    if (_Prefixes == null) {
      _Prefixes = new Dictionary<string, string> ();
      _Prefixes[".."] = "⇱";
      _Prefixes["/dev"] = "⚠";
      _Prefixes["/etc"] = "✦";
      _Prefixes["/boot"] = "◉";
      _Prefixes["/proc"] = _Prefixes["/sys"] = "◎";
      _Prefixes["/usr"] = _Prefixes["/usr/local"] = "⬢";
      _Prefixes["/usr/X11R6"] = "▤";
      _Prefixes["/usr/src"] = _Prefixes["/usr/local/src"] = "⚒";
      _Prefixes["/bin"] = _Prefixes["/usr/bin"] = _Prefixes["/usr/local/bin"] = "⌬";
      _Prefixes["/sbin"] = _Prefixes["/usr/sbin"] = _Prefixes["/usr/local/sbin"] = "⏣";
      _Prefixes["/lib"] = _Prefixes["/usr/lib"] = _Prefixes["/usr/local/lib"] =
      _Prefixes["/lib32"] = _Prefixes["/usr/lib32"] = _Prefixes["/usr/local/lib32"] = "⬡";
      _Prefixes["/include"] = _Prefixes["/usr/include"] = _Prefixes["/usr/local/include"] = "○";
      _Prefixes["/tmp"] = "⌚";
      _Prefixes["/home"] = "⌂";
      _Prefixes["/root"] = "♔";
      _Prefixes["/usr/share"] = _Prefixes["/usr/local/share"] = "✧";
      _Prefixes["/var"] = "⚡";
      _Prefixes["/usr/games"] = _Prefixes["/usr/local/games"] = "☺";
      _Prefixes[Helpers.HomeDir] = "♜";
      _Prefixes[Helpers.HomeDir+"/Trash"] =
      _Prefixes[Helpers.HomeDir+"/.Trash"] = "♻";
      _Prefixes[Helpers.HomeDir+"/downloads"] =
      _Prefixes[Helpers.HomeDir+"/Downloads"] = "⬇";
      _Prefixes[Helpers.HomeDir+"/music"] =
      _Prefixes[Helpers.HomeDir+"/Music"] = "♬";
      _Prefixes[Helpers.HomeDir+"/photos"] =
      _Prefixes[Helpers.HomeDir+"/Photos"] =
      _Prefixes[Helpers.HomeDir+"/pictures"] =
      _Prefixes[Helpers.HomeDir+"/Pictures"] = "⚜";
      _Prefixes[Helpers.HomeDir+"/public_html"] = "⚓";
    }
    return _Prefixes;
  } }

  // Style for the DirStats
  public double BoxWidth = 0.1;

  public double MinFontSize = 0.5;
  public double MaxFontSize = 12.0;

  // Public state of the DirStats
  public string Name;
  public string LCName;
  public string FullName;
  public long Length;
  public string Suffix;
  public bool IsDirectory = false;
  public string Owner;
  public string Group;
  public DateTime LastModified;

  FileAccessPermissions Permissions;
  FileTypes FileType;

  // How to sort directories
  public IComparer<DirStats> Comparer = new NullComparer ();
  public SortingDirection SortDirection = SortingDirection.Ascending;

  // How to scale file sizes
  public IMeasurer Measurer = new NullMeasurer ();

  // Traversal progress flag, true if the
  // recursive traversal of the DirStats is completed.
  /** FAST */
  public virtual bool Complete
  { get { return !Measurer.DependsOnTotals || recursiveInfo.Complete; } }

  // Is the layout of this node and its subtree complete or was it interrupted?
  public bool LayoutComplete = false;

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
  /**
    The Entries getter gets the Entries for the files in this DirStats' directory.
    This should be made ASYNC.
    */
  DirStats[] Entries {
    get {
      lock (this) {
        if (_Entries == null) {
          Profiler p = new Profiler ("ENTRIES");
          bool isRoot = (FullName == Helpers.RootDir);
          DirStats[] e;
          try {
            UnixFileSystemInfo[] files = Helpers.EntriesMaybe (FullName);
            e = new DirStats[files.Length + (isRoot ? 0 : 1)];
            for (int i=0; i<files.Length; i++)
              e[i] = new DirStats (files[i]);
          } catch (System.UnauthorizedAccessException) {
            e = new DirStats[isRoot ? 0 : 1];
          }
          if (!isRoot) {
            DirStats parent = new DirStats (new UnixDirectoryInfo(Helpers.Dirname(FullName)));
            string pr = " ";
/*            if (Prefixes.ContainsKey(parent.FullName))
              pr = " " + Prefixes[parent.FullName] + " ";*/
            parent.Name = Prefixes[".."]+pr+parent.FullName;
            parent.LCName = "..";
            e[e.Length-1] = parent;
          }
          _Entries = e;
          p.Time ("Got {0} Entries", _Entries.Length);
        }
      }
      return _Entries;
    }
  }



  /* Constructor */

  /** BLOCKING */
  /**
    The DirStats constructor takes the UnixFileSystemInfo that it represents
    as its argument. The DirStats can then be used to draw the directory tree
    starting from the UnixFileSystemInfo's path.
    */
  public DirStats (UnixFileSystemInfo f)
  {
    Dir d;
    d = Helpers.IsDir(f) ? DirCache.GetCacheEntry(f.FullName) : new Dir();
    UpdateInfo (f, d);
    Scale = Height = 0.0;
    string[] split = Name.Split('.');
    Suffix = (Name[0] == '.') ? "" : split[split.Length-1];
  }

  /** BLOCKING */
  void UpdateInfo (UnixFileSystemInfo f, Dir r) {
    recursiveInfo = r;
    Name = f.Name;
    LCName = f.Name.ToLower ();
    FullName = f.FullName;
    if (Prefixes.ContainsKey(FullName))
      Name = Prefixes[FullName] + " " + Name;
    FileType = Helpers.FileType(f);
    Permissions = Helpers.FilePermissions(f);
    Length = Helpers.FileSize(f);
    LastModified = Helpers.LastModified(f);
    IsDirectory = Helpers.IsDir(f);
    Owner = Helpers.OwnerName(f);
    Group = Helpers.GroupName(f);
    if (!IsDirectory) {
      recursiveInfo.InProgress = false;
      recursiveInfo.Complete = true;
      recursiveInfo.TotalSize = Length;
      recursiveInfo.TotalCount = 1;
    }
  }


  /* Subtitles */

  /** FAST */
  /**
    Gets the information subtitle for the DirStats instance.
    For directories, the subtitle contains the size of the subtree in files and
    bytes.
    For files, the subtitle contains the size of the file.
    */
  public string GetSubTitle ()
  {
    if (IsDirectory) {
      string extras = "";
      if (
        !recursiveInfo.Complete &&
        !Measurer.DependsOnTotals &&
        GetRecursiveCount() == 0 &&
        GetRecursiveSize() == 0
      ) {
        if (_Entries != null) {
          // entries sans parent dir
          extras += String.Format("{0} entries", (_Entries.Length-1).ToString("N0"));
        }
      } else {
        extras += String.Format("{0} files", GetRecursiveCount().ToString("N0"));
        extras += String.Format(", {0} total", Helpers.FormatSI(GetRecursiveSize(), "B"));
      }
      return extras;
    } else {
      return String.Format("{0}", Helpers.FormatSI(Length, "B"));
    }
  }

  /** FAST */
  /**
    Returns a "rwxr-x--- owner group" string for the DirStats.
    */
  public string PermissionString ()
  {
    string pstring = PermString (
      FileAccessPermissions.UserRead,
      FileAccessPermissions.UserWrite,
      FileAccessPermissions.UserExecute
    ) + PermString (
      FileAccessPermissions.GroupRead,
      FileAccessPermissions.GroupWrite,
      FileAccessPermissions.GroupExecute
    ) + PermString (
      FileAccessPermissions.OtherRead,
      FileAccessPermissions.OtherWrite,
      FileAccessPermissions.OtherExecute
    );
    return String.Format ("{0} {1} {2}", pstring, Owner, Group);
  }

  /** FAST */
  /**
    @returns The "rwx"-string for the given permission enums.
    */
  string PermString (FileAccessPermissions r, FileAccessPermissions w, FileAccessPermissions x)
  {
    char[] chars = {'-', '-', '-'};
    if ((Permissions & r) == r) chars[0] = 'r';
    if ((Permissions & w) == w) chars[1] = 'w';
    if ((Permissions & x) == x) chars[2] = 'x';
    return new string(chars);
  }

  /** FAST */
  /**
    @returns The total size in bytes for the filesystem subtree of the DirStats.
    */
  public virtual double GetRecursiveSize () {
    return recursiveInfo.TotalSize;
  }

  /** FAST */
  /**
    @returns The total file count for the filesystem subtree of the DirStats.
    */
  public virtual double GetRecursiveCount () {
    return recursiveInfo.TotalCount;
  }


  /* Drawing helpers */

  /** FAST */
  /**
    Gets the scaled height for the DirStats.
    The scaled height is a float between 0 and 1 normalized
    so that the heights of the Entries of a DirStats sum to 1.
    */
  public double GetScaledHeight ()
  {
    return Height * Scale;
  }

  /** FAST */
  /**
    Gets the font size for the given device-space height of the DirStats.
    */
  double GetFontSize (double h)
  {
    return h * (IsDirectory ? (LCName == ".." ? 0.45 : 0.4) : 0.5);
  }

  /** FAST */
  /**
    Get the Cairo Color for the given filetype and permissions (permissions used
    to color executables green.)
    */
  public static Color GetColor (FileTypes filetype, FileAccessPermissions perm)
  {
    switch (filetype) {
      case FileTypes.Directory: return DirectoryColor;
      case FileTypes.BlockDevice: return BlockDeviceColor;
      case FileTypes.CharacterDevice: return CharacterDeviceColor;
      case FileTypes.Fifo: return FifoColor;
      case FileTypes.Socket: return SocketColor;
      case FileTypes.SymbolicLink: return SymlinkColor;
    }
    if ((perm & FileAccessPermissions.UserExecute) != 0)
      return ExecutableColor;
    return RegularFileColor;
  }


  /* Relayout */

  /** BLOCKING */
  /**
    Sorts the Entries of the DirStats instance.
    */
  public void Sort ()
  {
    Profiler p = new Profiler ("SORT");
    if (!IsDirectory) return;
    Array.Sort (Entries, Comparer);
    if (SortDirection == SortingDirection.Descending)
      Array.Reverse (Entries);
    MoveParentToFront() ;
    p.Time("Sorted {0} DirStats", Entries.Length);
  }
  void MoveParentToFront ()
  {
    if (FullName == Helpers.RootDir) return;
    int idx;
    DirStats[] e = Entries;
    DirStats tmp;
    for (idx=0; idx < e.Length; idx++)
      if (e[idx].LCName == "..") break;
    for (int i=idx; i > 0; i--) {
      tmp = e[i-1];
      e[i-1] = e[i];
      e[i] = tmp;
    }
  }

  /** BLOCKING */
  /**
    Relayouts the Entries of the DirStats instance.
    Sets the Height and Scale for each of the entries according to Measurer.
    */
  public void Relayout ()
  {
    if (!IsDirectory) return;
    Profiler p = new Profiler ("RELAYOUT");
    double totalHeight = 0.0;
    DirStats parent = null;
    foreach (DirStats f in Entries) {
      f.Height = Measurer.Measure(f);
      totalHeight += f.Height;
      if (f.LCName == "..") parent = f;
    }
    if (parent != null) {
      if (Entries.Length > 1) {
        totalHeight -= parent.Height;
        parent.Height = totalHeight / 32.0;
        totalHeight += parent.Height;
      } else {
        totalHeight += totalHeight * 32.0;
      }
    }
    double scale = 1.0 / totalHeight;
    foreach (DirStats f in Entries) {
      f.Scale = scale;
    }
    p.Time("Layouted {0} DirStats", Entries.Length);
  }


  /* Drawing */

  /** FAST */
  /**
    Checks if a 1 unit high object (DirStats is 1 unit high) is clipped by the
    target area and whether it falls between quarter pixels.
    If either is true, returns false, otherwise reckons the DirStats would be
    visible and returns true.
    */
  bool IsVisible (Context cr, double targetTop, double targetHeight)
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
    if (depth == 0) {
      FrameProfiler.Restart ();
      Height = Scale = 1.0;
    }
    if (!IsVisible(cr, targetBox.Y, targetBox.Height)) {
      return 0;
    }
    double h = GetScaledHeight ();
    uint c = 1;
    cr.Save ();
      cr.Scale (1, h);
      Helpers.DrawRectangle(cr, -0.01*BoxWidth, 0.0, BoxWidth*1.02, 1.02, targetBox);
      cr.Color = BackgroundColor;
      cr.Fill ();
      Color co = GetColor (FileType, Permissions);
      cr.Color = co;
      if (!recursiveInfo.Complete && Measurer.DependsOnTotals)
        cr.Color = UnfinishedDirectoryColor;
      if (LCName == "..")
        cr.Color = ParentDirectoryColor;
      if (depth > 0) {
        if (LCName == "..")
          Helpers.DrawRectangle (cr, 0.0, 0.02, BoxWidth, 0.90, targetBox);
        else
          Helpers.DrawRectangle (cr, 0.0, 0.02, BoxWidth, 0.98, targetBox);
        cr.Fill ();
      }
      if (cr.Matrix.Yy > 0.5 || depth < 2) DrawTitle (cr, depth);
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
  /**
    Sets up child area transform for the DirStats. Depth 0 child area is drawn
    at full height to waste less screen space.
    */
  void ChildTransform (Context cr, uint depth)
  {
    if (depth == 0) {
      cr.Translate (0.0, 0.08);
      cr.Scale (1.0, 0.92);
    } else if (LCName == "..") {
      cr.Translate (0.1*BoxWidth, 0.50);
      cr.Scale (0.9, 0.40);
    } else {
      cr.Translate (0.1*BoxWidth, 0.48);
      cr.Scale (0.9, 0.48);
    }
  }

  /** BLOCKING */
  /**
    Draws the title for the DirStats.
    Usually draws the filename bigger and the subtitle a bit smaller.

    If the DirStats is small, draws the subtitle at the same size and same time
    as the filename for speed.

    If the DirStats is very small, draws a rectangle instead of text for speed.
    */
  void DrawTitle (Context cr, uint depth)
  {
    double h = cr.Matrix.Yy;
    double rfs = GetFontSize(h);
    double fs = Helpers.Clamp(rfs, MinFontSize, MaxFontSize);
    cr.Save ();
      cr.Translate(BoxWidth * 1.1, 0.02);
      double x = cr.Matrix.X0;
      double y = cr.Matrix.Y0;
      cr.IdentityMatrix ();
      cr.Translate (x, y);
      cr.NewPath ();
      if (fs > 4) {
        if (depth == 0)
          cr.Translate (0, -fs*0.5);
        cr.MoveTo (0, -fs*0.2);
        Helpers.DrawText (cr, fs, Name);
        cr.RelMoveTo(0, fs*0.35);
        Helpers.DrawText (cr, fs * 0.7, "  " + GetSubTitle ());

        double sfs = Helpers.Clamp(
          IsDirectory ? rfs*0.05 : rfs*0.2,
          MinFontSize, MaxFontSize*0.6);
        if (sfs > 1) {
          cr.MoveTo (0, fs*1.4);
          Helpers.DrawText (cr, sfs, "Modified " + LastModified.ToString());
          cr.MoveTo (0, fs*1.4+sfs*1.4);
          Helpers.DrawText (cr, sfs, PermissionString ());
        }
      } else if (fs > 1) {
        cr.MoveTo (0, fs*0.1);
        Helpers.DrawText (cr, fs, Name + "  " + GetSubTitle ());
      } else {
        cr.Rectangle (0.0, 0.0, fs / 2 * (Name.Length+15), fs/3);
        cr.Fill ();
      }
    cr.Restore ();
  }

  /** BLOCKING */
  /**
    Draws the children entries of this DirStats.
    Bails out if no children are yet created and frame has run out of time.

    Sets up the child area transform, updates each child's layout and draws it.

    Sets LayoutComplete to true if all visible children have finished their
    layout.

    @returns The total amount of subtree DirStats drawn.
    */
  uint DrawChildren (Context cr, Rectangle targetBox, bool firstFrame, uint depth)
  {
    bool layoutComplete = true;
    LayoutComplete = false;
    if (FrameProfiler.Watch.ElapsedMilliseconds > MaxTimePerFrame
        && _Entries == null
    ) {
      return 0;
    }
    cr.Save ();
      ChildTransform (cr, depth);
      uint c = 0;
      foreach (DirStats d in Entries) {
        layoutComplete &= UpdateChild (d);
        c += d.Draw (cr, targetBox, firstFrame, depth+1);
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    LayoutComplete = layoutComplete;
    return c;
  }

  /** BLOCKING */
  /**
    Update the given DirStats' layout to match this.
    Bails out if the current frame has ran out of time.

    @returns Whether the layout completed successfully.
    */
  bool UpdateChild (DirStats d)
  {
    bool needRelayout = false;
    bool needSort = false;
    if (d.Comparer != Comparer || d.SortDirection != SortDirection) {
      needSort = true;
      needRelayout = true;
    }
    if (d.Measurer != Measurer) needRelayout = true;
    if (Measurer.DependsOnTotals && !d.recursiveInfo.Complete)
      needRelayout = true;
    if (FrameProfiler.Watch.ElapsedMilliseconds > MaxTimePerFrame)
      return !(needSort || needRelayout);
    d.Comparer = Comparer;
    d.SortDirection = SortDirection;
    d.Measurer = Measurer;
    if (needSort)
      d.Sort ();
    if (needRelayout)
      d.Relayout ();
    return true;
  }


  /* Click handler */

  /** BLOCKING */
  /**
    Click handles the click events directed at the DirStats.
    It takes the Cairo Context cr, the clip Rectangle target and the mouse
    device-space coordinates as its arguments, and returns a DirAction object.

    If the mouse coordinates lie outside the DirStats instance, returns
    DirAction.None.
    If the click is on a small item, returns a DirAction of the ZoomIn type.
    If the click is on a larget item, returns a Navigation DirAction for
    directories and a Open DirAction for files.

    @param cr Cairo.Context to query.
    @param target Target clip rectangle. See Draw for a better explanation.
    @param mouseX The X coordinate of the mouse pointer, X grows right from left.
    @param mouseY The Y coordinate of the mouse pointer, Y grows down from top.
    @returns The DirAction to take.
    */
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
        (retval.Type == DirAction.Action.ZoomIn && cr.Matrix.Yy < 40)
      ) {
        cr.NewPath ();
        double rfs = GetFontSize(h);
        double fs = Math.Max(MinFontSize, Math.Min(MaxFontSize, rfs));
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
  /**
    Passes the click check to the children of this DirStats.
    Sets up the child area transform and calls Click on each child in Entries.
    If a child is hit, returns the child's action.
    Otherwise returns DirAction.None.
    */
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


  /** BLOCKING */
  /**
    Finds the deepest DirStats that covers the full screen.
    Used to do zoom navigation.
    */
  public string FindCovering (Context cr, Rectangle target)
  {
    return FullName;
  }



  /* Directory traversal */

  /** BLOCKING */
  /**
    Cancels traversal of the DirStats recursive information.
    */
  public void CancelTraversal () {
    DirCache.CancelTraversal ();
  }

  /** ASYNC */
  /**
    Files a traversal request with DirCache if one isn't already in progress or
    completed.
    */
  void RequestInfo () {
    if (LCName == "..") return;
    if (Measurer.DependsOnTotals) {
      if (!recursiveInfo.InProgress && !recursiveInfo.Complete) {
        DirCache.RequestTraversal (FullName);
      }
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

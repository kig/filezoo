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
  public bool TraversalCancelled = false;

  // State variables for computing the recursive traversal of the DirStats
  public bool recursiveSizeComputed = false;
  public Dir recursiveInfo;

  static Dictionary<string,Dir> DirCache = new Dictionary<string,Dir> (200000);

  public class Dir {
    public string Path;
    public double TotalCount;
    public double TotalSize;
    public bool Complete;
    public bool InProgress;
    public int Missing;
    public Dir (string path) {
      Path = path;
      TotalCount = 1.0;
      TotalSize = 0.0;
      Complete = false;
      InProgress = false;
    }

    public Dir Finish () {
      lock (this) {
        Complete = true;
        InProgress = false;
      }
      return this;
    }
    public Dir Fail () { return Finish (); }
    public Dir Cancel () {
      lock (this) {
        Complete = false;
        InProgress = false;
      }
      return this;
    }
    public string ParentDir () {
      return System.IO.Path.GetDirectoryName(Path);
    }
    public void AddCount (double c) {
      TotalCount += c;
      string pdir = ParentDir();
      if (pdir == "") return;
      lock (DirCache)
        if (DirCache.ContainsKey(pdir)) DirCache[pdir].AddCount(c);
    }
    public void AddSize (double c) {
      TotalSize += c;
      string pdir = ParentDir();
      if (pdir == "") return;
      lock (DirCache)
        if (DirCache.ContainsKey(pdir)) DirCache[pdir].AddSize(c);
    }
    public void AddChildData (Dir c) {
      TotalSize += c.TotalSize;
      TotalCount += c.TotalCount;
      string pdir = ParentDir();
      if (pdir == "") return;
      lock (DirCache)
        if (DirCache.ContainsKey(pdir)) DirCache[pdir].AddChildData(c);
    }
    public void PropagateComplete () {
      string pdir = ParentDir();
      if (pdir == "") return;
      lock (DirCache)
        if (DirCache.ContainsKey(pdir)) DirCache[pdir].ChildFinished (this);
    }
    public void ChildFinished (Dir d) {
      lock (this) {
        Missing--;
if (Path == "/home/kig/downloads") {
    Console.WriteLine("{1}", Missing, d.Path);
}
        if (Missing <= 0) {
          Complete = true;
          InProgress = false;
        }
        if (Missing == 0) {
          string pdir = ParentDir();
          if (pdir == "") return;
          lock (DirCache)
            if (DirCache.ContainsKey(pdir)) DirCache[pdir].ChildFinished (this);
        }
      }
    }
  }

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

  static Dir GetCacheEntry (string name) {
    Dir dc;
    lock (DirCache) {
      if (DirCache.ContainsKey(name)) {
        dc = DirCache[name];
      } else {
        dc = new Dir (name);
        DirCache[name] = dc;
      }
    }
    return dc;
  }

  public static DirStats Get (UnixFileSystemInfo f) {
    DirStats d = new DirStats (f);
    if (d.IsDirectory)
      d.SetRecursiveInfo(GetCacheEntry(d.FullName));
    else
      d.SetRecursiveInfo(new Dir(d.FullName));
    return d;
  }

  protected DirStats (UnixFileSystemInfo f)
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

  public void SetRecursiveInfo (Dir dc)
  {
    recursiveInfo = dc;
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
    double fs;
    fs = h*0.4;
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

  public uint Draw (Context cr, double targetTop, double targetHeight, bool complexSubTitle, uint depth)
  {
    if (!IsVisible(cr, targetTop, targetHeight)) {
      return 0;
    }
    double h = GetScaledHeight ();
    uint c = 1;
    cr.Save ();
      cr.Scale (1, h);
      cr.Rectangle (-0.01*BoxWidth, 0.0, BoxWidth*1.02, 1.01);
      cr.Color = new Color (1,1,1);
      cr.Fill ();
      Color co = GetColor (Info.FileType, Info.FileAccessPermissions);
      cr.Color = co;
      cr.Rectangle (0.0, 0.02, BoxWidth, 0.98);
      cr.Fill ();
      if (depth > 0 && cr.Matrix.Yy > 1) DrawTitle (cr, complexSubTitle);
      if (IsDirectory && (depth == 0 || (cr.Matrix.Yy > 4)))
        c += DrawChildren(cr, targetTop, targetHeight, complexSubTitle, depth);
    cr.Restore ();
    return c;
  }

  uint DrawChildren (Context cr, double targetTop, double targetHeight, bool complexSubTitle, uint depth)
  {
    cr.Save ();
      if (depth > 0) {
        cr.Translate (0.1*BoxWidth, 0.48);
        cr.Scale (0.9, 0.48);
      }
      uint c = 0;
      foreach (DirStats d in Entries) {
        UpdateChild (d);
        c += d.Draw (cr, targetTop, targetHeight, complexSubTitle, depth+1);
        double h = d.GetScaledHeight();
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    return c;
  }

  void DrawTitle (Context cr, bool complexSubTitle)
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
      cr.MoveTo (0, -fs*0.2);
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
      if (retval == DirAction.None) {
        cr.NewPath ();
        double fs = GetFontSize(cr.Matrix.Yy);
        if (fs < 10) {
          advance += BoxWidth;
        } else {
          advance += Helpers.GetTextExtents (cr, fs, Name).XAdvance;
          advance += Helpers.GetTextExtents (cr, fs*0.7, "  " + GetSubTitle ()).XAdvance;
        }
        cr.Rectangle (0.0, 0.0, BoxWidth * 1.1 + advance, 1.0);
        cr.IdentityMatrix ();
        if (cr.InFill(mouseX,mouseY)) {
          if (fs < 10)
            retval = DirAction.ZoomIn(cr.Matrix.Yy / 20);
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
      if (depth > 0) {
        cr.Translate (0.1*BoxWidth, 0.48);
        cr.Scale (0.9, 0.48);
      }
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
    if (!recursiveInfo.InProgress && !recursiveInfo.Complete) {
      if (IsDirectory) {
        WaitCallback cb = new WaitCallback(DirSizeCallback);
        ThreadPool.QueueUserWorkItem(cb);
      } else {
        recursiveInfo.InProgress = false;
        recursiveInfo.Complete = true;
        recursiveInfo.TotalSize = Length;
        recursiveInfo.TotalCount = 1.0;
      }
    }
    return recursiveInfo.TotalSize;
  }

  public virtual double GetRecursiveCount ()
  {
    if (!recursiveSizeComputed)
      GetRecursiveSize ();
    return recursiveInfo.TotalCount;
  }

  void DirSizeCallback (Object stateInfo)
  {
    TraversalCancelled = false;
    DirSize(FullName);
  }





  Dir DirSize (string dirname)
  {
    Dir dc = GetCacheEntry(dirname);
    if (TraversalCancelled) return dc;
    UnixFileSystemInfo[] files;
    lock (dc) {
      if (dc.Complete || dc.InProgress) return dc;
      UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
      try { files = di.GetFileSystemEntries (); }
      catch (System.UnauthorizedAccessException) { return dc; }
      dc.InProgress = true;
      dc.Complete = false;
      dc.Missing = files.Length;
    }
    double count = 0.0;
    double size = 0.0;
    foreach (UnixFileSystemInfo f in files) {
      if (TraversalCancelled) { return dc.Cancel (); }
      count += 1.0;
      bool isDir = false;
      try { isDir = f.IsDirectory; } catch (System.InvalidOperationException) {}
      if (!isDir) {
        try { size += f.Length; }
        catch (System.InvalidOperationException) {}
        dc.Missing--;
      }
    }
    dc.AddCount (count);
    dc.AddSize (size);
    foreach (UnixFileSystemInfo f in files) {
      if (TraversalCancelled) { return dc.Cancel (); }
      bool isDir = false;
      try { isDir = f.IsDirectory; } catch (System.InvalidOperationException) {}
      if (isDir) DirSize(f.FullName);
    }
    lock (dc) {
      if (!dc.Complete) {
        dc.Complete = (dc.Missing <= 0);
        dc.InProgress = !dc.Complete;
        if (dc.Complete) dc.PropagateComplete ();
      }
    }
    return dc;
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

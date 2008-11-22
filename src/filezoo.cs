using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Gtk;
using Cairo;
using Mono.Unix;

class Filezoo : DrawingArea
{
  // current directory style
  public double BreadcrumbFontSize = 12;
  public double BreadcrumbMarginTop = 6;
  public double BreadcrumbMarginLeft = 12;
  public double BreadcrumbMarginRight = 12;

  // sort/size toolbar style
  public double ToolbarY = 24;
  public double ToolbarTitleFontSize = 6;
  public double ToolbarLabelFontSize = 9;

  public string SortLabel = "Sort ";
  public string SizeLabel = "Size ";
  public string OpenTerminalLabel = "Term";

  public Color ActiveColor = new Color (0,0,0,1);
  public Color InActiveColor = new Color (0,0,0,0.5);
  public Color TermColor = new Color (0,0,1,1);

  // filesystem view style
  public double FilesMarginLeft = 10;
  public double FilesMarginRight = 10;
  public double FilesMarginTop = 52;
  public double FilesMarginBottom = 10;

  // zoom speed settings, must be > 1 to zoom in the right direction
  public double ZoomInSpeed = 1.5;
  public double ZoomOutSpeed = 1.5;

  // Available sorts
  public SortHandler[] SortFields = {
    new SortHandler("Name", new NameComparer()),
    new SortHandler("Size", new SizeComparer()),
    new SortHandler("Date", new DateComparer()),
    new SortHandler("Type", new TypeComparer())
  };
  // current sort settings
  public SortHandler SortField;
  public SortingDirection SortDirection = SortingDirection.Ascending;

  // Available file sizers
  public SizeHandler[] SizeFields = {
    new SizeHandler("Flat", new FlatMeasurer()),
    new SizeHandler("Size", new SizeMeasurer()),
    new SizeHandler("Date", new DateMeasurer()),
    new SizeHandler("Count", new CountMeasurer()),
    new SizeHandler("Total", new TotalMeasurer())
  };
  // current file sizer
  public SizeHandler SizeField;

  // current zoomer
  public IZoomer Zoomer;

  // current directory
  private string CurrentDirPath = null;
  private DirStats CurrentDir = null;

  // Do we need to relayout before drawing
  public bool LayoutUpdateRequested = true;

  // Do we need to sort before drawing
  public bool SortUpdateRequested = true;

  // are we drawing the first frame of a new directory
  bool FirstFrameOfDir = true;

  // GUI state variables
  bool dragging = false;
  double dragStartX = 0.0;
  double dragStartY = 0.0;
  double dragX = 0.0;
  double dragY = 0.0;

  // font size state variable
  double FontSize;

  // Current dir invalidated?
  bool Invalidated = false;
  Dictionary<string,bool> Invalids = new Dictionary<string,bool> ();

  // first frame latency profiler
  Profiler dirLatencyProfiler = new Profiler ("----");

  // FileSystemWatcher for watching the CurrentDirPath
  FileSystemWatcher Watcher;

  /* Constructor */

  /** BLOCKING - startup dir latency */
  public Filezoo (string dirname)
  {
    SortField = SortFields[0];
    SizeField = SizeFields[0];
    Zoomer = new FlatZoomer ();

    Watcher = MakeWatcher (dirname);

    BuildDirs (dirname);

    AddEvents((int)(
        Gdk.EventMask.ButtonPressMask
      | Gdk.EventMask.ButtonReleaseMask
      | Gdk.EventMask.ScrollMask
      | Gdk.EventMask.PointerMotionMask
    ));
  }


  /* Files model */

  /** BLOCKING */
  void BuildDirs (string dirname)
  {
    Profiler p = new Profiler ();
    dirLatencyProfiler.Restart ();
    if (dirname != Helpers.RootDir) dirname = dirname.TrimEnd(Helpers.DirSepC);
    UnixDirectoryInfo d = new UnixDirectoryInfo (dirname);
    CurrentDirPath = d.FullName;
    if (CurrentDir != null) CurrentDir.CancelTraversal ();
    CurrentDir = new DirStats (d);
    if (Watcher.Path != CurrentDirPath) {
      Watcher.Dispose ();
      Watcher = MakeWatcher (CurrentDirPath);
    }
    FirstFrameOfDir = true;
    ResetZoom ();
    UpdateSort ();
    p.Time("BuildDirs");
  }

  /** FAST */
  void WatcherChanged (object source, FileSystemEventArgs e)
  {
    lock (this) {
      Console.WriteLine("Invalidating {0}: {1}", e.FullPath, e.ChangeType);
      Invalidated = true;
      Invalids[e.FullPath] = true;
    }
    UpdateLayout ();
  }

  /** FAST */
  void WatcherRenamed (object source, RenamedEventArgs e)
  {
    lock (this) {
      Console.WriteLine("Invalidating {0} and {1}: renamed to latter", e.FullPath, e.OldFullPath);
      Invalidated = true;
      Invalids[e.FullPath] = true;
      Invalids[e.OldFullPath] = true;
    }
    UpdateLayout ();
  }

  /** BLOCKING */
  /* Blows up on paths with non-UTF characters */
  FileSystemWatcher MakeWatcher (string dirname)
  {
    FileSystemWatcher watcher = new FileSystemWatcher ();
    watcher.IncludeSubdirectories = false;
    watcher.NotifyFilter = (
        NotifyFilters.LastWrite
      | NotifyFilters.Size
      | NotifyFilters.FileName
      | NotifyFilters.DirectoryName
      | NotifyFilters.CreationTime
    );
    try {
      watcher.Path = dirname;
    } catch (System.ArgumentException e) {
      Console.WriteLine("System.IO.FileSystemWatcher does not appreciate the characters in your path: {0}", dirname);
      Console.WriteLine("Here's the exception output: {0}", e);
      return watcher;
    }
    watcher.Filter = "";
    watcher.Changed += new FileSystemEventHandler (WatcherChanged);
    watcher.Created += new FileSystemEventHandler (WatcherChanged);
    watcher.Deleted += new FileSystemEventHandler (WatcherChanged);
    watcher.Renamed += new RenamedEventHandler (WatcherRenamed);
    try {
      watcher.EnableRaisingEvents = true;
    } catch (System.ArgumentException e) {
      Console.WriteLine("System.IO.FileSystemWatcher does not appreciate the characters in your path: {0}", dirname);
      Console.WriteLine("You should go and fix System.IO.Path.IsPathRooted.");
      Console.WriteLine("Here's the exception output: {0}", e);
    }
    return watcher;
  }



  /* Layout */

  /** FAST */
  void UpdateLayout ()
  {
    LayoutUpdateRequested = true;
    QueueDraw ();
  }

  /** FAST */
  void UpdateSort ()
  {
    SortUpdateRequested = true;
    UpdateLayout ();
  }

  /** BLOCKING */
  void RecreateLayout ()
  {
    Profiler p = new Profiler ();

    if (SortUpdateRequested) {
      CurrentDir.Comparer = SortField.Comparer;
      CurrentDir.SortDirection = SortDirection;
      CurrentDir.Sort ();
      SortUpdateRequested = false;
      FirstFrameOfDir = true;
      p.Time ("CurrentDir.Sort");
    }

    if (CurrentDir.Measurer != SizeField.Measurer) {
      FirstFrameOfDir = true;
      CurrentDir.Measurer = SizeField.Measurer;
    }
    CurrentDir.Relayout ();
    p.Time ("CurrentDir.Relayout");
    if (!SizeField.Measurer.DependsOnTotals)
      CurrentDir.CancelTraversal ();
    LayoutUpdateRequested = !CurrentDir.Complete;
  }



  /* Drawing */

  /** FAST */
  Rectangle Transform (Context cr, uint width, uint height)
  {
    double boxHeight = Math.Max(1, height-FilesMarginTop-FilesMarginBottom);
    double boxWidth = Math.Max(1, width-FilesMarginLeft-FilesMarginRight);
    cr.Translate(FilesMarginLeft, FilesMarginTop);
    double x = cr.Matrix.X0;
    double y = cr.Matrix.Y0;
    double w =  boxWidth * cr.Matrix.Xx;
    double h = boxHeight * cr.Matrix.Yy;
    cr.Rectangle (0, 0, boxWidth, boxHeight);
    cr.Clip ();
    cr.Scale (boxHeight, boxHeight);
    return new Rectangle (x,y,w,h);
  }

  /** BLOCKING */
  void Draw (Context cr, uint width, uint height)
  {
    lock (this) {
      if (Invalidated) {
        foreach (string path in Invalids.Keys)
          DirCache.Invalidate (path);
        BuildDirs (CurrentDirPath);
        Invalidated = false;
        Invalids.Clear ();
      }
    }
    if (LayoutUpdateRequested) RecreateLayout();

    cr.Save ();
      DrawClear (cr, width, height);
      DrawToolbars (cr, width, height);
      Rectangle targetBox = Transform (cr, width, height);
      DrawCurrentDir(cr, targetBox);
    cr.Restore ();

    dirLatencyProfiler.Stop ();
    if (FirstFrameOfDir) {
      dirLatencyProfiler.Time ("Directory latency");
      FirstFrameOfDir = false;
      QueueDraw ();
    }
    if (LayoutUpdateRequested || !CurrentDir.Complete || !CurrentDir.LayoutComplete)
      UpdateLayout();
  }

  /** FAST */
  void DrawClear (Context cr, uint width, uint height)
  {
    cr.Color = DirStats.BackgroundColor;
    cr.Rectangle (0,0, width, height);
    cr.Fill ();
  }

  /** FAST */
  void DrawToolbars (Context cr, uint width, uint height)
  {
    Profiler p = new Profiler ();
    cr.Save ();
      DrawBreadcrumb (cr, width);
      DrawSortBar (cr);
      DrawSizeBar (cr);
      DrawOpenTerminal (cr);
    cr.Restore ();
    p.Time ("DrawToolbars");
  }

  /** BLOCKING */
  void DrawCurrentDir (Context cr, Rectangle targetBox)
  {
    Profiler p = new Profiler ();
    cr.Save ();
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      uint c = CurrentDir.Draw (cr, targetBox, FirstFrameOfDir);
    cr.Restore ();
    p.Time (String.Format("DrawCurrentDir: {0} entries", c));
  }

  /** FAST */
  void DrawBreadcrumb (Context cr, uint width)
  {
    TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontSize, CurrentDirPath);
    cr.Color = DirStats.DirectoryColor;
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.Save ();
      double areaWidth = width-BreadcrumbMarginLeft-BreadcrumbMarginRight;
      cr.Rectangle (0,0,areaWidth, te.Height);
      cr.Clip ();
      cr.Translate (Math.Min(0,areaWidth-te.Width), 0);
      cr.MoveTo (0.0, 0.0);
      FontSize = (BreadcrumbFontSize);
      if (CurrentDirPath == Helpers.RootDir) {
        Helpers.DrawText (cr, FontSize, Helpers.RootDir);
      } else {
        foreach (string s in CurrentDirPath.Split(Helpers.DirSepC)) {
          Helpers.DrawText (cr, FontSize, s);
          Helpers.DrawText (cr, FontSize, Helpers.DirSepS);
        }
      }
    cr.Restore ();
  }

  /** FAST */
  void DrawSortBar (Context cr)
  {
    cr.MoveTo (0.0, ToolbarY);
    cr.Color = ActiveColor;
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * 0.4 );
    Helpers.DrawText (cr, ToolbarTitleFontSize, SortLabel);
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * -0.4 );
    FontSize = ToolbarLabelFontSize;
    foreach (SortHandler sf in SortFields) {
      cr.Color = (SortField == sf) ? ActiveColor : InActiveColor;
      Helpers.DrawText (cr, FontSize, sf.Name);
      Helpers.DrawText (cr, FontSize, " ");
    }
    cr.Color = ActiveColor;
    Helpers.DrawText (cr, FontSize, " ");
    bool SortDesc = (SortDirection == SortingDirection.Descending);
    Helpers.DrawText (cr, FontSize, (SortDesc ? "▾" : "▴") + " ");
    Helpers.DrawText (cr, FontSize, " ");
  }

  /** FAST */
  void DrawSizeBar (Context cr)
  {
    cr.Color = ActiveColor;
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * 0.4 );
    Helpers.DrawText (cr, ToolbarTitleFontSize, SizeLabel);
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * -0.4 );
    FontSize = ToolbarLabelFontSize;
    foreach (SizeHandler sf in SizeFields) {
      cr.Color = (SizeField == sf) ? ActiveColor : InActiveColor;
      Helpers.DrawText (cr, FontSize, sf.Name);
      Helpers.DrawText (cr, FontSize, " ");
    }
    Helpers.DrawText (cr, FontSize, " ");
  }

  /** FAST */
  void DrawOpenTerminal (Context cr)
  {
    FontSize = ToolbarLabelFontSize;
    cr.Color = InActiveColor;
    Helpers.DrawText (cr, FontSize, "  ");
    cr.Color = TermColor;
    Helpers.DrawText (cr, FontSize, OpenTerminalLabel);
  }


  /* Click handling */

  /** BLOCKING */
  void Click (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save ();
      if (ClickBreadcrumb (cr, width, x, y)) {
        cr.Restore ();
        return;
      }
      double advance = 0.0;
      if (
        ClickSortBar (ref advance, cr, x, y) ||
        ClickSizeBar (ref advance, cr, x, y) ||
        ClickOpenTerminal (ref advance, cr, x, y)
      ) {
        cr.Restore ();
        return;
      }
    cr.Restore ();
    cr.Save();
      ClickCurrentDir(cr, width, height, x, y);
    cr.Restore ();
  }

  /** BLOCKING */
  void ClickCurrentDir (Context cr, uint width, uint height, double x, double y)
  {
    Rectangle box = Transform (cr, width, height);
    cr.Scale (1, Zoomer.Z);
    cr.Translate (0.0, Zoomer.Y);
    DirAction action = CurrentDir.Click (cr, box, x, y);
    switch (action.Type) {
      case DirAction.Action.Open:
        Console.WriteLine("Open {0}", action.Path);
        Helpers.OpenFile(action.Path);
        break;
      case DirAction.Action.Navigate:
        Console.WriteLine("Navigate {0}", action.Path);
        BuildDirs (action.Path);
        break;
      case DirAction.Action.ZoomIn:
        Console.WriteLine("ZoomIn {0}x", 1 / action.Height);
        cr.Save ();
          cr.IdentityMatrix ();
          ZoomBy(cr, width, height, x, y, 1 / action.Height);
        cr.Restore ();
        break;
    }
  }

  /** FAST */
  bool ClickBreadcrumb (Context cr, uint width, double x, double y)
  {
    TextExtents te1 = Helpers.GetTextExtents (cr, BreadcrumbFontSize, CurrentDirPath);
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.Save ();
      double areaWidth = width-BreadcrumbMarginLeft-BreadcrumbMarginRight;
      cr.Rectangle (0,0,areaWidth, te1.Height);
      cr.Clip ();
      cr.Translate (Math.Min(0,areaWidth-te1.Width), 0);
      cr.MoveTo (0.0, 0.0);
      FontSize = (BreadcrumbFontSize);
      double advance = 0.0;
      int hitIndex = 0;
      string[] segments = CurrentDirPath.Split(Helpers.DirSepC);
      if (CurrentDirPath != Helpers.RootDir) {
        foreach (string s in segments) {
          TextExtents te = Helpers.GetTextExtents (cr, FontSize, s + Helpers.DirSepS);
          if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
            string newDir = String.Join(Helpers.DirSepS, segments, 0, hitIndex+1);
            if (newDir == "") newDir = Helpers.RootDir;
            if (newDir != CurrentDirPath)
              BuildDirs (newDir);
            cr.Restore ();
            return true;
          }
          advance += te.XAdvance;
          hitIndex += 1;
        }
      }
    cr.Restore ();
    return false;
  }

  /** FAST */
  bool ClickSortBar (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    cr.Translate (0, ToolbarY);
    FontSize = ToolbarTitleFontSize;
    advance += Helpers.GetTextExtents (cr, FontSize, SortLabel).XAdvance;
    FontSize = ToolbarLabelFontSize;
    foreach (SortHandler sf in SortFields) {
      te = Helpers.GetTextExtents (cr, FontSize, sf.Name);
      if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
        if (sf == SortField) {
          SortDirection = (SortDirection == SortingDirection.Ascending) ?
                          SortingDirection.Descending :
                          SortingDirection.Ascending;
        } else {
          SortField = sf;
        }
        ResetZoom ();
        UpdateSort ();
        return true;
      }
      advance += te.XAdvance;
      advance += Helpers.GetTextExtents (cr, FontSize, " ").XAdvance;
    }
    advance += Helpers.GetTextExtents (cr, FontSize, " ").XAdvance;
    bool SortDesc = (SortDirection == SortingDirection.Descending);
    te = Helpers.GetTextExtents (cr, FontSize, (SortDesc ? "▾" : "▴") + " ");
    if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
      SortDirection = (SortDirection == SortingDirection.Ascending) ?
                      SortingDirection.Descending :
                      SortingDirection.Ascending;
      ResetZoom ();
      UpdateSort ();
      return true;
    }
    advance += te.XAdvance;
    advance += Helpers.GetTextExtents (cr, FontSize, " ").XAdvance;
    return false;
  }

  /** FAST */
  bool ClickSizeBar (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    FontSize = ToolbarTitleFontSize;
    advance += Helpers.GetTextExtents (cr, FontSize, SizeLabel).XAdvance;
    FontSize = ToolbarLabelFontSize;
    foreach (SizeHandler sf in SizeFields) {
      te = Helpers.GetTextExtents (cr, FontSize, sf.Name);
      if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
        SizeField = sf;
        ResetZoom ();
        UpdateLayout ();
        return true;
      }
      advance += te.XAdvance;
      advance += Helpers.GetTextExtents (cr, FontSize, " ").XAdvance;
    }
    advance += Helpers.GetTextExtents (cr, FontSize, " ").XAdvance;
    return false;
  }

  /** FAST */
  bool ClickOpenTerminal (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    FontSize = ToolbarLabelFontSize;
    advance += Helpers.GetTextExtents (cr, FontSize, "  ").XAdvance;
    te = Helpers.GetTextExtents (cr, FontSize, OpenTerminalLabel);
    if (Helpers.CheckTextExtents (cr, advance, te, x, y)) {
      Helpers.OpenTerminal(CurrentDirPath);
      return true;
    }
    advance += te.XAdvance;
    return false;
  }


  /* Zooming and panning */

  /** FAST */
  void ResetZoom () {
    Zoomer.ResetZoom ();
    Zoomer.SetZoom (0.0, CurrentDir.DefaultPan, CurrentDir.DefaultZoom);
  }

  /** FAST */
  void ZoomBy
  (Context cr, uint width, uint height, double x, double y, double factor)
  {
    double xr = x, yr = y, nz = Zoomer.Z * factor;
    if (CurrentDirPath == Helpers.RootDir && nz < 1) nz = 1;
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformPoint(ref xr, ref yr);
      double npy = (yr / nz) - (yr / Zoomer.Z) + Zoomer.Y;
//       Console.WriteLine(npy);
      Zoomer.SetZoom (0.0, npy, nz);
    cr.Restore ();
    cr.Save ();
      Rectangle r = Transform (cr, width, height);
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      Covering c = CurrentDir.FindCovering(cr, r, 0);
      if (c.Directory != CurrentDir) {
        BuildDirs(c.Directory.FullName);
        Zoomer.SetZoom (0.0, c.Pan, c.Zoom);
      }
    cr.Restore ();
    UpdateLayout();
  }

  /** FAST */
  void ZoomToward (Context cr, uint width, uint height, double x, double y) {
    ZoomBy (cr, width, height, x, y, ZoomInSpeed);
  }

  /** FAST */
  void ZoomAway (Context cr, uint width, uint height, double x, double y) {
    ZoomBy (cr, width, height, x, y, 1.0 / ZoomOutSpeed);
  }

  /** FAST */
  void PanBy (Context cr, uint width, uint height, double dx, double dy)
  {
    double xr = dx, yr = dy;
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformDistance(ref xr, ref yr);
      Zoomer.Y += yr / Zoomer.Z;
    cr.Restore ();
    UpdateLayout();
  }


  /* Event handlers */

  /** FAST */
  protected override bool OnButtonPressEvent (Gdk.EventButton e)
  {
    dragStartX = dragX = e.X;
    dragStartY = dragY = e.Y;
    dragging = false;
    return true;
  }

  /** BLOCKING */
  protected override bool OnButtonReleaseEvent (Gdk.EventButton e)
  {
    if (e.Button == 1 && !dragging) {
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        Click (cr, (uint)w, (uint)h, e.X, e.Y);
      }
    }
    dragging = false;
    return true;
  }

  /** FAST */
  protected override bool OnMotionNotifyEvent (Gdk.EventMotion e)
  {
    if ((e.State & Gdk.ModifierType.Button2Mask) == Gdk.ModifierType.Button2Mask ||
        (e.State & Gdk.ModifierType.Button1Mask) == Gdk.ModifierType.Button1Mask
    ) {
      dragging = dragging || ((Math.Abs(dragX - dragStartX) + Math.Abs(dragY - dragStartY)) > 4);
      double dx = e.X - dragX;
      double dy = e.Y - dragY;
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        PanBy (cr, (uint)w, (uint)h, dx, dy);
      }
    }
    dragX = e.X;
    dragY = e.Y;
    return true;
  }

  /** FAST */
  protected override bool OnScrollEvent (Gdk.EventScroll e)
  {
    if (e.Direction == Gdk.ScrollDirection.Up) {
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        ZoomToward (cr, (uint)w, (uint)h, e.X, e.Y);
      }
    }
    if (e.Direction == Gdk.ScrollDirection.Down) {
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        ZoomAway (cr, (uint)w, (uint)h, e.X, e.Y);
      }
    }
    return true;
  }

  /** BLOCKING */
  /**
    The expose event handler. Gets the Cairo.Context for the
    window and calls Draw with it and the window dimensions.

    @param e The expose event.
    @returns true
  */
  protected override bool OnExposeEvent (Gdk.EventExpose e)
  {
    using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
    {
      int w, h;
      e.Window.GetSize (out w, out h);
      Draw (cr, (uint)w, (uint)h);
    }
    return true;
  }

}





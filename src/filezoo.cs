using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Gtk;
using Cairo;
using Mono.Unix;

class Filezoo : DrawingArea
{
  // Filename Unicode icons
  public Dictionary<string, string> Prefixes = null;

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
  private FSEntry CurrentDirEntry;

  // Do we need to redraw?
  bool NeedRedraw = true;

  // Whether to quit after startup
  public bool QuitAfterFirstFrame = false;

  // are we drawing the first frame of a new directory
  bool FirstFrameOfDir = true;

  // GUI state variables
  bool dragging = false;
  double dragStartX = 0.0;
  double dragStartY = 0.0;
  double dragX = 0.0;
  double dragY = 0.0;

  uint Width = 1;
  uint Height = 1;

  // font size state variable
  double FontSize;

  // first frame latency profiler
  Profiler dirLatencyProfiler = new Profiler ("----", 100);

  // interaction profiler, time from user event to draw complete
  Profiler InteractionProfiler = new Profiler ("UI", 100);

  // empty surface for PreDraw context.
  ImageSurface PreDrawSurface = new ImageSurface (Format.A1, 1, 1);


  // modification monitor
  DateTime LastRedraw = DateTime.Now;

  bool Active = true;

  bool PreDrawComplete = true;

  Menu ContextMenu;

  /* Constructor */

  /** BLOCKING - startup dir latency */
  public Filezoo (string dirname)
  {
    SortField = SortFields[0];
    SizeField = SizeFields[0];
    Zoomer = new FlatZoomer ();

    BuildDirs (dirname);

    GLib.Timeout.Add (50, new GLib.TimeoutHandler (CheckUpdates));
    GLib.Timeout.Add (1030, new GLib.TimeoutHandler (LongMonitor));

    AddEvents((int)(
        Gdk.EventMask.ButtonPressMask
      | Gdk.EventMask.ButtonReleaseMask
      | Gdk.EventMask.ScrollMask
      | Gdk.EventMask.PointerMotionMask
    ));
  }

  bool CheckUpdates ()
  {
    if (LastRedraw != FSCache.LastChange) {
      LastRedraw = FSCache.LastChange;
      PreDraw ();
    }
    if (!PreDrawComplete || NeedRedraw) {
      NeedRedraw = false;
      QueueDraw ();
    }
    return Active;
  }

  bool LongMonitor ()
  { lock (FSCache.Cache) {
    foreach (FSEntry e in CurrentDirEntry.Entries) {
      DateTime mtime = Helpers.LastChange(e.FullName);
      if (e.IsDirectory && !(e.LastFileChange == mtime)) {
        e.LastFileChange = mtime;
        FSCache.Invalidate(e.FullName);
      }
    }
    return Active;
  } }


  /* Files model */

  /** BLOCKING */
  void BuildDirs (string dirname)
  {
    Profiler p = new Profiler ();
    dirLatencyProfiler.Restart ();
    if (dirname != Helpers.RootDir) dirname = dirname.TrimEnd(Helpers.DirSepC);
    UnixDirectoryInfo d = new UnixDirectoryInfo (dirname);
    CurrentDirPath = d.FullName;
    dirLatencyProfiler.Restart ();
    FSCache.Watch (CurrentDirPath);
    CurrentDirEntry = FSCache.Get (CurrentDirPath);
    FSCache.FilePass (CurrentDirPath);
    FirstFrameOfDir = true;
    FSCache.CancelTraversal ();
    ResetZoom ();
    PreDraw ();
    p.Time("BuildDirs");
  }


  /* Layout */

  /** BLOCKING */
  void UpdateLayout ()
  {
    PreDraw ();
  }

  System.Object PreDrawLock = new System.Object ();
  System.Object PreDrawProgressLock = new System.Object ();
  bool PreDrawInProgress = false;
  /** BLOCKING */
  void PreDraw ()
  {
    lock (PreDrawLock) {
      FSDraw.CancelPreDraw();
      lock (PreDrawProgressLock) {
        if (PreDrawInProgress) return;
        FSCache.Measurer = SizeField.Measurer;
        FSCache.SortDirection = SortDirection;
        FSCache.Comparer = SortField.Comparer;
        PreDrawComplete = false;
        WaitCallback cb = new WaitCallback(PreDrawCallback);
        ThreadPool.QueueUserWorkItem(cb);
      }
    }
  }

  /** ASYNC */
  void PreDrawCallback (object state)
  {
    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
    if (!FSCache.Measurer.DependsOnTotals)
      FSCache.CancelTraversal ();
    FSCache.CancelThumbnailing ();
    lock (PreDrawProgressLock) {
      if (PreDrawInProgress) return;
      PreDrawInProgress = true;
      using (Context cr = new Context (PreDrawSurface)) {
        cr.IdentityMatrix ();
        Rectangle target = Transform (cr, Width, Height);
        cr.Scale (1, Zoomer.Z);
        cr.Translate (0.0, Zoomer.Y);
        PreDrawComplete = FSDraw.PreDraw (CurrentDirEntry, cr, target, 0);
        if (PreDrawComplete) NeedRedraw = true;
      }
      PreDrawInProgress = false;
    }
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
    }
    if (Helpers.StartupProfiler.Watch.IsRunning) {
      Helpers.StartupProfiler.Time ("Draw complete");
      Helpers.StartupProfiler.Total ("Startup complete");
      Helpers.StartupProfiler.Stop ();
      if (QuitAfterFirstFrame) Application.Quit ();
    }
  }

  /** FAST */
  void DrawClear (Context cr, uint width, uint height)
  {
    cr.Color = FSDraw.BackgroundColor;
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
    cr.Restore ();
    p.Time ("DrawToolbars");
  }

  /** BLOCKING */
  void DrawCurrentDir (Context cr, Rectangle targetBox)
  {
    Profiler p = new Profiler ();
    uint c;
    cr.Save ();
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      lock (FSCache.Cache) {
        FSCache.SortEntries(CurrentDirEntry);
        FSCache.MeasureEntries(CurrentDirEntry);
        c = FSDraw.Draw(CurrentDirEntry, Prefixes, cr, targetBox);
      }
    cr.Restore ();
    p.Time (String.Format("DrawCurrentDir: {0} entries", c));
  }

  /** FAST */
  void DrawBreadcrumb (Context cr, uint width)
  {
    TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontSize, CurrentDirPath);
    cr.Color = FSDraw.DirectoryColor;
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
        ClickSizeBar (ref advance, cr, x, y)
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
    List<ClickHit> hits = FSDraw.Click (CurrentDirEntry, cr, box, x, y);
    foreach (ClickHit c in hits) {
      if (c.Height < 16) {
        Console.WriteLine("ZoomIn {0}x", 18 / c.Height);
        cr.Save ();
          cr.IdentityMatrix ();
          ZoomBy(cr, width, height, x, y, 18 / c.Height);
        cr.Restore ();
        break;
      } else {
        if (c.Target.IsDirectory) {
          Console.WriteLine("Navigate {0}", c.Target.FullName);
          BuildDirs (c.Target.FullName);
          ResetZoom ();
          UpdateLayout ();
        } else {
          Console.WriteLine("Open {0}", c.Target.FullName);
          Helpers.OpenFile(c.Target.FullName);
        }
        break;
      }
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
            if (newDir != CurrentDirPath) {
              BuildDirs (newDir);
            } else {
              ResetZoom ();
              UpdateLayout ();
            }
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
        UpdateLayout ();
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
      UpdateLayout ();
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



  /* Context menu code duplication, banzai */

  string[] exSuffixes = {"bz2", "gz", "rar", "tar", "zip"};

  /** BLOCKING */
  void ContextClick (Menu menu, Context cr, uint width, uint height, double x, double y)
  {
    cr.Save();
      Rectangle box = Transform (cr, width, height);
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      List<ClickHit> hits = FSDraw.Click (CurrentDirEntry, cr, box, x, y);
      if (hits.Count == 0) {
        FillMenu (menu, new ClickHit(CurrentDirEntry, cr.Matrix.Yy));
      } else {
        foreach (ClickHit c in hits) {
          if (c.Height > 8) {
            FillMenu (menu, c);
            break;
          }
        }
      }
    cr.Restore ();
  }

  void FillMenu (Menu menu, ClickHit c)
  {
    menu.Title = c.Target.FullName;
    if (c.Target.IsDirectory) {
    // Directory menu items

      MenuItem goTo = new MenuItem ("Go to " + c.Target.Name);
      goTo.Activated += new EventHandler(delegate {
        BuildDirs (menu.Title); });
      menu.Append (goTo);

      MenuItem term = new MenuItem ("Open terminal");
      term.Activated += new EventHandler(delegate {
        Helpers.OpenTerminal (menu.Title); });
      menu.Append (term);

    } else {
    // File menu items

      MenuItem open = new MenuItem ("Open " + c.Target.Name);
      open.Activated += new EventHandler(delegate {
        Helpers.OpenFile (menu.Title); });
      menu.Append (open);

      /** DESTRUCTIVE */
      if (Array.IndexOf (exSuffixes, c.Target.Suffix) > -1) {
        MenuItem ex = new MenuItem ("Extract");
        ex.Activated += new EventHandler(delegate {
          Helpers.ExtractFile (menu.Title); });
        menu.Append (ex);
      }

    }

    /** DESTRUCTIVE */
    MenuItem trash = new MenuItem ("Delete");
    trash.Activated += new EventHandler(delegate {
      Helpers.Delete(menu.Title); });
    menu.Append (trash);
  }



  /* Zooming and panning */

  /** FAST */
  void ResetZoom () {
    Zoomer.ResetZoom ();
    Zoomer.SetZoom (0.0, FSDraw.DefaultPan, FSDraw.DefaultZoom);
  }

  /** BLOCKING */
  void ZoomBy
  (Context cr, uint width, uint height, double x, double y, double factor)
  {
    double xr = x, yr = y, nz = Zoomer.Z * factor;
    if (CurrentDirPath == Helpers.RootDir && nz < 1) nz = 1;
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformPoint(ref xr, ref yr);
      double npy = (yr / nz) - (yr / Zoomer.Z) + Zoomer.Y;
      Zoomer.SetZoom (0.0, npy, nz);
    cr.Restore ();
    cr.Save ();
      Rectangle r = Transform (cr, width, height);
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      Covering c = FSDraw.FindCovering(CurrentDirEntry, cr, r, 0);
      if (c.Directory.FullName != CurrentDirPath) {
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
    if (e.Button == 3) {
      if (ContextMenu != null) ContextMenu.Dispose ();
      ContextMenu = new Menu ();
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        ContextClick (ContextMenu, cr, (uint)w, (uint)h, e.X, e.Y);
      }
      ContextMenu.ShowAll ();
      ContextMenu.Popup ();
    }
    return true;
  }

  /** BLOCKING */
  protected override bool OnButtonReleaseEvent (Gdk.EventButton e)
  {
    if (e.Button == 1 && !dragging) {
      InteractionProfiler.Restart ();
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
      InteractionProfiler.Restart ();
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
    InteractionProfiler.Restart ();
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
      if (Width != (uint)w || Height != (uint)h) {
        Width = (uint) w;
        Height = (uint) h;
      }
      if (Helpers.StartupProfiler.Watch.IsRunning) {
        Helpers.StartupProfiler.Time ("First expose");
        Helpers.StartupProfiler.Total ("Pre-drawing startup");
      }
      Draw (cr, Width, Height);
    }
    if (InteractionProfiler.Watch.IsRunning) {
      InteractionProfiler.Time ("Interaction latency");
      InteractionProfiler.Stop ();
    }
    return true;
  }

}





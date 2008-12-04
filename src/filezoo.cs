/*
    Filezoo - a small and fast file manager
    Copyright (C) 2008  Ilmari Heikkinen

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Timers;
using System.IO;
using Gtk;
using Cairo;
using Mono.Unix;

public class Filezoo : DrawingArea
{
  // Filename Unicode icons
  public Dictionary<string, string> Prefixes = null;

  public string BreadcrumbFontFamily = "Sans";
  public string ToolbarTitleFontFamily = "Sans";
  public string ToolbarLabelFontFamily = "Sans";

  public string FileNameFontFamily = "Sans";
  public string FileInfoFontFamily = "Sans";

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

  public Color ActiveColor = new Color (0,0,0,1);
  public Color InActiveColor = new Color (0,0,0,0.5);

  // filesystem view style
  public double FilesMarginLeft = 10;
  public double FilesMarginRight = 0;
  public double FilesMarginTop = 52;
  public double FilesMarginBottom = 0;

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
  public string CurrentDirPath = null;
  public FSEntry CurrentDirEntry;

  // Do we need to redraw?
  bool NeedRedraw = true;

  // Do we need to redraw the filesystem view?
  bool FSNeedRedraw = true;

  // Whether to quit after startup
  public bool QuitAfterFirstFrame = false;

  // are we drawing the first frame of a new directory
  bool FirstFrameOfDir = true;

  // GUI state variables
  bool dragging = false;
  double dragStartX = 0.0;
  double dragStartY = 0.0;
  double dragX = 200.0;
  double dragY = -200.0;

  public uint Width = 1;
  public uint Height = 1;

  // first frame latency profiler
  Profiler dirLatencyProfiler = new Profiler ("----", 100);

  // interaction profiler, time from user event to draw complete
  Profiler InteractionProfiler = new Profiler ("UI", 100);

  // empty surface for PreDraw context.
  ImageSurface PreDrawSurface = new ImageSurface (Format.A1, 1, 1);


  // modification monitor
  DateTime LastRedraw = DateTime.Now;

  bool PreDrawComplete = true;

  bool InitComplete = false;

  Menu ContextMenu;

  public FSDraw Renderer;

  /* Constructor */

  /** BLOCKING - startup dir latency */
  public Filezoo (string dirname)
  {
    Renderer = new FSDraw ();

    SortField = SortFields[0];
    SizeField = SizeFields[0];
    Zoomer = new FlatZoomer ();

    CurrentDirPath = dirname;

    AddEvents((int)(
        Gdk.EventMask.ButtonPressMask
      | Gdk.EventMask.ButtonReleaseMask
      | Gdk.EventMask.ScrollMask
      | Gdk.EventMask.PointerMotionMask
      | Gdk.EventMask.EnterNotifyMask
      | Gdk.EventMask.LeaveNotifyMask
    ));

    LeaveNotifyEvent += delegate (object sender, LeaveNotifyEventArgs e)
    {
      dragX = 200;
      dragY = -200;
    };

    ThreadStart ts = new ThreadStart (PreDrawCallback);
    Thread t = new Thread(ts);
    t.IsBackground = true;
    t.Start ();

  }

  public void CompleteInit ()
  {
    Helpers.StartupProfiler.Time ("First expose");
    SetCurrentDir (CurrentDirPath);
    Helpers.StartupProfiler.Time ("SetCurrentDir");
    System.Timers.Timer t = new System.Timers.Timer(50);
    t.Elapsed += new ElapsedEventHandler (CheckUpdates);
    System.Timers.Timer t2 = new System.Timers.Timer(1000);
    t2.Elapsed += new ElapsedEventHandler (LongMonitor);
    t.Enabled = true;
    t2.Enabled = true;
    InitComplete = true;
    Helpers.StartupProfiler.Total ("Pre-drawing startup");
  }

  public void MockDraw (uint w, uint h)
  {
    using (ImageSurface s = new ImageSurface (Format.ARGB32, 1, 1)) {
      using (Context cr = new Context (s)) {
        Draw (cr, w, h);
      }
    }
  }

  void CheckUpdates (object source, ElapsedEventArgs e)
  {
    if (LastRedraw != FSCache.LastChange) {
      LastRedraw = FSCache.LastChange;
      PreDraw ();
    }
    if (!PreDrawComplete || NeedRedraw) {
      NeedRedraw = false;
      FSNeedRedraw = true;
      QueueDraw ();
    }
  }

  void LongMonitor (object source, ElapsedEventArgs ev)
  {
    foreach (FSEntry e in CurrentDirEntry.Entries) {
      DateTime mtime = Helpers.LastChange(e.FullName);
      if (e.IsDirectory && !(e.LastFileChange == mtime)) {
        e.LastFileChange = mtime;
        FSCache.Invalidate(e.FullName);
      }
    }
  }


  /* Files model */

  /** BLOCKING */
  public void SetCurrentDir (string dirname)
  {
    Profiler p = new Profiler ();
    dirLatencyProfiler.Restart ();
    FirstFrameOfDir = true;

    if (dirname != Helpers.RootDir) dirname = dirname.TrimEnd(Helpers.DirSepC);
    UnixDirectoryInfo d = new UnixDirectoryInfo (dirname);
    CurrentDirPath = d.FullName;

    FSCache.CancelTraversal ();

    FSCache.FilePass (CurrentDirPath);
    CurrentDirEntry = FSCache.Get (CurrentDirPath);
    FSCache.Watch (CurrentDirPath);

    ResetZoom ();
    PreDraw ();
    p.Time("SetCurrentDir");
  }


  /* Layout */

  /** BLOCKING */
  void UpdateLayout ()
  {
    PreDraw ();
  }

  System.Object PreDrawLock = new System.Object ();
  bool PreDrawInProgress = false;
  /** BLOCKING */
  void PreDraw ()
  {
    Renderer.CancelPreDraw();
    lock (PreDrawLock) {
      if (PreDrawInProgress) return;
      PreDrawInProgress = true;
    }
    PreDrawComplete = false;
    FSCache.Measurer = SizeField.Measurer;
    FSCache.SortDirection = SortDirection;
    FSCache.Comparer = SortField.Comparer;
  }

  /** ASYNC */
  void PreDrawCallback ()
  {
    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
    while (true) {
      if (PreDrawInProgress) {
        try {
          if (!FSCache.Measurer.DependsOnTotals)
            FSCache.CancelTraversal ();
          FSCache.CancelThumbnailing ();
          using (Context cr = new Context (PreDrawSurface)) {
            cr.IdentityMatrix ();
            Rectangle target = Transform (cr, Width, Height);
            cr.Scale (1, Zoomer.Z);
            cr.Translate (0.0, Zoomer.Y);
            PreDrawComplete = Renderer.PreDraw (CurrentDirEntry, cr, target, 0);
            if (PreDrawComplete) NeedRedraw = true;
          }
        } finally {
          PreDrawInProgress = false;
        }
      } else {
        Thread.Sleep (10);
      }
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
    cr.Color = Renderer.BackgroundColor;
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
    p.Total ("DrawToolbars");
  }

  /** BLOCKING */
  void DrawCurrentDir (Context cr, Rectangle targetBox)
  {
    Profiler p = new Profiler ();
    uint c;
    cr.Save ();
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      Renderer.FileNameFontFamily = FileNameFontFamily;
      Renderer.FileInfoFontFamily = FileInfoFontFamily;
      c = Renderer.Draw(CurrentDirEntry, Prefixes, cr, targetBox);
    cr.Restore ();
    p.Time (String.Format("DrawCurrentDir: {0} entries", c));
  }

  /** FAST */
  void DrawBreadcrumb (Context cr, uint width)
  {
    Profiler p = new Profiler ();
    p.Time("In breadcrumb");
    TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, CurrentDirPath+"/");
    p.Time("GetTextExtents");
    cr.Color = Renderer.DirectoryColor;
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.Save ();
      double areaWidth = width-BreadcrumbMarginLeft-BreadcrumbMarginRight;
      cr.Rectangle (0,0,areaWidth, te.Height);
      cr.Clip ();
      cr.Translate (Math.Min(0,areaWidth-te.Width), 0);
      cr.MoveTo (0.0, 0.0);
      if (CurrentDirPath == Helpers.RootDir) {
        Helpers.DrawText (cr, BreadcrumbFontFamily, BreadcrumbFontSize, Helpers.RootDir);
      } else {
    p.Time("start DrawText");
        foreach (string s in CurrentDirPath.Split(Helpers.DirSepC)) {
          Helpers.DrawText (cr, BreadcrumbFontFamily, BreadcrumbFontSize, s);
          Helpers.DrawText (cr, BreadcrumbFontFamily, BreadcrumbFontSize, Helpers.DirSepS);
        }
    p.Time("DrawText");
      }
    cr.Restore ();
  }

  /** FAST */
  void DrawSortBar (Context cr)
  {
    cr.MoveTo (0.0, ToolbarY);
    cr.Color = ActiveColor;
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * 0.4 );
    Helpers.DrawText (cr, ToolbarTitleFontFamily, ToolbarTitleFontSize, SortLabel);
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * -0.4 );
    foreach (SortHandler sf in SortFields) {
      cr.Color = (SortField == sf) ? ActiveColor : InActiveColor;
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    }
    cr.Color = ActiveColor;
    Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    bool SortDesc = (SortDirection == SortingDirection.Descending);
    Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, (SortDesc ? "▾" : "▴") + " ");
    Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
  }

  /** FAST */
  void DrawSizeBar (Context cr)
  {
    cr.Color = ActiveColor;
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * 0.4 );
    Helpers.DrawText (cr, ToolbarTitleFontFamily, ToolbarTitleFontSize, SizeLabel);
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * -0.4 );
    foreach (SizeHandler sf in SizeFields) {
      cr.Color = (SizeField == sf) ? ActiveColor : InActiveColor;
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    }
    Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
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
    List<ClickHit> hits = Renderer.Click (CurrentDirEntry, cr, box, x, y);
    foreach (ClickHit c in hits) {
      if (c.Height < 16) {
        double nz = (c.Target.IsDirectory ? 20 : 18) / c.Height;
        Console.WriteLine("ZoomIn {0}x", nz);
        cr.Save ();
          cr.IdentityMatrix ();
          ZoomBy(cr, width, height, x, y, nz);
        cr.Restore ();
        break;
      } else {
        if (c.Target.IsDirectory) {
          Console.WriteLine("Navigate {0}", c.Target.FullName);
          SetCurrentDir (c.Target.FullName);
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
    TextExtents te1 = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, CurrentDirPath);
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.Save ();
      double areaWidth = width-BreadcrumbMarginLeft-BreadcrumbMarginRight;
      cr.Rectangle (0,0,areaWidth, te1.Height);
      cr.Clip ();
      cr.Translate (Math.Min(0,areaWidth-te1.Width), 0);
      cr.MoveTo (0.0, 0.0);
      double advance = 0.0;
      int hitIndex = 0;
      string[] segments = CurrentDirPath.Split(Helpers.DirSepC);
      if (CurrentDirPath != Helpers.RootDir) {
        foreach (string s in segments) {
          TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, s + Helpers.DirSepS);
          if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
            string newDir = String.Join(Helpers.DirSepS, segments, 0, hitIndex+1);
            if (newDir == "") newDir = Helpers.RootDir;
            if (newDir != CurrentDirPath) {
              SetCurrentDir (newDir);
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
    advance += Helpers.GetTextExtents (cr, ToolbarTitleFontFamily, ToolbarTitleFontSize, SortLabel).XAdvance;
    foreach (SortHandler sf in SortFields) {
      te = Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
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
      advance += Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ").XAdvance;
    }
    advance += Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ").XAdvance;
    bool SortDesc = (SortDirection == SortingDirection.Descending);
    te = Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, (SortDesc ? "▾" : "▴") + " ");
    if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
      SortDirection = (SortDirection == SortingDirection.Ascending) ?
                      SortingDirection.Descending :
                      SortingDirection.Ascending;
      ResetZoom ();
      UpdateLayout ();
      return true;
    }
    advance += te.XAdvance;
    advance += Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ").XAdvance;
    return false;
  }

  /** FAST */
  bool ClickSizeBar (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    advance += Helpers.GetTextExtents (cr, ToolbarTitleFontFamily, ToolbarTitleFontSize, SizeLabel).XAdvance;
    foreach (SizeHandler sf in SizeFields) {
      te = Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
        SizeField = sf;
        ResetZoom ();
        UpdateLayout ();
        return true;
      }
      advance += te.XAdvance;
      advance += Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ").XAdvance;
    }
    advance += Helpers.GetTextExtents (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ").XAdvance;
    return false;
  }



  /* Context menu */


  /** BLOCKING */
  void ContextClick (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save();
      Rectangle box = Transform (cr, width, height);
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      List<ClickHit> hits = Renderer.Click (CurrentDirEntry, cr, box, x, y);
      ClickHit ch = new ClickHit(CurrentDirEntry, cr.Matrix.Yy);
      foreach (ClickHit c in hits) {
        if (c.Height > 8) {
          ch = c;
          break;
        }
      }
    cr.Restore ();

    if (ContextMenu != null) ContextMenu.Dispose ();
    ContextMenu = new FilezooContextMenu (this, ch);
    ContextMenu.ShowAll ();
    ContextMenu.Popup ();
  }


  /* Zooming and panning */

  /** FAST */
  void ResetZoom () {
    Zoomer.ResetZoom ();
    Zoomer.SetZoom (0.0, Renderer.DefaultPan, Renderer.DefaultZoom);
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
      Covering c = Renderer.FindCovering(CurrentDirEntry, cr, r, 0);
      if (c.Directory.FullName != CurrentDirPath) {
        SetCurrentDir(c.Directory.FullName);
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
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        ContextClick (cr, (uint)w, (uint)h, e.X, e.Y);
      }
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
    if (SillyFlare && !DrawQueued) {
      DrawQueued = true;
      QueueDraw();
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

  bool DrawQueued = false;

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
      DrawQueued = false;
      e.Window.GetSize (out w, out h);
      if (Width != (uint)w || Height != (uint)h || CachedSurface == null) {
        if (CachedSurface != null) CachedSurface.Destroy ();
        CachedSurface = new ImageSurface(Format.ARGB32, w, h);
        Width = (uint) w;
        Height = (uint) h;
      }
      if (!InitComplete) {
        CompleteInit ();
      }
      if (!EffectInProgress && FSNeedRedraw) {
        FSNeedRedraw = false;
        using (Context scr = new Context (CachedSurface)) {
          Draw (scr, Width, Height);
        }
      }
      cr.Save ();
        using (Pattern p = new Pattern (CachedSurface)) {
          cr.Operator = Operator.Source;
          cr.Source = p;
          cr.Paint ();
          cr.Operator = Operator.Over;
          if (DrawEffects (cr, Width, Height))
            QueueDraw ();
        }
      cr.Restore ();
    }
    if (InteractionProfiler.Watch.IsRunning) {
      InteractionProfiler.Time ("Interaction latency");
      InteractionProfiler.Stop ();
    }
    return true;
  }

  Random rng = new Random ();

  double flareX = 200;
  double flareY = -200;

  bool SillyFlare = true;

  bool DrawEffects  (Context cr, uint w, uint h)
  {
    if (!SillyFlare) return false;
    if (FlareGradient == null) {
      FGRadius = Helpers.ImageWidth(FlareGradientImage);
      FlareGradient = Helpers.RadialGradientFromImage(FlareGradientImage);
      FlareSpike = new ImageSurface(FlareSpikeImage);
      RainbowSprite = new ImageSurface(RainbowSpriteImage);
    }
    cr.Save ();
//       double t = DateTime.Now.ToFileTime() / 1e7;
      double dx = dragX - flareX;
      double dy = dragY - flareY;
      flareX += dx / 10;
      flareY += dy / 10;
      double s = Math.Min(1, Math.Max(0.02, 0.35 / (1 + 0.002*(dx*dx + dy*dy))));
      if (s < 0.03)
        s *= 1 + rng.NextDouble();
      cr.Translate(flareX, flareY);
      cr.Save ();
        cr.Scale (s, s);
        cr.Source = FlareGradient;
        cr.Operator = Operator.Add;
        cr.Arc(0, 0, FGRadius, 0, Math.PI * 2);
        cr.Fill ();
      cr.Restore ();
      cr.Save ();
        cr.Scale (s, s);
        cr.Operator = Operator.Add;
        using (Pattern p = new Pattern(RainbowSprite)) {
          cr.Save ();
          cr.Translate (10, -RainbowSprite.Height/2);
          cr.Rectangle (0, 0, RainbowSprite.Width, RainbowSprite.Height);
          cr.Source = p;
          cr.Fill ();
          cr.Restore ();
          cr.Save ();
          cr.Scale(-1, 1);
          cr.Translate (10, -RainbowSprite.Height/2);
          cr.Rectangle (0, 0, RainbowSprite.Width, RainbowSprite.Height);
          cr.Source = p;
          cr.Fill ();
          cr.Restore ();
        }
      cr.Restore ();
      cr.Save ();
        cr.Scale (Math.Sqrt(s), Math.Sqrt(s));
        using (Pattern p = new Pattern(FlareSpike)) {
          cr.Translate (-FlareSpike.Width/2.0, -FlareSpike.Height/2.0);
          cr.Rectangle (0, 0, FlareSpike.Width, FlareSpike.Height);
          cr.Operator = Operator.Add;
          cr.Source = p;
          cr.Fill ();
        }
      cr.Restore ();
    cr.Restore ();
    if (dx*dx < 1 && dy*dy < 1) return false;
    return true;
  }

  string FlareGradientImage = "flare_gradient.png";
  string FlareSpikeImage = "flare_spike.png";
  string RainbowSpriteImage = "rainbow_sprite.png";
  RadialGradient FlareGradient = null;
  int FGRadius;
  ImageSurface FlareSpike;
  ImageSurface RainbowSprite;

  bool EffectInProgress = false;

  ImageSurface CachedSurface;

}





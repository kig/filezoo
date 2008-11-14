using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Gtk;
using Cairo;
using Mono.Unix;

class Filezoo : DrawingArea
{
  public double BreadcrumbFontSize = 12;
  public double BreadcrumbMarginTop = 2;
  public double BreadcrumbMarginLeft = 12;

  public double ToolbarY = 24;
  public double ToolbarTitleFontSize = 6;
  public double ToolbarLabelFontSize = 9;

  public double FilesMarginLeft = 10;
  public double FilesMarginRight = 10;
  public double FilesMarginTop = 52;
  public double FilesMarginBottom = 10;

  private static Gtk.Window win = null;
  private string TopDirName = null;
  private DirStats TopDir = null;

  double ZoomInSpeed = 1.5;
  double ZoomOutSpeed = 2.0;
  bool LayoutUpdateRequested = true;

  bool FirstFrameOfDir = true;

  bool dragging = false;
  double dragStartX = 0.0;
  double dragStartY = 0.0;
  double dragX = 0.0;
  double dragY = 0.0;

  double FontSize = 15;

  public Color ActiveColor = new Color (0,0,0,1);
  public Color InActiveColor = new Color (0,0,0,0.5);
  public Color TermColor = new Color (0,0,1,1);

  public string SortLabel = "Sort ";
  public string SizeLabel = "Size ";
  public string OpenTerminalLabel = "Term";

  public SortHandler[] SortFields = {
    new SortHandler("Name", new NameComparer()),
    new SortHandler("Size", new SizeComparer()),
    new SortHandler("Date", new DateComparer()),
    new SortHandler("Type", new TypeComparer())
  };
  public SizeHandler[] SizeFields = {
    new SizeHandler("Flat", new FlatMeasurer()),
    new SizeHandler("Size", new SizeMeasurer()),
    new SizeHandler("Count", new CountMeasurer()),
    new SizeHandler("Total", new TotalMeasurer())
  };
  public SortHandler SortField;
  public SizeHandler SizeField;
  public SortingDirection SortDirection = SortingDirection.Ascending;
  public IZoomer Zoomer;

//   Traversal TraversalServer;

  Profiler dirLatencyProfiler = new Profiler ();


  /**
    The Main method inits the Gtk application and creates a Filezoo instance
    to run.
  */
  static void Main (string[] args)
  {
    Application.Init ();
    new Filezoo (args.Length > 0 ? args[0] : ".");
    Application.Run ();
  }


  /* Constructor */

  Filezoo (string topDirName)
  {
    SortField = SortFields[0];
    SizeField = SizeFields[0];
//     TraversalServer = new Traversal ();
    Zoomer = new FlatZoomer ();
    win = new Window ("Filezoo");
    BuildDirs (topDirName);
    win.SetDefaultSize (400, 768);
    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    AddEvents((int)Gdk.EventMask.ButtonPressMask);
    AddEvents((int)Gdk.EventMask.ButtonReleaseMask);
    AddEvents((int)Gdk.EventMask.ScrollMask);
    AddEvents((int)Gdk.EventMask.PointerMotionMask);
    win.Add (this);
    win.ShowAll ();
  }


  /* Files model */

  void BuildDirs (string dirname)
  {
    dirLatencyProfiler.Reset ();
    dirLatencyProfiler.Start ();
    Profiler p = new Profiler ();
    TopDirName = System.IO.Path.GetFullPath(dirname);
    if (TopDir != null)
      TopDir.TraversalCancelled = true;
    TopDir = new DirStats(new UnixDirectoryInfo (TopDirName));
    FirstFrameOfDir = true;
    ResetZoom ();
    UpdateSort ();
    p.Time("BuildDirs");
  }


  /* Layout */

  void UpdateLayout ()
  {
    LayoutUpdateRequested = true;
    win.QueueDraw ();
  }

  bool SortUpdateRequested = true;

  void UpdateSort ()
  {
    SortUpdateRequested = true;
    UpdateLayout ();
  }

  void ReCreateLayout ()
  {
    Profiler p = new Profiler ();

    if (SortUpdateRequested) {
      TopDir.Comparer = SortField.Comparer;
      TopDir.SortDirection = SortDirection;
      TopDir.Sort ();
      SortUpdateRequested = false;
      p.Time ("TopDir.Sort");
    }

    TopDir.Measurer = SizeField.Measurer;
    TopDir.Zoomer = Zoomer;
    TopDir.Relayout ();
    p.Time ("TopDir.Relayout");
    LayoutUpdateRequested = TopDir.TraversalInProgress;
  }

  /* Drawing */

  void Transform (Context cr, uint width, uint height)
  {
    double boxSize = Math.Max(1, height-FilesMarginTop-FilesMarginBottom);
    cr.Translate(FilesMarginLeft, FilesMarginTop);
    cr.Rectangle (0, 0, width-FilesMarginLeft-FilesMarginRight, boxSize);
    cr.Clip ();
    cr.Scale (boxSize, boxSize);
  }

  void Draw (Context cr, uint width, uint height)
  {
    Profiler p = new Profiler ();

    if (LayoutUpdateRequested) ReCreateLayout();

    // Clear the drawing area
    cr.Save ();
      cr.Color = new Color (1,1,1);
      cr.Rectangle (0,0, width, height);
      cr.Fill ();
    cr.Restore ();

    // Draw the toolbars
    cr.Save ();
      DrawBreadcrumb (cr);
      DrawSortBar (cr);
      DrawSizeBar (cr);
      DrawOpenTerminal (cr);
    cr.Restore ();

    p.Time ("Draw toolbars");

    // Draw the filesystem view
    cr.Save ();
      Transform (cr, width, height);
      cr.Translate (0.0, Zoomer.Y);
      cr.Scale(0.001, 0.001);
      double y = Zoomer.Y * 1000.0;
      TopDir.Draw (cr, y, 1000.0, !FirstFrameOfDir);
    cr.Restore ();

    p.Time ("TopDir.Draw");

    dirLatencyProfiler.Stop ();
    if (FirstFrameOfDir) {
      dirLatencyProfiler.Time ("Directory latency");
      win.QueueDraw ();
      FirstFrameOfDir = false;
    }
    if (LayoutUpdateRequested || TopDir.TraversalInProgress)
      UpdateLayout();
  }

  void DrawBreadcrumb (Context cr)
  {
    cr.Color = new Color (0,0,1);
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.MoveTo (0.0, 0.0);
    FontSize = (BreadcrumbFontSize);
    if (TopDirName == "/") {
      Helpers.DrawText (cr, FontSize, "/");
    } else {
      foreach (string s in TopDirName.Split('/')) {
        Helpers.DrawText (cr, FontSize, s);
        Helpers.DrawText (cr, FontSize, "/");
      }
    }
  }

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

  void DrawOpenTerminal (Context cr)
  {
    FontSize = ToolbarLabelFontSize;
    cr.Color = InActiveColor;
    Helpers.DrawText (cr, FontSize, " |  ");
    cr.Color = TermColor;
    Helpers.DrawText (cr, FontSize, OpenTerminalLabel);
  }


  /* Click handling */

  void Click (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save ();
      if (ClickBreadcrumb (cr, x, y)) {
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
      Transform (cr, width, height);
      cr.Translate (0.0, Zoomer.Y);
      cr.Scale(0.001, 0.001);
      double yr = Zoomer.Y * 1000.0;
      DirAction action = TopDir.Click (cr, yr, 1000.0, x, y);
      switch (action.Type) {
        case DirAction.Action.Open:
          Helpers.OpenFile(action.Path);
          break;
        case DirAction.Action.Navigate:
          BuildDirs (action.Path);
          break;
        case DirAction.Action.ZoomIn:
          cr.Save ();
            cr.IdentityMatrix ();
            ZoomBy(cr, width, height, x, y, 22.0 / action.Height);
          cr.Restore ();
          break;
      }
    cr.Restore ();
  }

  bool ClickBreadcrumb (Context cr, double x, double y)
  {
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.MoveTo (0.0, 0.0);
    FontSize = (BreadcrumbFontSize);
    double advance = 0.0;
    int hitIndex = 0;
    string[] segments = TopDirName.Split('/');
    if (TopDirName != "/") {
      foreach (string s in segments) {
        TextExtents te = Helpers.GetTextExtents (cr, FontSize, s + "/");
        if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
          string newDir = String.Join("/", segments, 0, hitIndex+1);
          if (newDir == "") newDir = "/";
          if (newDir != TopDirName)
            BuildDirs (newDir);
          return true;
        }
        advance += te.XAdvance;
        hitIndex += 1;
      }
    }
    return false;
  }

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

  bool ClickOpenTerminal (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    FontSize = ToolbarLabelFontSize;
    advance += Helpers.GetTextExtents (cr, FontSize, " |  ").XAdvance;
    te = Helpers.GetTextExtents (cr, FontSize, OpenTerminalLabel);
    if (Helpers.CheckTextExtents (cr, advance, te, x, y)) {
      Helpers.OpenTerminal(TopDirName);
      return true;
    }
    advance += te.XAdvance;
    return false;
  }


  /* Zooming and panning */

  void ResetZoom ()
  {
    Zoomer.ResetZoom ();
  }

  void ZoomBy (Context cr, uint width, uint height, double x, double y, double factor)
  {
    double xr = x, yr = y, nz = Math.Max(1.0, Zoomer.Z * factor);
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformPoint(ref xr, ref yr);
      double npy = (yr / nz) - (yr / Zoomer.Z) + (Zoomer.Y / Zoomer.Z);
      Zoomer.SetZoom (0.0, npy*nz, nz);
    cr.Restore ();
    UpdateLayout();
  }

  void ZoomToward (Context cr, uint width, uint height, double x, double y)
  {
    ZoomBy (cr, width, height, x, y, ZoomInSpeed);
  }

  void ZoomAway (Context cr, uint width, uint height, double x, double y)
  {
    ZoomBy (cr, width, height, x, y, 1.0 / ZoomOutSpeed);
  }

  void PanBy (Context cr, uint width, uint height, double dx, double dy)
  {
    double xr = dx, yr = dy;
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformDistance(ref xr, ref yr);
      Zoomer.Y += yr;
    cr.Restore ();
    UpdateLayout();
  }


  /* Event handlers */

  protected override bool OnButtonPressEvent (Gdk.EventButton e)
  {
    dragStartX = dragX = e.X;
    dragStartY = dragY = e.Y;
    dragging = false;
    return true;
  }

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

  /**
    The quit event handler. Calls Application.Quit.
  */
  void OnQuit (object sender, DeleteEventArgs e)
  {
    Application.Quit ();
  }
}


class SortHandler {
  public string Name;
  public IComparer<DirStats> Comparer;
  public SortHandler (string name, IComparer<DirStats> comparer) {
    Name = name;
    Comparer = comparer;
  }
}

class SizeHandler {
  public string Name;
  public IMeasurer Measurer;
  public SizeHandler (string name, IMeasurer measurer) {
    Name = name;
    Measurer = measurer;
  }
}



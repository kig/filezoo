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
  public double BreadcrumbMarginTop = 2;
  public double BreadcrumbMarginLeft = 12;

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

  // first frame latency profiler
  Profiler dirLatencyProfiler = new Profiler ();


  /* Constructor */

  public Filezoo (string dirname)
  {
    SortField = SortFields[0];
    SizeField = SizeFields[0];
    Zoomer = new FlatZoomer ();

    BuildDirs (dirname);

    AddEvents((int)(
        Gdk.EventMask.ButtonPressMask
      | Gdk.EventMask.ButtonReleaseMask
      | Gdk.EventMask.ScrollMask
      | Gdk.EventMask.PointerMotionMask
    ));
  }


  /* Files model */

  void BuildDirs (string dirname)
  {
    Profiler p = new Profiler ();
    dirLatencyProfiler.Restart ();
    if (dirname != "/") dirname = dirname.TrimEnd('/');
    UnixDirectoryInfo d = new UnixDirectoryInfo (dirname);
    CurrentDirPath = d.FullName;
    if (CurrentDir != null) CurrentDir.CancelTraversal ();
    CurrentDir = DirStats.Get (d);
    FirstFrameOfDir = true;
    ResetZoom ();
    UpdateSort ();
    p.Time("BuildDirs");
  }




  /* Layout */

  void UpdateLayout ()
  {
    LayoutUpdateRequested = true;
    QueueDraw ();
  }

  void UpdateSort ()
  {
    SortUpdateRequested = true;
    UpdateLayout ();
  }

  void RecreateLayout ()
  {
    Profiler p = new Profiler ();

    if (SortUpdateRequested) {
      CurrentDir.Comparer = SortField.Comparer;
      CurrentDir.SortDirection = SortDirection;
      CurrentDir.Sort ();
      SortUpdateRequested = false;
      p.Time ("CurrentDir.Sort");
    }

    CurrentDir.Measurer = SizeField.Measurer;
    CurrentDir.Zoomer = Zoomer;
    CurrentDir.Relayout ();
    p.Time ("CurrentDir.Relayout");
    LayoutUpdateRequested = !CurrentDir.Complete;
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
    if (LayoutUpdateRequested) RecreateLayout();

    cr.Save ();
      DrawClear (cr, width, height);
      DrawToolbars (cr);
      Transform (cr, width, height);
      DrawCurrentDir(cr);
    cr.Restore ();

    dirLatencyProfiler.Stop ();
    if (FirstFrameOfDir) {
      dirLatencyProfiler.Time ("Directory latency");
      FirstFrameOfDir = false;
      QueueDraw ();
    }
    if (LayoutUpdateRequested || !CurrentDir.Complete)
      UpdateLayout();
  }

  void DrawClear (Context cr, uint width, uint height)
  {
    cr.Color = new Color (1,1,1);
    cr.Rectangle (0,0, width, height);
    cr.Fill ();
  }

  void DrawToolbars (Context cr)
  {
    Profiler p = new Profiler ();
    cr.Save ();
      DrawBreadcrumb (cr);
      DrawSortBar (cr);
      DrawSizeBar (cr);
      DrawOpenTerminal (cr);
    cr.Restore ();
    p.Time ("DrawToolbars");
  }

  void DrawCurrentDir (Context cr)
  {
    Profiler p = new Profiler ();
    cr.Save ();
      double boxHeight = cr.Matrix.Yy;
      double boxTop = cr.Matrix.Y0;
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      uint c = CurrentDir.Draw (cr, boxTop, boxHeight, FirstFrameOfDir, 0);
    cr.Restore ();
    p.Time (String.Format("DrawCurrentDir: {0} entries", c));
  }

  void DrawBreadcrumb (Context cr)
  {
    cr.Color = new Color (0,0,1);
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.MoveTo (0.0, 0.0);
    FontSize = (BreadcrumbFontSize);
    if (CurrentDirPath == "/") {
      Helpers.DrawText (cr, FontSize, "/");
    } else {
      foreach (string s in CurrentDirPath.Split('/')) {
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
    Helpers.DrawText (cr, FontSize, "  ");
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
      ClickCurrentDir(cr, width, height, x, y);
    cr.Restore ();
  }

  void ClickCurrentDir (Context cr, uint width, uint height, double x, double y)
  {
    Transform (cr, width, height);
    double boxHeight = cr.Matrix.Yy;
    double boxTop = cr.Matrix.Y0;
    cr.Scale (1, Zoomer.Z);
    cr.Translate (0.0, Zoomer.Y);
    DirAction action = CurrentDir.Click (cr, boxTop, boxHeight, x, y, 0);
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

  bool ClickBreadcrumb (Context cr, double x, double y)
  {
    cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
    cr.MoveTo (0.0, 0.0);
    FontSize = (BreadcrumbFontSize);
    double advance = 0.0;
    int hitIndex = 0;
    string[] segments = CurrentDirPath.Split('/');
    if (CurrentDirPath != "/") {
      foreach (string s in segments) {
        TextExtents te = Helpers.GetTextExtents (cr, FontSize, s + "/");
        if (Helpers.CheckTextExtents(cr, advance, te, x, y)) {
          string newDir = String.Join("/", segments, 0, hitIndex+1);
          if (newDir == "") newDir = "/";
          if (newDir != CurrentDirPath)
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

  void ResetZoom ()
  {
    Zoomer.ResetZoom ();
  }

  void ZoomBy (Context cr, uint width, uint height, double x, double y, double factor)
  {
    double xr = x, yr = y, nz = Math.Max (1.0, Zoomer.Z * factor);
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformPoint(ref xr, ref yr);
      double npy = (yr / nz) - (yr / Zoomer.Z) + Zoomer.Y;
      Zoomer.SetZoom (0.0, npy, nz);
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
      Zoomer.Y += yr / Zoomer.Z;
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

}





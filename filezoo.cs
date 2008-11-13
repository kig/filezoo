using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Gtk;
using Cairo;
using Mono.Unix;

class Filezoo : DrawingArea
{
  public Pango.FontDescription UIFont = Pango.FontDescription.FromString ("Verdana");

  public double TopDirFontSize = 12;
  public double TopDirMarginTop = 24;
  public double TopDirMarginLeft = 12;

  public double ToolbarY = 19;
  public double ToolbarTitleFontSize = 6;
  public double ToolbarLabelFontSize = 9;

  public double FilesMarginLeft = 10;
  public double FilesMarginRight = 10;
  public double FilesMarginTop = 52;
  public double FilesMarginBottom = 10;

  private static Gtk.Window win = null;
  private string TopDirName = null;
  private double TotalSize = 0.0;
  private ArrayList Files = null;

  double ZoomSpeed = 1.5;
  bool LayoutUpdateRequested = true;

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
  public bool SortDesc = false;
  public IZoomer Zoomer;

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
    Zoomer = new FlatZoomer ();
    Files = new ArrayList();
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
    TopDirName = System.IO.Path.GetFullPath(dirname);
    foreach (DirStats f in Files)
      f.TraversalCancelled = true;
    Files = GetDirStats (dirname);
    ResetZoom ();
    UpdateLayout();
  }

  ArrayList GetDirStats (string dirname)
  {
    ArrayList stats = new ArrayList ();
    UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
    UnixFileSystemInfo[] files = di.GetFileSystemEntries ();
    foreach (UnixFileSystemInfo f in files)
      stats.Add (new DirStats (f));
    return stats;
  }


  /* Layout */

  void UpdateLayout ()
  {
    LayoutUpdateRequested = true;
    win.QueueDraw ();
  }

  void ReCreateLayout ()
  {
    IZoomer zoomer = Zoomer;
    IMeasurer measurer = SizeField.Measurer;
    IComparer comparer = SortField.Comparer;

    Files.Sort(comparer);
    if (SortDesc) Files.Reverse();

    double totalHeight = 0.0;
    foreach (DirStats f in Files) {
      double height = measurer.Measure(f);
      f.Height = height;
      totalHeight += height;
    }
    double position = 0.0;
    bool trav = false;
    foreach (DirStats f in Files) {
      double zoom = zoomer.GetZoomAt(position);
      f.Zoom = zoom;
      f.Scale = 1.0 / totalHeight;
      position += f.Height / totalHeight;
      trav = (trav || f.TraversalInProgress);
    }
    if (!trav) {
      LayoutUpdateRequested = false;
    }
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
    if (LayoutUpdateRequested) ReCreateLayout();
    cr.Save ();
      cr.Color = new Color (1,1,1);
      cr.Rectangle (0,0, width, height);
      cr.Fill ();
      cr.Save ();
        DrawTopDir (cr);
        DrawSortBar (cr);
        DrawSizeBar (cr);
        DrawOpenTerminal (cr);
      cr.Restore ();
      Transform (cr, width, height);
      cr.Translate (0.0, Zoomer.Y);
      cr.LineWidth = 0.001;
      bool trav = LayoutUpdateRequested;
      foreach (DirStats d in Files) {
        d.Draw (cr);
        cr.Translate (0, d.GetScaledHeight ());
        trav = (trav || d.TraversalInProgress);
      }
    cr.Restore ();
    if (trav) UpdateLayout();
  }

  void DrawTopDir (Context cr)
  {
    cr.Color = new Color (0,0,1);
    cr.Translate (TopDirMarginLeft, TopDirMarginTop);
    cr.MoveTo (0.0, 0.0);
    FontSize = (TopDirFontSize);
    if (TopDirName == "/") {
      DrawText (cr, "/");
    } else {
      foreach (string s in TopDirName.Split('/')) {
        DrawText (cr, s);
        DrawText (cr, "/");
      }
    }
  }

  void DrawSortBar (Context cr)
  {
    cr.MoveTo (0.0, ToolbarY);
    cr.Color = ActiveColor;
    FontSize = ToolbarTitleFontSize;
    cr.RelMoveTo (0, -1);
    DrawText (cr, SortLabel);
    cr.RelMoveTo (0, 1);
    FontSize = ToolbarLabelFontSize;
    foreach (SortHandler sf in SortFields) {
      cr.Color = (SortField == sf) ? ActiveColor : InActiveColor;
      DrawText (cr, sf.Name);
      DrawText (cr, " ");
    }
    cr.Color = ActiveColor;
    DrawText (cr, " ");
    DrawText (cr, (SortDesc ? "▾" : "▴") + " ");
    DrawText (cr, " ");
  }

  void DrawSizeBar (Context cr)
  {
    cr.Color = ActiveColor;
    FontSize = ToolbarTitleFontSize;
    cr.RelMoveTo (0, -1);
    DrawText (cr, SizeLabel);
    cr.RelMoveTo (0, 1);
    FontSize = ToolbarLabelFontSize;
    foreach (SizeHandler sf in SizeFields) {
      cr.Color = (SizeField == sf) ? ActiveColor : InActiveColor;
      DrawText (cr, sf.Name);
      DrawText (cr, " ");
    }
    DrawText (cr, " ");
  }

  void DrawOpenTerminal (Context cr)
  {
    FontSize = ToolbarLabelFontSize;
    cr.Color = InActiveColor;
    DrawText (cr, " |  ");
    cr.Color = TermColor;
    DrawText (cr, OpenTerminalLabel);
  }

  void DrawText (Context cr, string text)
  {
    Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
    UIFont.Size = (int)(FontSize * Pango.Scale.PangoScale);
    layout.FontDescription = UIFont;
    layout.SetText (text);
    Pango.Rectangle pe, le;
    layout.GetExtents(out pe, out le);
    double w = (double)le.Width / (double)Pango.Scale.PangoScale,
           h = (double)le.Height / (double)Pango.Scale.PangoScale;
    cr.RelMoveTo (0, -h);
    Pango.CairoHelper.ShowLayout (cr, layout);
    cr.RelMoveTo (w, h);
  }

  TextExtents GetTextExtents (Context cr, string text)
  {
    Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
    UIFont.Size = (int)(FontSize * Pango.Scale.PangoScale);
    layout.FontDescription = UIFont;
    layout.SetText (text);
    Pango.Rectangle pe, le;
    layout.GetExtents(out pe, out le);
    double w = (double)le.Width / (double)Pango.Scale.PangoScale,
           h = (double)le.Height / (double)Pango.Scale.PangoScale;
    TextExtents te = new TextExtents ();
    te.Height = h;
    te.Width = w;
    te.XAdvance = w;
    te.YAdvance = 0;
    return te;
  }


  /* Click handling */

  void Click (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save ();
      if (ClickTopDir (cr, x, y)) {
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
      foreach (DirStats d in Files) {
        bool[] action = d.Click (cr, TotalSize, x, y);
        if (action[0]) {
          if (action[1]) {
            BuildDirs (d.GetFullPath ());
          }
          win.QueueDraw();
          break;
        }
        cr.Translate (0, d.GetScaledHeight ());
      }
    cr.Restore ();
  }

  bool ClickTopDir (Context cr, double x, double y)
  {
    cr.Translate (TopDirMarginLeft, TopDirMarginTop);
    cr.MoveTo (0.0, 0.0);
    FontSize = (TopDirFontSize);
    double advance = 0.0;
    int hitIndex = 0;
    string[] segments = TopDirName.Split('/');
    if (TopDirName != "/") {
      foreach (string s in segments) {
        TextExtents e = GetTextExtents (cr, s + "/");
        cr.Save ();
          cr.NewPath ();
          cr.Rectangle (advance, -e.Height, e.XAdvance, e.Height);
          cr.IdentityMatrix ();
          if (cr.InFill (x, y)) {
            string newDir = String.Join("/", segments, 0, hitIndex+1);
            if (newDir == "") newDir = "/";
            if (newDir != TopDirName)
              BuildDirs (newDir);
            return true;
          }
        cr.Restore ();
        advance += e.XAdvance;
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
    advance += GetTextExtents (cr, SortLabel).XAdvance;
    FontSize = ToolbarLabelFontSize;
    foreach (SortHandler sf in SortFields) {
      te = GetTextExtents (cr, sf.Name);
      if (CheckTextExtents(cr, advance, te, x, y)) {
        if (sf == SortField) {
          SortDesc = !SortDesc;
        } else {
          SortField = sf;
        }
        ResetZoom ();
        UpdateLayout ();
        return true;
      }
      advance += te.XAdvance;
      advance += GetTextExtents (cr, " ").XAdvance;
    }
    advance += GetTextExtents (cr, " ").XAdvance;
    te = GetTextExtents (cr, (SortDesc ? "▾" : "▴") + " ");
    if (CheckTextExtents(cr, advance, te, x, y)) {
      SortDesc = !SortDesc;
      ResetZoom ();
      UpdateLayout ();
      return true;
    }
    advance += te.XAdvance;
    advance += GetTextExtents (cr, " ").XAdvance;
    return false;
  }

  bool ClickSizeBar (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    FontSize = ToolbarTitleFontSize;
    advance += GetTextExtents (cr, SizeLabel).XAdvance;
    FontSize = ToolbarLabelFontSize;
    foreach (SizeHandler sf in SizeFields) {
      te = GetTextExtents (cr, sf.Name);
      if (CheckTextExtents(cr, advance, te, x, y)) {
        SizeField = sf;
        ResetZoom ();
        UpdateLayout ();
        return true;
      }
      advance += te.XAdvance;
      advance += GetTextExtents (cr, " ").XAdvance;
    }
    advance += GetTextExtents (cr, " ").XAdvance;
    return false;
  }

  bool ClickOpenTerminal (ref double advance, Context cr, double x, double y)
  {
    TextExtents te;
    FontSize = ToolbarLabelFontSize;
    advance += GetTextExtents (cr, " |  ").XAdvance;
    te = GetTextExtents (cr, OpenTerminalLabel);
    if (CheckTextExtents (cr, advance, te, x, y)) {
      string cd = UnixDirectoryInfo.GetCurrentDirectory ();
      UnixDirectoryInfo.SetCurrentDirectory (TopDirName);
      Process.Start ("urxvt");
      UnixDirectoryInfo.SetCurrentDirectory (cd);
      return true;
    }
    advance += te.XAdvance;
    return false;
  }

  bool CheckTextExtents (Context cr, double advance, TextExtents te, double x, double y)
  {
    bool retval = false;
    cr.Save ();
      cr.Rectangle (advance, -te.Height, te.Width, te.Height * 1.5);
      cr.IdentityMatrix ();
      retval = cr.InFill (x, y);
    cr.Restore ();
    return retval;
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
    ZoomBy (cr, width, height, x, y, ZoomSpeed);
  }

  void ZoomAway (Context cr, uint width, uint height, double x, double y)
  {
    ZoomBy (cr, width, height, x, y, 1 / ZoomSpeed);
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
  public IComparer Comparer;
  public SortHandler (string name, IComparer comparer) {
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



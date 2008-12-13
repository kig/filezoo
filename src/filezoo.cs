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
    new SizeHandler("Entries", new EntriesMeasurer()),
//     new SizeHandler("Count", new CountMeasurer()),
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

  private static TargetEntry [] target_table = new TargetEntry [] {
    new TargetEntry ("text/uri-list", 0, 0),
    new TargetEntry ("application/x-color", 0, 1),
    new TargetEntry ("text/plain", 0, 2),
    new TargetEntry ("application/octet-stream", 0, 2),
    new TargetEntry ("STRING", 0, 2)
  };

  /** BLOCKING - startup dir latency */
  public Filezoo (string dirname)
  {
    Renderer = new FSDraw ();

    SortField = SortFields[0];
    SizeField = SizeFields[0];
    Zoomer = new FlatZoomer ();

    CurrentDirPath = dirname;

    /** DESCTRUCTIVE */
    DragDataReceived += delegate (object sender, DragDataReceivedArgs e) {
      string type = e.SelectionData.Type.Name;
      Gdk.DragAction action = e.Context.SuggestedAction;
      string targetPath = FindHit (Width, Height, e.X, e.Y, 8).Target.FullName;
      if (type == "application/x-color") {
        /** DESCTRUCTIVE */
        Console.WriteLine ("Would set {0} color to {1}", targetPath, BitConverter.ToString(e.SelectionData.Data));
      } else if (type == "text/uri-list" || (type == "text/plain" && Helpers.IsURI(e.SelectionData.Text))) {
        /** DESCTRUCTIVE */
        string data = Helpers.BytesToASCII(e.SelectionData.Data);
        string[] uris = data.Split(new char[] {'\r','\n','\0'}, StringSplitOptions.RemoveEmptyEntries);
        if (action == Gdk.DragAction.Move) {
          moveUris(uris, targetPath);
        } else if (action == Gdk.DragAction.Copy) {
          copyUris(uris, targetPath);
        } else if (action == Gdk.DragAction.Ask) {
          DragURIMenu(uris, targetPath);
        }
      } else {
        /** DESCTRUCTIVE */
        if (Helpers.IsDir(targetPath)) {
          DragDataToCreateFileMenu(targetPath, e.SelectionData.Data);
        } else {
          DragDataToFileMenu(targetPath, e.SelectionData.Data);
        }
      }
    };

    Gtk.Drag.DestSet (this, DestDefaults.All, target_table,
        Gdk.DragAction.Move
      | Gdk.DragAction.Copy
      | Gdk.DragAction.Ask
    );

    AddEvents((int)(
        Gdk.EventMask.ButtonPressMask
      | Gdk.EventMask.ButtonReleaseMask
      | Gdk.EventMask.ScrollMask
      | Gdk.EventMask.PointerMotionMask
      | Gdk.EventMask.EnterNotifyMask
      | Gdk.EventMask.LeaveNotifyMask
    ));

    ThreadStart ts = new ThreadStart (PreDrawCallback);
    Thread t = new Thread(ts);
    t.IsBackground = true;
    t.Start ();

  }

  /** DESCTRUCTIVE, BLOCKING */
  void moveUris (string[] uris, string targetPath)
  {
    targetPath = Helpers.IsDir ( targetPath ) ? targetPath : Helpers.Dirname (targetPath);
    Helpers.MoveURIs(uris, targetPath);
  }

  /** DESCTRUCTIVE, BLOCKING */
  void copyUris (string[] uris, string targetPath)
  {
    targetPath = Helpers.IsDir ( targetPath ) ? targetPath : Helpers.Dirname (targetPath);
    Helpers.CopyURIs(uris, targetPath);
  }

  void DragURIMenu (string[] sources, string target)
  {
    Menu menu = new Menu();
    MenuItem move = new MenuItem("_Move");
    MenuItem copy = new MenuItem("_Copy");
    move.Activated += delegate { moveUris (sources, target); };
    copy.Activated += delegate { copyUris (sources, target); };
    menu.Append (move);
    menu.Append (copy);
    menu.ShowAll ();
    menu.Popup ();
  }

  /** DESCTRUCTIVE, BLOCKING */
  void DragDataToCreateFileMenu (string target, byte[] data)
  {
    Menu menu = new Menu();
    MenuItem newfile = new MenuItem("_Create file from dragged data");
    newfile.Activated += delegate {
      Helpers.TextPrompt ("Create file", "Filename for dragged data",
        target + Helpers.DirSepS + "new_file", "Create",
        target.Length+1, target.Length+1, -1,
        delegate (string filename) {
          Helpers.NewFileWith(filename, data);
        });
    };
    menu.Append (newfile);
    menu.ShowAll ();
    menu.Popup ();
  }

  /** DESCTRUCTIVE, BLOCKING */
  void DragDataToFileMenu (string targetFile, byte[] data)
  {
    Menu menu = new Menu();
    MenuItem newfile = new MenuItem("_Create file from dragged data");
    string target = Helpers.Dirname(targetFile);
    newfile.Activated += delegate {
      Helpers.TextPrompt ("Create file", "Filename for dragged data",
        target + Helpers.DirSepS + "new_file", "Create",
        target.Length+1, target.Length+1, -1,
        delegate (string filename) {
          Helpers.NewFileWith(filename, data);
        });
    };
    MenuItem replace = new MenuItem(String.Format("_Replace contents of {0}", Helpers.Basename(targetFile)));
    replace.Activated += delegate { Helpers.ReplaceFileWith(targetFile, data); };
    MenuItem append = new MenuItem(String.Format("_Append to {0}", Helpers.Basename(targetFile)));
    append.Activated += delegate { Helpers.AppendToFile(targetFile, data); };

    menu.Append (newfile);
    menu.Append (new SeparatorMenuItem ());
    menu.Append (replace);
    menu.Append (append);
    menu.ShowAll ();
    menu.Popup ();
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
    GLib.Timeout.Add (16, new GLib.TimeoutHandler(CheckRedraw));
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

  bool CheckRedraw ()
  {
    if (!PreDrawComplete || NeedRedraw) {
      NeedRedraw = false;
      FSNeedRedraw = true;
      QueueDraw ();
    }
    return true;
  }

  void CheckUpdates (object source, ElapsedEventArgs e)
  {
    if (LastRedraw != FSCache.LastChange) {
      LastRedraw = FSCache.LastChange;
      PreDraw ();
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
      FSCache.Measurer = SizeField.Measurer;
      FSCache.SortDirection = SortDirection;
      FSCache.Comparer = SortField.Comparer;
      PreDrawInProgress = true;
    }
    PreDrawComplete = false;
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
      cr.NewPath ();
      cr.IdentityMatrix ();
      DrawToolbars (cr, width, height);
      cr.NewPath ();
      cr.IdentityMatrix ();
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

  double FirstProgress = 0;
  double LastProgress = 0;
  double CylinderRotation = 0;
  void DrawBackground (Context cr, uint width, uint height)
  {
    cr.Save ();
      DrawClear (cr, width, height);
      double t = DateTime.Now.ToFileTime() / 1e7;
      cr.Save ();
        Color ca = Renderer.DirectoryBGColor;
        ca.A = 0.3;
        cr.Color = ca;
        cr.LineWidth = 0.5;
        cr.Translate (250, -50);
        cr.Rotate (0.1);
        for (double y=0; y<6; y++) {
        for (double i=0; i<6-y; i++) {
          cr.Save ();
            double hr = 45;
            double iscale = Math.Sin(-i/20 * Math.PI*2 + CylinderRotation);
            double xscale = Math.Cos(-i/20 * Math.PI*2 + 0.1 + CylinderRotation);
            cr.Translate (iscale * hr * 3, -100 + y*hr*(2*1.732) + hr*(i%2)*1.732);
            hr = 40;
            cr.Scale (xscale, 1);
            cr.NewPath ();
            cr.MoveTo (0, -hr);
            cr.LineTo (0.866*hr, -0.5*hr);
            cr.LineTo (0.866*hr, 0.5*hr);
            cr.LineTo (0, hr);
            cr.LineTo (-0.866*hr, 0.5*hr);
            cr.LineTo (-0.866*hr, -0.5*hr);
            cr.ClosePath ();
            cr.Color = ca;
            cr.Stroke ();
            cr.Color = Renderer.SocketColor;
            cr.Translate (0.75 * -0.866*hr, 0.75 * -0.5*hr);
            double x2 = cr.Matrix.X0, y2 = cr.Matrix.Y0;
            cr.IdentityMatrix ();
            cr.Arc (x2,y2, 1, 0, Math.PI*2);
            cr.Fill ();
          cr.Restore ();
        }
        }
        CylinderRotation += 0.02;
      cr.Restore ();
      if (CurrentDirEntry.InProgress || (t - LastProgress < 3)) {
        if (FirstProgress == 0) { FirstProgress = LastProgress = t; }
        if (CurrentDirEntry.InProgress) LastProgress = t;
        double opacity = Math.Min(3, t-FirstProgress) - Math.Max(0, t-LastProgress);
        t = (t * 0.1) % Math.PI*2;
        Color c = Renderer.UnfinishedDirectoryColor;
        c.A = 0.1*opacity;
        cr.Color = c;
        cr.LineWidth = 1;
        double n = 6;
        double af = Math.PI*2/n;
        double r = 450*(4.4+0.3*cosScale(opacity/3));
        for (double i=0; i<n; i++) {
          cr.Arc (-450*4, 1000/4, r, t+i*af, t+(i+0.7)*af);
          cr.Stroke ();
        }
        for (double i=0; i<n; i++) {
          cr.Arc (-450*4, 1000/4, r+5, -t+i*af, -t+(i+0.7)*af);
          cr.Stroke ();
        }
        if (CurrentDirEntry.InProgress) {
          cr.NewPath ();
            // find FSCache.LastTraversed [or ancestor] y position from model
            // draw line there
          cr.NewPath ();
        }
        UpdateLayout ();
      } else {
        FirstProgress = 0;
        LastProgress = 0;
      }
    cr.Restore ();
  }

  double cosScale (double n) {
    return 0.5*(1-Math.Cos(n*Math.PI));
  }

  /** FAST */
  void DrawToolbars (Context cr, uint width, uint height)
  {
    Profiler p = new Profiler ();
    cr.Save ();
      cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
      DrawBreadcrumb (cr, width);
      cr.Translate (0, ToolbarY);
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

  string rootChar = "/";
  string dirSep = "/";

  /** FAST */
  void DrawBreadcrumb (Context cr, uint width)
  {
    cr.NewPath ();
    cr.MoveTo (0.0, 0.0);
    Profiler p = new Profiler ();
    p.Time("In breadcrumb");
    TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, String.Join(dirSep, CurrentDirPath.Split(Helpers.DirSepC)) + dirSep);
    p.Time("GetTextExtents");
    cr.Color = Renderer.DirectoryFGColor;
    cr.Save ();
      double areaWidth = width-BreadcrumbMarginLeft-BreadcrumbMarginRight;
      cr.Rectangle (0,0,areaWidth, te.Height);
      cr.Clip ();
      cr.Translate (Math.Min(0,areaWidth-te.Width), 0);
      cr.MoveTo (0.0, 0.0);
      if (CurrentDirPath == Helpers.RootDir) {
        Helpers.DrawText (cr, BreadcrumbFontFamily, BreadcrumbFontSize, rootChar);
      } else {
    p.Time("start DrawText");
        foreach (string s in CurrentDirPath.Split(Helpers.DirSepC)) {
          Helpers.DrawText (cr, BreadcrumbFontFamily, BreadcrumbFontSize, s == "" ? rootChar : (s+dirSep));
        }
    p.Time("DrawText");
      }
    cr.Restore ();
  }

  /** FAST */
  void DrawSortBar (Context cr)
  {
    cr.NewPath ();
    cr.MoveTo (0.0, 0.0);
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
    bool SortDesc = (SortDirection == SortingDirection.Descending);
    Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " " + (SortDesc ? "▾" : "▴") + "  ");
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
  }



  /* Click handling */

  /** BLOCKING */
  void Click (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save ();
      cr.Translate (BreadcrumbMarginLeft, BreadcrumbMarginTop);
      cr.Operator = Operator.Dest;
      if (ClickBreadcrumb (cr, width, x, y)) {
        cr.Restore ();
        return;
      }
      cr.Translate (0, ToolbarY);
      if (
        ClickSortBar (cr, x, y) ||
        ClickSizeBar (cr, x, y)
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
        // Console.WriteLine("ZoomIn {0}x", nz);
        cr.Save ();
          cr.IdentityMatrix ();
          ZoomBy(cr, width, height, x, y, nz);
        cr.Restore ();
        break;
      } else {
        if (c.Target.IsDirectory) {
          // Console.WriteLine("Navigate {0}", c.Target.FullName);
          SetCurrentDir (c.Target.FullName);
          ResetZoom ();
          UpdateLayout ();
        } else {
          // Console.WriteLine("Open {0}", c.Target.FullName);
          Helpers.OpenFile(c.Target.FullName);
        }
        break;
      }
    }
  }

  /** FAST */
  bool ClickBreadcrumb (Context cr, uint width, double x, double y)
  {
    if (CurrentDirPath == Helpers.RootDir) return false;
    TextExtents te1 = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, String.Join(dirSep, CurrentDirPath.Split(Helpers.DirSepC)) + dirSep);
    cr.Save ();
      double areaWidth = width-BreadcrumbMarginLeft-BreadcrumbMarginRight;
      cr.Rectangle (0,0,areaWidth, te1.Height);
      cr.Clip ();
      cr.Translate (Math.Min(0,areaWidth-te1.Width), 0);
      cr.MoveTo (0.0, 0.0);
      int hitIndex = 0;
      string[] segments = CurrentDirPath.Split(Helpers.DirSepC);
      foreach (string s in segments) {
        string name = (s == "") ? rootChar : s+dirSep;
        TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, name);
        if (Helpers.CheckTextExtents(cr, te, x, y)) {
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
        cr.RelMoveTo( te.XAdvance, 0 );
        hitIndex += 1;
      }
    cr.Restore ();
    return false;
  }

  /** FAST */
  bool ClickSortBar (Context cr, double x, double y)
  {
    cr.NewPath ();
    cr.MoveTo (0.0, 0.0);
    TextExtents te;
    cr.Color = ActiveColor;
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * 0.4 );
    Helpers.DrawText (cr, ToolbarTitleFontFamily, ToolbarTitleFontSize, SortLabel);
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * -0.4 );
    foreach (SortHandler sf in SortFields) {
      cr.Color = (SortField == sf) ? ActiveColor : InActiveColor;
      te = Helpers.GetTextExtents (
        cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      if (Helpers.CheckTextExtents(cr, te, x, y)) {
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
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    }
    cr.Color = ActiveColor;
    bool SortDesc = (SortDirection == SortingDirection.Descending);
    te = Helpers.GetTextExtents (
      cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " " + (SortDesc ? "▾" : "▴") + "  ");
    if (Helpers.CheckTextExtents(cr, te, x, y)) {
      SortDirection = (SortDirection == SortingDirection.Ascending) ?
                      SortingDirection.Descending :
                      SortingDirection.Ascending;
      ResetZoom ();
      UpdateLayout ();
      return true;
    }
    Helpers.DrawText (
      cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " " + (SortDesc ? "▾" : "▴") + "  ");
    return false;
  }

  /** FAST */
  bool ClickSizeBar (Context cr, double x, double y)
  {
    TextExtents te;
    cr.Color = ActiveColor;
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * 0.4 );
    Helpers.DrawText (cr, ToolbarTitleFontFamily, ToolbarTitleFontSize, SizeLabel);
    cr.RelMoveTo ( 0.0, ToolbarLabelFontSize * -0.4 );
    foreach (SizeHandler sf in SizeFields) {
      cr.Color = (SizeField == sf) ? ActiveColor : InActiveColor;
      te = Helpers.GetTextExtents (
        cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      if (Helpers.CheckTextExtents(cr, te, x, y)) {
        SizeField = sf;
        ResetZoom ();
        UpdateLayout ();
        return true;
      }
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    }
    Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    return false;
  }


  public List<ClickHit> FindHits (uint width, uint height, double x, double y)
  {
    List<ClickHit> hits;
    using (Context cr = new Context (CachedSurface)) {
      cr.IdentityMatrix ();
      cr.Save();
        Rectangle box = Transform (cr, width, height);
        cr.Scale (1, Zoomer.Z);
        cr.Translate (0.0, Zoomer.Y);
        hits = Renderer.Click (CurrentDirEntry, cr, box, x, y);
        hits.Add (new ClickHit(CurrentDirEntry, cr.Matrix.Yy));
      cr.Restore ();
    }
    return hits;
  }

  public ClickHit FindHit (uint width, uint height, double x, double y, double minHeight)
  {
    List<ClickHit> hits = FindHits(width, height, x, y);
    ClickHit ch = hits[hits.Count-1];
    foreach (ClickHit c in hits) {
      if (c.Height > minHeight) {
        ch = c;
        break;
      }
    }
    return ch;
  }


  /* Context menu */


  /** BLOCKING */
  void ContextClick (uint width, uint height, double x, double y)
  {
    if (ContextMenu != null) ContextMenu.Dispose ();
    ContextMenu = new FilezooContextMenu (this, FindHit(width, height, x, y, 8));
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
      int w, h;
      e.Window.GetSize (out w, out h);
      ContextClick ((uint)w, (uint)h, e.X, e.Y);
    }
    return true;
  }

  /** BLOCKING */
  protected override bool OnButtonReleaseEvent (Gdk.EventButton e)
  {
    if (e.Button == 1 && !dragging) {
      InteractionProfiler.Restart ();
      int w, h;
      e.Window.GetSize (out w, out h);
      using (Context scr = new Context (CachedSurface)) {
        scr.IdentityMatrix ();
        Click (scr, (uint)w, (uint)h, e.X, e.Y);
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
    flareTargetX = Width/2;
    flareTargetY = -100;
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
      bool sizeChanged = false;
      if (Width != (uint)w || Height != (uint)h || CachedSurface == null) {
        if (CachedSurface != null) CachedSurface.Destroy ();
        CachedSurface = new ImageSurface(Format.ARGB32, w, h);
        sizeChanged = true;
        Width = (uint) w;
        Height = (uint) h;
        if (InitComplete) UpdateLayout ();
      }
      if (!InitComplete) {
        CompleteInit ();
      }
      if (sizeChanged || (!EffectInProgress && FSNeedRedraw)) {
        FSNeedRedraw = false;
        using (Context scr = new Context (CachedSurface)) {
          scr.Save ();
            scr.Operator = Operator.Source;
            scr.SetSourceRGBA (0,0,0,0);
            scr.Paint ();
          scr.Restore ();
          Draw (scr, Width, Height);
        }
      }
      cr.Save ();
        using (Pattern p = new Pattern (CachedSurface)) {
          cr.Operator = Operator.Over;
          DrawBackground (cr, Width, Height);
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

  double flareTargetX = 200;
  double flareTargetY = -200;

  bool SillyFlare = false;

  bool DrawEffects  (Context cr, uint w, uint h)
  {
    if (!SillyFlare) return false;
    if (FlareGradient == null) {
      FGRadius = Helpers.ImageWidth(FlareGradientImage);
      FlareGradient = Helpers.RadialGradientFromImage(FlareGradientImage);
      BlackGradient = new RadialGradient(0,0,0, 0,0,FGRadius);
      BlackGradient.AddColorStop(0, new Color(0,0,0,1));
      BlackGradient.AddColorStop(1, new Color(0,0,0,0));
      FlareSpike = new ImageSurface(FlareSpikeImage);
      RainbowSprite = new ImageSurface(RainbowSpriteImage);
    }
    cr.Save ();
//       double t = DateTime.Now.ToFileTime() / 1e7;
      double dx = flareTargetX - flareX;
      double dy = flareTargetY - flareY;
      flareX += dx / 20;
      flareY += dy / 20;
      double s = Math.Min(1, Math.Max(0.02, 0.35 / (1 + 0.002*(dx*dx + dy*dy))));
      if (s < 0.03)
        s *= 1 + rng.NextDouble();
      cr.Translate(flareX, flareY);
      cr.Save ();
        cr.Scale (s, s);
        cr.Arc(0, 0, FGRadius, 0, Math.PI * 2);
/*        cr.Source = BlackGradient;
        cr.Operator = Operator.Over;
        cr.FillPreserve ();*/
        cr.Source = FlareGradient;
        cr.Operator = Operator.Add;
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

  string FlareGradientImage = "res/flare_gradient.png";
  string FlareSpikeImage = "res/flare_spike.png";
  string RainbowSpriteImage = "res/rainbow_sprite.png";
  RadialGradient FlareGradient = null;
  RadialGradient BlackGradient;
  int FGRadius;
  ImageSurface FlareSpike;
  ImageSurface RainbowSprite;

  bool EffectInProgress = false;

  ImageSurface CachedSurface;

}





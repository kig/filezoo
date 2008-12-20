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
  public double ZoomInSpeed = 2;
  public double ZoomOutSpeed = 2;

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
  Profiler InteractionProfiler = new Profiler ("UI", 30);

  // empty surface for PreDraw context.
  ImageSurface PreDrawSurface = new ImageSurface (Format.A1, 1, 1);

  // empty surface for etc context.
  ImageSurface EtcSurface = new ImageSurface (Format.A1, 1, 1);


  // modification monitor
  DateTime LastRedraw = DateTime.Now;

  bool PreDrawComplete = true;

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

  TargetEntry[] targets = new TargetEntry[] {
    new TargetEntry ("text/uri-list", 0, 0),
    new TargetEntry ("text/plain", 0, 2),
    new TargetEntry ("STRING", 0, 2)
  };

  FSEntry DragSourceEntry;
  string DragSourcePath;

  public bool Cancelled = false;

  public Dictionary<string,bool> Selection = new Dictionary<string,bool> ();

  Clipboard clipboard;

  Gdk.Cursor clickCursor = new Gdk.Cursor (Gdk.CursorType.Hand2);

  Gdk.Cursor defaultCursor = new Gdk.Cursor (Gdk.CursorType.Arrow);

  Gdk.Cursor panCursor = new Gdk.Cursor (Gdk.CursorType.Hand1);
  Gdk.Cursor dragCursor = new Gdk.Cursor (Gdk.CursorType.Hand1);

  Gdk.Cursor copyCursor = new Gdk.Cursor (Gdk.CursorType.LeftPtr);
  Gdk.Cursor shiftCopyCursor = new Gdk.Cursor (Gdk.CursorType.RightPtr);
  Gdk.Cursor clearSelCursor = new Gdk.Cursor (Gdk.CursorType.Circle);

  /** BLOCKING - startup dir latency */
  public Filezoo (string dirname)
  {
    Selection = new Dictionary<string, bool> ();
    Renderer = new FSDraw ();

    clipboard = Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", true));

    SortField = SortFields[0];
    SizeField = SizeFields[0];
    Zoomer = new FlatZoomer ();

    SetCurrentDir (dirname);

    Helpers.StartupProfiler.Time ("SetCurrentDir");

    CanFocus = true;
    KeyPressEvent += delegate (object o, KeyPressEventArgs args) {
      Cancelled = true;
      Gdk.ModifierType state = args.Event.State;
      switch (args.Event.Key) {
        case Gdk.Key.Control_L:
        case Gdk.Key.Control_R:
          state |= Gdk.ModifierType.ControlMask;
          break;
        case Gdk.Key.Shift_L:
        case Gdk.Key.Shift_R:
          state |= Gdk.ModifierType.ShiftMask;
          break;
        case Gdk.Key.Alt_L:
        case Gdk.Key.Alt_R:
          state |= Gdk.ModifierType.Mod1Mask;
          break;
      }
      SetCursor (state);
    };

    KeyReleaseEvent += delegate (object o, KeyReleaseEventArgs args) {
      Cancelled = true;
      if (args.Event.Key == Gdk.Key.Escape && Selection.Count > 0) {
        ClearSelection ();
        args.RetVal = true;
      } else if ((args.Event.State & Gdk.ModifierType.ControlMask) == Gdk.ModifierType.ControlMask) {
        switch (args.Event.Key) {
          case Gdk.Key.x:
            CutSelection(CurrentDirPath);
            break;
          case Gdk.Key.c:
            CopySelection(CurrentDirPath);
            break;
          case Gdk.Key.v:
            PasteSelection(CurrentDirPath);
            break;
        }
      } else {
        switch (args.Event.Key) {
          case Gdk.Key.Delete:
            TrashSelection ();
            break;
          case Gdk.Key.BackSpace:
            GoToParent ();
            break;
          case Gdk.Key.Home:
            SetCurrentDir (Helpers.HomeDir);
            break;
        }
      }
      Gdk.ModifierType state = args.Event.State;
      switch (args.Event.Key) {
        case Gdk.Key.Control_L:
        case Gdk.Key.Control_R:
          state &= ~Gdk.ModifierType.ControlMask;
          break;
        case Gdk.Key.Shift_L:
        case Gdk.Key.Shift_R:
          state &= ~Gdk.ModifierType.ShiftMask;
          break;
        case Gdk.Key.Alt_L:
        case Gdk.Key.Alt_R:
          state &= ~Gdk.ModifierType.Mod1Mask;
          break;
      }
      SetCursor (state);
    };

    DragDataGet += delegate (object o, DragDataGetArgs args) {
      string items = "file://" + DragSourcePath;
      if (Selection.Count > 0 && Selection.ContainsKey(DragSourcePath))
        items = GetSelectionData ();
      args.SelectionData.Set(args.SelectionData.Target, 8, System.Text.Encoding.UTF8.GetBytes(items));
      args.SelectionData.Text = items;
    };

    DragEnd += delegate {
      GetSelectionData ();
      Cancelled = true;
      dragInProgress = false;
      DragSourceEntry = null;
      DragSourcePath = null;
    };

    /** DESCTRUCTIVE */
    DragDataReceived += delegate (object sender, DragDataReceivedArgs e) {
      Cancelled = true;
      string targetPath = FindHit (Width, Height, e.X, e.Y, 8).Target.FullName;
      HandleSelectionData (e.SelectionData, e.Context.SuggestedAction, targetPath);
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
      | Gdk.EventMask.KeyPressMask
      | Gdk.EventMask.KeyReleaseMask
      | Gdk.EventMask.LeaveNotifyMask
    ));

    ThreadStart ts = new ThreadStart (PreDrawCallback);
    Thread t = new Thread(ts);
    t.IsBackground = true;
    t.Start ();

    System.Timers.Timer t1 = new System.Timers.Timer(50);
    t1.Elapsed += new ElapsedEventHandler (CheckUpdates);
    System.Timers.Timer t2 = new System.Timers.Timer(1000);
    t2.Elapsed += new ElapsedEventHandler (LongMonitor);
    t1.Enabled = true;
    t2.Enabled = true;
    GLib.Timeout.Add (16, new GLib.TimeoutHandler(CheckRedraw));
    Helpers.StartupProfiler.Total ("Pre-drawing startup");

  }

  void GoToParent () {
    if (CurrentDirPath != Helpers.RootDir) {
      SetCurrentDir (Helpers.Dirname(CurrentDirPath));
    }
  }

  void SetCursor (Gdk.ModifierType state) {
    if (dragInProgress) {
      GdkWindow.Cursor = dragCursor;
    } else if ((state & Gdk.ModifierType.ShiftMask) == Gdk.ModifierType.ShiftMask) {
      GdkWindow.Cursor = shiftCopyCursor;
    } else if ((state & Gdk.ModifierType.Mod1Mask) == Gdk.ModifierType.Mod1Mask) {
      GdkWindow.Cursor = clearSelCursor;
    } else if ((state & Gdk.ModifierType.ControlMask) == Gdk.ModifierType.ControlMask) {
      GdkWindow.Cursor = copyCursor;
    } else if (!dragging && dragY < FilesMarginTop) {
      GdkWindow.Cursor = defaultCursor;
    } else if (dragging || FindHit (Width, Height, dragX, dragY, 8).Target == CurrentDirEntry) {
      GdkWindow.Cursor = panCursor;
    } else {
      GdkWindow.Cursor = clickCursor;
    }
  }

  public string GetSelectionData ()
  {
    List<string> paths = new List<string> ();
    List<string> invalids = new List<string> ();
    foreach(string p in Selection.Keys) {
      if (Helpers.FileExists(p))
        paths.Add("file://"+p);
      else
        invalids.Add(p);
    }
    invalids.ForEach(ToggleSelection);
    return String.Join("\r\n", paths.ToArray());
  }

  public void HandleSelectionData (SelectionData sd, Gdk.DragAction action, string targetPath)
  {
    string type = sd.Type.Name;
    if (type == "application/x-color") {
      /** DESCTRUCTIVE */
      Console.WriteLine ("Would set {0} color to {1}", targetPath, BitConverter.ToString(sd.Data));
    } else if (type == "text/uri-list" || ((type == "text/plain" || type == "STRING") && Helpers.IsURI(sd.Text))) {
      /** DESCTRUCTIVE */
      string data = Helpers.BytesToASCII(sd.Data);
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
        DragDataToCreateFileMenu(targetPath, sd.Data);
      } else {
        DragDataToFileMenu(targetPath, sd.Data);
      }
    }
  }

  bool cut = false;

  public void CutSelection (string targetPath)
  {
    CopySelection (targetPath);
    cut = true;
  }

  public void CopySelection (string targetPath)
  {
    string items = "file://" + targetPath;
    if (Selection.Count > 0)
      items = GetSelectionData ();
    SetClipboard (items);
    cut = false;
  }

  /** DESTRUCTIVE */
  public void PasteSelection (string targetPath)
  {
    bool handled = false;
    clipboard.RequestContents(Gdk.Atom.Intern("text/uri-list", true), delegate(Clipboard cb, SelectionData data) {
      if (data.Length > -1) {
        Helpers.PrintSelectionData(data);
        handled = true;
        HandleSelectionData(data, cut ? Gdk.DragAction.Move : Gdk.DragAction.Copy, targetPath);
        if (cut) ClearSelection ();
        cut = false;
      }
    });
    clipboard.RequestContents(Gdk.Atom.Intern("application/x-color", true), delegate(Clipboard cb, SelectionData data) {
      if (data.Length > -1 && !handled) {
        Helpers.PrintSelectionData(data);
        handled = true;
        HandleSelectionData(data, cut ? Gdk.DragAction.Move : Gdk.DragAction.Copy, targetPath);
      }
    });
    clipboard.RequestContents(Gdk.Atom.Intern("text/plain", true), delegate(Clipboard cb, SelectionData data) {
      if (data.Length > -1 && !handled) {
        Helpers.PrintSelectionData(data);
        handled = true;
        HandleSelectionData(data, cut ? Gdk.DragAction.Move : Gdk.DragAction.Copy, targetPath);
      }
    });
    clipboard.RequestContents(Gdk.Atom.Intern("STRING", true), delegate(Clipboard cb, SelectionData data) {
      if (data.Length > -1 && !handled) {
        Helpers.PrintSelectionData(data);
        handled = true;
        HandleSelectionData(data, cut ? Gdk.DragAction.Move : Gdk.DragAction.Copy, targetPath);
      }
    });
  }

  void SetClipboard (string items)
  {
    clipboard.SetWithData(targets,
      delegate (Clipboard cb, SelectionData data, uint info) {
        data.Set(data.Target, 8, System.Text.Encoding.UTF8.GetBytes(items));
        data.Text = items;
      },
      delegate (Clipboard cb) {
        cut = false;
      }
    );
  }


  /** DESCTRUCTIVE, BLOCKING */
  public void MoveSelectionTo (string targetPath)
  {
    moveUris (new List<string>(Selection.Keys).ToArray (), targetPath);
    ClearSelection ();
  }

  /** DESCTRUCTIVE, BLOCKING */
  public void CopySelectionTo (string targetPath)
  {
    copyUris (new List<string>(Selection.Keys).ToArray (), targetPath);
  }

  /** DESCTRUCTIVE, BLOCKING */
  public void TrashSelection ()
  {
    foreach (string path in (new List<string>(Selection.Keys))) {
      Helpers.Trash(path);
      FSCache.Invalidate (path);
    }
    ClearSelection ();
  }

  /** DESCTRUCTIVE, BLOCKING */
  void moveUris (string[] uris, string targetPath)
  {
    targetPath = Helpers.IsDir ( targetPath ) ? targetPath : Helpers.Dirname (targetPath);
    Helpers.MoveURIs(uris, targetPath);
    foreach (string u in uris) {
      if (u.StartsWith ("/")) FSCache.Invalidate (u);
      else if (u.StartsWith("file://")) FSCache.Invalidate (u.Substring(7));
    }
    FSCache.Invalidate (targetPath);
  }

  /** DESCTRUCTIVE, BLOCKING */
  void copyUris (string[] uris, string targetPath)
  {
    targetPath = Helpers.IsDir ( targetPath ) ? targetPath : Helpers.Dirname (targetPath);
    Helpers.CopyURIs(uris, targetPath);
    foreach (string u in uris) {
      if (u.StartsWith ("/")) FSCache.Invalidate (u);
      else if (u.StartsWith("file://")) FSCache.Invalidate (u.Substring(7));
    }
    FSCache.Invalidate (targetPath);
  }

  /** ASYNC */
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
    MenuItem newfile = new MenuItem("Create file from data");
    newfile.Activated += delegate {
      Helpers.TextPrompt ("Create file", "Filename for data",
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
    MenuItem newfile = new MenuItem("Create file from data");
    string target = Helpers.Dirname(targetFile);
    newfile.Activated += delegate {
      Helpers.TextPrompt ("Create file", "Filename for data",
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


  /** BLOCKING */
  public void OpenFile (string path)
  {
    string suffix = Helpers.Extname(path).ToLower ();
    string epath = Helpers.EscapePath(path);
    string dir = Helpers.IsDir(path) ? path : Helpers.Dirname(path);
    if (FilezooContextMenu.imageSuffixes.Contains(suffix)) {
      Helpers.RunCommandInDir("gqview", epath, dir);
    } else if (FilezooContextMenu.audioSuffixes.Contains(suffix)) {
      Helpers.RunCommandInDir("amarok", "-p --load " + epath, dir);
    } else if (FilezooContextMenu.videoSuffixes.Contains(suffix)) {
      Helpers.RunCommandInDir("mplayer", epath, dir);
    } else if (FilezooContextMenu.archiveSuffixes.Contains(suffix)) {
      Helpers.RunCommandInDir("ex", epath, dir);
    } else {
      Helpers.OpenFile(path);
    }
  }


  public void MockDraw (uint w, uint h)
  {
    using (Context cr = new Context (EtcSurface)) {
      Draw (cr, w, h);
    }
  }

  bool LimitedRedraw = false;

  bool CheckRedraw ()
  {
    if (!PreDrawComplete || NeedRedraw) {
      NeedRedraw = false;
      FSNeedRedraw = true;
      LimitedRedraw = false;
      QueueDraw ();
    } else if (LimitedRedraw) {
      LimitedRedraw = false;
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
      if (e.LastFileChange != mtime) {
        e.LastFileChange = mtime;
        FSCache.Invalidate(e.FullName);
      }
    }
  }


  /* Files model */

  /** BLOCKING */
  public void SetCurrentDir (string dirname)
  {
    SetCurrentDir (dirname, true);
  }
  public void SetCurrentDir (string dirname, bool resetZoom)
  {
    Profiler p = new Profiler ();
    dirLatencyProfiler.Restart ();
    FirstFrameOfDir = true;

    if (dirname != Helpers.RootDir) dirname = dirname.TrimEnd(Helpers.DirSepC);
    UnixDirectoryInfo d = new UnixDirectoryInfo (dirname);
    string odp = CurrentDirPath;
    CurrentDirPath = d.FullName;

    if (odp != CurrentDirPath) {
      if (CurrentDirEntry != null && CurrentDirEntry.InProgress)
        FSCache.CancelTraversal ();
      CurrentDirEntry = FSCache.FastGet (CurrentDirPath);
      FSCache.Watch (CurrentDirPath);
    }

    FSNeedRedraw = true;
    if (resetZoom) ResetZoom ();
    UpdateLayout ();
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
  bool clearTraversal = false;
  /** BLOCKING */
  void PreDraw ()
  {
    Renderer.CancelPreDraw();
    lock (PreDrawLock) {
      if (PreDrawInProgress) return;
      if (FSCache.Measurer != null && SizeField != null)
        if (!SizeField.Measurer.DependsOnTotals && FSCache.Measurer.DependsOnTotals)
          clearTraversal = true;
      FSCache.Measurer = SizeField.Measurer;
      FSCache.SortDirection = SortDirection;
      FSCache.Comparer = SortField.Comparer;
      PreDrawInProgress = true;
    }
    PreDrawComplete = false;
  }

//   long lastTenframe = 0;

  /** ASYNC */
  void PreDrawCallback ()
  {
    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
    while (true) {
      if (PreDrawInProgress) {
        try {
          if (!FSCache.Measurer.DependsOnTotals)
            FSCache.CancelTraversal ();
          if (clearTraversal) {
            clearTraversal = false;
            FSCache.ClearTraversalCache ();
          }
//           FSCache.PruneCache (10);
          /*
          if (FSDraw.frame % 10 > lastTenframe) {
            lastTenframe = FSDraw.frame % 10;
            FSCache.PruneCache (100);
          }
          */
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
        Color ca = Renderer.DirectoryFGColor;
        ca.A = 0.3;
        cr.Color = ca;
        cr.LineWidth = 0.5;
        cr.Translate (270, -50);
        cr.Rotate (0.15);
        for (double y=0; y<6; y++) {
        for (double i=0; i<6-y; i++) {
          cr.Save ();
            double hr = 45;
            double iscale = Math.Sin(-i/20 * Math.PI*2 + CylinderRotation);
            double xscale = Math.Cos(-i/20 * Math.PI*2 + 0.1 + CylinderRotation);
            cr.Translate (iscale * hr * 3, -100 + y*hr*(2*1.732) + hr*(i%2)*1.732);
            hr = 45;
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
        Color c = Renderer.RegularFileColor;
        c.A = 0.1*opacity;
        cr.Color = c;
        cr.LineWidth = 1;
        double n = 6;
        double af = Math.PI*2/n;
        double r = 400*(4.4+0.3*cosScale(opacity/3));
        for (double i=0; i<n; i++) {
          cr.Arc (-400*4, 1000/4, r, t+i*af, t+(i+0.7)*af);
          cr.Stroke ();
        }
        for (double i=0; i<n; i++) {
          cr.Arc (-400*4, 1000/4, r+5, -t+i*af, -t+(i+0.7)*af);
          cr.Stroke ();
        }
        if (CurrentDirEntry.InProgress) {
          cr.NewPath ();
            // find FSCache.LastTraversed [or ancestor] y position from model
            // draw line there
          cr.NewPath ();
        }
        LimitedRedraw = true;
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
      c = Renderer.Draw(CurrentDirEntry, Prefixes, Selection, cr, targetBox);
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

  FSEntry spanStart = null;
  FSEntry spanEnd = null;

  void ToggleSpan (FSEntry start, FSEntry end)
  {
    if (start.ParentDir == end.ParentDir) {
      FSEntry[] entries = end.ParentDir.Entries.ToArray ();
      int startIdx = Array.IndexOf(entries, start);
      int endIdx = Array.IndexOf(entries, end);
      if (startIdx > -1 && endIdx > -1) {
        int i = Math.Min (startIdx, endIdx);
        int j = Math.Max (startIdx, endIdx);
        for (;i<=j;i++) {
          if (i != startIdx)
            ToggleSelection(entries[i].FullName);
        }
      }
      NeedRedraw = true;
    }
  }

  public void ToggleSelection (string path)
  {
    if (Selection.ContainsKey(path)) Selection.Remove(path);
    else Selection[path] = true;
    NeedRedraw = true;
  }

  public void ClearSelection ()
  {
    Selection.Clear ();
    UpdateLayout ();
  }

  /** BLOCKING */
  void ClickCurrentDir (Context cr, uint width, uint height, double x, double y)
  {
    Rectangle box = Transform (cr, width, height);
    if (x < box.X || x > box.X+box.Width || y < box.Y || y > box.Y+box.Height)
      return;
    cr.Scale (1, Zoomer.Z);
    cr.Translate (0.0, Zoomer.Y);
    List<ClickHit> hits = Renderer.Click (CurrentDirEntry, Prefixes, cr, box, x, y);
    foreach (ClickHit c in hits) {
      if (c.Height < 15.9) {
        if (c.Target.ParentDir == CurrentDirEntry) {
          double nz = (c.Target.IsDirectory ? 24 : 18) / c.Height;
          // Console.WriteLine("ZoomIn {0}x", nz);
          cr.Save ();
            cr.IdentityMatrix ();
            ZoomBy(cr, width, height, x, y, nz);
          cr.Restore ();
          break;
        }
      } else if (DoubleClick) {
        break;
      } else {
        if (AltKeyDown) {
          ClearSelection ();
        } else if (CtrlKeyDown) {
          ToggleSelection(c.Target.FullName);
          spanStart = c.Target;
          spanEnd = null;
        } else if (ShiftKeyDown) {
          if (spanStart != null) {
            if (spanEnd != null) {
              ToggleSpan(spanStart, spanEnd);
            }
            spanEnd = c.Target;
            ToggleSpan(spanStart, spanEnd);
          } else {
            ToggleSelection(c.Target.FullName);
            spanStart = c.Target;
            spanEnd = null;
          }
        } else {
          if (c.Target.IsDirectory) {
            // Console.WriteLine("Navigate {0}", c.Target.FullName);
            SetCurrentDir (c.Target.FullName);
          } else {
            // Console.WriteLine("Open {0}", c.Target.FullName);
            OpenFile(c.Target.FullName);
          }
        }
        UpdateLayout ();
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
      cr.Translate (Math.Min(0,areaWidth-te1.Width), -BreadcrumbMarginTop);
      if (areaWidth - te1.Width >= 0)
        cr.Translate(-(BreadcrumbMarginLeft+1), 0);
      cr.MoveTo (0.0, 0.0);
      int hitIndex = 0;
      string[] segments = CurrentDirPath.Split(Helpers.DirSepC);
      foreach (string s in segments) {
        string name = (s == "") ? rootChar : s+dirSep;
        TextExtents te = Helpers.GetTextExtents (cr, BreadcrumbFontFamily, BreadcrumbFontSize, name);
        te.Height += BreadcrumbMarginTop;
        if (s == "")
          te.Width += BreadcrumbMarginLeft+1;
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
        if (s == "" && (areaWidth - te1.Width >= 0))
          cr.Translate(BreadcrumbMarginLeft+1, 0);
        cr.RelMoveTo( te.XAdvance, 0 );
        hitIndex += 1;
      }
      cr.IdentityMatrix ();
      cr.Rectangle (0,-1,width,2);
      if (cr.InFill(x,y)) {
        ResetZoom ();
        UpdateLayout ();
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
        SortField = sf;
        ResetZoom ();
        UpdateLayout ();
        return true;
      }
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, sf.Name);
      Helpers.DrawText (cr, ToolbarLabelFontFamily, ToolbarLabelFontSize, " ");
    }
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

  /** BLOCKING */
  public List<ClickHit> FindHits (uint width, uint height, double x, double y)
  {
    Profiler p = new Profiler ("FindHits", 10);
    List<ClickHit> hits;
    using (Context cr = new Context (CachedSurface)) {
      cr.IdentityMatrix ();
      cr.Save();
        Rectangle box = Transform (cr, width, height);
        cr.Scale (1, Zoomer.Z);
        cr.Translate (0.0, Zoomer.Y);
        hits = Renderer.Click (CurrentDirEntry, Prefixes, cr, box, x, y);
        hits.Add (new ClickHit(CurrentDirEntry, cr.Matrix.Yy));
      cr.Restore ();
    }
    p.Time ("Found {0} hits", hits.Count);
    return hits;
  }

  /** BLOCKING */
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
    ZoomVelocity = 1;
    ThrowVelocity = 0;
    ThrowFrames.Clear ();
  }

  bool NeedZoomCheck = false;

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
    NeedZoomCheck = true;
    UpdateLayout();
  }

  /** BLOCKING */
  void ZoomToward (Context cr, uint width, uint height, double x, double y) {
    if (ZoomVelocity >= 1)
      ZoomVelocity *= Math.Pow(ZoomInSpeed, 0.3);
    else
      ZoomVelocity = 1;
    NeedRedraw = true;
  }

  /** BLOCKING */
  void ZoomAway (Context cr, uint width, uint height, double x, double y) {
    if (ZoomVelocity <= 1)
      ZoomVelocity *= Math.Pow(1 / ZoomOutSpeed, 0.3);
    else
      ZoomVelocity = 1;
    NeedRedraw = true;
  }

  /** BLOCKING */
  void PanBy (Context cr, uint width, uint height, double dx, double dy)
  {
    double xr = dx, yr = dy;
    cr.Save ();
      Transform (cr, width, height);
      cr.InverseTransformDistance(ref xr, ref yr);
      Zoomer.Y += yr / Zoomer.Z;
    cr.Restore ();
    NeedZoomCheck = true;
    UpdateLayout();
  }

  /** BLOCKING */
  void CheckZoomNavigation (Context cr, uint width, uint height)
  {
    Profiler p = new Profiler ("CheckZoomNavigation");
    cr.Save ();
      Rectangle r = Transform (cr, width, height);
      cr.Scale (1, Zoomer.Z);
      cr.Translate (0.0, Zoomer.Y);
      Covering c = Renderer.FindCovering(CurrentDirEntry, cr, r, 0);
      if (c.Directory.FullName != CurrentDirPath) {
        SetCurrentDir(c.Directory.FullName, false);
        Zoomer.SetZoom (0.0, c.Pan, c.Zoom);
      }
    cr.Restore ();
    p.Time ("Checked");
  }


  /* Event handlers */

  double ZoomVelocity = 1;

  double ThrowVelocity = 0;
  List<ThrowFrame> ThrowFrames = new List<ThrowFrame> ();
  struct ThrowFrame {
    public double X, Y, Time;
    public ThrowFrame (double x, double y) {
      X=x; Y=y;
      Time = DateTime.Now.ToFileTime () / 1e7;
    }
  }

  /** FAST */
  protected override bool OnButtonPressEvent (Gdk.EventButton e)
  {
    Profiler p = new Profiler ("OnButtonPressEvent", 10);
    GrabFocus ();
    SetCursor (e.State);
    p.Time ("SetCursor");
    dragStartX = dragX = e.X;
    dragStartY = dragY = e.Y;
    ThrowVelocity = 0;
    ZoomVelocity = 1;
    ThrowFrames.Clear ();
    Cancelled = false;
    if (e.Button == 1 || e.Button == 2)
      ThrowFrames.Add (new ThrowFrame(e.X, e.Y));
    dragging = false;
    if (e.Button == 3) {
      int w, h;
      e.Window.GetSize (out w, out h);
      ContextClick ((uint)w, (uint)h, e.X, e.Y);
    } else if (e.Button == 1 && !dragging) {
      DoubleClick = (e.Type == Gdk.EventType.TwoButtonPress);
    }
    if (e.Button == 1) {
      int w, h;
      e.Window.GetSize (out w, out h);
      DragSourceEntry = FindHit((uint)w, (uint)h, e.X, e.Y, 8).Target;
      DragSourcePath = DragSourceEntry.FullName;
    }
    p.Time ("Handled");
    return true;
  }

  bool AltKeyDown = false;
  bool CtrlKeyDown = false;
  bool ShiftKeyDown = false;
  bool DoubleClick = false;

  /** BLOCKING */
  protected override bool OnButtonReleaseEvent (Gdk.EventButton e)
  {
    Profiler p = new Profiler ("OnButtonReleaseEvent", 10);
    GrabFocus ();
    SetCursor (e.State);
    p.Time ("SetCursor");
    ZoomVelocity = 1;
    if (Cancelled) {
      Cancelled = DoubleClick = false;
    } else {
      if (e.Button == 1 && !dragging) {
      InteractionProfiler.Start ();
        int w, h;
        e.Window.GetSize (out w, out h);
        AltKeyDown = (e.State & Gdk.ModifierType.Mod1Mask) == Gdk.ModifierType.Mod1Mask;
        CtrlKeyDown = (e.State & Gdk.ModifierType.ControlMask) == Gdk.ModifierType.ControlMask;
        ShiftKeyDown = (e.State & Gdk.ModifierType.ShiftMask) == Gdk.ModifierType.ShiftMask;
        if (AltKeyDown)
          ClearSelection ();
        using (Context scr = new Context (CachedSurface)) {
          scr.IdentityMatrix ();
          Click (scr, (uint)w, (uint)h, e.X, e.Y);
        }
        DoubleClick = false;
      }
      if (e.Button == 1 && ThrowFrames.Count > 0) {
        ThrowFrames.Add (new ThrowFrame(e.X, e.Y));
        int len = Math.Min(10, ThrowFrames.Count-1);
        double vy = 0;
        for (int i=ThrowFrames.Count-len; i<ThrowFrames.Count; i++) {
          vy += ThrowFrames[i].Y - ThrowFrames[i-1].Y;
        }
        vy /= len;
        // Console.WriteLine("ThrowFrames.Count: {0}, vy: {1}", ThrowFrames.Count, vy);
        if (Math.Abs(vy) > 5) {
          ThrowVelocity = vy*2;
          NeedRedraw = true;
        } else {
          ThrowVelocity = 0;
        }
        ThrowFrames.Clear ();
      }
    }
    if (e.Button == 1) {
      if (!dragInProgress) {
        DragSourceEntry = null;
        DragSourcePath = null;
      }
      dragInProgress = false;
    }
    if (e.Button == 2) { ControlLineVisible = false; NeedRedraw = true; }
    dragging = false;
    panning = panning && dragInProgress;
    p.Time ("Handled");
    return true;
  }

  bool panning = false;
  bool dragInProgress = false;
  bool ControlLineVisible = false;

  /** FAST */
  protected override bool OnMotionNotifyEvent (Gdk.EventMotion e)
  {
    Profiler p = new Profiler ("OnMotionNotifyEvent", 10);
    SetCursor (e.State);
    p.Time("SetCursor");
    bool left = (e.State & Gdk.ModifierType.Button1Mask) == Gdk.ModifierType.Button1Mask;
    bool middle = (e.State & Gdk.ModifierType.Button2Mask) == Gdk.ModifierType.Button2Mask;

    if (!left && !middle) panning = dragging = false;

    if (!Cancelled) {
      if (left || middle)
        ThrowFrames.Add (new ThrowFrame(e.X, e.Y));
      if (middle) {
        dragging = true;
        panning = true;
      }
      if (left) {
        bool ctrl = (e.State & Gdk.ModifierType.ControlMask) == Gdk.ModifierType.ControlMask;
        dragging = dragging || ((Math.Abs(dragX - dragStartX) + Math.Abs(dragY - dragStartY)) > 4);
        panning = panning || !ctrl || (DragSourceEntry == CurrentDirEntry);
        if (!dragInProgress && !panning && dragging) {
          Gdk.DragAction action = Gdk.DragAction.Move;
          if ((e.State & Gdk.ModifierType.ShiftMask) == Gdk.ModifierType.ShiftMask)
            action = Gdk.DragAction.Copy;
          if ((e.State & Gdk.ModifierType.Mod1Mask) == Gdk.ModifierType.Mod1Mask)
            action = Gdk.DragAction.Ask;
          dragInProgress = true;
          Drag.Begin (this, new TargetList(targets), action, 1, e);
          SetCursor (e.State);
          Cancelled = true;
          ThrowVelocity = 0;
          ThrowFrames.Clear ();
        }
      }
      if (panning) {
        InteractionProfiler.Restart ();
        double dx = e.X - dragX;
        double dy = e.Y - dragY;
        using ( Context cr = new Context (EtcSurface) ) {
          if (middle) {
            ControlLineVisible = true;
            double z = Math.Pow((dx < 0 ? ZoomInSpeed : (1 / ZoomOutSpeed)), (Math.Abs(dx) / 50));
            if (Height - e.Y < 50)
              ThrowVelocity = -(50 - (Height - e.Y)) / 2;
            else if (e.Y < 50 + FilesMarginTop)
              ThrowVelocity = (50 + FilesMarginTop - e.Y) / 2;
            else
              ThrowVelocity = 0;
/*            if (Width - e.X < 50)
              ZoomVelocity = Math.Pow((1/ZoomOutSpeed), (50 - (Width - e.X)) / 250);
            else if (e.X < 50)
              ZoomVelocity = Math.Pow(ZoomInSpeed, (50 - e.X) / 250);
            else*/
              ZoomVelocity = 1;
            ZoomBy (cr, Width, Height, e.X, e.Y, z);
            PanBy (cr, Width, Height, dx, -dy);
          } else {
            ControlLineVisible = false;
            ThrowVelocity = 0;
            PanBy (cr, Width, Height, dx, dy);
          }
        }
      }
    } else {
      panning = false;
    }
    if (SillyFlare) {
      LimitedRedraw = true;
    }
    dragX = e.X;
    dragY = e.Y;
    flareTargetX = Width/2;
    flareTargetY = -100;
    p.Time("Handled");
    return true;
  }

  /** FAST */
  protected override bool OnScrollEvent (Gdk.EventScroll e)
  {
    InteractionProfiler.Start ();
    Profiler p = new Profiler ("ScrollEvent", 10);
    if (e.Direction == Gdk.ScrollDirection.Up) {
      using (Context cr = new Context (EtcSurface))
        ZoomToward (cr, Width, Height, e.X, e.Y);
    }
    if (e.Direction == Gdk.ScrollDirection.Down) {
      using (Context cr = new Context (EtcSurface))
        ZoomAway (cr, Width, Height, e.X, e.Y);
    }
    p.Time("Handled");
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
      int x, y;
      GetPointer(out x, out y);
      bool sizeChanged = false;
      if (InteractionProfiler.Watch.IsRunning)
        InteractionProfiler.Time ("From UI action to expose");
      if (Width != (uint)w || Height != (uint)h || CachedSurface == null) {
        if (CachedSurface != null) CachedSurface.Destroy ();
        CachedSurface = new ImageSurface(Format.ARGB32, w, h);
        sizeChanged = true;
        Width = (uint) w;
        Height = (uint) h;
        UpdateLayout ();
      }
      if (Cancelled) {
        ThrowFrames.Clear ();
        ThrowVelocity = 0;
      }
      if (ThrowVelocity != 0) {
        using ( Context ecr = new Context (EtcSurface) )
          PanBy (ecr, Width, Height, 0, ThrowVelocity);
        ThrowVelocity *= 0.98;
        if (Math.Abs(ThrowVelocity) < 1)
          ThrowVelocity = 0;
      }
      if (ZoomVelocity != 1) {
        using ( Context ecr = new Context (EtcSurface) )
          ZoomBy (ecr, Width, Height, x, y, ZoomVelocity);
        ZoomVelocity = Math.Pow(ZoomVelocity, 0.8);
        if (Math.Abs(1 - ZoomVelocity) < 0.001)
          ZoomVelocity = 1;
      }
      if (NeedZoomCheck) CheckZoomNavigation(cr, Width, Height);
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
          if (ControlLineVisible && panning) {
            cr.Save ();
              cr.Color = Renderer.DirectoryFGColor;
              cr.Rectangle (0, dragY, Width, 1);
              cr.Fill ();
            cr.Restore ();
          }
          cr.Source = p;
          cr.Paint ();
          cr.Operator = Operator.Over;
          if (DrawEffects (cr, Width, Height))
            LimitedRedraw = true;
        }
      cr.Restore ();
    }
    if (InteractionProfiler.Watch.IsRunning) {
      InteractionProfiler.Total ("Interaction latency");
      InteractionProfiler.Stop ();
      InteractionProfiler.Reset ();
      InteractionProfiler.TotalElapsed = 0;
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
/*        cr.Arc(0, 0, FGRadius, 0, Math.PI * 2);
        cr.Source = BlackGradient;
        cr.Operator = Operator.Over;*/
        cr.FillPreserve ();
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





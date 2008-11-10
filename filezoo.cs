using System;
using System.Collections;
using System.IO;
using Gtk;
using Cairo;
using Mono.Unix;

class Filezoo : DrawingArea
{

  private static Gtk.Window win = null;
  private string TopDirName = null;
  private double TotalSize = 0.0;
  private ArrayList Files = null;

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

  Filezoo (string topDirName)
  {
    TopDirName = System.IO.Path.GetFullPath(topDirName);
    BuildDirs (TopDirName);
    win = new Window ("Filezoo");
    win.SetDefaultSize (768, 768);
    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    AddEvents((int)Gdk.EventMask.ButtonPressMask);
    win.Add (this);
    win.ShowAll ();
  }

  void BuildDirs (string dirname)
  {
    Files = new ArrayList ();
    UnixDirectoryInfo di = new UnixDirectoryInfo(dirname);
    UnixFileSystemInfo[] files = di.GetFileSystemEntries ();
    double size = 0.0;
    foreach (UnixFileSystemInfo f in files)
    {
      double dsz;
      if (f.FileType == FileTypes.Directory) {
        dsz = dirSize(System.IO.Path.Combine(dirname, f.Name));
      } else {
        dsz = (double)f.Length;
      }
      size += dsz;
      Files.Add (new DirStats (dirname, f.Name, dsz, f.FileType, f.FileAccessPermissions));
    }
    TotalSize = size;
  }

  static double dirSize (string dirname)
  {
    UnixDirectoryInfo di = new UnixDirectoryInfo(dirname);
    UnixFileSystemInfo[] files = di.GetFileSystemEntries ();
    double size = 0.0;
    foreach (UnixFileSystemInfo f in files) {
      if (f.FileType == FileTypes.Directory) {
        size += dirSize(System.IO.Path.Combine(dirname, f.Name));
      } else {
        size += (double)f.Length;
      }
    }
    return size;
  }


  void Transform (Context cr, uint width, uint height)
  {
    uint boxSize = Math.Min(width, height);
    cr.Scale (boxSize, boxSize);
    cr.Translate (0.015, 0.015);
    cr.Scale (0.97, 0.97);
  }

  void Draw (Context cr, uint width, uint height)
  {
    cr.Save ();
      cr.Color = new Color (1,1,1);
      cr.Rectangle (0,0, width, height);
      cr.Fill ();
      Transform (cr, width, height);
      cr.LineWidth = 0.001;
      foreach (DirStats d in Files) {
        d.Draw (cr, TotalSize);
        cr.Translate (0, d.Height);
      }
    cr.Restore ();
  }

  void Click (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save ();
      Transform (cr, width, height);
      foreach (DirStats d in Files) {
        if (d.Click (cr, TotalSize, x, y)) {
          if (d.Control) {
            TopDirName = d.GetFullPath ();
            BuildDirs (TopDirName);
          }
          win.QueueDraw();
          break;
        }
        cr.Translate (0, d.Height);
      }
    cr.Restore ();
  }

  protected override bool OnButtonPressEvent (Gdk.EventButton e)
  {
    if (e.Button == 1) {
      using ( Context cr = Gdk.CairoHelper.Create (e.Window) )
      {
        int w, h;
        e.Window.GetSize (out w, out h);
        Click (cr, (uint)w, (uint)h, e.X, e.Y);
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

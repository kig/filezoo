using System;
using System.Collections;
using System.IO;
using Gtk;
using Cairo;

class Filezoo : DrawingArea
{

  private static Gtk.Window win = null;
  private string TopDirName = null;
  private double TotalSize = 0.0;
  private ArrayList Files = null;
  private ArrayList Dirs = null;

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
    win.Add (this);
    win.ShowAll ();
  }

  void BuildDirs (string dirname)
  {
    Files = new ArrayList ();
    Dirs = new ArrayList ();
    DirectoryInfo di = new DirectoryInfo(dirname);
    FileInfo[] files = di.GetFiles ();
    DirectoryInfo[] dirs = di.GetDirectories ();
    double size = 0.0;
    foreach (FileInfo f in files)
    {
      size += (double)f.Length;
      Files.Add (new DirStats (f.Name, (double)f.Length));
    }
    foreach (DirectoryInfo d in dirs)
    {
      double dsz = dirSize(System.IO.Path.Combine(dirname, d.Name));
      size += dsz;
      Dirs.Add (new DirStats (d.Name, dsz));
    }
    TotalSize = size;
  }

  static double dirSize (string dirname)
  {
    DirectoryInfo di = new DirectoryInfo(dirname);
    FileInfo[] files = di.GetFiles ();
    DirectoryInfo[] dirs = di.GetDirectories ();
    double size = 0.0;
    foreach (FileInfo f in files)
      size += (double)f.Length;
    foreach (DirectoryInfo d in dirs)
      size += dirSize(System.IO.Path.Combine(dirname, d.Name));
    return size;
  }


  void Draw (Context cr, uint width, uint height)
  {
    uint boxSize = Math.Min(width, height);
    cr.Save ();
      cr.Color = new Color (1,1,1);
      cr.Rectangle (0,0, width, height);
      cr.Fill ();
      cr.Scale (boxSize, boxSize);
      cr.Save ();
        cr.LineWidth = 0.002;
        cr.Translate (0.015, 0.015);
        cr.Scale (0.97, 0.97);
        cr.Color = new Color (0,0,1);
        foreach (DirStats d in Dirs)
        {
          double scaled = d.Length / TotalSize;
          cr.Rectangle (0.0, 0.0, 0.2, scaled);
          cr.Stroke ();
          cr.Save ();
            double fs = Math.Max(0.001, Math.Min(0.03, 0.8 * scaled));
            cr.SetFontSize(fs);
            cr.MoveTo (0.21, scaled / 2 + fs / 4);
            cr.ShowText (d.Name + "/ " + d.Length.ToString() + " bytes");
          cr.Restore ();
          cr.Translate (0, scaled);
        }
        cr.Color = new Color (0,0,0);
        foreach (DirStats d in Files)
        {
          double scaled = d.Length / TotalSize;
          cr.Rectangle (0.0, 0.0, 0.2, scaled);
          cr.Stroke ();
          cr.Save ();
            double fs = Math.Max(0.001, Math.Min(0.03, 0.8 * scaled));
            cr.SetFontSize(fs);
            cr.MoveTo (0.21, scaled / 2 + fs / 4);
            cr.ShowText (d.Name + " " + d.Length.ToString() + " bytes");
          cr.Restore ();
          cr.Translate (0, scaled);
        }
      cr.Restore ();
    cr.Restore ();
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

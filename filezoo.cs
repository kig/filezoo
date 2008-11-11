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
    BuildDirs (topDirName);
    win = new Window ("Filezoo");
    win.SetDefaultSize (768, 768);
    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    AddEvents((int)Gdk.EventMask.ButtonPressMask);
    win.Add (this);
    win.ShowAll ();
  }

  interface IMeasurer {
    double Measure (DirStats d);
  }

  public class SizeMeasurer : IMeasurer {
    public double Measure (DirStats d) {
      return d.GetRecursiveSize ();
    }
  }

  public class CountMeasurer : IMeasurer {
    public double Measure (DirStats d) {
      double mul = (d.Info.Name[0] == '.') ? 0.1 : 1.0;
      return d.GetRecursiveCount () * mul;
    }
  }

  public class FlatMeasurer : IMeasurer {
    public double Measure (DirStats d) {
      double mul = (d.Info.Name[0] == '.') ? 0.1 : 1.0;
      return 1.0 * mul;
    }
  }

  interface IZoomer {
    double Zoom (double position);
  }

  public class FlatZoomer : IZoomer {
    public double Zoom (double position) {
      return 1.0;
    }
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

  void UpdateLayout (ArrayList files, IMeasurer measurer, IZoomer zoomer, IComparer comparer)
  {
    files.Sort(comparer);
    double totalHeight = 0.0;
    foreach (DirStats f in files) {
      double height = measurer.Measure(f);
      f.Height = height;
      totalHeight += height;
    }
    double position = 0.0;
    foreach (DirStats f in files) {
      double zoom = zoomer.Zoom(position);
      f.Zoom = zoom;
      f.Scale = 1.0 / totalHeight;
      position += f.Height / totalHeight;
    }
  }

  void BuildDirs (string dirname)
  {
    TopDirName = System.IO.Path.GetFullPath(dirname);
    Files = GetDirStats (dirname);
    UpdateLayout(Files, new FlatMeasurer(), new FlatZoomer(), new DirStats.nameComparer());
  }

  void Transform (Context cr, uint width, uint height)
  {
    uint boxSize = height;
    cr.Scale (boxSize, boxSize);
    cr.Translate (0.015, 0.045);
    cr.Scale (0.94, 0.94);
  }

  void Draw (Context cr, uint width, uint height)
  {
    cr.Save ();
      cr.Color = new Color (1,1,1);
      cr.Rectangle (0,0, width, height);
      cr.Fill ();
      Transform (cr, width, height);
      cr.Save ();
        cr.Color = new Color (0,0,1);
        cr.Translate (0.005, -0.015);
        cr.SetFontSize (0.03);
        if (TopDirName == "/") {
          cr.ShowText("/");
        } else {
          foreach (string s in TopDirName.Split('/')) {
            cr.ShowText(s);
            cr.ShowText("/ ");
          }
        }
      cr.Restore ();
      cr.LineWidth = 0.001;
      foreach (DirStats d in Files) {
        d.Draw (cr);
        cr.Translate (0, d.GetScaledHeight ());
      }
    cr.Restore ();
  }

  void Click (Context cr, uint width, uint height, double x, double y)
  {
    cr.Save ();
      Transform (cr, width, height);
      cr.Save ();
        cr.Color = new Color (0,0,1);
        cr.Translate (0.005, -0.015);
        cr.SetFontSize (0.03);
        double advance = 0.0;
        int i = 0;
        bool pathHit = false;
        string[] segments = TopDirName.Split('/');
        if (TopDirName != "/") {
          foreach (string s in segments) {
            TextExtents e = cr.TextExtents(s + "/ ");
            cr.Save ();
              cr.NewPath ();
              cr.Rectangle (advance, -e.Height, e.XAdvance, e.Height);
              cr.IdentityMatrix ();
              if (cr.InFill (x, y)) {
                pathHit = true;
                break;
              }
            cr.Restore ();
            advance += e.XAdvance;
            i += 1;
          }
        }
      cr.Restore ();
      if (pathHit) {
        string newDir = String.Join("/", segments, 0, i+1);
        if (newDir == "") newDir = "/";
        if (newDir != TopDirName) {
          Console.WriteLine("Navigating to {0}", newDir);
          BuildDirs (newDir);
          win.QueueDraw();
        }
      } else {
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

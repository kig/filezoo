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

  SortHandler[] SortFields = {
    new SortHandler("Name", new DirStats.nameComparer()),
    new SortHandler("Size", new DirStats.sizeComparer())
  };
  SizeHandler[] SizeFields = {
    new SizeHandler("Uniform", new FlatMeasurer()),
    new SizeHandler("Size", new SizeMeasurer()),
    new SizeHandler("File count", new CountMeasurer())
  };
  SortHandler SortField;
  SizeHandler SizeField;
  bool SortDesc = false;

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
    SortField = SortFields[0];
    SizeField = SizeFields[0];
    BuildDirs (topDirName);
    win = new Window ("Filezoo");
    win.SetDefaultSize (400, 768);
    win.DeleteEvent += new DeleteEventHandler (OnQuit);
    AddEvents((int)Gdk.EventMask.ButtonPressMask);
    win.Add (this);
    win.ShowAll ();
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
      double mul = (d.Info.Name[0] == '.') ? 0.05 : 1.0;
      return d.GetRecursiveCount () * mul;
    }
  }

  public class FlatMeasurer : IMeasurer {
    public double Measure (DirStats d) {
      double mul = (d.Info.Name[0] == '.') ? 0.05 : 1.0;
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

  void UpdateLayout ()
  {
    IZoomer zoomer = new FlatZoomer ();
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
    foreach (DirStats f in Files) {
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
    UpdateLayout();
  }

  void Transform (Context cr, uint width, uint height)
  {
    uint boxSize = height;
    cr.Scale (boxSize, boxSize);
    cr.Translate (0.01, 0.065);
    cr.Scale (0.92, 0.92);
  }

  public double TopDirFontSize = 0.02;
  public double TopDirMarginTop = -0.042;
  public double TopDirMarginLeft = 0.005;

  void DrawTopDir (Context cr)
  {
    cr.Color = new Color (0,0,1);
    cr.Translate (TopDirMarginLeft, TopDirMarginTop);
    cr.SetFontSize (TopDirFontSize);
    if (TopDirName == "/") {
      cr.ShowText("/");
    } else {
      foreach (string s in TopDirName.Split('/')) {
        cr.ShowText(s);
        cr.ShowText("/ ");
      }
    }
  }

  Color ActiveColor = new Color (0,0,0,1);
  Color InActiveColor = new Color (0,0,0,0.5);

  void DrawSortBar (Context cr)
  {
    cr.MoveTo (0.0, TopDirFontSize * 1.32);
    cr.SetFontSize (TopDirFontSize * 0.8);
    cr.Color = ActiveColor;
    cr.ShowText ("Sort: ");
    foreach (SortHandler sf in SortFields) {
      cr.Color = (SortField == sf) ? ActiveColor : InActiveColor;
      cr.ShowText (sf.Name);
      if (sf != SortFields[SortFields.Length-1]) {
        cr.Color = InActiveColor;
        cr.ShowText (" | ");
      }
    }
    cr.Color = ActiveColor;
    cr.ShowText ("  ");
    cr.ShowText (" " + (SortDesc ? "▾" : "▴") + " ");
    cr.ShowText ("   ");
  }

  void DrawSizeBar (Context cr)
  {
    cr.ShowText ("Size: ");
    foreach (SizeHandler sf in SizeFields) {
      cr.Color = (SizeField == sf) ? ActiveColor : InActiveColor;
      cr.ShowText (sf.Name);
      if (sf != SizeFields[SizeFields.Length-1]) {
        cr.Color = InActiveColor;
        cr.ShowText (" | ");
      }
    }
  }

  bool CheckTextExtents (Context cr, double advance, TextExtents te, double x, double y)
  {
    bool retval = false;
    cr.Save ();
      cr.Rectangle (advance, -te.Height, te.Width, te.Height);
      cr.IdentityMatrix ();
      retval = cr.InFill (x, y);
    cr.Restore ();
    return retval;
  }

  bool ClickSortBar (out double advance, Context cr, double x, double y)
  {
    cr.Translate (0, TopDirFontSize * 1.32);
    cr.SetFontSize (TopDirFontSize * 0.8);
    TextExtents te;
    advance = 0.0;
    advance += cr.TextExtents ("Sort: ").XAdvance;
    foreach (SortHandler sf in SortFields) {
      te = cr.TextExtents (sf.Name);
      if (CheckTextExtents(cr, advance, te, x, y)) {
        if (sf == SortField) {
          SortDesc = !SortDesc;
        } else {
          SortField = sf;
        }
        return true;
      }
      advance += te.XAdvance;
      if (sf != SortFields[SortFields.Length-1]) {
        advance += cr.TextExtents (" | ").XAdvance;
      }
    }
    advance += cr.TextExtents ("  ").XAdvance;
    te = cr.TextExtents (" " + (SortDesc ? "▾" : "▴") + " ");
    if (CheckTextExtents(cr, advance, te, x, y)) {
      SortDesc = !SortDesc;
      return true;
    }
    advance += te.XAdvance;
    advance += cr.TextExtents ("   ").XAdvance;
    return false;
  }

  bool ClickSizeBar (double advance, Context cr, double x, double y)
  {
    TextExtents te;
    advance += cr.TextExtents ("Size: ").XAdvance;
    foreach (SizeHandler sf in SizeFields) {
      te = cr.TextExtents (sf.Name);
      if (CheckTextExtents(cr, advance, te, x, y)) {
        SizeField = sf;
        return true;
      }
      advance += te.XAdvance;
      if (sf != SizeFields[SizeFields.Length-1]) {
        advance += cr.TextExtents (" | ").XAdvance;
      }
    }
    return false;
  }

  void Draw (Context cr, uint width, uint height)
  {
    cr.Save ();
      cr.Color = new Color (1,1,1);
      cr.Rectangle (0,0, width, height);
      cr.Fill ();
      Transform (cr, width, height);
      cr.Save ();
        DrawTopDir (cr);
        DrawSortBar (cr);
        DrawSizeBar (cr);
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
        cr.Translate (TopDirMarginLeft, TopDirMarginTop);
        cr.SetFontSize (TopDirFontSize);
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
        if (!pathHit && (ClickSortBar (out advance, cr, x, y) || ClickSizeBar (advance, cr, x, y))) {
          UpdateLayout ();
          win.QueueDraw ();
          cr.Restore ();
          return;
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

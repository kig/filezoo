using System.Collections;
using System.Diagnostics;
using System;
using Mono.Unix;
using Cairo;

public static class Helpers {
  public static Pango.FontDescription UIFont = Pango.FontDescription.FromString ("Verdana");

  static Hashtable FontCache = new Hashtable(20);
  static Hashtable LayoutCache = new Hashtable(20);
  static bool fontCacheInit = false;

  static Pango.Layout GetFont(Context cr, double fontSize)
  {
    if (!fontCacheInit) {
      fontCacheInit = true;
      for (int i=1; i<40; i++)
        GetFont (cr, i * 0.5);
    }
    if (!FontCache.Contains(fontSize)) {
      Pango.FontDescription font = Pango.FontDescription.FromString ("Verdana");
      font.Size = (int)(fontSize * Pango.Scale.PangoScale);
      FontCache.Add(fontSize, font);

      Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
      layout.FontDescription = font;
      LayoutCache.Add(fontSize, layout);
    }
    return (Pango.Layout)LayoutCache[fontSize];
  }

  public static void DrawText (Context cr, double fontSize, string text)
  {
  Stopwatch wa = new Stopwatch ();
  wa.Start ();
    Pango.Layout layout = GetFont (cr, fontSize);
    layout.SetText (text);
    Pango.Rectangle pe, le;
    layout.GetExtents(out pe, out le);
  wa.Stop ();
//   Console.WriteLine ("DrawText GetExtents: {0}", wa.ElapsedTicks);
  wa.Reset ();
  wa.Start ();
    double w = (double)le.Width / (double)Pango.Scale.PangoScale;
    Pango.CairoHelper.ShowLayout (cr, layout);
  wa.Stop ();
//   Console.WriteLine ("DrawText ShowLayout: {0}", wa.ElapsedTicks);
    cr.RelMoveTo (w, 0);
  }

  public static TextExtents GetTextExtents (Context cr, double fontSize, string text)
  {
    TextExtents te = new TextExtents ();
      Pango.Layout layout = GetFont (cr, fontSize);
      layout.SetText (text);
      Pango.Rectangle pe, le;
      layout.GetExtents(out pe, out le);
      double w = (double)le.Width / (double)Pango.Scale.PangoScale,
            h = (double)le.Height / (double)Pango.Scale.PangoScale;
      te.Height = h;
      te.Width = w;
      te.XAdvance = w;
      te.YAdvance = 0;
    return te;
  }


  public static bool CheckTextExtents (Context cr, double advance, TextExtents te, double x, double y)
  {
    bool retval = false;
    cr.Save ();
      cr.Rectangle (advance, 0.0, te.Width, te.Height * 1.2);
      cr.IdentityMatrix ();
      retval = cr.InFill (x, y);
    cr.Restore ();
    return retval;
  }


  public static void OpenTerminal (string path)
  {
    string cd = UnixDirectoryInfo.GetCurrentDirectory ();
    UnixDirectoryInfo.SetCurrentDirectory (path);
    Process.Start ("urxvt");
    UnixDirectoryInfo.SetCurrentDirectory (cd);
  }

  public static void OpenFile (string path)
  {
    Process.Start (path);
  }

  public static string FormatSI (double sz, string unit)
  {
    string suffix = "";
    if (sz >= 1e9) {
      suffix = "G";
      sz /= 1e9;
    } else if (sz >= 1e6) {
      suffix = "M";
      sz /= 1e6;
    } else if (sz >= 1e3) {
      suffix = "k";
      sz /= 1e3;
    } else {
      return String.Format("{0} "+unit, sz.ToString("N0"));
    }
    return String.Format("{0} {1}"+unit, sz.ToString("N1"), suffix);
  }

}


public class Profiler
{
  public static bool GlobalPrintProfile = true;

  protected Stopwatch Watch;
  public Print PrintProfile = Print.Global;

  public Profiler ()
  {
    Watch = new Stopwatch ();
    Watch.Start ();
  }

  public void Time (string message)
  {
    double elapsedMilliseconds = Watch.ElapsedTicks / 10000.0;
    if (
      (PrintProfile == Print.Always) ||
      ((PrintProfile == Print.Global) && GlobalPrintProfile)
    ) {
      string time = String.Format ("{0} ms ", elapsedMilliseconds.ToString("N1"));
      Console.WriteLine (time.PadLeft(12) + message);
    }
    Restart ();
  }

  public void Restart () { Reset (); Start (); }
  public void Reset () { Watch.Reset (); }
  public void Start () { Watch.Start (); }
  public void Stop () { Watch.Stop (); }

  public enum Print {
    Global,
    Always,
    Never
  }
}

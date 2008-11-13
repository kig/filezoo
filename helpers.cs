using System.Collections;
using Cairo;

public static class Helpers {
  public static Pango.FontDescription UIFont = Pango.FontDescription.FromString ("Verdana");

  static Hashtable FontCache = new Hashtable(20);
  static bool fontCacheInit = false;

  static Pango.FontDescription GetFont(double fontSize)
  {
    if (!fontCacheInit) {
      fontCacheInit = true;
      for (int i=1; i<40; i++)
        GetFont (i * 0.5);
    }
    if (!FontCache.Contains(fontSize)) {
      Pango.FontDescription font = Pango.FontDescription.FromString ("Verdana");
      font.Size = (int)(fontSize * Pango.Scale.PangoScale);
      FontCache.Add(fontSize, font);
    }
    return (Pango.FontDescription)FontCache[fontSize];
  }

  public static void DrawText (Context cr, double fontSize, string text)
  {
    using (Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr)) {
      layout.FontDescription = GetFont(fontSize);
      layout.SetText (text);
      Pango.Rectangle pe, le;
      layout.GetExtents(out pe, out le);
      double w = (double)le.Width / (double)Pango.Scale.PangoScale;
      Pango.CairoHelper.ShowLayout (cr, layout);
      cr.RelMoveTo (w, 0);
    }
  }

  public static TextExtents GetTextExtents (Context cr, double fontSize, string text)
  {
    TextExtents te = new TextExtents ();
    using (Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr)) {
      layout.FontDescription = GetFont(fontSize);
      layout.SetText (text);
      Pango.Rectangle pe, le;
      layout.GetExtents(out pe, out le);
      double w = (double)le.Width / (double)Pango.Scale.PangoScale,
            h = (double)le.Height / (double)Pango.Scale.PangoScale;
      te.Height = h;
      te.Width = w;
      te.XAdvance = w;
      te.YAdvance = 0;
    }
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

}
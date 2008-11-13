using Cairo;

public static class Helpers {
  public static Pango.FontDescription UIFont = Pango.FontDescription.FromString ("Verdana");

  public static void DrawText (Context cr, double fontSize, string text)
  {
    Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
    UIFont.Size = (int)(fontSize * Pango.Scale.PangoScale);
    layout.FontDescription = UIFont;
    layout.SetText (text);
    Pango.Rectangle pe, le;
    layout.GetExtents(out pe, out le);
    double w = (double)le.Width / (double)Pango.Scale.PangoScale;
//     double h = (double)le.Height / (double)Pango.Scale.PangoScale;
    Pango.CairoHelper.ShowLayout (cr, layout);
    cr.RelMoveTo (w, 0);
  }

  public static TextExtents GetTextExtents (Context cr, double fontSize, string text)
  {
    Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
    UIFont.Size = (int)(fontSize * Pango.Scale.PangoScale);
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


  public static bool CheckTextExtents (Context cr, double advance, TextExtents te, double x, double y)
  {
    bool retval = false;
    cr.Save ();
      cr.Rectangle (advance, 0.0, te.Width, te.Height * 1.5);
      cr.IdentityMatrix ();
      retval = cr.InFill (x, y);
    cr.Restore ();
    return retval;
  }

}
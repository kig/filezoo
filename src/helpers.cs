using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System;
using Mono.Unix;
using Cairo;

public static class Helpers {

  public static Profiler StartupProfiler = new Profiler ("STARTUP");

  public static char DirSepC = System.IO.Path.DirectorySeparatorChar;
  public static string DirSepS = System.IO.Path.DirectorySeparatorChar.ToString ();
  public static string RootDir = System.IO.Path.DirectorySeparatorChar.ToString ();

  public static string HomeDir = UnixEnvironment.RealUser.HomeDirectory;

  /* Text drawing helpers */

  public static Pango.FontDescription UIFont = Pango.FontDescription.FromString ("Verdana");

  static Hashtable FontCache = new Hashtable(21);
  static Hashtable LayoutCache = new Hashtable(21);
  static bool fontCacheInit = false;

  /** BLOCKING */
  static Pango.Layout GetFont(Context cr, double fontSize)
  {
    if (!fontCacheInit) {
      fontCacheInit = true;
      GetFont (cr, 0.5);
      for (int i=1; i<20; i++)
        GetFont (cr, i);
    }
    if (!FontCache.Contains(fontSize)) {
      Pango.FontDescription font = Pango.FontDescription.FromString ("Sans");
      font.Size = (int)(fontSize * Pango.Scale.PangoScale);
      FontCache.Add(fontSize, font);

      Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
      layout.FontDescription = font;
      LayoutCache.Add(fontSize, layout);
    }
    return (Pango.Layout)LayoutCache[fontSize];
  }

  /** FAST */
  static double QuantizeFontSize (double fs) { return Math.Max(0.5, Math.Floor(fs)); }

  /** BLOCKING */
  public static void DrawText (Context cr, double fontSize, string text)
  {
  Stopwatch wa = new Stopwatch ();
  wa.Start ();
    Pango.Layout layout = GetFont (cr, QuantizeFontSize(fontSize));
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

  /** BLOCKING */
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


  /** FAST */
  public static bool CheckTextExtents
  (Context cr, double advance, TextExtents te, double x, double y)
  {
    bool retval = false;
    cr.Save ();
      cr.Rectangle (advance, 0.0, te.Width, te.Height * 1.2);
      cr.IdentityMatrix ();
      retval = cr.InFill (x, y);
    cr.Restore ();
    return retval;
  }


  /* Rectangle drawing helpers */

  /** FAST */
  public static void DrawRectangle
  (Context cr, double x, double y, double w, double h, Rectangle target)
  {
    double x_a = cr.Matrix.X0+x*cr.Matrix.Xx;
    double y_a = cr.Matrix.Y0+y*cr.Matrix.Yy;
    double w_a = cr.Matrix.Xx*w;
    double h_a = cr.Matrix.Yy*h;
    double y2_a = y_a + h_a;
    double x2_a = x_a + w_a;
    x_a = Clamp(x_a, -1, target.X+target.Width+1);
    x2_a = Clamp(x2_a, -1, target.X+target.Width+1);
    y_a = Clamp(y_a, -1, target.Y+target.Height+1);
    y2_a = Clamp(y2_a, -1, target.Y+target.Height+1);
    w_a = Math.Max(0.5, x2_a - x_a);
    if (h_a < 0.25 && (Math.Floor(y*4) == Math.Floor((y+h)*4)))
      return;
    h_a = Math.Max(0.5, y2_a - y_a);
    cr.Save ();
      cr.IdentityMatrix ();
      cr.Rectangle (x_a, y_a, w_a, h_a);
    cr.Restore ();
  }


  /* File opening helpers */

  /** ASYNC */
  public static void OpenTerminal (string path)
  {
    string cd = UnixDirectoryInfo.GetCurrentDirectory ();
    UnixDirectoryInfo.SetCurrentDirectory (path);
    Process.Start ("urxvt");
    UnixDirectoryInfo.SetCurrentDirectory (cd);
  }

  /** ASYNC */
  public static void OpenFile (string path)
  {
    Process.Start ("gnome-open", EscapePath(path));
  }


  /* String formatting helpers */

  /** FAST */
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


  /* File info helpers */

  /** BLOCKING */
  public static UnixFileSystemInfo[] Entries (string dirname) {
    UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
    return di.GetFileSystemEntries ();
  }

  /** BLOCKING */
  public static UnixFileSystemInfo[] EntriesMaybe (string dirname) {
    try { return Entries(dirname); }
    catch (System.IO.FileNotFoundException) { return new UnixFileSystemInfo[0]; }
  }

  /** BLOCKING */
  public static FileTypes FileType (UnixFileSystemInfo f) {
    try { return f.FileType; }
    catch (System.InvalidOperationException) { return FileTypes.RegularFile; }
  }

  /** BLOCKING */
  public static FileAccessPermissions FilePermissions (UnixFileSystemInfo f) {
    try { return f.FileAccessPermissions; }
    catch (System.InvalidOperationException) { return (FileAccessPermissions)0; }
  }

  /** BLOCKING */
  public static long FileSize (UnixFileSystemInfo f) {
    try { return f.Length; }
    catch (System.InvalidOperationException) { return 0; }
  }

  /** BLOCKING */
  public static bool IsDir (UnixFileSystemInfo f) {
    try { return f.IsDirectory; }
    catch (System.InvalidOperationException) { return false; }
  }

  /** BLOCKING */
  public static bool IsDir (string s) {
    return IsDir (new UnixDirectoryInfo(s));
  }

  /** BLOCKING */
  public static bool FileExists (string path) {
    return (new UnixFileInfo (path)).Exists;
  }

  /** BLOCKING */
  public static DateTime LastModified (UnixFileSystemInfo f) {
    try {
      return f.LastWriteTime;
    } catch (System.InvalidOperationException) {
      return DateTime.Now;
    }
  }

  /** BLOCKING */
  public static DateTime LastModified (string path) {
    UnixFileSystemInfo f = new UnixFileInfo (path);
    return LastModified(f);
  }

  public static Dictionary<long,string> OwnerNameCache = new Dictionary<long,string> ();
  /** BLOCKING */
  public static string OwnerName (UnixFileSystemInfo f) {
    try {
      long uid = f.ToStat().st_uid;
      if (OwnerNameCache.ContainsKey(uid)) {
        return OwnerNameCache[uid];
      } else {
        try {
          UnixUserInfo uf = f.OwnerUser;
          return OwnerNameCache[uf.UserId] = uf.UserName;
        } catch (System.ArgumentException) {
          return OwnerNameCache[uid] = uid.ToString();
        }
      }
    }
    catch (System.InvalidOperationException) { return ""; }
  }

  public static Dictionary<long,string> GroupNameCache = new Dictionary<long,string> ();
  /** BLOCKING */
  public static string GroupName (UnixFileSystemInfo f) {
    try {
      long gid = f.ToStat().st_gid;
      if (GroupNameCache.ContainsKey(gid)) {
        return GroupNameCache[gid];
      } else {
        try {
          UnixGroupInfo uf = f.OwnerGroup;
          return GroupNameCache[uf.GroupId] = uf.GroupName;
        } catch (System.ArgumentException) {
          return GroupNameCache[gid] = gid.ToString();
        }
      }
    }
    catch (System.InvalidOperationException) { return ""; }
  }

  /** BLOCKING */
  public static ArrayList SubDirs (string path) {
    ArrayList a = new ArrayList ();
    try {
      foreach (UnixFileSystemInfo e in Entries (path))
        if (IsDir (e)) a.Add (e);
    }
    catch (System.InvalidOperationException) {}
    catch (System.IO.FileNotFoundException) {}
    return a;
  }

  /** BLOCKING */
  public static ArrayList SubDirnames (string path) {
    ArrayList a = new ArrayList ();
    foreach (UnixFileSystemInfo d in SubDirs (path))
      a.Add (d.FullName);
    return a;
  }


  /* Path helpers */

  /** FAST */
  public static string Dirname (string path) {
    if (path == RootDir) return "";
    char[] sa = {DirSepC};
    string p = srev(srev(path).Split(sa, 2)[1]);
    return (p.Length == 0 ? RootDir : p);
  }

  public static string Basename (string path) {
    if (path == RootDir) return "";
    char[] sa = {DirSepC};
    string p = srev(srev(path).TrimEnd(sa).Split(sa, 2)[0]);
    return p;
  }

  static Regex specialChars = new Regex("(?=[^a-zA-Z0-9_.,-])");
  /** FAST */
  public static string EscapePath (string path) {
    return specialChars.Replace(path, @"\");
  }

  /** FAST */
  static string srev (string s) {
    char [] c = s.ToCharArray ();
    Array.Reverse (c);
    return new string (c);
  }


  /* Math helpers */

  /** FAST */
  public static double Clamp (double v, double min, double max) {
    if (min > max) {
      double tmp = min;
      min = max;
      max = tmp;
    }
    return Math.Max(min, Math.Min(max, v));
  }
}



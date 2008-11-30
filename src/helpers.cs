/*
    profiler.cs - simple profiler class for printing out execution times
    Copyright (C) 2008  Ilmari Heikkinen

    Permission is hereby granted, free of charge, to any person
    obtaining a copy of this software and associated documentation
    files (the "Software"), to deal in the Software without
    restriction, including without limitation the rights to use,
    copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the
    Software is furnished to do so, subject to the following
    conditions:

    The above copyright notice and this permission notice shall be
    included in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
    OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
    HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
    WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
    FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
    OTHER DEALINGS IN THE SOFTWARE.
*/


using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System;
using Mono.Unix;
using Cairo;

public static class Helpers {

  public static Profiler StartupProfiler = new Profiler ("STARTUP");

  public static char DirSepC = System.IO.Path.DirectorySeparatorChar;
  public static string DirSepS = System.IO.Path.DirectorySeparatorChar.ToString ();
  public static string RootDir = System.IO.Path.DirectorySeparatorChar.ToString ();

  public static string HomeDir = UnixEnvironment.RealUser.HomeDirectory;

  public static string TrashDir = HomeDir + DirSepS + ".Trash";
  public static string ThumbDir = HomeDir + DirSepS + ".filezoo";
  public static string NormalThumbDir = ThumbDir + DirSepS + "Thumbs";

  /* Text drawing helpers */

  static Dictionary<double,Pango.FontDescription> FontCache = new Dictionary<double,Pango.FontDescription> (21);
//   static Dictionary<double,Pango.Layout> LayoutCache = new Dictionary<double,Pango.Layout> (21);
  static bool fontCacheInit = false;

  static object FontCacheLock = new Object ();

  /** BLOCKING */
  static Pango.Layout GetFont(Context cr, double fontSize)
  {
    Profiler p = new Profiler ("GetFont");
    lock (FontCacheLock) {
      p.Time ("Got FontCacheLock");
      if (!fontCacheInit) {
        fontCacheInit = true;
        CreateFont (0.5);
        for (int i=1; i<20; i++)
          CreateFont (i);
        p.Time ("FontCache init");
      }
      if (!FontCache.ContainsKey(fontSize)) {
        CreateFont (fontSize);
        p.Time ("Created font");
      }

      Pango.Layout layout = Pango.CairoHelper.CreateLayout (cr);
      layout.FontDescription = FontCache[fontSize];
      p.Time ("Created layout");
      return layout;
    }
  }

  static void CreateFont (double fontSize)
  {
    Pango.FontDescription font = Pango.FontDescription.FromString ("Sans");
    font.Size = (int)(fontSize * Pango.Scale.PangoScale);
    FontCache[fontSize] = font;
  }

  /** FAST */
  static double QuantizeFontSize (double fs) { return Math.Max(0.5, Math.Floor(fs)); }

  /** BLOCKING */
  public static void DrawText (Context cr, double fontSize, string text)
  {
    Profiler p = new Profiler ("DrawText");
    Pango.Layout layout = GetFont (cr, QuantizeFontSize(fontSize));
    lock (layout) {
      layout.SetText (text);
      Pango.Rectangle pe, le;
      layout.GetExtents(out pe, out le);
      p.Time ("GetExtents");
      double w = (double)le.Width / (double)Pango.Scale.PangoScale;
      Pango.CairoHelper.ShowLayout (cr, layout);
      p.Time ("ShowLayout");
      cr.RelMoveTo (w, 0);
    }
  }

  /** BLOCKING */
  public static TextExtents GetTextExtents (Context cr, double fontSize, string text)
  {
    Profiler p = new Profiler ("TextExtents");
    TextExtents te = new TextExtents ();
      Pango.Layout layout = GetFont (cr, fontSize);
    p.Time ("GetFont");
      lock (layout) {
        layout.SetText (text);
        Pango.Rectangle pe, le;
        layout.GetExtents(out pe, out le);
    p.Time ("layout.GetExtents");
        double w = (double)le.Width / (double)Pango.Scale.PangoScale,
              h = (double)le.Height / (double)Pango.Scale.PangoScale;
        te.Height = h;
        te.Width = w;
        te.XAdvance = w;
        te.YAdvance = 0;
      }
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

  /** DESTRUCTIVE, ASYNC */
  public static void ExtractFile (string path)
  {
    Process.Start ("ex", EscapePath(path));
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void Trash (string path)
  {
    if (!FileExists (TrashDir))
      new UnixDirectoryInfo(TrashDir).Create();
    Move (path, TrashDir + DirSepS + Basename(path));
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void Move (string src, string dst)
  {
    for (int i=0; i<10; i++) {
      if (FileExists(dst)) {
        try {
          if (IsDir(dst))
            new UnixDirectoryInfo(dst).Delete(true);
          else
            new UnixFileInfo(dst).Delete();
        } catch (Exception) {}
      }
      try {
        System.IO.File.Move (src, dst);
        break;
      } catch (Exception) {}
    }
  }

  /** ASYNC, DESTRUCTIVE */
  public static ImageSurface GetThumbnail (string path)
  {
    try {
      Profiler pr = new Profiler ("GetThumbnail", 10);
      ImageSurface thumb = null;
      string thumbPath;
      if (path.StartsWith(ThumbDir))
        thumbPath = path;
      else
        thumbPath = NormalThumbDir + DirSepS + ThumbnailHash (path) + ".png";
      pr.Time ("md5sum");
      if (!FileExists(thumbPath)) {
        if (!FileExists(ThumbDir))
          new UnixDirectoryInfo(ThumbDir).Create ();
        if (!FileExists(NormalThumbDir))
          new UnixDirectoryInfo(NormalThumbDir).Create ();
        if (CreateThumbnail(path, thumbPath, 128)) {
          pr.Time ("create thumbnail");
          thumb = new ImageSurface (thumbPath);
        }
      } else {
        thumb = new ImageSurface (thumbPath);
      }
      if (thumb == null)
        throw new ArgumentException (String.Format("Failed to thumbnail {0}",path), "path");
      pr.Time ("load as ImageSurface");
      return thumb;
    } catch (Exception e) {
      Console.WriteLine ("Thumbnailing failed for {0}: {1}", path, e);
      ImageSurface thumb = new ImageSurface (Format.ARGB32, 1, 1);
      using (Context cr = new Context(thumb)) {
        cr.Color = new Color (1,0,0);
        cr.Rectangle (0,0,2,2);
        cr.Fill ();
      }
      return thumb;
    }
  }

  /** ASYNC */
  public static bool CreateThumbnail (string path, string thumbPath, uint size)
  {
    string s = size.ToString ();
    ProcessStartInfo psi = new ProcessStartInfo ();
    psi.FileName = "convert";
    psi.Arguments = EscapePath(path) + "[0] -thumbnail "+s+"x"+s+" " + EscapePath(thumbPath);
    psi.UseShellExecute = false;
    Process p = Process.Start (psi);
    p.WaitForExit ();
    return FileExists(thumbPath);
  }

  /** ASYNC, FAST-ish (~200 us if file in page cache) */
  public static string ThumbnailHash (string path)
  {
    byte[] buf = new byte[16384];
    UnixFileInfo u = new UnixFileInfo(path);
    Int64 mtime = (Int64)(u.LastWriteTime.ToBinary ());
    Int64 size = (Int64)u.Length;
    for (int i=0; i<8; i++) {
      buf[i] = (byte)(mtime >> (i*8));
      buf[i+8] = (byte)(size >> (i*8));
    }
    UnixStream us = u.OpenRead();
    us.Read(buf, 16, 16384-16);
    us.Close ();

    byte[] digest;
    using (HashAlgorithm hash = HashAlgorithm.Create ("MD5"))
      digest = hash.ComputeHash (buf);

    return BitConverter.ToString (digest);
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
    catch (Exception) { return new UnixFileSystemInfo[0]; }
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

  public static DateTime DefaultTime = DateTime.Parse ("1900-01-01T00:00:00.0000000Z");

  /** BLOCKING */
  public static DateTime LastModified (UnixFileSystemInfo f) {
    try {
      return f.LastWriteTime;
    } catch (Exception) {
      return DefaultTime;
    }
  }

  /** BLOCKING */
  public static DateTime LastModified (string path) {
    UnixFileSystemInfo f = new UnixFileInfo (path);
    return LastModified(f);
  }

  /** BLOCKING */
  public static DateTime LastChange (string path) {
    UnixFileSystemInfo f = new UnixFileInfo (path);
    try {
      if (f.LastWriteTime > f.LastStatusChangeTime)
        return f.LastWriteTime;
      else
        return f.LastStatusChangeTime;
    } catch (Exception) {
      return DefaultTime;
    }
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

  public static string Extname (string path) {
    if (path == RootDir) return "";
    string bn = Basename(path);
    if (bn[0] == '.') return "";
    char[] sa = {'.'};
    string p = srev(srev(path).TrimEnd(sa).Split(sa, 2)[0]);
    if (p == bn) return "";
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



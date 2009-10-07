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
using Gtk;
using Cairo;

public static class Helpers {

  public static Profiler StartupProfiler = new Profiler ("STARTUP");

  public static char DirSepC = System.IO.Path.DirectorySeparatorChar;
  public static string DirSepS = System.IO.Path.DirectorySeparatorChar.ToString ();
  public static string RootDir = System.IO.Path.DirectorySeparatorChar.ToString ();

  public static string HomeDir = UnixEnvironment.RealUser.HomeDirectory;

  public static string Shell = UnixEnvironment.RealUser.ShellProgram;

  public static string TrashDir = HomeDir + DirSepS + ".Trash";
  public static string ThumbDir = HomeDir + DirSepS + ".thumbnails";
  public static string NormalThumbDir = ThumbDir + DirSepS + "normal";
  public static string LargeThumbDir = ThumbDir + DirSepS + "large";

  public static uint thumbSize = 128;
  public static uint largeThumbSize = 128;

  /* Text drawing helpers */

  static Dictionary<string,Pango.FontDescription> FontCache = new Dictionary<string,Pango.FontDescription> ();

  static Pango.Layout layout = null;

  /** BLOCKING */
  static Pango.Layout GetLayout(Context cr, string family, uint fontSize)
  {
    Profiler p = new Profiler ("GetFont");
    if (layout == null)
      layout = Pango.CairoHelper.CreateLayout (cr);
    Pango.CairoHelper.UpdateLayout (cr, layout);
    layout.FontDescription = CreateFont (family, fontSize);
    p.Time ("Got font and created layout");
    return layout;
  }

  static Pango.FontDescription CreateFont (string family, uint fontSize)
  {
    string key = family+((int)(fontSize * Pango.Scale.PangoScale)).ToString();
    if (!FontCache.ContainsKey(key)) {
      Pango.FontDescription font = Pango.FontDescription.FromString (family);
      font.Size = Math.Max(1, (int)(fontSize * Pango.Scale.PangoScale));
      FontCache[key] = font;
      return font;
    } else {
      return FontCache[key];
    }
  }

  /** FAST */
  static uint QuantizeFontSize (double fs) { return Math.Max(1, (uint)Math.Floor(fs)); }

  public static bool ShowTextExtents = false;
  static TextExtents te = new TextExtents ();
  static Pango.Rectangle pe, le;

  /** BLOCKING */
  public static void DrawText (Context cr, string family, double fontSize, string text) {
    DrawText (cr, family, fontSize, text, Pango.Alignment.Left);
  }
  public static void DrawText (Context cr, string family, double fontSize, string text, Pango.Alignment alignment)
  {
//   return;
  lock (FontCache) {
//   LogError("DrawText {0}", text);
    Profiler p = new Profiler ("DrawText");
    double w,h;
    Pango.Layout layout = GetLayout (cr, family, QuantizeFontSize(fontSize));
    layout.SetText (text);
    layout.Alignment = alignment;
    layout.GetExtents(out pe, out le);
    p.Time ("GetExtents {0}", pe);
    w = (double)le.Width / (double)Pango.Scale.PangoScale;
    h = (double)le.Height / (double)Pango.Scale.PangoScale;
    if (alignment == Pango.Alignment.Right) {
      cr.RelMoveTo (-w, 0);
    } else if (alignment == Pango.Alignment.Center) {
      cr.RelMoveTo (-w/2, 0);
    }
    Pango.CairoHelper.ShowLayout (cr, layout);
    p.Time ("ShowLayout");
    if (ShowTextExtents) {
      cr.Save ();
        PointD pt = cr.CurrentPoint;
        cr.MoveTo (pt.X, pt.Y);
        cr.RelLineTo(w, 0);
        cr.RelLineTo(0, h);
        cr.Operator = Operator.Over;
        cr.Color = new Color (1,0.5,1,0.5);
        cr.LineWidth = 0.5;
        cr.Stroke ();
        cr.MoveTo (pt.X, pt.Y);
      cr.Restore ();
    }
    cr.RelMoveTo (w, 0);
  } }

  /** BLOCKING */
  public static TextExtents GetTextExtents (Context cr, string family, double fontSize, string text)
  {
//   return te;
  lock (FontCache) {
//   LogError("GetTextExtents {0}", text);
    double w,h;
    Pango.Layout layout = GetLayout (cr, family, QuantizeFontSize(fontSize));
    layout.SetText (text);
    layout.GetExtents(out pe, out le);
    w = (double)le.Width / (double)Pango.Scale.PangoScale;
    h = (double)le.Height / (double)Pango.Scale.PangoScale;
    te.Height = h;
    te.Width = w;
    te.XAdvance = w;
    te.YAdvance = 0;
    return te;
  } }


  /** FAST */
  public static bool CheckTextExtents
  (Context cr, TextExtents te, double x, double y)
  {
    bool retval = false;
    cr.Save ();
      PointD pt = cr.CurrentPoint;
      cr.NewPath ();
      cr.Rectangle (pt.X, pt.Y, te.Width, te.Height);
      cr.IdentityMatrix ();
      retval = cr.InFill (x, y);
      if (ShowTextExtents) {
        cr.Operator = Operator.Over;
        cr.Color = new Color (1,0.5,1,0.5);
        cr.LineWidth = 0.5;
        cr.Stroke ();
      }
    cr.Restore ();
    cr.MoveTo (pt.X, pt.Y);
    return retval;
  }


  /* Rectangle drawing helpers */

  /** FAST */
  public static void DrawRectangle
  (Context cr, double x, double y, double w, double h, Rectangle target)
  {
    Matrix matrix = cr.Matrix;
    double x_a = matrix.X0+x*matrix.Xx;
    double y_a = matrix.Y0+y*matrix.Yy;
    double w_a = matrix.Xx*w;
    double h_a = matrix.Yy*h;
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
//   return;
    cr.Save ();
      cr.IdentityMatrix ();
      cr.Rectangle (x_a, y_a, w_a, h_a);
    cr.Restore ();
  }


  /* File opening helpers */

  public static string ReadCmd (string cmd, string args)
  {
    ProcessStartInfo psi = new ProcessStartInfo ();
    psi.FileName = cmd;
    psi.Arguments = args;
    psi.UseShellExecute = false;
    psi.RedirectStandardOutput = true;
    Process p = Process.Start (psi);
//     p.PriorityClass = ProcessPriorityClass.Idle;
    p.ProcessorAffinity = (IntPtr)0x0002;
    string rv = p.StandardOutput.ReadToEnd ();
    p.WaitForExit ();
    return rv;
  }

  /** ASYNC */
  public static Process OpenTerminal (string path)
  {
    return RunCommandInDir ("urxvt", "", path);
  }

  /** ASYNC */
  public static Process RunCommandInDir (string cmd, string args, string path)
  {
    string cd = UnixDirectoryInfo.GetCurrentDirectory ();
    try {
      UnixDirectoryInfo.SetCurrentDirectory (path);
      Process p = Process.Start (cmd, args);
      UnixDirectoryInfo.SetCurrentDirectory (cd);
      return p;
    } catch (Exception) {
      // SetCurrentDirectory fails when path doesn't exist.
      // We return null because executing the command in a random
      // directory is not a good idea and throwing an exception will just
      // cause unwanted application crashes.
      return null;
    }
  }

  /** ASYNC */
  public static Process RunShellCommandInDir (string cmd, string args, string path)
  {
    return Helpers.RunCommandInDir (Shell, "-c " + Helpers.EscapePath(cmd + (args == "" ? args : " " + args)), path);
  }

  /** ASYNC */
  public static Process OpenFile (string path)
  {
    return Process.Start ("kfmclient exec", EscapePath(path));
  }

  public static string GetMime (string path)
  {
    Gnome.Vfs.Vfs.Initialize ();
    return Gnome.Vfs.MimeType.GetMimeTypeForUri(path);
  }

  /** ASYNC */
  public static Process OpenURL (string url) {
    return Process.Start ("firefox", "-new-tab " + Helpers.EscapePath(url));
  }

  /** ASYNC */
  public static Process Search (string query) {
    return OpenURL ("http://google.com/search?q=" + System.Uri.EscapeDataString(query));
  }

  /** BLOCKING */
  public static bool IsValidCommand (string cmd) {
    int l = ReadCmd ("which", EscapePath (cmd)).Length;
    return (l > 0 && l >= cmd.Length);
  }

  /** BLOCKING */
  public static bool IsValidCommandLine (string cmdline)
  {
    if (cmdline.Length == 0) return false;
    string[] split = cmdline.Trim(' ').Split(' ');
    string cmd = split[0];
    return IsValidCommand(cmd);
  }

  /** BLOCKING */
  public static bool IsPlausibleCommandLine (string cmdline, string dir)
  {
    if (cmdline.Length == 0) return false;
    string[] split = cmdline.Trim(' ').Split(' ');
    string cmd = split[0];
    if (!IsValidCommand(cmd)) return false;
    bool first = true;
    string cd = UnixDirectoryInfo.GetCurrentDirectory ();
    UnixDirectoryInfo.SetCurrentDirectory (dir);
    bool retval = true;
    foreach (string arg in split) {
      if (first) first = false;
      else {
        if (arg[0] == '-' || arg.Contains("*") || arg.Contains("?") || FileExists(arg)) {
          retval = true;
          break;
        } else {
          retval = false;
        }
      }
    }
    UnixDirectoryInfo.SetCurrentDirectory (cd);
    return retval;
  }


  /** DESTRUCTIVE, ASYNC */
  public static void ExtractFile (string path)
  {
    Process.Start ("exa", EscapePath(path));
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void Trash (string path)
  {
    if (!FileExists (TrashDir))
      MkdirP(TrashDir);
    Move (path, TrashDir + DirSepS + Basename(path), true);
  }

  /** DESTRUCTIVE, BLOCKING */
  public static bool Delete (string path)
  {
    if (!FileExists(path)) return false;
    try {
    LogError("Deleting {0}", path);
      if (IsDir(path))
        new UnixDirectoryInfo(path).Delete(true);
      else
        new UnixFileInfo(path).Delete();
      return true;
    } catch (Exception e) {
      LogError(e);
      return false;
    }
  }

  /** DESTRUCTIVE, BLOCKING */
  public static bool Move (string src, string dst) {
    return Move (src,dst,false);
  }
  public static bool Move (string src, string dst, bool deleteOverwrite)
  {
    if (FileExists(dst)) {
      try {
        if (deleteOverwrite) Delete(dst);
        else Trash(dst);
      } catch (Exception e) { LogError(e); }
    }
    try {
      MoveURI (src, dst);
      return true;
    } catch (Exception e) { LogError(e); }
    return false;
  }

  /** DESTRUCTIVE, BLOCKING */
  public static bool Copy (string src, string dst) {
    return Copy (src,dst,false);
  }
  public static bool Copy (string src, string dst, bool deleteOverwrite)
  {
    if (FileExists(dst)) {
      try {
        if (deleteOverwrite) Delete(dst);
        else Trash(dst);
      } catch (Exception e) { LogError(e); }
    }
    try {
      CopyURI (src, dst);
      return true;
    } catch (Exception e) { LogError(e); }
    return false;
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void Touch (string path)
  { try {
    if (FileExists(path)) {
      File.SetLastWriteTime(path, DateTime.Now);
    } else {
      MkdirP (Dirname(path));
      FileStream fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.Read);
      fs.Close ();
      File.SetLastWriteTime(path, DateTime.Now);
    }
  } catch (Exception e) { LogError(e); } }

  /** DESTRUCTIVE, BLOCKING */
  public static void MkdirP (string path)
  { try {
    Directory.CreateDirectory(path);
  } catch (Exception e) { LogError(e); } }

  /** DESTRUCTIVE, BLOCKING */
  public static void NewFileWith (string path, byte[] data)
  {
    if (FileExists(path)) Trash(path);
    File.WriteAllBytes (path, data);
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void ReplaceFileWith (string path, byte[] data)
  {
    if (FileExists(path)) Trash(path);
    File.WriteAllBytes (path, data);
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void AppendToFile (string path, byte[] data)
  {
    using (FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write)) {
      fs.Write (data, 0, data.Length);
    }
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void CopyURI (string src, string dst)
  {
    CopyURIs (new string[] {src}, new string[] {dst});
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void MoveURI (string src, string dst)
  {
    MoveURIs (new string[] {src}, new string[] {dst});
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void CopyURIs (string[] src, string dst)
  {
    LogError ("Copy {0} to {1}", String.Join(", ", src), dst);
    CopyURIs (src, ExpandDsts(src, dst));
  }
  public static void CopyURIs (string[] src, string[] dst)
  {
    CopyURIs (StringsToUris(src), StringsToUris(dst));
  }
  public static void CopyURIs (Gnome.Vfs.Uri[] sources, Gnome.Vfs.Uri[] targets)
  {
    XferURIs (sources, targets, false);
  }

  /** DESTRUCTIVE, BLOCKING */
  public static void MoveURIs (string[] src, string dst)
  {
    LogError ("Move {0} to {1}", String.Join(", ", src), dst);
    MoveURIs (src, ExpandDsts(src, dst));
  }
  public static void MoveURIs (string[] src, string[] dst)
  {
    MoveURIs (StringsToUris(src), StringsToUris(dst));
  }
  public static void MoveURIs (Gnome.Vfs.Uri[] sources, Gnome.Vfs.Uri[] targets)
  {
    XferURIs (sources, targets, true);
  }

  /** FAST */
  public static Gnome.Vfs.Uri[] StringsToUris (string[] src)
  {
    Gnome.Vfs.Vfs.Initialize ();
    Gnome.Vfs.Uri[] uris = new Gnome.Vfs.Uri[src.Length];
    for (int i=0; i<src.Length; i++)
      uris[i] = new Gnome.Vfs.Uri(src[i]);
    return uris;
  }

  /** FAST */
  public static string[] ExpandDsts (string[] src, string dst)
  {
    string[] dsts = new string[src.Length];
    for (int i=0; i<src.Length; i++)
      dsts[i] = dst + DirSepS + Basename(src[i]);
    return dsts;
  }

  /** DESTRUCTIVE, ASYNC */
  public static void XferURIs
  (Gnome.Vfs.Uri[] sources, Gnome.Vfs.Uri[] targets, bool removeSources)
  {
    XferURIs(sources, targets, removeSources, ConsoleXferProgressCallback);
  }
  public static void XferURIs
  (Gnome.Vfs.Uri[] sources, Gnome.Vfs.Uri[] targets, bool removeSources,
   Gnome.Vfs.XferProgressCallback callback)
  {
    Gnome.Vfs.Vfs.Initialize ();
    Gnome.Vfs.XferOptions mode = Gnome.Vfs.XferOptions.Recursive;
    if (removeSources) mode = mode | Gnome.Vfs.XferOptions.Removesource;
    Gnome.Vfs.Xfer.XferUriList (
      sources, targets, mode,
      Gnome.Vfs.XferErrorMode.Query,
      Gnome.Vfs.XferOverwriteMode.Replace,
      callback
    );
  }

  /** BLOCKING */
  public static int ConsoleXferProgressCallback (Gnome.Vfs.XferProgressInfo info)
  {
    switch (info.Status) {
      case Gnome.Vfs.XferProgressStatus.Vfserror:
        LogError("{0}: {1} in {2} -> {3}", info.Status, info.VfsStatus, info.SourceName, info.TargetName);
        return (int)Gnome.Vfs.XferErrorAction.Abort;
      case Gnome.Vfs.XferProgressStatus.Overwrite:
        LogError("{0}: {1} in {2} -> {3}", info.Status, info.VfsStatus, info.SourceName, info.TargetName);
        return (int)Gnome.Vfs.XferOverwriteAction.Abort;
      default:
//         LogError("{0} / {1} {2} -> {3}", info.BytesCopied, info.BytesTotal, info.SourceName, info.TargetName);
        return 1;
    }
  }

  /** DESTRUCTIVE, ASYNC */
  public static ImageSurface GetThumbnail (string path)
  {
    try {
      Profiler pr = new Profiler ("GetThumbnail", 500);
      ImageSurface thumb = null;
      string thumbPath;
      if (path.StartsWith(ThumbDir)) {
        thumbPath = path;
      } else {
        thumbPath = NormalThumbDir + DirSepS + ThumbnailHash (path) + ".png";
        if (FileExists (thumbPath) && (LastModified(path) >= LastModified(thumbPath)))
          Trash(thumbPath);
      }
      pr.Time ("ThumbnailHash");
      if (!FileExists(thumbPath)) {
        if (!FileExists(ThumbDir))
          new UnixDirectoryInfo(ThumbDir).Create ();
        if (!FileExists(NormalThumbDir))
          new UnixDirectoryInfo(NormalThumbDir).Create ();
        if (CreateThumbnail(path, thumbPath, thumbSize)) {
          pr.Time ("create thumbnail");
          thumb = new ImageSurface (thumbPath);
        }
      } else {
        thumb = new ImageSurface (thumbPath);
        if (thumb.Width > thumbSize || thumb.Height > thumbSize) {
          ImageSurface nthumb = ScaleDownSurface (thumb, thumbSize);
          thumb.Destroy ();
          thumb.Destroy ();
          thumb = nthumb;
        }
      }
      if (thumb == null || thumb.Width < 1 || thumb.Height < 1) {
        if (FileExists(thumbPath)) Trash(thumbPath);
        throw new ArgumentException (String.Format("Failed to thumbnail {0}",path), "path");
      }
      pr.Time ("load as ImageSurface");
      return thumb;
    } catch (Exception e) {
      LogError ("Thumbnailing failed for {0}: {1}", path, e);
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
  public static ImageSurface ScaleDownSurface (ImageSurface s, uint size)
  {
    double scale = (double)size / (double)Math.Max(s.Width, s.Height);
    int nw = Math.Max(1, (int)(s.Width * scale));
    int nh = Math.Max(1, (int)(s.Height * scale));
    ImageSurface rv = new ImageSurface (Format.ARGB32, nw, nh);
    using (Context cr = new Context(rv)) {
      using (Pattern p = new Pattern(s)) {
        cr.Rectangle (0,0, rv.Width, rv.Height);
        cr.Scale (scale, scale);
        cr.Pattern = p;
        cr.Fill ();
      }
    }
    return rv;
  }

  /** ASYNC */
  public static int ImageWidth (string path)
  {
    using (ImageSurface s = new ImageSurface (path))
      return s.Width;
  }

  /** BLOCKING */
  public static RadialGradient RadialGradientFromImage (string path)
  {
    using (ImageSurface s = new ImageSurface (path)) {
      RadialGradient g = new RadialGradient(0,0,0, 0,0,s.Width);
      for (int i=0; i<s.Width; i++)
        g.AddColorStop((double)i / ((double)s.Width-1), Sample(s, i, 0));
      return g;
    }
  }

  /** FAST */
  public static Color Sample (ImageSurface s, int x, int y)
  {
    int i = s.Stride * y, j;
    switch (s.Format) {
      case Format.ARGB32:
        j = i + x*4;
        return new Color (s.Data[j+2]/255.0, s.Data[j+1]/255.0, s.Data[j+0]/255.0, s.Data[j+3]/255.0);
      case Format.RGB24:
        j = i + x*4;
        return new Color (s.Data[j+2]/255.0, s.Data[j+1]/255.0, s.Data[j]/255.0);
      default:
        throw new ArgumentException (String.Format("{0} not supported", s.Format), "s.Format");
    }
  }

  /** ASYNC */
  public static bool CreateThumbnail (string path, string thumbPath, uint size)
  {
    string s = size.ToString ();
    string tmp = thumbPath+".tmp.png";
    string cmd = "convert";
    string args = EscapePath(path) + "[0] -thumbnail " +s+"x"+s+" " + EscapePath(tmp);
    ReadCmd(cmd, args);
    if (FileExists(tmp))
      Move (tmp, thumbPath);
    return FileExists(thumbPath);
  }

  /** FAST */
  public static string ThumbnailHash (string path) {
    return BitConverter.ToString(MD5("file://"+path)).Replace("-", "").ToLower();
  }

  /** ASYNC */
  public static byte[] MD5 (string s) {
    return MD5 (new System.Text.ASCIIEncoding().GetBytes(s));
  }

  /** ASYNC */
  public static byte[] MD5 (byte[] b) {
    using (HashAlgorithm h = HashAlgorithm.Create ("MD5"))
      return h.ComputeHash (b);
  }

  /** ASYNC */
  public static ImageSurface ToImageSurface (Gdk.Pixbuf pixbuf)
  {
    ImageSurface s = new ImageSurface (Format.ARGB32, pixbuf.Width, pixbuf.Height);
    using (Context cr = new Context(s)) {
      cr.Operator = Operator.Source;
      Gdk.CairoHelper.SetSourcePixbuf(cr, pixbuf, 0, 0);
      cr.Paint ();
    }
    return s;
  }

  /** NONE */
  public static ImageSurface ToImageSurface (string filename)
  {
    Gdk.Pixbuf pixbuf = new Gdk.Pixbuf (filename);
    return ToImageSurface(pixbuf);
  }

  /* String formatting helpers */

  /** FAST */
  public static string FormatSI (double sz, string unit)
  {
    return FormatSI (sz, unit, 1);
  }
  public static string FormatSI (double sz, string unit, uint decimals)
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
    return String.Format("{0} {1}"+unit, sz.ToString(String.Format("N{0}", decimals)), suffix);
  }


  /* File info helpers */

  /** BLOCKING */
  public static UnixFileSystemInfo[] Entries (string dirname) {
    return Entries (new UnixDirectoryInfo (dirname));
  }
  public static UnixFileSystemInfo[] Entries (UnixDirectoryInfo di) {
    return di.GetFileSystemEntries ();
  }

  /** BLOCKING */
  public static UnixFileSystemInfo[] EntriesMaybe (string dirname) {
    try { return Entries(dirname); }
    catch (Exception) { return new UnixFileSystemInfo[0]; }
  }

  public static UnixFileSystemInfo[] EntriesMaybe (UnixDirectoryInfo di) {
    try { return Entries(di); }
    catch (Exception) { return new UnixFileSystemInfo[0]; }
  }

  /** BLOCKING */
  public static FileTypes FileType (UnixFileSystemInfo f) {
    try {
      return (new UnixSymbolicLinkInfo(f.FullName)).FileType;
    }
    catch (System.InvalidOperationException) { return FileTypes.RegularFile; }
  }

  /** BLOCKING */
  public static string ReadLink (string path) {
    try {
      return (new UnixSymbolicLinkInfo(path)).ContentsPath;
    } catch (Exception) { return ""; }
  }

  /** BLOCKING */
  public static FileAccessPermissions FilePermissions (UnixFileSystemInfo f) {
    try { return f.FileAccessPermissions; }
    catch (System.InvalidOperationException) { return (FileAccessPermissions)0; }
  }

  /** BLOCKING */
  public static long FileSize (string fn) {
    try { return UnixFileSystemInfo.GetFileSystemEntry(fn).Length; }
    catch (System.InvalidOperationException) { return 0; }
  }
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
    return (UnixFileSystemInfo.GetFileSystemEntry (path)).Exists;
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


  public static string GetHomeDir (string username) {
    return new UnixUserInfo(username).HomeDirectory;
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

  public static string TildeExpand (string path) {
    if (path[0] == '~') {
      string[] split = path.Split(DirSepS.ToCharArray(), 2);
      string tildeExpr = split[0];
      try {
        string baseDir = HomeDir;
        if (tildeExpr != "~")
          baseDir = GetHomeDir(tildeExpr.Substring(1));
        return baseDir + (split.Length > 1 ? DirSepS + split[1] : "");
      } catch (Exception) { return path; }
    } else {
      return path;
    }
  }

  public static bool IsURI (string s)
  {
    try {
      new Uri (s);
      return true;
    } catch (Exception) {
      return false;
    }
  }

  /** FAST */
  public static string EscapePath (string path) {
    return "'" + path.Replace("'", @"'\''") + "'";
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


  public static string[] Without (string[] a, string v)
  {
    List<string> l = new List<string> (a.Length);
    bool first = true;
    foreach(string s in a) {
      if (first && s == v)
        first = false;
      else
        l.Add(s);
    }
    return l.ToArray();
  }

  public static string BytesToASCII (byte[] b)
  {
    return new System.Text.ASCIIEncoding().GetString(b);
  }

  /* Gtk helpers */

  public delegate void TextPromptHandler (string s);

  public static void TextPrompt
  (string title, string labelText, string entryText, string buttonText,
   int position, int selectStart, int selectEnd, TextPromptHandler onOk)
  {
    Dialog d = new Dialog();
    d.Modal = false;
    d.ActionArea.Layout = ButtonBoxStyle.Spread;
    d.HasSeparator = false;
    d.BorderWidth = 10;
    d.Title = title;
    Label label = new Label (labelText);
    label.UseUnderline = false;
    d.VBox.Add (label);
    Entry e = new Entry (entryText);
    e.WidthChars = Math.Min(100, e.Text.Length + 10);
    e.Activated += new EventHandler (delegate { d.Respond(ResponseType.Ok); });
    d.VBox.Add (e);
    d.AddButton (buttonText, ResponseType.Ok);

    d.Response += new ResponseHandler(delegate (object obj, ResponseArgs args) {
      if (args.ResponseId == ResponseType.Ok) {
        onOk(e.Text);
      } else {
        LogError (args.ResponseId);
      }
      d.Unrealize ();
      d.Destroy ();
    });

    d.ShowAll ();
    e.Position = position;
    e.SelectRegion(selectStart, selectEnd);
  }

  public static void PrintDragData (DragDataReceivedArgs e) {
    LogError("SuggestedAction: {0}, X:{1} Y:{2}", e.Context.SuggestedAction, e.X, e.Y);
    PrintSelectionData(e.SelectionData);
  }

  public static void PrintSelectionData (SelectionData sd) {
    LogError ();
    LogError("Selection: {0}", sd.Selection.Name);
    LogError("Target: {0}", sd.Target.Name);
    string[] targets = new string[sd.Targets.Length];
    for (int i=0; i<targets.Length; i++) targets[i] = sd.Targets[i].Name;
    LogError("Targets: {0}", String.Join(", ", targets));
    LogError("Format: {0}", sd.Format);
    LogError("Length: {0}", sd.Length);
    if (sd.Length < 0) return;
    LogError("Data: {0}", BitConverter.ToString(sd.Data));
    LogError("Pixbuf: {0}", sd.Pixbuf);
    LogError("Text: {0}", sd.Text);
    LogError("Type: {0}", sd.Type.Name);
    LogError("Uris: {0}", sd.Uris);
  }


  public static bool PrintErrors = true;

  public static void LogError ()
  {
    if (PrintErrors)
    Console.Error.WriteLine();
  }

  public static void LogError (string s)
  {
    if (PrintErrors)
    Console.Error.WriteLine(s);
  }
  public static void LogError (Exception s)
  {
    if (PrintErrors)
    Console.Error.WriteLine(s);
  }
  public static void LogError (object s)
  {
    if (PrintErrors)
    Console.Error.WriteLine(s);
  }
  public static void LogError (string s, params object[] list)
  {
    if (PrintErrors)
    Console.Error.WriteLine(s, list);
  }



  public static bool PrintDebugs = false;

  public static void LogDebug ()
  {
    if (PrintDebugs)
    Console.Error.WriteLine();
  }

  public static void LogDebug (string s)
  {
    if (PrintDebugs)
    Console.Error.WriteLine(s);
  }
  public static void LogDebug (Exception s)
  {
    if (PrintDebugs)
    Console.Error.WriteLine(s);
  }
  public static void LogDebug (object s)
  {
    if (PrintDebugs)
    Console.Error.WriteLine(s);
  }
  public static void LogDebug (string s, params object[] list)
  {
    if (PrintDebugs)
    Console.Error.WriteLine(s, list);
  }

}



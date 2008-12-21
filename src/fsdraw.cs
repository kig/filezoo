/*
    Filezoo - a small and fast file manager
    Copyright (C) 2008  Ilmari Heikkinen

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Mono.Unix;
using Cairo;


public class FSDraw
{
  // Colors for the different file types, quite like ls
  public Color DirectoryFGColor = new Color (0,0,1);
  public Color DirectoryBGColor = new Color (0,0,1);
  public Color BlockDeviceColor = new Color (0.75,0.5,0);
  public Color CharacterDeviceColor = new Color (0.5,0.25,0);
  public Color FifoColor = new Color (0.75,0,0.22);
  public Color SocketColor = new Color (0.75,0,0.82);
  public Color SymlinkColor = new Color (0,0.75,0.93);
  public Color ExecutableColor = new Color (0.2,0.6,0);
  public Color RegularFileColor = new Color (0,0,0);
  public Color ParentDirectoryColor = new Color (0,0,1);

  public Color UnfinishedDirectoryColor = new Color (0.5, 0, 1);
  public Color BackgroundColor = new Color (1,1,1);

  public string FileNameFontFamily = "Sans";
  public string FileInfoFontFamily = "Sans";

  // Style for the entries
  public double BoxWidth = 128.0;

  public double MinFontSize = 0.5;
  public double MaxFontSize = 12.0;

  Profiler FrameProfiler = new Profiler ();

  public double DefaultZoom = 2.0;
  public double DefaultPan = -0.43;

  public static Int64 frame = 0;

  /* Constructor */

  public FSDraw ()
  {
  }


  /* Titles */

  /** FAST */
  public string GetTitle (FSEntry d, Dictionary<string,string> prefixes) {
    string name = d.FullName == Helpers.RootDir ? d.FullName : d.Name;
    if (d.LinkTarget != null) name += " ➞ " + d.LinkTarget;
    if (prefixes != null && prefixes.ContainsKey(d.FullName))
      name = prefixes[d.FullName] + " " + name;
    return name;
  }

  /** FAST */
  /**
    Gets the information subtitle for the FSEntry instance.
    For directories, the subtitle contains the size of the subtree in files and
    bytes.
    For files, the subtitle contains the size of the file.
    */
  public string GetSubTitle (FSEntry d)
  {
    if (d.IsDirectory) {
      string extras = "";
      // entries sans parent dir
      extras += String.Format("{0} ", d.Count.ToString("N0"));
      extras += (d.Count == 1) ? "entry" : "entries";
      if (FSCache.Measurer.DependsOnTotals) {
/*        extras += String.Format(", {0} ", d.SubTreeCount.ToString("N0"));
        extras += d.SubTreeCount == 1 ? "file" : "files";*/
        extras += String.Format(", {0} total", Helpers.FormatSI(d.SubTreeSize, "B"));
      }
      return extras;
    } else {
      return String.Format("{0}", Helpers.FormatSI(d.Size, "B"));
    }
  }

  /** FAST */
  /**
    Returns a "rwxr-x--- owner group" string for the FSEntry.
    */
  public string PermissionString (FSEntry d)
  {
    string pstring = PermString (
      d.Permissions,
      FileAccessPermissions.UserRead,
      FileAccessPermissions.UserWrite,
      FileAccessPermissions.UserExecute
    ) + PermString (
      d.Permissions,
      FileAccessPermissions.GroupRead,
      FileAccessPermissions.GroupWrite,
      FileAccessPermissions.GroupExecute
    ) + PermString (
      d.Permissions,
      FileAccessPermissions.OtherRead,
      FileAccessPermissions.OtherWrite,
      FileAccessPermissions.OtherExecute
    );
    return String.Format ("{0} {1} {2}", pstring, d.Owner, d.Group);
  }

  /** FAST */
  /**
    @returns The "rwx"-string for the given permission enums.
    */
  string PermString (FileAccessPermissions permissions, FileAccessPermissions r, FileAccessPermissions w, FileAccessPermissions x)
  {
    char[] chars = {'-', '-', '-'};
    if ((permissions & r) == r) chars[0] = 'r';
    if ((permissions & w) == w) chars[1] = 'w';
    if ((permissions & x) == x) chars[2] = 'x';
    return new string(chars);
  }


  /* Drawing helpers */

  /** FAST */
  /**
    Gets the font size for the given device-space height of the FSEntry.
    */
  double GetFontSize (FSEntry d, double h)
  {
    return h * (d.IsDirectory ? 0.4 : 0.5);
  }

  /** FAST */
  /**
    Get the Cairo Color for the given filetype and permissions (permissions used
    to color executables green.)
    */
  public Color GetColor (FileTypes filetype, FileAccessPermissions perm)
  {
    switch (filetype) {
      case FileTypes.Directory: return DirectoryBGColor;
      case FileTypes.BlockDevice: return BlockDeviceColor;
      case FileTypes.CharacterDevice: return CharacterDeviceColor;
      case FileTypes.Fifo: return FifoColor;
      case FileTypes.Socket: return SocketColor;
      case FileTypes.SymbolicLink: return SymlinkColor;
    }
    if ((perm & FileAccessPermissions.UserExecute) != 0)
      return ExecutableColor;
    return RegularFileColor;
  }
  public Color GetFontColor (FileTypes filetype, FileAccessPermissions perm)
  {
    if (filetype == FileTypes.Directory) return DirectoryFGColor;
    return GetColor(filetype, perm);
  }


  /* Drawing */

  /** FAST */
  /**
    Checks if a 1 unit high object (DirStats is 1 unit high) is clipped by the
    target area and whether it falls between quarter pixels.
    If either is true, returns false, otherwise reckons the DirStats would be
    visible and returns true.
    */
  public bool IsVisible (DrawEntry d, Context cr, Rectangle target)
  {
    Matrix matrix = cr.Matrix;
    double h = matrix.Yy * d.Height;
    double y = matrix.Y0 - target.Y;
    // rectangle doesn't intersect any half-pixel midpoints
    if (h < 0.5 && (Math.Floor(y*2) == Math.Floor((y+h)*2)))
      return false;
    return ((y < target.Height) && ((y+h) > 0.0));
  }

  /** BLOCKING */
  /**
    Draw draws an FSEntry instance to the given Cairo Context, clipping it
    to the device-space Rectangle targetBox.

    If the FSEntry instance's on-screen presence is small, Draw won't draw its children.
    If the FSEntry is hidden, Draw won't draw it or its children.

    Draw uses the targetBox rectangle to determine its visibility (i.e. does the
    FSEntry instance fall outside the draw area and what size to clip the drawn
    rectangles.)

    @param d The FSEntry to draw.
    @param cr The Cairo.Context to draw on.
    @param targetBox The device-space clip box for determining object visibility.
    @param firstFrame Whether this frame should be drawn as fast as possible.
    @returns The number of files instances drawn.
  */
  public uint Draw
  (FSEntry d,
    Dictionary<string, string> prefixes,
    Dictionary<string, bool> selection,
    Context cr, Rectangle target) {
    return Draw (new DrawEntry(d), prefixes, selection, cr, target, 0);
  }
  public uint Draw
  (DrawEntry d, Dictionary<string, string> prefixes,
   Dictionary<string, bool> selection, Context cr, Rectangle target, uint depth)
  {
    d.F.LastDraw = frame;
    if (depth == 0) {
      FSEntry o = d.F;
      while ((o = o.ParentDir) != null)
        o.LastDraw = frame;
      FrameProfiler.Restart ();
    }
    if (d.GroupTitle != null && d.GroupHeight * cr.Matrix.Yy > 1)
      DrawGroupTitle (d.GroupTitle, d.GroupHeight, cr, target);
    if (depth > 0 && !IsVisible(d, cr, target)) {
      return 0;
    }
    double h = depth == 0 ? 1 : d.Height;
    uint c = 1;
    cr.Save ();
      cr.Scale (1, h);
      Matrix matrix = cr.Matrix;
      double rBoxWidth = BoxWidth / target.Height; // to keep pixel boxwidth the same
      Color bg = new Color (0,0,0,1);
      bg.A = 0.3;
      bg.A *= Helpers.Clamp(1-0.2*(matrix.Yy / target.Height), 0.0, 1);
      cr.Color = bg;
      if (depth == 0) {
        cr.Translate (0.005*rBoxWidth, 0);
        matrix.X0 += 0.005*rBoxWidth*matrix.Xx;
      }
      Helpers.DrawRectangle(cr, -0.01*rBoxWidth, 0.0, rBoxWidth*1.02, 1.02, target);
      cr.Fill ();
      Color co = GetColor (d.F.FileType, d.F.Permissions);
      if (!d.F.Complete && FSCache.Measurer.DependsOnTotals)
        co = UnfinishedDirectoryColor;
       if (d.F.IsDirectory) // fade out dir based on size on screen
        co.A *= 0.2 * Helpers.Clamp(1-0.2*(matrix.Yy / target.Height), 0.0, 1);
       else
        co.A *= 0.1 + 0.7 * Helpers.Clamp(1-0.5*(matrix.Yy / target.Height), 0.0, 1);
      cr.Color = co;
      Helpers.DrawRectangle (cr, 0.0, 0.02, rBoxWidth, 0.96, target);
      if (d.F.Thumbnail != null)
        DrawThumb (d.F, cr, target);
      else
        cr.Fill ();
      if (d.F.IsDirectory) {
        cr.Save ();
          if (matrix.Yy > 8 && matrix.Yy < 4000) {
            Helpers.DrawRectangle (cr, 0.0, 0.02, rBoxWidth, 0.96, target);
            using (LinearGradient g = new LinearGradient (0.0,0.02,0.0,0.96)) {
              g.AddColorStop (0, new Color (0,0,0,0.8));
              g.AddColorStop (Helpers.Clamp(1 / matrix.Yy, 0.001, 0.01), new Color (0,0,0,0));
              g.AddColorStop (0.75, new Color (0, 0, 0, co.A));
              g.AddColorStop (1, new Color (0,0,0,co.A*1.8));
              cr.Pattern = g;
              cr.Fill ();
              Helpers.DrawRectangle (cr, 0.0, 0.98, rBoxWidth, Math.Min(0.01, 1 / matrix.Yy), target);
              cr.Color = new Color (0,0,0,0.8);
              cr.Fill ();
            }
          }
          if (matrix.Yy > 2) {
            cr.Color = (!d.F.Complete && FSCache.Measurer.DependsOnTotals) ? FifoColor : RegularFileColor;
            double lh = (matrix.Yy * 0.02 > 3) ? (3 / matrix.Yy) : 0.02;
            Helpers.DrawRectangle (cr, rBoxWidth * 0.95, 0.02, 0.05*rBoxWidth, lh, target);
            Helpers.DrawRectangle (cr, 0, 0.02, 0.05*rBoxWidth, lh, target);
            cr.Fill ();
          }
        cr.Restore ();
        co = DirectoryFGColor;
      }
      // Color is a struct, so changing the A doesn't propagate
      co.A = 1;
      cr.Color = co;
      if (matrix.Yy > 0.5 || depth < 2)
        DrawTitle (d.F, prefixes, cr, target, depth);
      if (d.F.IsDirectory) {
        bool childrenVisible = matrix.Yy > 2;
        bool shouldDrawChildren = depth == 0 || childrenVisible;
        if (shouldDrawChildren) {
          c += DrawChildren(d, prefixes, selection ,cr, target, depth);
        }
      }
      if (selection.ContainsKey(d.F.FullName)) {
        using (LinearGradient g = new LinearGradient (0.0, 0.0, 1.0, 0.0)) {
          Helpers.DrawRectangle (cr, 0.0, 0.02, 1.0, 0.96, target);
          Color co2 = RegularFileColor;
          co2.A = 0.1;
          g.AddColorStop (0, co2);
          co2.A = 0.7;
          g.AddColorStop (1, co2);
          cr.Pattern = g;
          cr.Fill ();
        }
      }
    cr.Restore ();
    if (depth == 0) {
      FrameProfiler.Stop ();
      frame++;
    }
    return c;
  }

  /** BLOCKING */
  /**
    Draws the thumbnail of the FSEntry.
    */
  void DrawThumb (FSEntry d, Context cr, Rectangle target) {
    ImageSurface thumb;
    lock (d) {
      thumb = d.Thumbnail;
      if (thumb == null) return;
      double rBoxWidth = BoxWidth / target.Height;
      using (Pattern p = new Pattern (thumb)) {
        cr.Save ();
            Matrix matrix = cr.Matrix;
            double wr = matrix.Xx * rBoxWidth;
            double hr = matrix.Yy * 0.96;
            double wscale = wr / thumb.Width;
            double hscale = hr / thumb.Height;
            double scale = Math.Max (wscale, hscale);
            double x = 0.5*rBoxWidth*(1 - (scale/wscale));
            double y = 0.02 + 0.5*0.48*(1 - (scale/hscale));
            cr.Translate (x, y);
            cr.Scale (scale / matrix.Xx, scale / matrix.Yy);
            Matrix pm = p.Matrix;
            pm.Xx = Math.Max(1, pm.Xx);
            pm.Yy = Math.Max(1, pm.Yy);
            p.Matrix = pm;
          cr.Pattern = p;
          cr.Fill ();
        cr.Restore ();
      }
    }
  }

  double ChildYOffset = 0.48;
  double ChildBoxHeight = 0.44;

  /** FAST */
  /**
    Sets up child area transform for the FSEntry.
    */
  void ChildTransform (DrawEntry d, Context cr, Rectangle target)
  {
    double rBoxWidth = BoxWidth / target.Height;
    double fac = 0.1 * Helpers.Clamp(1-(cr.Matrix.Yy / target.Height), 0.0, 1.0);
    cr.Translate (0.5*fac*rBoxWidth, ChildYOffset);
    cr.Scale (1.0-fac, ChildBoxHeight);
  }

  /** BLOCKING */
  /**
    Draws the title for the FSEntry.
    Usually draws the filename bigger and the subtitle a bit smaller.

    If the FSEntry is small, draws the subtitle at the same size and same time
    as the filename for speed.

    If the FSEntry is very small, draws a rectangle instead of text for speed.
    */
  void DrawTitle
  (FSEntry d, Dictionary<string, string> prefixes, Context cr, Rectangle target, uint depth)
  {
    Color co2 = DirectoryFGColor;
    double rBoxWidth = BoxWidth / target.Height;
    Matrix matrix = cr.Matrix;
    double h = matrix.Yy;
    double rfs = GetFontSize(d, h);
    double fs = Helpers.Clamp(rfs, MinFontSize, MaxFontSize);
    co2.A = Math.Min(1, fs / MaxFontSize);
    cr.Color = co2;
    cr.Save ();
      cr.Translate(rBoxWidth * 1, 0.02);
      double be = matrix.X0 + rBoxWidth * matrix.Xx;
      cr.Translate(rBoxWidth * 0.1, 0.0);
      if (d.IsDirectory && rfs > 60)
        cr.Translate(0.0, 0.46);
      double x = matrix.X0 + rBoxWidth * 1.1 * matrix.Xx;
      double y = cr.Matrix.Y0;
      if (d.IsDirectory && rfs > 60)
        y -= 60;
      if (y > -fs*4) {
        cr.IdentityMatrix ();
        cr.Translate (x, y);
        cr.NewPath ();
        string name = GetTitle(d, prefixes);
        if (fs > 4) {
          if (depth == 0)
            cr.Translate (0, -fs*0.5);
          if (d.IsDirectory && depth < 2)
            DrawTitleLine (cr, fs, h, be, x, depth);
          cr.MoveTo (0, -fs*0.2);
          Helpers.DrawText (cr, FileNameFontFamily, fs, name);

          double sfs = Helpers.Clamp(
            d.IsDirectory ? rfs*0.18 : rfs*0.28,
            MinFontSize, MaxFontSize*0.6);
          if (sfs > 1) {
            double a = sfs / (MaxFontSize*0.6);
            Color co = DirectoryFGColor; // GetFontColor (d.FileType, d.Permissions);
            co.A = a*a;
            cr.Color = co;
            cr.MoveTo (0, fs*1.1+sfs*0.7);
            Helpers.DrawText (cr, FileInfoFontFamily, sfs*1.1, d.LastModified.ToString() + " - " + GetSubTitle (d));
            cr.MoveTo (0, fs*1.1+sfs*2.1);
            Helpers.DrawText (cr, FileInfoFontFamily, sfs, PermissionString (d));
          }
        } else if (fs > 1) {
          cr.MoveTo (0, fs*0.1);
          Helpers.DrawText (cr, FileNameFontFamily, fs, name);
        } else {
          Helpers.DrawRectangle (cr, 0.0, 0.0, fs / 2 * name.Length, fs/3, target);
          cr.Fill ();
        }
      }
    cr.Restore ();
  }

  void DrawGroupTitle (string title, double h, Context cr, Rectangle target)
  {
    Matrix matrix = cr.Matrix;
    double fs = Math.Min (20.0, matrix.Yy * h * 0.66);
    double szf = Math.Min(1, 10 * matrix.Yy / target.Height);
    cr.Save ();
      double x = matrix.X0;
      double y = matrix.Y0;
      double rBoxWidth = BoxWidth / target.Height;
      double w = Math.Min(rBoxWidth * matrix.Xx * 4, target.Width);
      if (y > -4*fs) {
        cr.IdentityMatrix ();
        cr.Rectangle (x, y, w-x, 1);
        Color co = DirectoryFGColor;
        co.A = (0.1 + 0.2 * (fs/20)) * szf;
        cr.Color = co;
        cr.Fill ();
        if (fs > 2) {
          co.A = szf * (0.8 - 0.6 * (Math.Abs(fs-15)/15));
          cr.Color = co;
          cr.MoveTo(w, y);
          Helpers.DrawText (cr, FileInfoFontFamily, fs, title, Pango.Alignment.Right);
          cr.NewPath ();
        }
      }
    cr.Restore ();
  }

  void DrawTitleLine (Context cr, double fs, double h, double be, double x, uint depth)
  {
    cr.Save ();
      cr.Rectangle (20, 0, be-x+2-22, 50);
      cr.Clip ();
      cr.MoveTo (-3, fs*1.3);
      double cw = be-x, ch = (h > 50 ? 1 : (h/50)*(h/50)) * (x-be);
      cr.RelLineTo (cw+6,0);
      cr.RelLineTo (cw,ch);
      Color c = RegularFileColor;
//       c.A = Math.Min(1, h / 50);
      cr.Color = c;
      cr.LineCap = LineCap.Square;
      cr.LineWidth = 2 - depth;
      cr.Stroke ();
    cr.Restore ();
  }

  /** BLOCKING */
  /**
    Draws the children entries of a FSEntry.
    Bails out if no children are yet created and frame has run out of time.

    Sets up the child area transform and draws each child.

    @returns The total amount of subtree files drawn.
    */
  uint DrawChildren
  (DrawEntry d, Dictionary<string, string> prefixes,
   Dictionary<string, bool> selection, Context cr, Rectangle target, uint depth)
  {
    List<DrawEntry> entries = d.F.DrawEntries;
    if (entries == null) return 0;
    cr.Save ();
      ChildTransform (d, cr, target);
      uint c = 0;
      foreach (DrawEntry child in entries) {
        double h = child.Height;
        c += Draw (child, prefixes, selection, cr, target, depth+1);
        cr.Translate (0.0, h);
      }
    cr.Restore ();
    return c;
  }


  /* Visibility */

  System.Object PreDrawCancelLock = new System.Object ();
  System.Object PreDrawLock = new System.Object ();
  int PreDrawInProgress = 0;
  bool PreDrawCancelled = false;

  public void CancelPreDraw ()
  {
    lock (PreDrawCancelLock) {
      PreDrawCancelled = true;
      while (PreDrawInProgress != 0)
        Thread.Sleep (5);
      PreDrawCancelled = false;
    }
  }

  /** ASYNC */
  public bool PreDraw (FSEntry d, Context cr, Rectangle target, uint depth)
  {
    return PreDraw (new DrawEntry (d), cr, target, depth);
  }
  public bool PreDraw (DrawEntry d, Context cr, Rectangle target, uint depth)
  {
    lock (PreDrawLock) PreDrawInProgress ++;
    bool rv = true;
    try {
      if (depth == 0  && d.F.IsDirectory && FSCache.Measurer.DependsOnTotals && (d.F.Complete || !d.F.InProgress))
          FSCache.RequestTraversal(d.F.FullName);
      if (depth > 0 && !IsVisible(d, cr, target)) return true;
      double h = depth == 0 ? 1 : d.Height;
      if (PreDrawCancelled) {
        rv = false;
      } else {
        cr.Save ();
          cr.Scale (1, h);
          d.F.LastDraw = FSDraw.frame;
          RequestThumbnail (d.F.FullName, (int)cr.Matrix.Yy);
          if (d.F.IsDirectory) {
            bool childrenVisible = cr.Matrix.Yy > 2;
            bool shouldDrawChildren = (depth == 0 || childrenVisible);
            if (shouldDrawChildren)
              rv &= PreDrawChildren(d, cr, target, depth);
          }
        cr.Restore ();
      }
    } finally {
      lock (PreDrawLock) PreDrawInProgress --;
    }
    return rv;
  }

  /** ASYNC */
  bool PreDrawChildren (DrawEntry d, Context cr, Rectangle target, uint depth)
  {
    ChildTransform (d, cr, target);
    FSCache.FilePass(d.F.FullName);
    FSCache.UpdateDrawEntries(d.F);
    if (PreDrawCancelled) return false;
    foreach (DrawEntry ch in d.F.DrawEntries) {
      PreDraw (ch, cr, target, depth+1);
      cr.Translate (0.0, ch.Height);
      if (PreDrawCancelled) return false;
    }
    return true;
  }

  /** ASYNC */
  void RequestThumbnail (string path, int priority)
  {
    FSCache.FetchThumbnail (path, priority);
  }


  /* Click handler */

  /** BLOCKING */
  /**
    Click handles the click events directed at the FSEntry.
    It takes the Cairo Context cr, the clip Rectangle target and the mouse
    device-space coordinates as its arguments, and returns an List<ClickHit> of
    ClickHit objects for the entries hit.

    @param d FSEntry to query.
    @param cr Cairo.Context to query.
    @param target Target clip rectangle. See Draw for a better explanation.
    @param mouseX The X coordinate of the mouse pointer, X grows right from left.
    @param mouseY The Y coordinate of the mouse pointer, Y grows down from top.
    @returns A List<ClickHit> of the hit entries.
    */
  public List<ClickHit> Click
  (FSEntry d,
    Dictionary<string, string> prefixes,
    Context cr, Rectangle target, double mouseX, double mouseY)
  { return Click (new DrawEntry(d), prefixes, cr, target, mouseX, mouseY, 0); }
  public List<ClickHit> Click
  (DrawEntry d,
    Dictionary<string, string> prefixes,
    Context cr, Rectangle target, double mouseX, double mouseY, uint depth)
  {
    // return empty list if click outside target or if non-root d is not visible
    if (
      (depth == 0 &&
        (mouseX < target.X || mouseX > target.X+target.Width ||
        mouseY < target.Y || mouseY > target.Y+target.Height)) ||
      (depth > 0 && !IsVisible(d, cr, target))
    ) {
      return new List<ClickHit> ();
    }
    double h = depth == 0 ? 1 : d.Height;
    List<ClickHit> retval = new List<ClickHit> ();
    double advance = 0.0;
    cr.Save ();
      cr.Scale (1, h);
      double rBoxWidth = BoxWidth / target.Height;
      Matrix matrix = cr.Matrix;
      if (matrix.Y0 < mouseY+1 && matrix.Y0+matrix.Yy > mouseY-1) {
        if (d.F.IsDirectory && (matrix.Yy > 16) && d.F.DrawEntries != null)
          retval.AddRange( ClickChildren (d, prefixes, cr, target, mouseX, mouseY, depth) );
        cr.NewPath ();
        h = matrix.Yy;
        double rfs = GetFontSize(d.F, h);
        double fs = Helpers.Clamp(rfs, MinFontSize, MaxFontSize);
        string name = GetTitle(d.F, prefixes);
        if (fs < 10) {
          advance += fs * name.Length / matrix.Xx;
        } else if (matrix.Y0 > -fs*4 && matrix.Y0 < target.Y + target.Height + fs*4) {
          cr.Save ();
            cr.IdentityMatrix ();
            advance += Helpers.GetTextExtents (cr, FileNameFontFamily, fs, name).XAdvance / matrix.Xx;
          cr.Restore ();
        }
        cr.Rectangle (0.0, 0.0, rBoxWidth * 1.1 + advance, 1.0);
        double ys = matrix.Yy;
        cr.IdentityMatrix ();
        if (cr.InFill(mouseX,mouseY))
          retval.Add(new ClickHit(d.F, ys));
      }
    cr.Restore ();
    return retval;
  }

  /** BLOCKING */
  /**
    Passes the click check to the children of the FSEntry.
    Sets up the child area transform and calls Click on each child in d.DrawEntries.
    Returns the first Click return value with one or more entries.
    If nothing was hit, returns an empty List.
    */
  List<ClickHit> ClickChildren
  (DrawEntry d,
    Dictionary<string, string> prefixes,
    Context cr, Rectangle target, double mouseX, double mouseY, uint depth)
  {
    List<DrawEntry> entries = d.F.DrawEntries;
    List<ClickHit> retval = new List<ClickHit> ();
    if (entries == null) return retval;
    cr.Save ();
      ChildTransform (d, cr, target);
      foreach (DrawEntry child in entries) {
        retval = Click (child, prefixes, cr, target, mouseX, mouseY, depth+1);
        if (retval.Count > 0) break;
        cr.Translate (0.0, child.Height);
      }
    cr.Restore ();
    return retval;
  }


  /* Covering */

  /** BLOCKING */
  /**
    Finds the deepest FSEntry that covers the full screen.
    Used to do zoom navigation.

    Returns the Covering with the FSEntry, the relative zoom and the relative pan.
    */
  public Covering FindCovering
  (FSEntry d, Context cr, Rectangle target, uint depth)
  {
    return FindCovering (new DrawEntry(d), cr, target, depth);
  }
  public Covering FindCovering
  (DrawEntry d, Context cr, Rectangle target, uint depth)
  {
    Covering retval = (depth == 0 ? GetCovering(d.F, cr, target) : null);
    if (!d.F.IsDirectory || (depth > 0 && !IsVisible(d, cr, target)))
      return retval;
    double h = depth == 0 ? 1 : d.Height;
    cr.Save ();
      cr.Scale (1, h);
      if (cr.Matrix.Y0 <= target.Y && cr.Matrix.Y0+cr.Matrix.Yy >= target.Y+target.Height) {
        retval = GetCovering (d.F, cr, target);
        List<DrawEntry> entries = d.F.DrawEntries;
        if (entries != null) {
          ChildTransform (d, cr, target);
          foreach (DrawEntry ch in entries) {
            Covering c = FindCovering (ch, cr, target, depth+1);
            if (c != null) {
              retval = c;
              break;
            }
            cr.Translate (0.0, ch.Height);
          }
        }
      } else if (depth == 0 && d.F.FullName != Helpers.RootDir) { // navigating upwards
        double scale = 1 / DefaultZoom;
        double position = ChildYOffset;
        int i = 0;
        List<DrawEntry> entries = d.F.ParentDir.DrawEntries;
        if (entries == null || d.F.ParentDir.Comparer != FSCache.Comparer || d.F.ParentDir.Measurer != FSCache.Measurer) {
          if (FSCache.NeedFilePass( d.F.ParentDir.FullName ))
            FSCache.FilePass ( d.F.ParentDir.FullName );
          FSCache.UpdateDrawEntries ( d.F.ParentDir );
          entries = d.F.ParentDir.DrawEntries;
        }
        foreach (DrawEntry ch in entries) {
          if (ch.F.FullName == d.F.FullName) {
            scale = ChildBoxHeight * ch.Height / retval.Zoom;
            break;
          }
          i++;
          position += ChildBoxHeight * ch.Height;
        }
        retval = new Covering (d.F.ParentDir, 1 / scale, -position+(retval.Pan *retval.Zoom* scale));
      }
    cr.Restore ();
    return retval;
  }

  Covering GetCovering (FSEntry d, Context cr, Rectangle target)
  {
    double z = cr.Matrix.Yy / target.Height;
    double pan = (cr.Matrix.Y0-target.Y) / (target.Height*z);
    return new Covering (d, z, pan);
  }


}


public struct ClickHit
{
  public FSEntry Target;
  public double Height;
  public ClickHit (FSEntry d, double h)
  {
    Target = d;
    Height = h;
  }
}


public class Covering
{
  public double Zoom;
  public double Pan;
  public FSEntry Directory;
  public Covering (FSEntry d, double z, double p)
  {
    Directory = d;
    Zoom = z;
    Pan = p;
  }
}

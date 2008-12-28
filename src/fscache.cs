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

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Timers;
using System.Linq;
using System.IO;
using System;
using Mono.Unix;
using Cairo;

/**
  FSCache is the filesystem cache server.
  It is a static class, acting as a global singleton.

  The FSCache allows retrieving its cache entries with:

    FSEntry d = FSCache.Get(path);

  To request traversal of a path (and thus ensuring that the cache entries
  underlying it get traversed), you use RequestTraversal:

    FSCache.RequestTraversal(path);

  A cache entry is dynamically updated as it is traversed. Once the traversal
  of the cache entry is complete, the cache entry is marked as complete.

    bool complete = FSCache.Get(path).Complete;

  If a cache entry is a current target for traversal, its InProgress is true.
  The way you should use the cache to retrieve a dynamically updating cache
  entry for a filesystem path is as follows:

    FSEntry d = FSCache.Get(path);
    if (!d.Complete && !d.InProgress)
      FSCache.RequestTraversal(path);

  When you want to cancel all traversals currently in progress (maybe you
  navigated to a different directory and want to traverse its entries first),
  use CancelTraversal:

    FSCache.CancelTraversal();

  Note that CancelTraversal blocks until all traversals have exited, which may
  take hundreds of milliseconds.

  To invalidate a cache entry (e.g. FileSystemWatcher told you of a change in
  the filesystem and now you want to update the cache to match reality), use
  Invalidate:

    FSCache.Invalidate(path);

  */
public static class FSCache
{
  public static Dictionary<string,FSEntry> Cache = new Dictionary<string,FSEntry> ();
  public static FileSystemWatcher Watcher;
  static Object WatchLock = new Object ();

  static Dictionary<string,bool> Invalids = new Dictionary<string,bool> ();

  public static IMeasurer Measurer;
  public static IComparer<FSEntry> Comparer;
  public static SortingDirection SortDirection;

  public static DateTime LastChange = DateTime.Now;

  static Profiler TraversalProfiler = new Profiler ("Traversal", 0);


  static bool TraversalCancelled = false;
  static long TraversalCounter = 0;
  public static long OptimalTraverseThreads = 4;
  static long TraverseThreadCount = 0;
  static Object CancelLock = new Object ();
  static Object TCLock = new Object ();

  static System.Timers.Timer InvalidsTimer = null;

  static Thread ThumbnailThread;
  static PriorityQueue ThumbnailQueue = new PriorityQueue ();
  static Dictionary<string,FSEntry> ThumbnailCache = new Dictionary<string,FSEntry> ();

  static Dictionary<string,TraversalInfo> TraversalCache = new Dictionary<string,TraversalInfo> ();


  /** BLOCKING */
  public static void Watch (string path)
  { lock (WatchLock) {
    if (Watcher == null || Watcher.Path != path) {
      if (Watcher != null) Watcher.Dispose ();
      Watcher = MakeWatcher (path);
      if (InvalidsTimer == null) {
        InvalidsTimer = new System.Timers.Timer ();
        InvalidsTimer.Elapsed += new ElapsedEventHandler(ProcessInvalids);
        InvalidsTimer.Interval = 100;
        InvalidsTimer.Enabled = true;
      }
    }
  } }

  /** BLOCKING */
  public static FSEntry FastGet (string path)
  {
    if (Cache.ContainsKey(path)) return Cache[path];
    else return Get (path);
  }

  /** BLOCKING */
  public static FSEntry Get (string path)
  { lock (Cache) {
    if (Cache.ContainsKey(path))
      return Cache[path];
    FSEntry f = new FSEntry (path);
    Cache[path] = f;
    CreateParents (f);
    TraversalInfo i = new TraversalInfo(0,0,DateTime.Now);
    bool hasInfo = false;
    lock (TraversalCache) {
      if (TraversalCache.ContainsKey(path)) {
        i = TraversalCache[path];
        hasInfo = true;
      }
    }
    if (hasInfo)
      SetCountAndSize(path, i.count, i.size);
    return f;
  } }

  /** ASYNC */
  public static void CancelTraversal ()
  {
    lock (CancelLock) {
      TraversalCancelled = true;
      while (TraversalCounter != 0) {
        Thread.Sleep (50);
      }
      lock (Cache) {
        foreach (FSEntry d in Cache.Values)
          d.InProgress = false;
      }
      TraversalCancelled = false;
    }
  }

  /** ASYNC */
  public static void RequestTraversal (string dirname)
  {
    lock (CancelLock) {}
    TraversalProfiler.Restart ();
    ThreadTraverse(dirname);
  }

  /** BLOCKING */
  public static void FilePass (string path) { FilePass (Get(path)); }
  public static void FilePass (FSEntry f)
  {
    string path = f.FullName;
    if (f.FilePassDone && f.LastFileChange == Helpers.LastChange(path)) return;
    f.LastFileChange = Helpers.LastChange(path);
    if (f.IsDirectory) {
      List<FSEntry> entries = new List<FSEntry> ();
      long count = 0;
      foreach (UnixFileSystemInfo u in Helpers.EntriesMaybe (f.FullName)) {
        FSEntry d = Get (u.FullName);
        entries.Add (d);
        count++;
      }
      lock (Cache) {
        f.Entries.Clear ();
        foreach(FSEntry e in entries) f.Entries.Add(e);
        f.Count = count;
        f.FilePassDone = true;
        if (AllChildrenComplete(path))
          SetComplete (path);
        f.LastChange = LastChange = DateTime.Now;
      }
    }
  }

  /** ASYNC */
  public static void UpdateDrawEntries (FSEntry f)
  { lock (Cache) {
    if (!f.IsDirectory) return;
    bool needRefresh = false;
    // measure entries
    // sort entries
    if (!(
      f.Comparer == Comparer
      && f.SortDirection == SortDirection
      && f.LastSort == f.LastChange
    )) {
      DateTime lc = f.LastChange;
      f.Comparer = Comparer;
      f.SortDirection = SortDirection;
      f.Entries.Sort(Comparer);
      if (SortDirection == SortingDirection.Descending)
        f.Entries.Reverse();
      f.LastSort = lc;
      //Console.WriteLine("Sorted {0}", f.FullName);
      needRefresh = true;
    }
    if (!(f.Measurer == Measurer && f.LastMeasure == f.LastChange)) {
      DateTime lc = f.LastChange;
      f.Measurer = Measurer;
      double totalHeight = 0.0;
      foreach (FSEntry e in f.Entries) {
        if (Measurer.DependsOnEntries && !e.FilePassDone)
          FilePass (e);
        e.Height = Measurer.Measure(e);
        totalHeight += e.Height;
      }
      double scale = 1.0 / totalHeight;
      foreach (FSEntry e in f.Entries) {
        e.Height *= scale;
      }
      f.LastMeasure = lc;
      //Console.WriteLine("Measured {0}", f.FullName);
      needRefresh = true;
    }
    if (needRefresh) {
      List<DrawEntry> entries = new List<DrawEntry>(f.Entries.Select(o => new DrawEntry (o)));
      // set group titles (e.g. first letter of name, suffix)
      if (entries.Count > 0) {
        DrawEntry prevGroup = entries[0];
        double heightSum = prevGroup.Height;
        prevGroup.GroupTitle = ((IGrouping)Comparer).GroupTitle(prevGroup.F);
        for (int i=1; i<entries.Count; i++) {
          DrawEntry current = entries[i];
          DrawEntry prev = entries[i-1];
          if (((IGrouping)Comparer).GroupChanged(prev.F, current.F)) {
            current.GroupTitle = ((IGrouping)Comparer).GroupTitle(current.F);
            prevGroup.GroupHeight = heightSum;
            prevGroup = current;
            heightSum = 0;
          }
          heightSum += current.Height;
        }
        prevGroup.GroupHeight = heightSum;
      }
      f.DrawEntries = entries;
    }
  } }


  /** ASYNC */
  public static void Invalidate (string path)
  { lock (Cache) {
    //Console.WriteLine("Invalidate on {0}", path);
    if (Cache.ContainsKey(path)) {
      if (Helpers.FileExists(path)) {
        Modified (path);
      } else {
        Deleted (path);
      }
    } else if (Cache.ContainsKey(Helpers.Dirname(path))) {
      Modified (Helpers.Dirname(path));
    } else {
      return;
    }
  } }

  /** ASYNC */
  public static void ProcessInvalids (object sender, ElapsedEventArgs e)
  {
    string[] paths;
    lock (Invalids) {
      if (Invalids.Keys.Count == 0) return;
      paths = new string[Invalids.Keys.Count];
      Invalids.Keys.CopyTo (paths, 0);
      Invalids.Clear ();
    }
    foreach (string path in paths)
      Invalidate (path);
  }

  /** ASYNC */
  public static bool NeedFilePass (string path)
  { lock (Cache) {
    return NeedFilePass (Get(path));
  } }
  public static bool NeedFilePass (FSEntry d)
  { lock (Cache) {
    return !d.FilePassDone;
  } }

  static Gnome.ThumbnailFactory TNF = new Gnome.ThumbnailFactory (Gnome.ThumbnailSize.Normal);
  /** ASYNC */
  public static void FetchThumbnail (string path, int priority)
  {
    FSEntry f = Get (path);
    if (f.Thumbnail != null) return;
    if (f.IsDirectory) return;
    string uri = "file://" + path;
    Gnome.Vfs.Vfs.Initialize ();
    string mime = Gnome.Vfs.MimeType.GetMimeTypeForUri(uri);
    if (TNF.CanThumbnail(uri, mime, f.LastModified))
    {
        lock (ThumbnailQueue) ThumbnailQueue.Enqueue(f.FullName, priority);
    }
    lock (ThumbnailQueue) {
      if (ThumbnailThread == null) {
        ThumbnailThread = new Thread(new ThreadStart(ThumbnailQueueProcessor));
        ThumbnailThread.IsBackground = true;
        ThumbnailThread.Start ();
      }
    }
  }

  /** ASYNC */
  public static void CancelThumbnailing ()
  { lock (ThumbnailQueue) {
    ThumbnailQueue.Clear ();
  } }

  /** ASYNC */
  public static void PruneCache (int maxFrameDelta)
  { lock (Cache) {
    List<string> deletions = new List<string> ();
    foreach (FSEntry d in Cache.Values) {
      if (d.ParentDir != null && FSDraw.frame - d.LastDraw > maxFrameDelta) {
        DestroyThumbnail(d);
        d.ParentDir.LastChange = d.ParentDir.LastFileChange = DateTime.Now;
        d.ParentDir.FilePassDone = false;
        if (d.ParentDir.Entries != null)
          d.ParentDir.Entries.Clear ();
        d.Entries = null;
        d.DrawEntries = null;
        deletions.Add(d.FullName);
      }
    }
    foreach (string k in deletions)
      Cache.Remove(k);
    if (deletions.Count > 0) {
      LastChange = DateTime.Now;
    }
  } }

  /** ASYNC */
  public static void ClearTraversalCache ()
  { lock (Cache) {
    lock (TraversalCache) {
      foreach (FSEntry d in Cache.Values) {
        if (d.IsDirectory) {
          d.Complete = false;
          d.SubTreeCount = d.SubTreeSize = 0;
        }
      }
      TraversalCache.Clear ();
    }
  } }



  /* Tree editing */

  /** BLOCKING */
  static void CreateParents (FSEntry f)
  {
    if (f.FullName == Helpers.RootDir) return;
    f.ParentDir = Get (Helpers.Dirname (f.FullName));
  }

  /** ASYNC */
  static void AddCountAndSize (string path, long count, long size)
  { lock (Cache) {
    FSEntry d = Get (path);
    d.SubTreeCount += count;
    d.SubTreeSize += size;
    d.LastChange = LastChange = DateTime.Now;
    foreach (FSEntry a in GetAncestors (path)) {
      a.SubTreeCount += count;
      a.SubTreeSize += size;
      a.LastChange = LastChange;
    }
  } }

  /** ASYNC */
  static void SetCountAndSize (string path, long count, long size)
  { lock (Cache) {
    FSEntry d = Get (path);
    long oldCount = d.SubTreeCount, oldSize = d.SubTreeSize;
    d.SubTreeCount = count;
    d.SubTreeSize = size;
    d.Complete = true;
    d.InProgress = false;
    d.LastChange = LastChange = DateTime.Now;
    foreach (FSEntry a in GetAncestors (path)) {
      if (!a.Complete) {
        a.SubTreeCount += count-oldCount;
        a.SubTreeSize += size-oldSize;
        a.LastChange = LastChange;
      }
    }
  } }

  /** ASYNC */
  static void SetComplete (string path)
  { lock (Cache) {
    FSEntry d = Get (path);
    d.Complete = true;
    d.LastChange = LastChange = DateTime.Now;
    if (path != Helpers.RootDir) {
      string p = Helpers.Dirname (path);
      if (p.Length > 0 && AllChildrenComplete (p))
        SetComplete (p);
    }
  } }

  /** ASYNC */
  static void SetIncomplete (string path)
  { lock (Cache) {
    FSEntry d = Get (path);
    d.Complete = false;
    d.LastChange = LastChange = DateTime.Now;
    if (path != Helpers.RootDir) {
      string p = Helpers.Dirname (path);
      if (p.Length > 0)
        SetIncomplete (p);
    }
  } }

  /** ASYNC */
  static bool AllChildrenComplete (string path)
  { lock (Cache) {
    if (NeedFilePass(path)) return false;
    foreach (FSEntry c in GetChildren (path))
      if (!c.Complete) return false;
    return true;
  } }

  /** ASYNC */
  static List<FSEntry> GetChildren (string path)
  { lock (Cache) {
    return Get(path).Entries;
  } }

  /** ASYNC */
  static List<FSEntry> GetAncestors (string path)
  { lock (Cache) {
    string s = Helpers.Dirname (path);
    List<FSEntry> a = new List<FSEntry> ();
    while (s.Length > 0) {
      a.Add (Get (s));
      s = Helpers.Dirname (s);
    }
    return a;
  } }



  /* Cache invalidation */

  /** ASYNC */
  static void Deleted (string path)
  { lock (Cache) {
    //Console.WriteLine("Deleted on {0}", path);
    // ditch path's children, ditch path, excise path from parent,
    // set parent complete if path was the only incomplete child in it
    FSEntry d = Get (path);
    LastChange = DateTime.Now;
    DestroyThumbnail(d);
    DeleteChildren (path);
    d.ParentDir.Entries.Remove(d);
    if (d.IsDirectory)
      AddCountAndSize (d.ParentDir.FullName, -d.SubTreeCount, -d.SubTreeSize);
    else
      AddCountAndSize (d.ParentDir.FullName, -1, -d.Size);
    if (!d.Complete && AllChildrenComplete(d.ParentDir.FullName))
      SetComplete (d.ParentDir.FullName);
    d.LastChange = d.ParentDir.LastChange = LastChange = DateTime.Now;
  } }

  /** ASYNC */
  static void DeleteChildren (string path)
  { lock (Cache) {
    if (Cache.ContainsKey(path)) {
      FSEntry d = Cache[path];
      DestroyThumbnail(d);
      Cache.Remove (path);
      lock (TraversalCache)
        if (TraversalCache.ContainsKey(path))
          TraversalCache.Remove(path);
      if (d.Entries != null) {
        foreach (FSEntry c in d.Entries)
          DeleteChildren (c.FullName);
        d.Entries.Clear ();
      }
      d.DrawEntries = null;
    }
  } }

  /** ASYNC */
  static void Modified (string path)
  { lock (Cache) {
    //Console.WriteLine("Modified on {0}", path);
    // excise path data from parent
    // redo path's file pass
    // enter new data to parent
    FSEntry d = Get (path);
    DestroyThumbnail(d);
    lock (TraversalCache)
      if (TraversalCache.ContainsKey(path))
        TraversalCache.Remove(path);
    if (d.IsDirectory) {
      d.FilePassDone = false;
      bool oc = d.Complete;
      FilePass (path);
      if (d.Complete != oc) {
        if (d.Complete) SetComplete(path);
        else SetIncomplete(path);
      }
      d.LastChange = LastChange = DateTime.Now;
    } else {
      UnixSymbolicLinkInfo u = new UnixSymbolicLinkInfo (path);
      long oldSize = d.Size;
      d.Setup(u);
      AddCountAndSize(d.ParentDir.FullName, 0, d.Size-oldSize);
      d.LastChange = LastChange = DateTime.Now;
    }
  } }



  /* Filesystem watching */

  /** FAST */
  static void WatcherChanged (object source, FileSystemEventArgs e)
  { lock (Invalids) {
    //Console.WriteLine("Invalidating {0}: {1}", e.FullPath, e.ChangeType);
    Invalids[e.FullPath] = true;
  } }

  /** FAST */
  static void WatcherRenamed (object source, RenamedEventArgs e)
  { lock (Invalids) {
    //Console.WriteLine("Invalidating {0} -> {1}: renamed", e.FullPath, e.OldFullPath);
    Invalids[e.FullPath] = true;
    Invalids[e.OldFullPath] = true;
  } }

  /** BLOCKING */
  /* Blows up on paths with non-UTF characters */
  static FileSystemWatcher MakeWatcher (string dirname)
  {
    FileSystemWatcher watcher = new FileSystemWatcher ();
    watcher.IncludeSubdirectories = false;
    watcher.NotifyFilter = (
        NotifyFilters.LastWrite
      | NotifyFilters.Size
      | NotifyFilters.FileName
      | NotifyFilters.DirectoryName
      | NotifyFilters.CreationTime
    );
    try {
      watcher.Path = dirname;
    } catch (System.ArgumentException e) {
      Console.WriteLine("System.IO.FileSystemWatcher does not like your path: {0}", dirname);
      Console.WriteLine("Here's the exception output: {0}", e);
      return watcher;
    }
    watcher.Filter = "";
    watcher.Changed += new FileSystemEventHandler (WatcherChanged);
    watcher.Created += new FileSystemEventHandler (WatcherChanged);
    watcher.Deleted += new FileSystemEventHandler (WatcherChanged);
    watcher.Renamed += new RenamedEventHandler (WatcherRenamed);
    try {
      watcher.EnableRaisingEvents = true;
    } catch (System.ArgumentException e) {
      Console.WriteLine("System.IO.FileSystemWatcher does not like your path: {0}", dirname);
      Console.WriteLine("Here's the exception output: {0}", e);
    }
    return watcher;
  }



  /* Traversal internals */

  /** ASYNC */
  static bool StartTraversal (FSEntry d)
  { lock (Cache) {
    if (d.Complete || d.InProgress) return false;
    d.InProgress = true;
    return true;
  } }

  /** BLOCKING */
  static void ThreadTraverse (string dirname) {
    lock (Cache) {
      FSEntry d = Get(dirname);
      if (d.Complete || d.InProgress) return;
    }
    WaitCallback cb = new WaitCallback(TraverseCallback);
    ThreadPool.QueueUserWorkItem(cb, dirname);
  }

  /** ASYNC */
  static void TraverseCallback (object state) {
    TraversalProfiler.Time("{0} started traversal thread", (string)state);
    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
    lock (TCLock) TraverseThreadCount++;
    Traverse ((string)state);
    lock (TCLock) TraverseThreadCount--;
  }

  /** ASYNC */
  static void Traverse (string dirname)
  {
    lock (TCLock) TraversalCounter++;
    try { TraverseDir (dirname); }
    catch (Exception e) { Console.WriteLine("Traverse failed with {0}", e); }
    lock (TCLock) TraversalCounter--;
    TraversalProfiler.Time("{0} completed", dirname);
  }

  /** ASYNC */
  static void TraverseDir (string dirname)
  {
    if (TraversalCancelled) return;
    FSEntry d = Get (dirname);
    if (!StartTraversal (d)) return;
    ProcessStartInfo psi = new ProcessStartInfo ();
    psi.FileName = "du";
    psi.Arguments = "-0 -P -b --apparent-size "+Helpers.EscapePath(dirname);
    psi.UseShellExecute = false;
    psi.RedirectStandardOutput = true;
    Process p = Process.Start (psi);
//     p.PriorityClass = ProcessPriorityClass.Idle;
    p.ProcessorAffinity = (IntPtr)0x0002;
    using (BinaryReader b = new BinaryReader(p.StandardOutput.BaseStream)) {
      while (true) {
        string l = ReadNullTerminatedLine(b);
        if (l.Length == 0) break;
        ApplyDuString (l);
        if (TraversalCancelled) {
          p.Kill ();
          return;
        }
      }
    }
    p.WaitForExit ();
  }

  static string ReadNullTerminatedLine(BinaryReader s)
  {
    byte[] buf = new byte[4096];
    int i=0;
    byte j;
    try {
      while ((j=s.ReadByte()) > 0) {
        if (i > buf.Length) Array.Resize<byte>(ref buf, buf.Length*2);
        buf[i] = j;
        ++i;
      }
    } catch (Exception) {}
    Array.Resize<byte>(ref buf, i);
    return (new System.Text.UTF8Encoding().GetString(buf));
  }

  public static string LastTraversed = "";

  static void ApplyDuString (string l)
  {
    char[] tab = {'\t'};
    string[] size_date_path = l.Split(tab, 2);
    Int64 size = Int64.Parse(size_date_path[0]);
    string path = size_date_path[1];
    lock (TraversalCache) {
      TraversalCache[path] = new TraversalInfo(size, 1, DateTime.Now);
    }
    LastTraversed = path;
    lock (Cache) {
      SetCountAndSize(path, 0, size);
      LastChange = DateTime.Now;
    }
  }

  struct TraversalInfo {
    public Int64 size;
    public Int64 count;
    public DateTime time;
    public TraversalInfo(Int64 sz, Int64 c, DateTime t)
    { size = sz; count = c; time = t; }
  }




  /* Thumbnailing internals */

  static void DestroyThumbnail(FSEntry d)
  { lock (Cache) { lock (d) {
    ImageSurface tn = d.Thumbnail;
    if (tn != null) {
      tn.Destroy ();
      tn.Destroy ();
      d.Thumbnail = null;
      ThumbnailCache.Remove(d.FullName);
    }
  } } }

  /* returns "should retry" */
  static bool GetThumbnail (string path)
  {
    if ( DateTime.Now.Subtract(Helpers.LastModified(path)).TotalSeconds <= 1 )
      return true;
    FSEntry f = Get (path);
    if (f.Thumbnail == null) {
      string uri = "file://"+path;
      string mime = Gnome.Vfs.MimeType.GetMimeTypeForUri(uri);
      ImageSurface tn;
      string tfn = TNF.Lookup(uri, f.LastModified);
      if (tfn != null) {
        if ( DateTime.Now.Subtract(Helpers.LastModified(tfn)).TotalSeconds <= 1 )
          return true;
        tn = new ImageSurface (tfn);
      } else {
        Gdk.Pixbuf pbuf = TNF.GenerateThumbnail(uri, mime);
        if (pbuf != null) {
          TNF.SaveThumbnail(pbuf, uri, f.LastModified);
        } else {
          double since = DateTime.Now.Subtract(Helpers.LastModified(path)).TotalSeconds;
          bool fileRecentlyModified = since < 5;
          if (!fileRecentlyModified)
            TNF.CreateFailedThumbnail(uri, f.LastModified);
          return fileRecentlyModified;
        }
        tn = Helpers.ToImageSurface(pbuf);
      }
      if (tn.Width < 1 || tn.Height < 1) {
        tn.Destroy ();
        tn.Destroy ();
        bool tnRecentlyModified = ( DateTime.Now.Subtract(Helpers.LastModified(tfn)).TotalSeconds < 2 );
        return tnRecentlyModified;
      }
      lock (Cache) {
        f.Thumbnail = tn;
        ThumbnailCache[f.FullName] = f;
        bool checkForOld = true;
        int deletecount = 0;
        if (ThumbnailCache.Count > 4000) {
          FSEntry oldest = f;
          foreach (FSEntry e in ThumbnailCache.Values) {
            if (e.LastDraw < oldest.LastDraw) oldest = e;
          }
          if (oldest.LastDraw != FSDraw.frame) {
            DestroyThumbnail(oldest);
            deletecount++;
            checkForOld = oldest.LastDraw < FSDraw.frame - 1000;
          }
        }
        if (checkForOld && ThumbnailCache.Count > 2000) {
          List<FSEntry> old = new List<FSEntry> ();
          foreach (FSEntry e in ThumbnailCache.Values) {
            if (e.LastDraw < FSDraw.frame - 1000) old.Add(e);
          }
          foreach (FSEntry e in old) {
            DestroyThumbnail(e);
            deletecount++;
          }
        }
        if (deletecount > 1) {
          Console.WriteLine("Expired {0} entries from ThumbnailCache", deletecount);
        }
      }
      LastChange = DateTime.Now;
    }
    return false;
  }

  static void ThumbnailQueueProcessor ()
  {
    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
    while (true) {
      while (ThumbnailQueue.Count > 0)
        ProcessThumbnailQueue ();
      Thread.Sleep(100);
    }
  }

  static void ProcessThumbnailQueue ()
  {
    string tn;
    lock (ThumbnailQueue) {
      if (ThumbnailQueue.Count > 0)
        tn = ThumbnailQueue.Dequeue();
      else
        return;
    }
    if (GetThumbnail (tn))
      lock (ThumbnailQueue)
        ThumbnailQueue.Enqueue (tn, 0);
  }

}


public enum SortingDirection {
  Ascending,
  Descending
}

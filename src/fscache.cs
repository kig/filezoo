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
using System.Threading;
using System.Timers;
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

  static Dictionary<string,bool> Invalids = new Dictionary<string,bool> ();

  public static IMeasurer Measurer;
  public static IComparer<FSEntry> Comparer;
  public static SortingDirection SortDirection;

  public static DateTime LastChange = DateTime.Now;

  static bool TraversalCancelled = false;
  static long TraversalCounter = 0;
  public static long OptimalTraverseThreads = 4;
  static long TraverseThreadCount = 0;
  static Object CancelLock = new Object ();
  static Object TCLock = new Object ();

  static System.Timers.Timer InvalidsTimer = null;

  /** BLOCKING */
  public static void Watch (string path)
  { lock (Cache) {
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
  public static FSEntry Get (string path)
  { lock (Cache) {
    if (Cache.ContainsKey(path))
      return Cache[path];
    FSEntry f = new FSEntry (path);
    Cache[path] = f;
    CreateParents (f);
    return f;
  } }

  /** BLOCKING */
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

  /** BLOCKING */
  public static void RequestTraversal (string dirname)
  {
    lock (CancelLock) {}
    ThreadTraverse(dirname);
  }


  /** BLOCKING */
  static void CreateParents (FSEntry f)
  {
    if (f.FullName == Helpers.RootDir) return;
    f.ParentDir = Get (Helpers.Dirname (f.FullName));
  }


  /** BLOCKING */
  public static void FilePass (string path)
  { FilePass (path, true); }
  public static void FilePass (string path, bool createFiles)
  {
    FSEntry f = Get (path);
    if (f.FilePassDone && f.LastFileChange == Helpers.LastChange(path)) return;
    f.LastFileChange = Helpers.LastChange(path);
    if (f.IsDirectory) {
      List<FSEntry> entries = new List<FSEntry> ();
      long size = 0, count = 0, subTreeSize = 0, subTreeCount = 0;
      foreach (UnixFileSystemInfo u in Helpers.EntriesMaybe (f.FullName)) {
        if (Helpers.IsDir(u) || createFiles) {
          FSEntry d = Get (u.FullName);
          entries.Add (d);
          size += d.Size;
          subTreeSize += d.SubTreeSize;
          subTreeCount += d.SubTreeCount;
        } else {
          long sz = Helpers.FileSize (u);
          size += sz;
          subTreeSize += sz;
          subTreeCount++;
        }
        count++;
      }
      lock (Cache) {
        f.Entries = entries;
        f.Size = size;
        f.Count = count;
        AddCountAndSize (path, subTreeCount-f.SubTreeCount, subTreeSize-f.SubTreeSize);
        f.FilePassDone = true;
        if (AllChildrenComplete(path))
          SetComplete (path);
        if (!createFiles) // force FilePass on next time
          f.LastFileChange = Helpers.DefaultTime;
      }
    }
  }

  /** ASYNC */
  public static void SortEntries (FSEntry f)
  { lock (Cache) {
    if (!f.IsDirectory) return;
    if (
      f.Comparer == Comparer
      && f.SortDirection == SortDirection
      && f.LastSort == f.LastChange
    ) return;
    DateTime lc = f.LastChange;
    f.Comparer = Comparer;
    f.SortDirection = SortDirection;
    List<FSEntry> entries = new List<FSEntry> (f.Entries);
    entries.Sort(Comparer);
    if (SortDirection == SortingDirection.Descending)
      entries.Reverse();
    f.Entries = entries;
    f.LastSort = lc;
    f.ReadyToDraw = (f.Measurer == Measurer && f.LastMeasure == f.LastChange);
  } }

  /** ASYNC */
  public static void MeasureEntries (FSEntry f)
  { lock (Cache) {
    if (!f.IsDirectory) return;
    if (f.Measurer == Measurer && f.LastMeasure == f.LastChange)
      return;
    f.Measurer = Measurer;
    double totalHeight = 0.0;
    foreach (FSEntry e in f.Entries) {
      e.Height = Measurer.Measure(e);
      totalHeight += e.Height;
    }
    double scale = 1.0 / totalHeight;
    foreach (FSEntry e in f.Entries) {
      e.Scale = scale;
    }
    f.LastMeasure = f.LastChange;
    f.ReadyToDraw = ( f.Comparer == Comparer
                      && f.SortDirection == SortDirection
                      && f.LastSort == f.LastChange);
  } }

  /** BLOCKING */
  public static void Invalidate (string path)
  { lock (Cache) {
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

  /* Thumbnails */

  static Thread ThumbnailThread;
  static PriorityQueue ThumbnailQueue = new PriorityQueue ();

  public static string[] thumbnailable = {"png", "jpg", "jpeg", "gif", "bmp", "ps", "pdf"};
  public static void FetchThumbnail (string path, int priority)
  {
    FSEntry f = Get (path);
    if (f.Thumbnail != null) return;
    if (Array.IndexOf (thumbnailable, f.Suffix) > -1)
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

  public static void CancelThumbnailing ()
  { lock (ThumbnailQueue) {
    ThumbnailQueue.Clear ();
  } }

  static void GetThumbnail (string path)
  {
    if (Get (path).Thumbnail == null) {
      Get (path).Thumbnail = Helpers.GetThumbnail (path);
      LastChange = DateTime.Now;
    }
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
    GetThumbnail (tn);
  }



  /* Tree editing */

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
  public static bool NeedFilePass (string path)
  { lock (Cache) {
    return !(Get(path).FilePassDone);
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
    // ditch path's children, ditch path, excise path from parent,
    // set parent complete if path was the only incomplete child in it
    FSEntry d = Get (path);
    DeleteChildren (path);
    List<FSEntry> e = new List<FSEntry> (d.ParentDir.Entries);
    e.Remove(d);
    d.ParentDir.Entries = e;
    AddCountAndSize (d.ParentDir.FullName, -d.SubTreeCount, -d.SubTreeSize);
    if (!d.Complete && AllChildrenComplete(d.ParentDir.FullName))
      SetComplete (d.ParentDir.FullName);
  } }

  /** ASYNC */
  static void DeleteChildren (string path)
  { lock (Cache) {
    if (Cache.ContainsKey(path)) {
      FSEntry d = Cache[path];
      Cache.Remove (path);
      foreach (FSEntry c in d.Entries)
        DeleteChildren (c.FullName);
    }
  } }

  /** ASYNC */
  static void Modified (string path)
  { lock (Cache) {
    // excise path data from parent
    // redo path's file pass
    // enter new data to parent
    FSEntry d = Get (path);
    if (d.IsDirectory) {
      d.FilePassDone = false;
      bool oc = d.Complete;
      FilePass (path);
      if (d.Complete != oc) {
        if (d.Complete) SetComplete(path);
        else SetIncomplete(path);
      }
    } else {
      UnixFileInfo u = new UnixFileInfo (path);
      long oldSize = d.Size;
      d.Setup(u);
      AddCountAndSize(d.ParentDir.FullName, 0, d.Size-oldSize);
    }
  } }



  /* Filesystem watching */

  /** FAST */
  static void WatcherChanged (object source, FileSystemEventArgs e)
  { lock (Invalids) {
//     Console.WriteLine("Invalidating {0}: {1}", e.FullPath, e.ChangeType);
    Invalids[e.FullPath] = true;
  } }

  /** FAST */
  static void WatcherRenamed (object source, RenamedEventArgs e)
  { lock (Invalids) {
//     Console.WriteLine("Invalidating {0} and {1}: renamed to latter", e.FullPath, e.OldFullPath);
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
      Console.WriteLine("System.IO.FileSystemWatcher does not appreciate the characters in your path: {0}", dirname);
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
      Console.WriteLine("System.IO.FileSystemWatcher does not appreciate the characters in your path: {0}", dirname);
      Console.WriteLine("You should go and fix System.IO.Path.IsPathRooted.");
      Console.WriteLine("Here's the exception output: {0}", e);
    }
    return watcher;
  }



  /* Traversal */

  /** ASYNC */
  static bool StartTraversal (string path)
  { lock (Cache) {
    FSEntry d = Get (path);
    if (d.Complete || d.InProgress) return false;
    d.InProgress = true;
    return true;
  } }

  /** BLOCKING */
  static void ThreadTraverse (string dirname) {
    WaitCallback cb = new WaitCallback(TraverseCallback);
    ThreadPool.QueueUserWorkItem(cb, dirname);
  }

  /** ASYNC */
  static void TraverseCallback (object state) {
    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
    lock (TCLock) TraverseThreadCount++;
    Traverse ((string)state);
    lock (TCLock) TraverseThreadCount--;
  }

  /** ASYNC */
  static void TraverseSub (string dirname)
  {
    bool useThread;
    useThread = TraverseThreadCount < OptimalTraverseThreads;
    if (useThread) ThreadTraverse (dirname);
    else Traverse (dirname);
  }

  /** ASYNC */
  static void Traverse (string dirname)
  {
    lock (TCLock) TraversalCounter++;
    try { TraverseDir (dirname); }
    catch (Exception e) { Console.WriteLine("Traverse failed with {0}", e); }
    lock (TCLock) TraversalCounter--;
  }

  /** ASYNC */
  static void TraverseDir (string dirname)
  {
    if (TraversalCancelled) return;
    if (!StartTraversal (dirname)) return;
    if (NeedFilePass (dirname))
      FilePass (dirname, false);
    if (TraversalCancelled) return;
    foreach (FSEntry f in Get(dirname).Entries) {
      if (f.IsDirectory) TraverseSub(f.FullName);
      if (TraversalCancelled) return;
    }
  }

}


public enum SortingDirection {
  Ascending,
  Descending
}

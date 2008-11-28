using System.Collections.Generic;
using System.Threading;
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
  static Dictionary<string,FSEntry> Cache = new Dictionary<string,FSEntry> ();
  static FileSystemWatcher Watcher;

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

  /** BLOCKING */
  public static void Watch (string path)
  { lock (Cache) {
    if (Watcher == null || Watcher.Path != path) {
      if (Watcher != null) Watcher.Dispose ();
      Watcher = MakeWatcher (path);
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
  {
    FSEntry f = Get (path);
    lock (f) {
      if (f.FilePassDone) return;
      if (f.IsDirectory) {
        List<FSEntry> entries = new List<FSEntry> ();
        long size = 0, count = 0, subTreeSize = 0, subTreeCount = 0;
        foreach (UnixFileSystemInfo u in Helpers.EntriesMaybe (f.FullName)) {
          FSEntry d = Get (u.FullName);
          entries.Add (d);
          size += d.Size;
          subTreeSize += d.SubTreeSize;
          subTreeCount += d.SubTreeCount;
          count++;
        }
        lock (Cache) {
          f.Entries = entries;
          f.Size = size;
          f.Count = count;
          AddCountAndSize (path, subTreeSize-f.SubTreeSize, subTreeCount-f.SubTreeCount);
          if (AllChildrenComplete(path))
            SetComplete (path);
          f.FilePassDone = true;
        }
      }
    }
  }

  /** ASYNC */
  public static void SortEntries (FSEntry f)
  { lock (Cache) {
    if (!f.IsDirectory) return;
    if (f.Comparer != Comparer || f.SortDirection != SortDirection
                               || f.LastSort != f.LastChange) {
      f.Comparer = Comparer;
      f.SortDirection = SortDirection;
      lock (f) {
        List<FSEntry> entries = new List<FSEntry> (f.Entries);
        entries.Sort(Comparer);
        if (SortDirection == SortingDirection.Descending)
          entries.Reverse();
        f.Entries = entries;
      }
      f.LastSort = f.LastChange;
      f.ReadyToDraw = (f.Measurer == Measurer && f.LastMeasure == f.LastChange);
    }
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
      if (Helpers.FileExists(path) && Helpers.IsDir(path)) {
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
    AddCountAndSize (d.ParentDir.FullName, -d.TotalCount, -d.TotalSize);
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
    d.FilePassDone = false;
    bool oc = d.Complete;
    FilePass (path);
    if (d.Complete != oc) {
      if (d.Complete) SetComplete(path);
      else SetIncomplete(path);
    }
  } }



  /* Filesystem watching */

  /** FAST */
  static void WatcherChanged (object source, FileSystemEventArgs e)
  { lock (Cache) {
    Console.WriteLine("Invalidating {0}: {1}", e.FullPath, e.ChangeType);
    Invalidate (e.FullPath);
  } }

  /** FAST */
  static void WatcherRenamed (object source, RenamedEventArgs e)
  { lock (Cache) {
    Console.WriteLine("Invalidating {0} and {1}: renamed to latter", e.FullPath, e.OldFullPath);
    Invalidate (e.FullPath);
    Invalidate (e.OldFullPath);
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

  /** ASYNC */
  static void Fail (string path)
  { lock (Cache) {
    SetComplete (path);
  } }

  /** BLOCKING */
  static void ThreadTraverse (string dirname) {
    WaitCallback cb = new WaitCallback(TraverseCallback);
    ThreadPool.QueueUserWorkItem(cb, dirname);
  }

  /** ASYNC */
  static void TraverseCallback (object state) {
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
    UnixFileSystemInfo[] files;
    if (!StartTraversal (dirname)) return;
    try { files = Helpers.Entries (dirname); }
    catch (Exception) {
      Fail (dirname);
      return;
    }
    if (NeedFilePass (dirname))
      FilePass (dirname);
    if (TraversalCancelled) return;
    foreach (UnixFileSystemInfo f in files) {
      if (Helpers.IsDir(f)) TraverseSub(f.FullName);
      if (TraversalCancelled) return;
    }
  }

}


public enum SortingDirection {
  Ascending,
  Descending
}

using Mono.Unix;
using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.IO;


/**
  DirCache is the filesystem cache server.
  It is a static class, acting as a global singleton.

  The DirCache allows retrieving its cache entries with:

    Dir d = DirCache.GetCacheEntry(path);

  To request traversal of a path (and thus ensuring that the cache entries
  underlying it get traversed), you use RequestTraversal:

    DirCache.RequestTraversal(path);

  A cache entry is dynamically updated as it is traversed. Once the traversal
  of the cache entry is complete, the cache entry is marked as complete.

    bool complete = DirCache.GetCacheEntry(path).Complete;

  If a cache entry is a current target for traversal, its InProgress is true.
  The way you should use the cache to retrieve a dynamically updating cache
  entry for a filesystem path is as follows:

    Dir d = DirCache.GetCacheEntry(path);
    if (!d.Complete && !d.InProgress)
      DirCache.RequestTraversal(path);

  When you want to cancel all traversals currently in progress (maybe you
  navigated to a different directory and want to traverse its entries first),
  use CancelTraversal:

    DirCache.CancelTraversal();

  Note that CancelTraversal blocks until all traversals have exited, which may
  take hundreds of milliseconds.

  To invalidate a cache entry (e.g. FileSystemWatcher told you of a change in
  the filesystem and now you want to update the cache to match reality), use
  Invalidate:

    DirCache.Invalidate(path);

  */
public static class DirCache
{
  static Dictionary<string,Dir> Cache = new Dictionary<string,Dir> (100000);
  static Dictionary<string,ArrayList> Children = new Dictionary<string,ArrayList> (100000);

  static bool TraversalCancelled = false;
  static long TraversalCounter = 0;
  public static long OptimalTraverseThreads = 4;
  static long TraverseThreadCount = 0;
  static Dir CancelLock = new Dir ();
  static Dir TCLock = new Dir ();

  /** BLOCKING */
  public static Dir GetCacheEntry (string path)
  { lock (Cache) {
      Dir dc;
      if (Cache.ContainsKey(path)) {
        dc = Cache[path];
      } else {
        dc = new Dir (path);
        dc.LastModified = Helpers.LastModified(path);
        Cache[path] = dc;
        string s = Helpers.Dirname (path);
        if (s.Length > 0) GetCacheEntry (s);
      }
      return dc;
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
        foreach (Dir d in Cache.Values)
          d.InProgress = false;
      }
      TraversalCancelled = false;
    }
  }

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

  /** BLOCKING */
  public static void RequestTraversal (string dirname)
  {
    lock (CancelLock) {}
    ThreadTraverse(dirname);
  }





  /** ASYNC */
  static void AddChild (string path, Dir d)
  { lock (Cache) {
    GetChildren(path).Add (d);
  } }

  /** ASYNC */
  static ArrayList GetAncestors (string path)
  { lock (Cache) {
    string s = Helpers.Dirname (path);
    ArrayList a = new ArrayList ();
    while (s.Length > 0) {
      a.Add (GetCacheEntry (s));
      s = Helpers.Dirname (s);
    }
    return a;
  } }

  /** ASYNC */
  static ArrayList GetChildren (string path)
  { lock (Cache) {
    if (Children.ContainsKey(path)) {
      return Children[path];
    } else {
      return (Children[path] = new ArrayList ());
    }
  } }

  /** ASYNC */
  static void Deleted (string path)
  { lock (Cache) {
    // ditch path's children, ditch path, excise path from parent,
    // set parent complete if path was the only incomplete child in it
    Dir d = GetCacheEntry (path);
    DeleteChildren (path);
    string parent = Helpers.Dirname (path);
    if (Children.ContainsKey(parent))
      Children[parent].Remove(d);
    AddCountAndSize (parent, -d.TotalCount, -d.TotalSize);
    if (!d.Complete && AllChildrenComplete(parent))
      SetComplete (parent);
  } }

  /** ASYNC */
  static void DeleteChildren (string path)
  { lock (Cache) {
    if (Children.ContainsKey(path)) {
      foreach (Dir c in Children[path])
        DeleteChildren (c.Path);
      Children.Remove(path);
    }
    if (Cache.ContainsKey(path))
      Cache.Remove (path);
  } }

  /** ASYNC */
  static void Modified (string path)
  { lock (Cache) {
    // excise path data from parent
    // redo path's file pass
    // enter new data to parent
    Dir d = GetCacheEntry (path);
    long count = 0;
    long size = 0;
    GetChildren(path).Clear();
    foreach (UnixFileSystemInfo f in Helpers.EntriesMaybe(path)) {
      count++;
      if (Helpers.IsDir(f)) AddChild (path, GetCacheEntry(f.FullName));
      else size += Helpers.FileSize(f);
    }
    foreach (Dir c in GetChildren(path)) {
      count += c.TotalCount;
      size += c.TotalSize;
    }
    AddCountAndSize (path, count-d.TotalCount, size-d.TotalSize);
    bool acc = AllChildrenComplete(path);
    if (d.Complete != acc) {
      if (acc) SetComplete(path);
      else SetIncomplete(path);
    }
  } }

  /** ASYNC */
  public static void Clear ()
  { lock (Cache) {
    Cache.Clear ();
    Children.Clear ();
  } }

  /** ASYNC */
  static void AddCountAndSize (string path, long count, long size)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    d.TotalCount += count;
    d.TotalSize += size;
    foreach (Dir a in GetAncestors (path)) {
      a.TotalCount += count;
      a.TotalSize += size;
    }
  } }

  /** ASYNC */
  static void SetComplete (string path)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    d.Complete = true;
    if (path != Helpers.RootDir) {
      string p = Helpers.Dirname (path);
      if (p.Length > 0 && AllChildrenComplete (p))
        SetComplete (p);
    }
  } }

  /** ASYNC */
  static void SetIncomplete (string path)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    d.Complete = false;
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
    foreach (Dir c in GetChildren (path))
      if (!c.Complete) return false;
    return true;
  } }

  /** ASYNC */
  static bool StartTraversal (string path)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    if (d.Complete || d.InProgress) return false;
    d.InProgress = true;
    return true;
  } }

  /** ASYNC */
  static void Fail (string path)
  { lock (Cache) {
    SetComplete (path);
  } }

  /** ASYNC */
  static void SetFilePassStats (string path, long count, long size)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    if (!d.FilePassDone) {
      AddCountAndSize (path, count, size);
      d.FilePassDone = true;
      if (AllChildrenComplete(path))
        SetComplete (path);
    } else {
      Console.WriteLine("SetFilePassStats {0} when FilePassDone", path);
      throw new System.ArgumentException ("Can't SetFilePassStats when FilePassDone");
    }
  } }

  /** ASYNC */
  static bool NeedFilePass (string path)
  { lock (Cache) {
    return !(GetCacheEntry(path).FilePassDone);
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
    lock (TCLock) useThread = TraverseThreadCount < OptimalTraverseThreads;
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
    catch (System.UnauthorizedAccessException) {
      Fail (dirname);
      return;
    }
    if (NeedFilePass (dirname)) {
      long count = 0;
      long size = 0;
      foreach (UnixFileSystemInfo f in files) {
        count++;
        if (Helpers.IsDir(f)) AddChild (dirname, GetCacheEntry(f.FullName));
        else size += Helpers.FileSize(f);
      }
      SetFilePassStats (dirname, count, size);
    }
    if (TraversalCancelled) return;
    foreach (UnixFileSystemInfo f in files) {
      if (Helpers.IsDir(f)) TraverseSub(f.FullName);
      if (TraversalCancelled) return;
    }
  }

}


public class Dir {
  public string Path;
  public long TotalCount = 0;
  public long TotalSize = 0;
  public DateTime LastModified;
  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;
  public Dir () { Path = ""; }
  public Dir (string path) { Path = path; }
}

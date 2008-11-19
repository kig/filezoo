using Mono.Unix;
using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.IO;


public static class DirCache
{
  static Dictionary<string,Dir> Cache = new Dictionary<string,Dir> (100000);
  static Dictionary<string,ArrayList> Children = new Dictionary<string,ArrayList> (100000);

  static bool TraversalCancelled = false;
  static long TraversalCounter = 0;
  public static long OptimalTraverseThreads = 10;
  static long TraverseThreadCount = 0;
  static Dir CancelLock = new Dir ();
  static Dir TCLock = new Dir ();

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

  public static void RequestTraversal (string dirname)
  {
    lock (CancelLock) {}
    ThreadTraverse(dirname);
  }





  static void AddChild (string path, Dir d)
  { lock (Cache) {
    GetChildren(path).Add (d);
  } }

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

  static ArrayList GetChildren (string path)
  { lock (Cache) {
    if (Children.ContainsKey(path)) {
      return Children[path];
    } else {
      return (Children[path] = new ArrayList ());
    }
  } }

  /* Here there be bugs, file count abnormally rises after some events */
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

  public static void Deleted (string path)
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

  public static void DeleteChildren (string path)
  { lock (Cache) {
    if (Children.ContainsKey(path)) {
      foreach (Dir c in Children[path])
        DeleteChildren (c.Path);
      Children.Remove(path);
    }
    if (Cache.ContainsKey(path))
      Cache.Remove (path);
  } }

  public static void Modified (string path)
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

  public static void Clear ()
  { lock (Cache) {
    Cache.Clear ();
    Children.Clear ();
  } }

  public static void AddCountAndSize (string path, long count, long size)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    d.TotalCount += count;
    d.TotalSize += size;
    foreach (Dir a in GetAncestors (path)) {
      a.TotalCount += count;
      a.TotalSize += size;
    }
  } }

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

  static bool AllChildrenComplete (string path)
  { lock (Cache) {
    if (NeedFilePass(path)) return false;
    foreach (Dir c in GetChildren (path))
      if (!c.Complete) return false;
    return true;
  } }

  static bool StartTraversal (string path)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    if (d.Complete || d.InProgress) return false;
    d.InProgress = true;
    return true;
  } }

  static void Fail (string path)
  { lock (Cache) {
    SetComplete (path);
  } }

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

  static bool NeedFilePass (string path)
  { lock (Cache) {
    return !(GetCacheEntry(path).FilePassDone);
  } }



  static void ThreadTraverse (string dirname) {
    WaitCallback cb = new WaitCallback(TraverseCallback);
    ThreadPool.QueueUserWorkItem(cb, dirname);
  }

  static void TraverseCallback (object state) {
    lock (TCLock) TraverseThreadCount++;
    Traverse ((string)state);
    lock (TCLock) TraverseThreadCount--;
  }

  static void TraverseSub (string dirname)
  {
    bool useThread;
    lock (TCLock) useThread = TraverseThreadCount < OptimalTraverseThreads;
    if (useThread) ThreadTraverse (dirname);
    else Traverse (dirname);
  }

  static void Traverse (string dirname)
  {
    lock (TCLock) TraversalCounter++;
    TraverseDir (dirname);
    lock (TCLock) TraversalCounter--;
  }

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

using Mono.Unix;
using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.IO;


public static class DirCache
{
  static Dictionary<string,Dir> Cache = new Dictionary<string,Dir> (200000);

  static bool TraversalCancelled = false;
  static long TraversalCounter = 0;
  static Dir CancelLock = new Dir ();
  static Dir TCLock = new Dir ();

  public static Dir GetCacheEntry (string name)
  { lock (Cache) {
      Dir dc;
      if (Cache.ContainsKey(name)) {
        dc = Cache[name];
      } else {
        dc = new Dir ();
        Cache[name] = dc;
        string s = Helpers.Dirname (name);
        while (s.Length > 0) {
          if (Cache.ContainsKey(s)) break;
          Cache[s] = new Dir ();
          s = Helpers.Dirname (s);
        }
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
    WaitCallback cb = new WaitCallback(TraverseCallback);
    ThreadPool.QueueUserWorkItem(cb, dirname);
  }





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
    ArrayList childNames = Helpers.SubDirnames (path);
    ArrayList a = new ArrayList ();
    foreach (string s in childNames)
      a.Add (GetCacheEntry (s));
    return a;
  } }

  public static void Invalidate (string path)
  { lock (Cache) {
      Cache.Clear ();
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

  static bool AllChildrenComplete (string path)
  { lock (Cache) {
      ArrayList children = GetChildren (path);
      foreach (Dir c in children)
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
      if (AllChildrenComplete(path))
        SetComplete (path);
      d.FilePassDone = true;
    } else {
      Console.WriteLine("SetFilePassStats {0} when FilePassDone", path);
      throw new System.ArgumentException ("Can't SetFilePassStats when FilePassDone");
    }
  } }

  static bool NeedFilePass (string path)
  { lock (Cache) {
    Dir d = GetCacheEntry (path);
    return !d.FilePassDone;
  } }

  static void TraverseCallback (object state) { Traverse ((string)state); }

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
        if (!Helpers.IsDir(f)) size += Helpers.FileSize(f);
      }
      SetFilePassStats (dirname, count, size);
    }
    if (TraversalCancelled) return;
    foreach (UnixFileSystemInfo f in files) {
      if (Helpers.IsDir(f)) Traverse(f.FullName);
      if (TraversalCancelled) return;
    }
  }

}


public class Dir {
  public long TotalCount = 1;
  public long TotalSize = 0;
  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;
}

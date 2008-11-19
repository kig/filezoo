using Mono.Unix;
using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.IO;


/*
  Refactor this into:
    * A Cache that is the only object that edits the cache and its entries.
    * Traversers that walk the tree and send information to Cache.
*/
public static class DirCache
{
  public static Dictionary<string,Dir> Cache = new Dictionary<string,Dir> (200000);

  public static Dir GetCacheEntry (string name) {
    Dir dc;
    lock (Cache) {
      if (Cache.ContainsKey(name)) {
        dc = Cache[name];
      } else {
        dc = new Dir (name);
        Cache[name] = dc;
        Dir c = dc;
        string s = c.ParentDir ();
        while (s.Length > 0) {
          if (Cache.ContainsKey(s)) break;
          Cache[s] = c = new Dir (s);
          s = c.ParentDir ();
        }
      }
    }
    return dc;
  }

  public static void Invalidate (string path) {
    lock (Cache) {
      Cache.Clear ();
    }
  }

  static bool TraversalCancelled = false;
  static long TraversalCounter = 0;
  static Dir CancelLock = new Dir ("");
  static Dir TCLock = new Dir ("");

  public static void CancelTraversal ()
  {
    lock (CancelLock) {
      TraversalCancelled = true;
      while (TraversalCounter != 0) {
//         Console.WriteLine(TraversalCounter);
        Thread.Sleep (50);
      }
      lock (Cache) {
        foreach (Dir d in Cache.Values)
          lock (d) { d.InProgress = false; }
      }
      TraversalCancelled = false;
    }
  }

  public static Dir RequestTraversal (string dirname)
  {
    lock (CancelLock) {
//       lock (TCLock) Console.WriteLine("Req {0}", TraversalCounter);
    }
    return Traverse (dirname);
  }

  static Dir Traverse (string dirname)
  {
    lock (TCLock) TraversalCounter++;
    Dir d = TraverseDir (dirname);
    lock (TCLock) TraversalCounter--;
    return d;
  }

  static Dir TraverseDir (string dirname)
  {
    Dir dc = GetCacheEntry(dirname);
    UnixFileSystemInfo[] files;
    lock (dc) {
      if (TraversalCancelled) return dc.Cancel ();
      if (dc.Complete) return dc;
      dc.InProgress = true;
      try { files = Helpers.Entries (dirname); }
      catch (System.UnauthorizedAccessException) { return dc.Fail (); }
      if (!dc.FilePassDone) {
        ulong count = 0;
        ulong size = 0;
        long missing = 0;
        foreach (UnixFileSystemInfo f in files) {
          count++;
          if (Helpers.IsDir(f)) missing++;
          else size += Helpers.FileSize(f);
        }
        dc.AddCount (count);
        dc.AddSize (size);
        dc.Missing = missing;
        dc.FilePassDone = true;
        if (dc.Missing == dc.Completed) return dc.SetComplete ();
      }
      if (TraversalCancelled) return dc.Cancel ();
    }
    foreach (UnixFileSystemInfo f in files) {
      if (Helpers.IsDir(f)) Traverse(f.FullName);
      if (TraversalCancelled) return dc.Cancel ();
    }
    return dc;
  }

}


public class Dir {
  public string Path;
  public ulong TotalCount = 1;
  public ulong TotalSize = 0;
  public long Missing = 0;
  public long Completed = 0;
  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;
  public bool Invalid = false;

  public Dir (string path) {
    Path = path;
  }

  public Dir SetComplete () {
    lock (this) {
      if (Invalid) return this;
      if (Missing != Completed) {
        Console.WriteLine("Dir.Missing != Dir.Completed in {0}: {1} != {2}", Path, Missing, Completed);
        throw new System.ArgumentException ("Missing != Completed");
      }
      InProgress = false;
      Complete = true;
      string pdir = ParentDir();
      if (pdir.Length == 0) return this;
      DirCache.GetCacheEntry(pdir).ChildFinished (this);
    }
    return this;
  }

  public void ChildFinished (Dir d) {
    lock (this) {
      Completed++;
      if (Missing == Completed) SetComplete ();
    }
  }

  public Dir Fail () {
    return SetComplete ();
  }

  public Dir Cancel () {
    lock (this) {
      InProgress = false;
      return this;
    }
  }

  public void AddCount (ulong c) {
    lock (this) {
      if (Invalid) return;
      TotalCount += c;
      string pdir = ParentDir();
      if (pdir.Length == 0) return;
      DirCache.GetCacheEntry(pdir).AddCount(c);
    }
  }

  public void AddSize (ulong c) {
    lock (this) {
      if (Invalid) return;
      TotalSize += c;
      string pdir = ParentDir();
      if (pdir.Length == 0) return;
      DirCache.GetCacheEntry(pdir).AddSize(c);
    }
  }

  public string ParentDir () {
    if (Path == Helpers.RootDir) return "";
    char[] sa = {Helpers.DirSepC};
    string p = srev(srev(Path).Split(sa, 2)[1]);
    return (p.Length == 0 ? Helpers.RootDir : p);
  }


  static string srev (string s) {
    char [] c = s.ToCharArray ();
    Array.Reverse (c);
    return new string (c);
  }
}

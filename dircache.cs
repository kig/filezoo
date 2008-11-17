using Mono.Unix;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

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
      }
    }
    return dc;
  }

  public static Dir Traverse (string dirname, ref bool TraversalCancelled)
  {
    Dir dc;
    UnixFileSystemInfo[] files;
    lock (Cache) {
      dc = GetCacheEntry(dirname);
      if (TraversalCancelled) return dc;
      if (dc.Complete || dc.InProgress) return dc;
      UnixDirectoryInfo di = new UnixDirectoryInfo (dirname);
      try { files = di.GetFileSystemEntries (); }
      catch (System.UnauthorizedAccessException) { return dc.Fail (); }
      dc.InProgress = true;
      dc.Complete = false;
      dc.Missing = files.Length;
    }
    double count = 0.0;
    int completed = 0;
    double size = 0.0;
    foreach (UnixFileSystemInfo f in files) {
      if (TraversalCancelled) { return dc.Cancel (); }
      count += 1.0;
      bool isDir = false;
      try { isDir = f.IsDirectory; } catch (System.InvalidOperationException) {}
      if (!isDir) {
        completed++;
        try { size += f.Length; }
        catch (System.InvalidOperationException) {}
      }
    }
    lock (dc) { dc.Completed += completed; }
    dc.AddCount (count);
    dc.AddSize (size);
    foreach (UnixFileSystemInfo f in files) {
      if (TraversalCancelled) { return dc.Cancel (); }
      bool isDir = false;
      try { isDir = f.IsDirectory; } catch (System.InvalidOperationException) {}
      if (isDir) Traverse(f.FullName, ref TraversalCancelled);
    }
    lock (dc) {
      if (!dc.Complete) {
        dc.Complete = (dc.Completed == dc.Missing);
        dc.InProgress = !dc.Complete;
        if (dc.Complete) dc.PropagateComplete ();
      }
    }
    return dc;
  }
}



public class Dir {
  public string Path;
  public double TotalCount = 1.0;
  public double TotalSize = 0.0;
  public bool Complete = false;
  public bool InProgress = false;
  public int Completed = 0;
  public int Missing = 0;

  public Dir (string path) {
    Path = path;
  }

  public Dir Finish () {
    lock (this) {
      Complete = true;
      InProgress = false;
    }
    return this;
  }

  public Dir Fail () {
    lock (this) {
      Completed = 0;
      Missing = 0;
      Complete = true;
      InProgress = false;
      PropagateComplete ();
    }
    return this;
  }
  public Dir Cancel () {
    lock (this) {
      Complete = false;
      InProgress = false;
    }
    return this;
  }

  public string ParentDir () {
    if (Path == "/") return "";
    char[] sa = {'/'};
    return srev(srev(Path).Split(sa, 2)[1]);
  }

  static string srev (string s) {
    char [] c = s.ToCharArray ();
    Array.Reverse (c);
    return new string (c);
  }

  public void AddCount (double c) {
    TotalCount += c;
    string pdir = ParentDir();
    if (pdir == "") return;
    DirCache.GetCacheEntry(pdir).AddCount(c);
  }

  public void AddSize (double c) {
    TotalSize += c;
    string pdir = ParentDir();
    if (pdir == "") return;
    DirCache.GetCacheEntry(pdir).AddSize(c);
  }

  public void AddChildData (Dir c) {
    TotalSize += c.TotalSize;
    TotalCount += c.TotalCount;
    string pdir = ParentDir();
    if (pdir == "") return;
    DirCache.GetCacheEntry(pdir).AddChildData(c);
  }

  public void PropagateComplete () {
    string pdir = ParentDir();
    if (pdir == "") return;
    DirCache.GetCacheEntry(pdir).ChildFinished (this);
  }

  public void ChildFinished (Dir d) {
    lock (this) {
      Completed++;
      if (Completed == Missing) {
        Complete = true;
        InProgress = false;
        string pdir = ParentDir();
        if (pdir == "") return;
        DirCache.GetCacheEntry(pdir).ChildFinished (this);
      }
    }
  }
}

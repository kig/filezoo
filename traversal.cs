using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using Mono.Unix;

public class Traversal {

  Thread Worker;
  Queue<DirectoryEntry> TraversalRequests;
  Dictionary<string, DirectoryEntry> Entries;

  public Traversal () {
    TraversalRequests = new Queue<DirectoryEntry> ();
    Entries = new Dictionary<string, DirectoryEntry> ();
    ThreadStart w = new ThreadStart (ProcessQueue);
    Worker = new Thread (w);
    Worker.IsBackground = true;
    Worker.Priority = ThreadPriority.BelowNormal;
    Worker.Start ();
  }

  void ProcessQueue () {
    while (true) {
      while (TraversalRequests.Count > 0) {
        DirectoryEntry d = TraversalRequests.Dequeue ();
        Console.WriteLine("Processing {0}", d.Path);
        ArrayList sd = d.Directories;
        d.TotalSize = d.FilesizeSum;
        d.TotalCount = d.Entries.Length;
        bool allComplete = true;
        if (sd.Count > 0) {
          foreach (UnixFileSystemInfo s in sd) {
            DirectoryEntry se = RequestInfo(s.FullName);
            if (se.Complete) {
              d.TotalSize += se.TotalSize;
              d.TotalCount += se.TotalCount;
            } else {
              allComplete = false;
            }
          }
        }
        PropagateTotalSize(d);
        if (allComplete) SetComplete(d);
      }
      Thread.Sleep (500);
    }
  }

  void PropagateTotalSize (DirectoryEntry d) {
    foreach (string a in d.Ancestors)
      if (Entries.ContainsKey(a)) {
        Entries[a].TotalSize += d.TotalSize;
        Entries[a].TotalCount += d.TotalCount;
      }
  }

  void SetComplete (DirectoryEntry d) {
    foreach (string a in d.Ancestors) {
      if (Entries.ContainsKey(a)) {
        DirectoryEntry ae = Entries[a];
        if (ae.Complete) continue;
        ArrayList sd = ae.Directories;
        bool allComplete = true;
        foreach (UnixFileSystemInfo s in sd) {
          if (!RequestInfo(s.FullName).Complete) {
            allComplete = false;
          }
        }
        ae.Complete = allComplete;
      }
    }
  }

  public DirectoryEntry RequestInfo (string path) {
    Console.WriteLine("Requesting {0}", path);
    if (!Entries.ContainsKey(path)) {
      Console.WriteLine("Queuing {0}", path);
      DirectoryEntry d = new DirectoryEntry (path);
      Entries.Add(path, d);
      TraversalRequests.Enqueue (d);
    }
    return Entries[path];
  }

  public class DirectoryEntry {
    public bool Complete;
    public string Path;
    public double TotalSize;
    public double TotalCount;
    public ArrayList Ancestors;

    public DirectoryEntry (string path) {
      Path = path;
      Complete = false;
      TotalSize = 0.0;
      TotalCount = 0.0;
      Ancestors = new ArrayList ();
      string[] segments = path.Split('/');
      string build = "";
      foreach (string s in segments) {
        build = build + "/" + s;
        Ancestors.Add(build);
      }
    }


    public double FilesizeSum {
      get {
        double sum = 0.0;
        foreach (UnixFileSystemInfo f in Files)
          sum += (double)f.Length;
        return sum;
      }
    }

    public UnixFileSystemInfo[] Entries {
      get {
        UnixDirectoryInfo di = new UnixDirectoryInfo (Path);
        try { return di.GetFileSystemEntries (); }
        catch (System.UnauthorizedAccessException) { return new UnixFileSystemInfo[0]; }
      }
    }
    public ArrayList Directories {
      get {
        ArrayList al = new ArrayList ();
        foreach (UnixFileSystemInfo e in Entries)
          if (e.IsDirectory) al.Add(e);
        return al;
      }
    }
    public ArrayList Files {
      get {
        ArrayList al = new ArrayList ();
        foreach (UnixFileSystemInfo e in Entries)
          if (!e.IsDirectory) al.Add(e);
        return al;
      }
    }
  }

}


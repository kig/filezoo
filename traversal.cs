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

  bool ClearRequested = false;

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
//         Console.WriteLine("Processing {0}", d.Path);
        if (ClearRequested) break;
        ArrayList sd = d.Directories;
        if (ClearRequested) break;
        d.TotalSize = d.FilesizeSum;
        if (ClearRequested) break;
        d.TotalCount = d.Entries.Length;
        if (ClearRequested) break;
        bool allComplete = true;
        if (sd.Count > 0) {
          foreach (UnixFileSystemInfo s in sd) {
            if (ClearRequested) break;
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
//         Console.WriteLine("Finished {0}", d.Path);
      }
      Thread.Sleep (50);
    }
  }

  public void ClearQueue () {
    ClearRequested = true;
    lock (TraversalRequests) {
      lock (Entries) {
        TraversalRequests.Clear ();
//         Console.WriteLine ("Cleared queue: {0}", TraversalRequests.Count);
        ArrayList removals = new ArrayList ();
        foreach (DirectoryEntry d in Entries.Values)
          if (!d.Complete) removals.Add(d.Path);
        foreach (string k in removals)
          Entries.Remove(k);
        ClearRequested = false;
      }
    }
  }

  public DirectoryEntry RequestInfo (string path) {
//     Console.WriteLine("Requesting {0}", path);
    lock (Entries) {
      if (!Entries.ContainsKey(path)) {
//         Console.WriteLine("Queuing {0}", path);
        DirectoryEntry d = new DirectoryEntry (path);
        Entries.Add(path, d);
        TraversalRequests.Enqueue (d);
      }
    }
    return Entries[path];
  }




  void PropagateTotalSize (DirectoryEntry d) {
    foreach (string a in d.Ancestors)
      if (Entries.ContainsKey(a)) {
        Entries[a].TotalSize += d.TotalSize;
        Entries[a].TotalCount += d.TotalCount;
      }
  }

  void SetComplete (DirectoryEntry d) {
    d.Complete = true;
    foreach (string a in d.Ancestors) {
//       Console.WriteLine("SetComplete {0}", a);
      if (Entries.ContainsKey(a)) {
        DirectoryEntry ae = Entries[a];
        if (ae.Complete) continue;
        if (ClearRequested) break;
        ArrayList sd = ae.Directories;
        bool allComplete = true;
        foreach (UnixFileSystemInfo s in sd) {
          if (ClearRequested) break;
          if (!RequestInfo(s.FullName).Complete) {
            allComplete = false;
          }
        }
//         Console.WriteLine("SetComplete {0} {1}", a, allComplete);
        ae.Complete = allComplete;
      }
    }
  }




  public class DirectoryEntry {
    public bool Complete;
    public string Path;
    public double TotalSize;
    public double TotalCount;

    public DirectoryEntry (string path) {
      Path = path;
      Complete = false;
      TotalSize = 0.0;
      TotalCount = 0.0;
    }

    ArrayList _Ancestors;
    public ArrayList Ancestors {
      get {
        if (_Ancestors == null) {
          ArrayList ancestors = new ArrayList ();
          string[] segments = Path.Split('/');
          string build = "";
          for (int i=0; i<segments.Length-1; i++) {
            string s = segments[i];
            if (build == "/") build = "/" + s;
            else build = build + "/" + s;
            ancestors.Add(build);
          }
          ancestors.Reverse ();
          _Ancestors = ancestors;
        }
        return _Ancestors;
      }
    }

    public double FilesizeSum {
      get {
        double sum = 0.0;
        foreach (UnixFileSystemInfo f in Files)
          try { sum += (double)f.Length; } catch (System.InvalidOperationException) {}
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
          try { if (e.IsDirectory) al.Add(e); } catch (System.InvalidOperationException) {}
        return al;
      }
    }
    public ArrayList Files {
      get {
        ArrayList al = new ArrayList ();
        foreach (UnixFileSystemInfo e in Entries)
          try { if (!e.IsDirectory) al.Add(e); }
          catch (System.InvalidOperationException) { al.Add(e); }
        return al;
      }
    }
  }

}


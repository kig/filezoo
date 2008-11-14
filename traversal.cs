using System;
using System.Diagnostics;
using System.Collections;
using System.Threading;
using System.IO;
using Mono.Unix;

public class Traversal {

  class DirectoryEntry {
    public bool Complete;
    public string Path;
    public double TotalSize;

    public DirectoryEntry (string path) {
      Path = path;
      Complete = false;
      TotalSize = 0.0;
    }

    public void SetTotalSize (double sz) { TotalSize = sz; }
    public void SetComplete (bool c) { Complete = c; }
  }

}

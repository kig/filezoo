using System;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Mono.Unix;
using Cairo;

public class DirStatsTraversal : DirStats
{
  Traversal TraversalServer;
  Traversal.DirectoryEntry TraversalInfo;

  public DirStatsTraversal (UnixFileSystemInfo f, Traversal ts) : base(f)
  {
    TraversalServer = ts;
  }

  public override bool TraversalInProgress {
    get {
      if (TraversalInfo == null) return false;
      return !TraversalInfo.Complete;
    }
    set {}
  }

  public override double GetRecursiveSize ()
  {
    if (!recursiveSizeComputed) {
      recursiveSizeComputed = true;
      if (IsDirectory) {
        TraversalInfo = TraversalServer.RequestInfo(FullName);
      } else {
        TraversalInfo = new Traversal.DirectoryEntry(FullName);
        TraversalInfo.TotalSize = Length;
        TraversalInfo.TotalCount = 1.0;
        TraversalInfo.Complete = true;
      }
    }
    return TraversalInfo.TotalSize;
  }

  public override double GetRecursiveCount ()
  {
    if (!recursiveSizeComputed)
      GetRecursiveSize ();
    return TraversalInfo.TotalCount;
  }
}

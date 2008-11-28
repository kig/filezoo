
using System.Collections.Generic;
using System;
using Mono.Unix;
using Cairo;


public class FSEntry
{

  public FSEntry ParentDir;
  public List<FSEntry> Entries = new List<FSEntry> ();

  public IMeasurer Measurer;
  public IComparer<FSEntry> Comparer;
  public SortingDirection SortDirection;

  public string Name;
  public string LCName;
  public string Suffix;
  public string FullName;

  public string Owner;
  public string Group;

  public DateTime LastModified;
  public FileAccessPermissions Permissions;
  public FileTypes FileType;

  public bool IsDirectory = false;

  public double Height = 1.0;
  public double Scale = 1.0;

  public long Count = 0;
  public long Size = 0;
  public long SubTreeCount = 0;
  public long SubTreeSize = 0;

  public long TotalCount { get { return Count + SubTreeCount; } }
  public long TotalSize { get { return Size + SubTreeSize; } }

  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;
  public bool HasThumbnail = false;

  public ImageSurface Thumbnail = null;


  public FSEntry (string path) : this (new UnixDirectoryInfo(path)) {}

  public FSEntry (UnixDirectoryInfo u)
  {
    FullName = u.FullName;
    Name = u.Name;
    LCName = Name.ToLower ();

    Owner = Helpers.OwnerName(u);
    Group = Helpers.GroupName(u);

    LastModified = Helpers.LastModified(u);
    Permissions = Helpers.FilePermissions(u);
    FileType = Helpers.FileType(u);

    IsDirectory = FileType == FileTypes.Directory;

    Suffix = IsDirectory ? "" : Helpers.Extname(Name).ToLower();

    Count = IsDirectory ? 0 : 1;
    Size = IsDirectory ? 0 : Helpers.FileSize(u);

    if (!IsDirectory) {
      Complete = true;
      FilePassDone = true;
    }
  }

}

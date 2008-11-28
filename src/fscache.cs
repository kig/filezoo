using System.Collections.Generic;
using System;
using Mono.Unix;
using Cairo;


public static class FSCache
{
  static Dictionary<string,FSEntry> Cache = new Dictionary<string,FSEntry> ();

  public static IMeasurer Measurer;
  public static IComparer<FSEntry> Comparer;
  public static SortingDirection SortDirection;

  public static FSEntry Get (string path)
  { lock (Cache) {
    if (Cache.ContainsKey(path))
      return Cache[path];
    FSEntry f = new FSEntry (path);
    Cache[path] = f;
    CreateParents (f);
    return f;
  } }

  static void CreateParents (FSEntry f)
  {
    if (f.FullName == Helpers.RootDir) return;
    f.ParentDir = Get (Helpers.Dirname (f.FullName));
  }

  public static void FilePass (FSEntry f)
  { lock (f) {
    if (f.FilePassDone) return;
    if (f.IsDirectory) {
      List<FSEntry> entries = new List<FSEntry> ();
      long size = 0, count = 0;
      foreach (UnixFileSystemInfo u in Helpers.EntriesMaybe (f.FullName)) {
        FSEntry d = Get (u.FullName);
        entries.Add (d);
        size += d.Size;
        count++;
      }
      f.Entries = entries;
      f.Size = size;
      f.Count = count;
    }
    f.FilePassDone = true;
  } }

  public static void SortEntries (FSEntry f)
  {
    if (f.Comparer != Comparer || f.SortDirection != SortDirection) {
      f.Comparer = Comparer;
      f.SortDirection = SortDirection;
      f.Entries.Sort(Comparer);
      if (SortDirection == SortingDirection.Descending)
        f.Entries.Reverse();
    }
  }

  public static void MeasureEntries (FSEntry f)
  {
    if (!f.IsDirectory) return;
    if (f.Measurer == Measurer) return;
    f.Measurer = Measurer;
    double totalHeight = 0.0;
    foreach (FSEntry e in f.Entries) {
      e.Height = Measurer.Measure(e);
      totalHeight += e.Height;
    }
    double scale = 1.0 / totalHeight;
    foreach (FSEntry e in f.Entries) {
      e.Scale = scale;
    }
  }

}


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
  public long TotalCount = 0;
  public long TotalSize = 0;

  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;
  public bool HasThumbnail = false;

  public ImageSurface Thumbnail
  {
    get { return null; }
  }


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
    TotalSize = Size;
    TotalCount = Count;
  }

}


public enum SortingDirection {
  Ascending,
  Descending
}

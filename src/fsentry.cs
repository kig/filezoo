/*
    Filezoo - a small and fast file manager
    Copyright (C) 2008  Ilmari Heikkinen

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using System.Threading;
using System;
using Mono.Unix;
using Cairo;


public class FSEntry
{
  public FSEntry ParentDir;
  public List<FSEntry> Entries;
  public List<DrawEntry> DrawEntries = null;

  public IMeasurer Measurer;
  public IComparer<FSEntry> Comparer;
  public SortingDirection SortDirection;
  public DateTime LastMeasure = Helpers.DefaultTime;
  public DateTime LastSort = Helpers.DefaultTime;
  public DateTime LastChange = DateTime.Now;
  public DateTime LastFileChange;

  // rolling 32-bit frame counter in 497 days at 100fps
  // rolling 64-bit frame counter around the time the sun burns out at 100fps
  // 63-bit like we have here takes just 3 billion years
  public Int64 LastDraw = 0;

  public string Name;
  public string LCName;
  public string Suffix;
  public string FullName;
  public string LinkTarget;

  public string Owner;
  public string Group;

  public DateTime LastModified;
  public FileAccessPermissions Permissions;
  public FileTypes FileType;

  public bool IsDirectory = false;

  public double Height = 1.0;

  public long Count = 0;
  public long Size = 0;
  public long SubTreeCount = 0;
  public long SubTreeSize = 0;

  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;

  public ImageSurface Thumbnail = null;


  public FSEntry (string path) : this (new UnixSymbolicLinkInfo(path)) {}

  public FSEntry (UnixFileSystemInfo u)
  {
    LastDraw = FSDraw.frame;
    Setup(u);
  }

  public void Setup (UnixFileSystemInfo u)
  {
    FullName = u.FullName;
    Name = u.Name;
    LCName = Name.ToLower ();

    Owner = Helpers.OwnerName(u);
    Group = Helpers.GroupName(u);

    LastModified = Helpers.LastModified(u);
    LastFileChange = Helpers.LastChange(FullName);
    Permissions = Helpers.FilePermissions(u);
    FileType = Helpers.FileType(u);

    IsDirectory = FileType == FileTypes.Directory;
    if (FileType == FileTypes.SymbolicLink) {
      LinkTarget = Helpers.ReadLink(FullName);
    }

    Suffix = IsDirectory ? "" : Helpers.Extname(Name).ToLower();

    Count = 1;
    Size = Helpers.FileSize(u);

    if (!IsDirectory) {
      SubTreeSize = Size;
      SubTreeCount = 1;
      Complete = true;
      FilePassDone = true;
    } else {
      Entries = new List<FSEntry> ();
    }
  }

  public FSEntry Copy() {
    return (FSEntry) MemberwiseClone();
  }
}

public class DrawEntry {
  public double Height;
  public FSEntry F;

  public string GroupTitle;
  public double GroupHeight;

  public DrawEntry (FSEntry f) {
    Height = f.Height;
    F = f;
  }
}

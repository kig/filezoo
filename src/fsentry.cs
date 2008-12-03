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
  public List<FSEntry> Entries = new List<FSEntry> ();

  public IMeasurer Measurer;
  public IComparer<FSEntry> Comparer;
  public SortingDirection SortDirection;
  public DateTime LastMeasure;
  public DateTime LastSort;
  public DateTime LastChange;
  public DateTime LastFileChange;

  public bool ReadyToDraw = false;

  public string Name;
  public string LCName;
  public string Suffix;
  public string FullName;
  public string LinkTarget = "";

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

  public bool Complete = false;
  public bool FilePassDone = false;
  public bool InProgress = false;

  public ImageSurface Thumbnail = null;


  public FSEntry (string path) : this (new UnixSymbolicLinkInfo(path)) {}

  public FSEntry (UnixFileSystemInfo u)
  {
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

    Count = IsDirectory ? 0 : 1;
    Size = IsDirectory ? 0 : Helpers.FileSize(u);

    if (!IsDirectory) {
      SubTreeSize = Size;
      SubTreeCount = 1;
      Complete = true;
      FilePassDone = true;
      ReadyToDraw = true;
    }
  }

}

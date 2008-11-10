using System;
using System.Diagnostics;
using System.IO;
using Mono.Unix;
using Cairo;

public class DirStats
{
  public double Length;
  public double Height;
  public string Dirname;
  public string Name;
  public bool Control;
  public FileTypes Filetype;
  public FileAccessPermissions Permissions;

  public static Color directoryColor = new Color (0,0,1);
  public static Color blockDeviceColor = new Color (1,1,0);
  public static Color characterDeviceColor = new Color (1,1,0);
  public static Color fifoColor = new Color (1,0,1);
  public static Color socketColor = new Color (1,0,0);
  public static Color symlinkColor = new Color (0,1,1);
  public static Color executableColor = new Color (0,1,0);
  public static Color fileColor = new Color (0,0,0);

  public DirStats (string dirname, string name, double length, FileTypes ft, FileAccessPermissions perm)
  {
    Length = length;
    Height = 0.0;
    Control = false;
    Dirname = dirname;
    Name = name;
    Filetype = ft;
    Permissions = perm;
  }

  public void Draw (Context cr, double totalSize)
  {
    Height = Length / totalSize;
    cr.Save ();
      cr.Rectangle (0.0, 0.0, 0.2, Height);
      cr.Color = GetColor (Filetype, Permissions);
//       cr.FillPreserve ();
//       cr.Color = new Color (1,1,1);
      cr.Stroke ();
//       cr.Color = GetColor (Filetype, Permissions);
      double fs = Math.Max(0.001, Math.Min(0.03, 0.8 * Height));
      cr.SetFontSize(fs);
      cr.MoveTo (0.21, Height / 2 + fs / 4);
      cr.ShowText (Name);
      if (Name != "..") {
        cr.SetFontSize(fs * 0.7);
        cr.ShowText (String.Format("  {0} bytes", Length.ToString("N0")));
      }
    cr.Restore ();
  }

  public bool Click (Context cr, double totalSize, double x, double y)
  {
    Height = Length / totalSize;
    bool retval;
    cr.Save ();
      cr.NewPath ();
      cr.Rectangle (0.0, 0.0, 0.2, Height);
      cr.IdentityMatrix ();
      retval = cr.InFill(x,y);
    cr.Restore ();
    if (retval)
      OpenFile ();
    return retval;
  }

  public string GetFullPath ()
  {
    return System.IO.Path.GetFullPath(System.IO.Path.Combine(Dirname, Name));
  }

  void OpenFile ()
  {
    if (Filetype == FileTypes.Directory)
    {
      Control = true;
    } else {
      Console.WriteLine("Opening {0}", GetFullPath ());
      Process proc = new Process ();
      proc.EnableRaisingEvents = false;
      proc.StartInfo.FileName = "gnome-open";
      proc.StartInfo.Arguments = GetFullPath ();
      proc.Start ();
      proc.WaitForExit ();
    }
  }

  Color GetColor (FileTypes filetype, FileAccessPermissions perm)
  {
    switch (filetype) {
      case FileTypes.Directory: return directoryColor;
      case FileTypes.BlockDevice: return blockDeviceColor;
      case FileTypes.CharacterDevice: return characterDeviceColor;
      case FileTypes.Fifo: return fifoColor;
      case FileTypes.Socket: return socketColor;
      case FileTypes.SymbolicLink: return symlinkColor;
    }
    if ((perm & FileAccessPermissions.UserExecute) != 0)
      return executableColor;
    return fileColor;
  }
}
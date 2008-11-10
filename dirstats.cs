using System;
using Mono.Unix;
using Cairo;

public class DirStats
{
  public double Length;
  public string Name;
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

  public DirStats (string name, double length, FileTypes ft, FileAccessPermissions perm)
  {
    Length = length;
    Name = name;
    Filetype = ft;
    Permissions = perm;
  }

  public double Draw (Context cr, double totalSize)
  {
    double height = Length / totalSize;
    cr.Save ();
      cr.Rectangle (0.0, 0.0, 0.2, height);
      cr.Color = GetColor (Filetype, Permissions);
      cr.FillPreserve ();
      cr.Color = new Color (1,1,1);
      cr.Stroke ();
      cr.Color = GetColor (Filetype, Permissions);
      double fs = Math.Max(0.001, Math.Min(0.03, 0.8 * height));
      cr.SetFontSize(fs);
      cr.MoveTo (0.21, height / 2 + fs / 4);
      cr.ShowText (Name + " " + Length.ToString() + " bytes");
    cr.Restore ();
    return height;
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
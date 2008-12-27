using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System;

[TestFixture]
public class HelpersTest
{

  static string testDir = "/tmp/filezoo_test/";

  [SetUp]
  public void setup ()
  {
    if (Helpers.FileExists(testDir))
      Helpers.Delete(testDir);
    Helpers.MkdirP(testDir);
  }

  [TearDown]
  public void teardown ()
  {
    Helpers.Delete(testDir);
  }

  [Test]
  public void FileExists ()
  {
    Assert.IsTrue(Helpers.FileExists(testDir));
    Assert.IsTrue(Helpers.FileExists(Helpers.Dirname(testDir)));
    Assert.IsFalse(Helpers.FileExists(testDir+"fileExistsTest"));
    Assert.IsFalse(Helpers.FileExists(testDir+"fileExistsTest"));
  }

  [Test]
  public void EscapePath ()
  {
    Assert.AreEqual ("'foo'\\''s \n\":/\r\t()[]'", Helpers.EscapePath("foo's \n\":/\r\t()[]"));
  }

  [Test]
  public void MkdirPSimple ()
  {
    Helpers.MkdirP(testDir + "mkdir_p");
    Assert.IsTrue(Helpers.FileExists(testDir + "mkdir_p"));
    Assert.IsTrue(Helpers.IsDir(testDir + "mkdir_p"));
  }

  [Test]
  public void MkdirPFancyFilenames ()
  {
    Helpers.MkdirP(testDir+"mkdir_p/foo/bar/baz/");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/foo/bar/baz"));
    Helpers.MkdirP(testDir+"mkdir_p");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p"));
    Helpers.MkdirP(testDir+"mkdir_p/foo and \nbar");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/foo and \nbar"));
    Helpers.MkdirP(testDir+"mkdir_p/foo and \nbar\x8f\xf0");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/foo and \nbar\x8f\xf0"));
  }

  [Test]
  public void MkdirPEvilFilenames ()
  {
    Helpers.MkdirP(testDir+"mkdir_p/:h#i:t!h[e]\n\t \rr(e\\");
    Assert.IsTrue(Helpers.FileExists(testDir+"mkdir_p/:h#i:t!h[e]\n\t \rr(e\\"));
  }

  [Test]
  public void Touch ()
  {
    var l = new List<string> { "a", "b", "c", ":h#i:t!h[e]\n\t \rr(e\\" };
    l.ForEach(s => {
      Helpers.Touch(testDir+s);
      Assert.IsTrue(Helpers.FileExists(testDir+s));
    });
    Thread.Sleep(1000);
    l.ForEach(s => {
      var td1 = DateTime.Now.Subtract(Helpers.LastModified(testDir+s));
      Assert.Greater( td1.TotalMilliseconds, 1000 );
      Helpers.Touch(testDir+s);
      var td = DateTime.Now.Subtract(Helpers.LastModified(testDir+s));
      Assert.Less( td.TotalMilliseconds, 1000 );
    });
  }

  [Test]
  public void TouchDir ()
  {
    var l = new List<string> { "a", "b", "c", ":h#i:t!h[e]\n\t \rr(e\\" };
    l.ForEach(s => {
      Helpers.MkdirP(testDir+s);
      Assert.IsTrue(Helpers.FileExists(testDir+s));
    });
    Thread.Sleep(1000);
    l.ForEach(s => {
      var td1 = DateTime.Now.Subtract(Helpers.LastModified(testDir+s));
      Assert.Greater( td1.TotalMilliseconds, 1000 );
      Helpers.Touch(testDir+s);
      var td = DateTime.Now.Subtract(Helpers.LastModified(testDir+s));
      Assert.Less( td.TotalMilliseconds, 1000 );
    });
  }

  [Test]
  public void Trash ()
  {
    string td = Helpers.TrashDir + Helpers.DirSepS + "trashTest";
    string trashFn = testDir + "trashTest";

    if (Helpers.FileExists(td))
      Helpers.Delete(td);

    Helpers.Touch(trashFn);
    Helpers.Trash(trashFn);
    Assert.IsFalse(Helpers.FileExists(trashFn));
    Assert.IsTrue(Helpers.FileExists(td));
    Helpers.Touch(trashFn);
    Helpers.Trash(trashFn);
    Assert.IsFalse(Helpers.FileExists(trashFn));
    Assert.IsTrue(Helpers.FileExists(td));
    Helpers.Delete(td);
  }

  [Test]
  public void TrashDir ()
  {
    string td = Helpers.TrashDir + Helpers.DirSepS + "trashDirTest";
    string trashDirFn = testDir + "trashDirTest";

    if (Helpers.FileExists(td))
      Helpers.Delete(td);

    Helpers.MkdirP(trashDirFn);
    Assert.IsTrue(Helpers.FileExists(trashDirFn));
    Helpers.Trash(trashDirFn);
    Assert.IsFalse(Helpers.FileExists(trashDirFn));
    Assert.IsTrue(Helpers.FileExists(td));
    Helpers.Delete(td);
  }

  [Test]
  public void Delete ()
  {
    string tfn = testDir+"deleteTest";
    if (Helpers.FileExists(tfn))
      Helpers.Delete(tfn);
    Assert.IsFalse(Helpers.FileExists(tfn));
    Helpers.Touch(tfn);
    Helpers.Delete(tfn);
    Assert.IsFalse(Helpers.FileExists(tfn));
    Helpers.MkdirP(tfn);
    Helpers.Delete(tfn);
    Assert.IsFalse(Helpers.FileExists(tfn));
  }

  [Test]
  public void Move ()
  {
    string t1 = testDir+"moveTest1";
    string t2 = testDir+"moveTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Move (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Helpers.MkdirP(t2);
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
  }

  [Test]
  public void CrossDeviceMove ()
  {
    string t1 = testDir+"moveTest1";
    string t2 = "/dev/shm/moveTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Move (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Helpers.MkdirP(t2);
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Helpers.Delete(t2);
  }

  [Test]
  public void MoveTrash ()
  {
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest2");
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest1");
    string t1 = testDir+"moveTest1";
    string t2 = testDir+"moveTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Touch(t2);
    Helpers.Move (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(Helpers.TrashDir+Helpers.DirSepS + "moveTest2"));
    Helpers.MkdirP(t1);
    Helpers.Move (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Assert.IsTrue(Helpers.FileExists(Helpers.TrashDir+Helpers.DirSepS + "moveTest1"));
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest2");
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest1");
  }

  [Test]
  public void MoveDelete ()
  {
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest2");
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest1");
    string t1 = testDir+"moveTest1";
    string t2 = testDir+"moveTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Touch(t2);
    Helpers.Move (t1, t2, true);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(Helpers.TrashDir+Helpers.DirSepS + "moveTest2"));
    Helpers.MkdirP(t1);
    Helpers.Move (t2, t1, true);
    Assert.IsFalse(Helpers.IsDir(t1));
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(Helpers.TrashDir+Helpers.DirSepS + "moveTest1"));
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest2");
    Helpers.Delete(Helpers.TrashDir+Helpers.DirSepS + "moveTest1");
  }

  [Test]
  public void Copy ()
  {
    string t1 = testDir+"copyTest1";
    string t2 = testDir+"copyTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Copy (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsTrue(Helpers.FileExists(t1));
    Helpers.Copy (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(t2));
    Helpers.Delete(t2);
    Helpers.MkdirP(t2);
    Helpers.Copy (t2, t1);
    Assert.IsTrue(Helpers.IsDir(t1));
    Assert.IsTrue(Helpers.IsDir(t2));
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(t2));
  }

  [Test]
  public void CrossDeviceCopy ()
  {
    string t1 = testDir+"copyTest1";
    string t2 = "/dev/shm/copyTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.Copy (t1, t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsTrue(Helpers.FileExists(t1));
    Helpers.Copy (t2, t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(t2));
    Helpers.Delete(t2);
    Helpers.MkdirP(t2);
    Helpers.Copy (t2, t1);
    Assert.IsTrue(Helpers.IsDir(t1));
    Assert.IsTrue(Helpers.IsDir(t2));
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(t2));
    Helpers.Delete(t2);
  }

  [Test]
  public void NewFileWith ()
  {
    string t1 = testDir+"NewFileWith";
    byte[] data = new byte[256];
    for (int i=0; i<data.Length; i++) data[i] = (byte)i;
    Helpers.NewFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
  }

  [Test]
  public void NewFileWithEmpty ()
  {
    string t1 = testDir+"NewFileWith";
    byte[] data = new byte[0];
    Helpers.NewFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
  }

  [Test]
  public void NewFileWithOverwrite ()
  {
    string t1 = testDir+"NewFileWith";
    byte[] data = new byte[0];
    Helpers.NewFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
    data = new byte[256];
    for (int i=0; i<data.Length; i++) data[i] = (byte)i;
    Helpers.NewFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
  }

  [Test]
  public void ReplaceFileWith ()
  {
    string t1 = testDir+"ReplaceFileWith";
    byte[] data = new byte[0];
    Helpers.ReplaceFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
    data = new byte[256];
    for (int i=0; i<data.Length; i++) data[i] = (byte)i;
    Helpers.ReplaceFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
    data = new byte[128];
    for (int i=0; i<data.Length; i++) data[i] = (byte)i;
    Helpers.ReplaceFileWith(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
  }

  [Test]
  public void AppendToFile ()
  {
    string t1 = testDir+"AppendToFile";
    byte[] data = new byte[128];
    for (int i=0; i<data.Length; i++) data[i] = (byte)i;
    Helpers.AppendToFile(t1, data);
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
    for (int i=0; i<data.Length; i++) data[i] = (byte)(i+128);
    Helpers.AppendToFile(t1, data);
    data = new byte[256];
    for (int i=0; i<data.Length; i++) data[i] = (byte)i;
    Assert.IsTrue (Helpers.FileExists(t1));
    Assert.AreEqual ( data.Length, Helpers.FileSize(t1) );
    Assert.AreEqual ( data, System.IO.File.ReadAllBytes(t1) );
  }

  [Test]
  public void CopyURI ()
  {
    string t1 = testDir+"copyTest1";
    string t2 = testDir+"copyTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.CopyURI ("file://"+t1, "file://"+t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsTrue(Helpers.FileExists(t1));
    Helpers.CopyURI ("file://"+t2, "file://"+t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(t2));
    Helpers.Delete(t2);
    Helpers.MkdirP(t2);
    Helpers.CopyURI ("file://"+t2, "file://"+t1);
    Assert.IsTrue(Helpers.IsDir(t1));
    Assert.IsTrue(Helpers.IsDir(t2));
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsTrue(Helpers.FileExists(t2));
    Helpers.Delete(t1);
    Helpers.CopyURI ("http://www.google.com", t1);
    Assert.IsFalse(Helpers.IsDir(t1));
    Assert.IsTrue(Helpers.FileExists(t1));
  }

  [Test]
  public void MoveURI ()
  {
    string t1 = testDir+"moveTest1";
    string t2 = testDir+"moveTest2";
    if (Helpers.FileExists(t1)) Helpers.Delete(t1);
    if (Helpers.FileExists(t2)) Helpers.Delete(t2);
    Helpers.Touch(t1);
    Helpers.MoveURI ("file://"+t1, "file://"+t2);
    Assert.IsTrue(Helpers.FileExists(t2));
    Assert.IsFalse(Helpers.FileExists(t1));
    Helpers.MoveURI ("file://"+t2, "file://"+t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
    Helpers.MkdirP(t2);
    Helpers.MoveURI ("file://"+t2, "file://"+t1);
    Assert.IsTrue(Helpers.FileExists(t1));
    Assert.IsFalse(Helpers.FileExists(t2));
  }

  [Test]
  public void CopyURIs ()
  {
    string[] src = new string[10];
    for (int i=0; i<src.Length; i++) {
      src[i] = testDir+i.ToString();
      Helpers.Touch(src[i]);
    }
    Helpers.MkdirP(testDir+"dst");
    Helpers.CopyURIs(src, testDir+"dst");
    for (int i=0; i<src.Length; i++) {
      Assert.IsTrue(Helpers.FileExists(src[i]));
      Assert.IsTrue(Helpers.FileExists(testDir+"dst/"+i.ToString()));
    }
  }

  [Test]
  public void MoveURIs ()
  {
    string[] src = new string[10];
    for (int i=0; i<src.Length; i++) {
      src[i] = testDir+i.ToString();
      Helpers.Touch(src[i]);
    }
    Helpers.MkdirP(testDir+"dst");
    Helpers.MoveURIs(src, testDir+"dst");
    for (int i=0; i<src.Length; i++) {
      Assert.IsFalse(Helpers.FileExists(src[i]));
      Assert.IsTrue(Helpers.FileExists(testDir+"dst/"+i.ToString()));
    }
  }

//   public ImageSurface GetThumbnail (string path)
//   public void ExtractFile (string path)

}


using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ProjectedFSLib.Managed.Test
{
    // Set of basic tests to exercise the entry points in the managed code API wrapper.
    //
    // The Microsoft.Windows.ProjFS managed API is a fairly thin wrapper around a set of native
    // APIs for ProjFS.  The native API functionality has its own tests that are routinely executed
    // at Microsoft in the normal course of OS development.
    //
    // The tests in this module are meant to ensure the managed wrapper APIs get coverage.  They 
    // kick off a separate provider process, set up some file system state in the source root, then
    // perform actions in the virtualization root and check whether the results are expected.
    // 
    // Note that these tests are not as comprehensive as the native API tests are.  They also don't
    // have access to a couple of private APIs that the native tests do, in particular one that allows
    // a provider process to receive notifications caused by its own I/O.  Normally ProjFS does not
    // send notifications to a provider of I/O that the provider itself performs, to avoid deadlocks
    // and loops in an unsuspecting provider.  Hence the necessity of the separate provider process
    // in these tests.
    public class BasicTests
    {
        private Helpers helpers;

        [OneTimeSetUp]
        public void ClassSetup()
        {
            helpers = new Helpers();
        }

        // We start the virtualization instance in the SetUp fixture, so that exercises the following
        // methods in Microsoft.Windows.ProjFS:
        //  VirtualizationInstance.VirtualizationInstance()
        //  VirtualizationInstance.MarkDirectoryAsVirtualizationRoot()
        //  VirtualizationInstance.StartVirtualizing()
        [SetUp]
        public void TestSetup()
        {
            helpers.CreateRootsForTest(
                out string sourceRoot,
                out string virtRoot);

            helpers.StartTestProvider(sourceRoot, virtRoot);
        }

        // We stop the virtualization instance in the TearDown fixture, so that exercises the following
        // methods in Microsoft.Windows.ProjFS:
        //  VirtualizationInstance.StopVirtualizing()
        [TearDown]
        public void TestTeardown()
        {
            helpers.GetRootNamesForTest(out string sourceRoot, out string virtRoot);

            // Recursively delete the source root directory.
            try
            {
                DirectoryInfo sourceInfo = new DirectoryInfo(sourceRoot);
                sourceInfo.Delete(true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("Deleting sourceroot {0}: {1}", virtRoot, ex.Message);
            }

            // Recursively delete the virtualization root directory.
            // 
            // Note that we haven't yet shut down the provider.  That means that everything will get
            // hydrated just before being deleted.  I did this because DirectoryInfo.Delete() doesn't
            // take into account reparse points when it recursively deletes, unlike e.g. DOS 'rmdir /s /q'.
            // So without crafting a custom recursive deleter that works like rmdir, this would throw
            // an IOException with the error ERROR_FILE_SYSTEM_VIRTUALIZATION_UNAVAILABLE if the provider
            // were not running.
            try
            {
                DirectoryInfo virtInfo = new DirectoryInfo(virtRoot);
                virtInfo.Delete(true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("Deleting virtroot {0}: {1}", virtRoot, ex.Message);
            }

            // Shut down the provider app.
            helpers.StopTestProvider();
        }

        // This case exercises the following methods in Microsoft.Windows.ProjFS:
        //  VirtualizationInstance.WritePlaceholderInfo()
        //  VirtualizationInstance.CreateWriteBuffer()
        //  VirtualizationInstance.WriteFileData()
        //  
        // It also illustrates the SimpleProvider implementation of the following callbacks:
        //  IRequiredCallbacks.GetPlaceholderInfoCallback()
        //  IRequiredCallbakcs.GetFileDataCallback()
        [TestCase("foo.txt")]
        [TestCase("dir1\\dir2\\dir3\\bar.txt")]
        public void TestCanReadThroughVirtualizationRoot(string destinationFile)
        {
            helpers.GetRootNamesForTest(out string sourceRoot, out string virtRoot);

            // Some contents to write to the file in the source and read out through the virtualization.
            string fileContent = nameof(TestCanReadThroughVirtualizationRoot);

            // Create a file that we can read through the virtualization root.
            helpers.CreateVirtualFile(destinationFile, fileContent);

            // Open the file through the virtualization and read its contents.
            string line = helpers.ReadFileInVirtRoot(destinationFile);

            Assert.That(fileContent, Is.EqualTo(line));
            Assert.That("RandomNonsense", Is.Not.EqualTo(line));
        }

        // This case exercises the following methods in Microsoft.Windows.ProjFS:
        //  DirectoryEnumerationResults.Add()
        //  
        // It also illustrates the SimpleProvider implementation of the following callbacks:
        //  IRequiredCallbacks.StartDirectoryEnumeration()
        //  IRequiredCallbacks.GetDirectoryEnumeration()
        //  IRequiredCallbacks.EndDirectoryEnumeration()
        [Test]
        public void TestEnumerationInVirtualizationRoot()
        {
            helpers.GetRootNamesForTest(out string sourceRoot, out string virtRoot);

            Random random = new Random();

            // Generate some randomly-named directories in the source.
            for (int i = 1; i < 10; i++)
            {
                string dirName = Path.Combine(sourceRoot, helpers.RandomString(10) + i);
                DirectoryInfo dirInfo = new DirectoryInfo(dirName);
                dirInfo.Create();

                // Make the time stamps something other than "now".
                FileSystemInfo fsInfo = dirInfo as FileSystemInfo;
                fsInfo.CreationTime = fsInfo.CreationTime
                    .AddDays(-random.Next(1, 30))
                    .AddHours(random.Next(0,23))
                    .AddMinutes(random.Next(0, 59));
                fsInfo.LastAccessTime = fsInfo.LastAccessTime
                    .AddDays(-random.Next(1, 30))
                    .AddHours(random.Next(0, 23))
                    .AddMinutes(random.Next(0, 59));
                fsInfo.LastWriteTime = fsInfo.LastWriteTime
                    .AddDays(-random.Next(1, 30))
                    .AddHours(random.Next(0, 23))
                    .AddMinutes(random.Next(0, 59));
            }

            // Generate some randomly-named files with random sizes in the source.
            for (int i = 1; i < 10; i++)
            {
                string fileName = Path.Combine(sourceRoot, helpers.RandomString(10) + i);
                FileInfo fileInfo = new FileInfo(fileName);
                using (FileStream fs = fileInfo.OpenWrite())
                {
                    Byte[] contents =
                        new UTF8Encoding(true).GetBytes(helpers.RandomString(random.Next(10, 100)));

                    fs.Write(contents, 0, contents.Length);
                }

                // Make the time stamps something other than "now".
                FileSystemInfo fsInfo = fileInfo as FileSystemInfo;
                fsInfo.CreationTime = fsInfo.CreationTime
                    .AddDays(-random.Next(1, 30))
                    .AddHours(random.Next(0, 23))
                    .AddMinutes(random.Next(0, 59));
                fsInfo.LastAccessTime = fsInfo.LastAccessTime
                    .AddDays(-random.Next(1, 30))
                    .AddHours(random.Next(0, 23))
                    .AddMinutes(random.Next(0, 59));
                fsInfo.LastWriteTime = fsInfo.LastWriteTime
                    .AddDays(-random.Next(1, 30))
                    .AddHours(random.Next(0, 23))
                    .AddMinutes(random.Next(0, 59));
            }

            // Enumerate the source to build a list of its contents.
            DirectoryInfo sourceDirInfo = new DirectoryInfo(sourceRoot);
            List<FileSystemInfo> sourceList = new List<FileSystemInfo>(sourceDirInfo.EnumerateFileSystemInfos());

            // Now enumerate the virtualization root.
            DirectoryInfo virtDirInfo = new DirectoryInfo(virtRoot);
            List<FileSystemInfo> virtList = new List<FileSystemInfo>(virtDirInfo.EnumerateFileSystemInfos());

            // Compare the enumerations.  They should be the same.
            Assert.That(sourceList.Count, Is.EqualTo(virtList.Count));

            for (int i = 0; i < sourceList.Count; i++)
            {
                Assert.That(sourceList[i].Name, Is.EqualTo(virtList[i].Name), "Name");
                Assert.That(sourceList[i].CreationTime, Is.EqualTo(virtList[i].CreationTime), "CreationTime");
                Assert.That(sourceList[i].LastAccessTime, Is.EqualTo(virtList[i].LastAccessTime), "LastAccessTime");
                Assert.That(sourceList[i].LastWriteTime, Is.EqualTo(virtList[i].LastWriteTime), "LastWriteTime");

                bool isSourceDirectory = (sourceList[i].Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                bool isVirtDirectory = (virtList[i].Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                Assert.That(isSourceDirectory, Is.EqualTo(isVirtDirectory), "IsDirectory");

                if (!isSourceDirectory)
                {
                    FileInfo sourceInfo = sourceList[i] as FileInfo;
                    FileInfo virtInfo = virtList[i] as FileInfo;

                    Assert.That(sourceInfo.Length, Is.EqualTo(virtInfo.Length), "Length");
                }
            }
        }

        [Test]
        public void TestNotificationFileOpened()
        {
            string fileName = "file.txt";

            // Create the virtual file.
            helpers.CreateVirtualFile(fileName);

            // Open the file to trigger the notification in the provider.
            helpers.ReadFileInVirtRoot(fileName);

            // Wait for the provider to signal that it processed the FileOpened and
            // FileHandleClosedNoModification notifications.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.FileOpened].WaitOne(100));
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.FileHandleClosedNoModification].WaitOne(100));
        }

        [Test]
        public void TestNotificationNewFileCreated()
        {
            string fileName = "newfile.txt";

            // Create a new file in the virtualization root.
            helpers.CreateFullFile(fileName);

            // Wait for the provider to signal that it processed the NewFileCreated notification.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.NewFileCreated].WaitOne(100));
        }

        [Test]
        public void TestNotificationFileOverwritten()
        {
            string fileName = "overwriteme.txt";

            // Create a virtual file.
            helpers.CreateVirtualFile(fileName, "Old content");

            using (FileStream fs = helpers.OpenFileInVirtRoot(fileName, FileMode.Create))
            {

            }

            // Wait for the provider to signal that it processed the FileOverwritten notification.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.FileOverwritten].WaitOne(100));
        }

        [Test]
        public void TestNotificationDelete()
        {
            string fileName = "deleteme.txt";

            // Create a virtual file.
            string filePath = helpers.CreateVirtualFile(fileName);

            // Delete the file.
            FileInfo fileInfo = new FileInfo(filePath);
            fileInfo.Delete();

            // Wait for the provider to signal that it processed the PreDelete and
            // FileHandleClosedFileModifiedOrDeleted notifications.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.PreDelete].WaitOne(100));
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.FileHandleClosedFileModifiedOrDeleted].WaitOne(100));
        }

        [Test]
        public void TestNotificationRename()
        {
            string fileName = "OldName.txt";

            // Create a virtual file.
            string filePath = helpers.CreateVirtualFile(fileName);

            // Rename the file.
            string directory = Path.GetDirectoryName(filePath);
            string newFileName = "NewName.txt";
            string newFilePath = Path.Combine(directory, newFileName);
            File.Move(filePath, newFilePath);

            // Wait for the provider to signal that it processed the PreRename and FileRenamed notifications.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.PreRename].WaitOne(100));
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.FileRenamed].WaitOne(100));
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(
          string lpFileName,
          string lpExistingFileName,
          IntPtr lpSecurityAttributes
          );

        [Test]
        public void TestNotificationHardLink()
        {
            string fileName = "linkTarget.txt";

            // Create a virtual file.
            string filePath = helpers.CreateVirtualFile(fileName);

            // Create a hard link to the virtual file.
            string directory = Path.GetDirectoryName(filePath);
            string linkName = "link.txt";
            string linkPath = Path.Combine(directory, linkName);
            Assert.That(CreateHardLink(linkPath, filePath, IntPtr.Zero));

            // Wait for the provider to signal that it processed the PreCreateHardlink and HardlinkCreated
            // notifications.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.PreCreateHardlink].WaitOne(100));
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.HardlinkCreated].WaitOne(100));
        }

        [Test]
        public void TestConvertToFull()
        {
            string fileName = "fileToWriteTo.txt";

            // Create a virtual file.
            string filePath = helpers.CreateVirtualFile(fileName, "Original content");

            // Write to the file.
            using (StreamWriter sw = File.AppendText(filePath))
            {
                sw.WriteLine("Some new content!");
            }

            // Wait for the provider to signal that it processed the FilePreConvertToFull notification.
            Assert.That(helpers.NotificationEvents[(int)Helpers.NotifyWaitHandleNames.FilePreConvertToFull].WaitOne(100));
        }
    }
}
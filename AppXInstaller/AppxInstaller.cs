using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

using Ionic.Zip;

using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;
using System.Security.Principal;

namespace AppXInstaller
{
    class AppXManifest
    {
        public AppXManifest(string packagePath)
        {
            XmlDocument manifest = new XmlDocument();
            
            using (ZipFile zip = ZipFile.Read(packagePath))
            {
                var reader = zip["AppxManifest.xml"].OpenReader();
                manifest.Load(reader);
            }

            var identity = manifest["Package"]["Identity"];

            Name = identity.GetAttribute("Name");
            Publisher = identity.GetAttribute("Publisher");
            Version = identity.GetAttribute("Version");
        }

        public string Name { private set; get; }
        public string Publisher { private set; get; }
        public string Version { private set; get; }
    }
    class AppxInstaller
    {
        private static PackageManager packageManager = new PackageManager();

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                bool success = true;
                string packagePath = args[0];
                AppXManifest manifest = new AppXManifest(packagePath);
                var temp = WindowsIdentity.GetCurrent();
                string sid = WindowsIdentity.GetCurrent().User.ToString();
                IEnumerable<Package> packages = packageManager.FindPackagesForUser(sid, manifest.Name, manifest.Publisher);

                if (packages.Count() == 0)
                {
                    success &= InstallPackage(packagePath); 
                }
                else
                {
                    Package package = packages.First();
                    PackageVersion version = package.Id.Version;

                    Console.WriteLine("This package is already installed!");
                    DisplayPackageInfo(package);
                    Console.WriteLine();
                    Console.WriteLine("New version: {0}", manifest.Version);
                    Console.WriteLine();
                    Console.WriteLine( "What do you want to do?");
                    Console.WriteLine( "1 - Replace package");
                    Console.WriteLine( "2 - Upgrade package");
                    Console.WriteLine( "3 - Uninstall package");
                    Console.WriteLine( "4 - Quit");

                    char command = (char)Console.Read();

                    Console.WriteLine();

                    switch (command)
                    {
                        case '1':
                            success &= UninstallPackage(package.Id.FullName);
                            success &= InstallPackage(packagePath);
                            break;
                        case '2':
                            success &= InstallPackage(packagePath, null, true);
                            break;
                        case '3':
                            success &= UninstallPackage(package.Id.FullName);
                            break;
                        case '4':
                            return;
                    }

                    if (!success)
                    {
                        Console.WriteLine("Press any key to continue...");
                        while (!Console.KeyAvailable);
                    }
                }
            }
            else
            {
                Console.WriteLine("To use this program, associate it with the .appx and .appxbundle file extensions.");
            }
        }

        private static bool InstallPackage(string inputPackageUri, IEnumerable<Uri> dependencyPackageUris = null, bool update = false)
        {
            Uri packageUri = new Uri(inputPackageUri);
            IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deploymentOperation;

            Console.WriteLine("Installing package...");

            if (update)
            {
                deploymentOperation = packageManager.UpdatePackageAsync(packageUri, dependencyPackageUris, DeploymentOptions.None);
            }
            else 
            {
                deploymentOperation = packageManager.AddPackageAsync(packageUri, dependencyPackageUris, DeploymentOptions.None);
            }

            // This event is signaled when the operation completes
            ManualResetEvent opCompletedEvent = new ManualResetEvent(false);

            // Define the delegate using a statement lambda
            deploymentOperation.Completed = (depProgress, status) => { opCompletedEvent.Set(); };

            // Wait until the operation completes
            opCompletedEvent.WaitOne();

            // Check the status of the operation
            if (deploymentOperation.Status == AsyncStatus.Error)
            {
                DeploymentResult deploymentResult = deploymentOperation.GetResults();
                Console.WriteLine("Error code: {0}", deploymentOperation.ErrorCode);
                Console.WriteLine("Error text: {0}", deploymentResult.ErrorText);
                return false;
            }
            else if (deploymentOperation.Status == AsyncStatus.Canceled)
            {
                Console.WriteLine("Installation canceled");
            }
            else if (deploymentOperation.Status == AsyncStatus.Completed)
            {
                Console.WriteLine("Installation succeeded");
            }
            else
            {
                Console.WriteLine("Installation status unknown");
                return false;
            }
            return true; 
        }

        private static bool UninstallPackage(string inputPackageFullName)
        {
            PackageManager packageManager = new Windows.Management.Deployment.PackageManager();

            IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deploymentOperation =
                packageManager.RemovePackageAsync(inputPackageFullName);
            // This event is signaled when the operation completes
            ManualResetEvent opCompletedEvent = new ManualResetEvent(false);

            // Define the delegate using a statement lambda
            deploymentOperation.Completed = (depProgress, status) => { opCompletedEvent.Set(); };

            // Wait until the operation completes
            opCompletedEvent.WaitOne();

            // Check the status of the operation
            if (deploymentOperation.Status == AsyncStatus.Error)
            {
                DeploymentResult deploymentResult = deploymentOperation.GetResults();
                Console.WriteLine("Error code: {0}", deploymentOperation.ErrorCode);
                Console.WriteLine("Error text: {0}", deploymentResult.ErrorText);
                return false;
            }
            else if (deploymentOperation.Status == AsyncStatus.Canceled)
            {
                Console.WriteLine("Removal canceled");
            }
            else if (deploymentOperation.Status == AsyncStatus.Completed)
            {
                Console.WriteLine("Removal succeeded");
            }
            else
            {
                Console.WriteLine("Removal status unknown");
                return false;
            }
            return true;
        }
        private static void DisplayPackageInfo(Package package)
        {
            Console.WriteLine("Name: {0}", package.Id.Name);
            Console.WriteLine("FullName: {0}", package.Id.FullName);
            Console.WriteLine("Version: {0}.{1}.{2}.{3}", package.Id.Version.Major, package.Id.Version.Minor,
                package.Id.Version.Build, package.Id.Version.Revision);
            Console.WriteLine("Publisher: {0}", package.Id.Publisher);
            Console.WriteLine("PublisherId: {0}", package.Id.PublisherId);
            Console.WriteLine("Installed Location: {0}", package.InstalledLocation.Path);
            Console.WriteLine("IsFramework: {0}", package.IsFramework);
        }
    }
}

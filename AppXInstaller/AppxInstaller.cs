using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

using Ionic.Crc;
using Ionic.Zip;

using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;
using System.Security.Principal;
using System.IO;

namespace AppXInstaller
{
    class AppXManifest
    {
        public AppXManifest(string packagePath)
        {
            PackagePath = packagePath;

            XmlDocument manifest = new XmlDocument();
            
            using (ZipFile zip = ZipFile.Read(packagePath))
            {
                ZipEntry manifestFile = zip["AppxManifest.xml"];

                // If no manifest file is found, it might be a bundle
                if (manifestFile == null)
                {
                    string appxPath = String.Format("{0}.appx", Path.GetFileNameWithoutExtension(packagePath));
                    ZipEntry appxFile = zip[appxPath];
                    CrcCalculatorStream appxReader = appxFile.OpenReader();
                    byte[] buffer = new byte[appxReader.Length];

                    appxReader.Read(buffer, 0, (int)appxReader.Length);

                    ZipFile appxZip = ZipFile.Read(new MemoryStream(buffer));
                    manifestFile = appxZip["AppxManifest.xml"];
                }

                CrcCalculatorStream reader = manifestFile.OpenReader();
                manifest.Load(reader);
            }

            XmlElement package = manifest["Package"];
            XmlElement identity = package["Identity"];

            Name = identity.GetAttribute("Name");
            Publisher = identity.GetAttribute("Publisher");
            Version = identity.GetAttribute("Version");

            XmlElement framework = package["Properties"]["Framework"];
            if (framework != null)
            {
                IsFramework = Boolean.Parse(framework.InnerText);
            }

            Dependencies = new List<string>();
            XmlElement dependencies = package["Dependencies"];
            if (dependencies != null)
            {
                foreach (XmlElement dependency in dependencies)
                {
                    Dependencies.Add(dependency.GetAttribute("Name"));
                }
            }
        }

        public Dictionary<string, AppXManifest> ParseDepencyManifests()
        {
            Dictionary<string, AppXManifest> depenencyManifests = new Dictionary<string, AppXManifest>();

            // Search the relative package path for depenency bundles
            string directory = Path.GetDirectoryName(PackagePath);
            string[] packagePaths = Directory.GetFiles(directory, "*appx");

            foreach (string packagePath in packagePaths)
            {
                AppXManifest dependency = new AppXManifest(packagePath);
                if (dependency.IsFramework && !depenencyManifests.ContainsKey(dependency.Name))
                {
                    depenencyManifests.Add(dependency.Name, dependency);
                }
            }
            return depenencyManifests;
        }

        public string Name { private set; get; }
        public string Publisher { private set; get; }
        public string Version { private set; get; }
        public List<string> Dependencies { private set; get; }
        public string PackagePath { private set; get; }
        public bool IsFramework { private set; get; }
    }

    class AppxInstaller
    {
        private static PackageManager packageManager = new PackageManager();
        private static Dictionary<string, AppXManifest> depenencyManifests;

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

                depenencyManifests = manifest.ParseDepencyManifests();

                if (packages.Count() == 0)
                {
                    success &= InstallPackage(packagePath, manifest.Dependencies); 
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
                            success &= InstallPackage(packagePath, manifest.Dependencies);
                            break;
                        case '2':
                            success &= InstallPackage(packagePath, manifest.Dependencies, true);
                            break;
                        case '3':
                            success &= UninstallPackage(package.Id.FullName);
                            break;
                        case '4':
                            return;
                    }
                }

                // If an error has been detected, wait for a key press
                if (!success)
                {
                    Console.WriteLine("Press any key to continue...");
                    while (!Console.KeyAvailable) ;
                }
            }
            else
            {
                Console.WriteLine("To use this program, associate it with the .appx and .appxbundle file extensions.");
            }
        }

        private static bool InstallPackage(string inputPackageUri, List<string> dependencies = null, bool update = false)
        {
            Uri packageUri = new Uri(inputPackageUri);
            IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deploymentOperation;
            List<Uri> dependencyPackageUris = new List<Uri>();
            
            
            // Construct depenency URIs relative to the package path
            foreach (string dependency in dependencies)
            {
                if (!depenencyManifests.ContainsKey(dependency))
                {
                    Console.WriteLine("Error: Missing dependency \"{0}\"", dependency);
                    return false;
                }
                dependencyPackageUris.Add(new Uri(depenencyManifests[dependency].PackagePath));
            }

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
                Console.WriteLine(deploymentOperation.ErrorCode);
                Console.WriteLine(deploymentResult.ExtendedErrorCode);

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

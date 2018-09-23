using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("efec6e69-fc4a-4e21-86e6-4a261c12d4db")]

[assembly: Addin("OpenSim.ApplicationPlugins.RemoteController", OpenSim.VersionInfo.VersionNumber)]
[assembly: AddinDependency("OpenSim", OpenSim.VersionInfo.VersionNumber)]

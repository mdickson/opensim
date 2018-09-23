using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("c023816d-194e-40c1-9195-a0f281d4ac5d")]

[assembly: Addin("OpenSim.ApplicationPlugins.RegionModulesController", OpenSim.VersionInfo.VersionNumber)]
[assembly: AddinDependency("OpenSim", OpenSim.VersionInfo.VersionNumber)]

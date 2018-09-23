using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("84a3082d-3011-4c13-835c-c7d93f97ac79")]


[assembly: Addin("OpenSim.Region.OptionalModules", OpenSim.VersionInfo.VersionNumber)]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

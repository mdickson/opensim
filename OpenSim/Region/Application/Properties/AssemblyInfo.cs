using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("f6700ed5-1e6f-44d8-8397-e5eac42b3856")]

[assembly: AddinRoot("OpenSim", OpenSim.VersionInfo.VersionNumber)]
[assembly: ImportAddinAssembly("OpenSim.Framework.dll")]

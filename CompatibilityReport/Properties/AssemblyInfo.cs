﻿using System.Resources;
using System.Reflection;
using System.Runtime.InteropServices;
using CompatibilityReport.Util;

// General Information about an assembly is controlled through the following set of attributes.
// Change these attribute values to modify the information associated with an assembly.
[assembly: AssemblyTitle(ModSettings.ModName + " v" + ModSettings.Version + ModSettings.ReleaseType)]
[assembly: AssemblyDescription(ModSettings.IUserModDescription)]
[assembly: AssemblyConfiguration(ModSettings.ReleaseType)]
[assembly: AssemblyCompany(ModSettings.ModAuthor)]
[assembly: AssemblyProduct(ModSettings.ModName)]
[assembly: AssemblyCopyright("Copyright © " + ModSettings.CopyrightYear + " " + ModSettings.ModAuthor + " (MIT License)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible to COM components.
// If you need to access a type in this assembly from COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("e43b190e-c881-4b71-af47-d50d978d874c")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers by using the '*' as shown below:
// [assembly: AssemblyVersion("0.1.*")]
[assembly: AssemblyVersion(ModSettings.Version + "." + ModSettings.Build)]
[assembly: AssemblyFileVersion(ModSettings.Version + "." + ModSettings.Build)]
[assembly: NeutralResourcesLanguage("en")]

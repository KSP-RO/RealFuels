#define CIBUILD_disabled
using System.Reflection;
using System.Runtime.CompilerServices;

// Information about this assembly is defined by the following attributes.
// Change them to the values specific to your project.

[assembly: AssemblyTitle("RealFuels")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("")]
[assembly: AssemblyCopyright("NathanKell")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// The assembly version has the format "{Major}.{Minor}.{Build}.{Revision}".
// The form "{Major}.{Minor}.*" will automatically update the build and revision,
// and "{Major}.{Minor}.{Build}.*" will update just the revision.

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersionAttribute("@FULL_VERSION@")]
#if CIBUILD
[assembly: AssemblyFileVersion("@MAJOR@.@MINOR@.@PATCH@.@BUILD@")]
#else
[assembly: AssemblyFileVersion("14.6.0.0")]
#endif

// The following attributes are used to specify the signing key for the assembly,
// if desired. See the Mono documentation for more information about signing.

//[assembly: AssemblyDelaySign(false)]
//[assembly: AssemblyKeyFile("")]

[assembly: KSPAssembly("RealFuels", 1, 0, 0)]
[assembly: KSPAssemblyDependency("SolverEngines", 3, 3)]

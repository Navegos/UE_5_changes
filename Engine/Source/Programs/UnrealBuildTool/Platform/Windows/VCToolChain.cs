// Copyright Epic Games, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using EpicGames.Core;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using UnrealBuildBase;

namespace UnrealBuildTool
{
	class VCToolChain : ISPCToolChain
	{
		/// <summary>
		/// The target being built
		/// </summary>
		protected ReadOnlyTargetRules Target;

		/// <summary>
		/// The Visual C++ environment
		/// </summary>
		protected VCEnvironment EnvVars;

		/// <summary>
		/// Length of a path string that will trigger a build warning, as long paths may cause unexpected errors with the MSVC toolchain.
		/// </summary>
		private static int MaxPathWarningLength = 260;

		public VCToolChain(ReadOnlyTargetRules Target, ILogger Logger)
			: base(Logger)
		{
			this.Target = Target;
			EnvVars = Target.WindowsPlatform.Environment!;

			Logger.LogDebug("Compiler: {Path}", EnvVars.CompilerPath);
			Logger.LogDebug("Linker: {Path}", EnvVars.LinkerPath);
			Logger.LogDebug("Library Manager: {Path}", EnvVars.LibraryManagerPath);
			Logger.LogDebug("Resource Compiler: {Path}", EnvVars.ResourceCompilerPath);

			if (Target.WindowsPlatform.ObjSrcMapFile != null)
			{
				try
				{
					File.Delete(Target.WindowsPlatform.ObjSrcMapFile);
				}
				catch
				{
				}
			}
		}

		public override FileReference? GetCppCompilerPath()
		{
			return EnvVars.CompilerPath;
		}

		/// <summary>
		/// Prepares the environment for building
		/// </summary>
		public override void SetEnvironmentVariables()
		{
			EnvVars.SetEnvironmentVariables();

			// Don't allow the INCLUDE environment variable to propagate. It's set by the IDE based on the IncludePath property in the project files which we
			// add to improve Visual Studio memory usage, but we don't actually need it to set when invoking the compiler. Doing so results in it being converted
			// into /I arguments by the CL driver, which results in errors due to the command line not fitting into the PDB debug record.
			Environment.SetEnvironmentVariable("INCLUDE", null);

			// Don't allow the CL or _CL_ environment variable to propagate.
			Environment.SetEnvironmentVariable("CL", null);
			Environment.SetEnvironmentVariable("_CL_", null);

			// Don't allow the LIBPATH environment variable to propagate.
			Environment.SetEnvironmentVariable("LIBPATH", null);
		}

		/// <summary>
		/// Returns the version info for the toolchain. This will be output before building.
		/// </summary>
		/// <returns>String describing the current toolchain</returns>
		public override void GetVersionInfo(List<string> Lines)
		{
			if (EnvVars.Compiler == EnvVars.ToolChain)
			{
				Lines.Add($"Using {WindowsPlatform.GetCompilerName(EnvVars.Compiler)} {EnvVars.ToolChainVersion} toolchain ({EnvVars.ToolChainDir}) and Windows {EnvVars.WindowsSdkVersion} SDK ({EnvVars.WindowsSdkDir}).");
			}
			else if (EnvVars.Compiler == WindowsCompiler.Intel)
			{
				Lines.Add($"Using {WindowsPlatform.GetCompilerName(EnvVars.Compiler)} {EnvVars.CompilerVersion} compiler ({EnvVars.CompilerDir}) based on Clang {MicrosoftPlatformSDK.GetClangVersionForIntelCompiler(EnvVars.CompilerPath)} with {WindowsPlatform.GetCompilerName(EnvVars.ToolChain)} {EnvVars.ToolChainVersion} runtime ({EnvVars.ToolChainDir}) and Windows {EnvVars.WindowsSdkVersion} SDK ({EnvVars.WindowsSdkDir}).");
			}
			else
			{
				Lines.Add($"Using {WindowsPlatform.GetCompilerName(EnvVars.Compiler)} {EnvVars.CompilerVersion} compiler ({EnvVars.CompilerDir}) with {WindowsPlatform.GetCompilerName(EnvVars.ToolChain)} {EnvVars.ToolChainVersion} runtime ({EnvVars.ToolChainDir}) and Windows {EnvVars.WindowsSdkVersion} SDK ({EnvVars.WindowsSdkDir}).");
			}

			if (EnvVars.ToolChain == WindowsCompiler.VisualStudio2019)
			{
				Lines.Add($"Notice: {WindowsPlatform.GetCompilerName(WindowsCompiler.VisualStudio2019)} will no longer be supported for the installed engine in UE 5.3. Compiling code for Microsoft platforms will require {WindowsPlatform.GetCompilerName(WindowsCompiler.VisualStudio2022)} in UE 5.4 and beyond.");
			}
		}

		public override void GetExternalDependencies(HashSet<FileItem> ExternalDependencies)
		{
			ExternalDependencies.Add(FileItem.GetItemByFileReference(EnvVars.CompilerPath));
			ExternalDependencies.Add(FileItem.GetItemByFileReference(EnvVars.LinkerPath));
		}

		public static void AddDefinition(List<string> Arguments, string Definition)
		{
			// Split the definition into name and value
			int ValueIdx = Definition.IndexOf('=');
			if (ValueIdx == -1)
			{
				AddDefinition(Arguments, Definition, null);
			}
			else
			{
				AddDefinition(Arguments, Definition.Substring(0, ValueIdx), Definition.Substring(ValueIdx + 1));
			}
		}

		public static void AddDefinition(List<string> Arguments, string Variable, string? Value)
		{
			// If the value has a space in it and isn't wrapped in quotes, do that now
			if (Value != null && !Value.StartsWith("\"") && (Value.Contains(' ') || Value.Contains('$')))
			{
				Value = "\"" + Value + "\"";
			}

			if (Value != null)
			{
				Arguments.Add("/D" + Variable + "=" + Value);
			}
			else
			{
				Arguments.Add("/D" + Variable);
			}
		}

		private static void CheckCommandLinePathLength(string PathString)
		{
			if (!Path.IsPathRooted(PathString))
			{
				string ResolvedPath = Path.Combine(Unreal.EngineSourceDirectory.FullName, PathString);
				if (ResolvedPath.Length > MaxPathWarningLength)
				{
					Log.TraceWarningOnce($"Relative path '{PathString}' when resolved will have length '{ResolvedPath.Length}' which is greater than MAX_PATH (260) and may cause unexpected errors with the MSVC toolchain.");
				}
			}
			else if (PathString.Length > MaxPathWarningLength)
			{
				Log.TraceWarningOnce($"Absolute path '{PathString}' has length '{PathString.Length}' which is greater than MAX_PATH (260) and may cause unexpected errors with the MSVC toolchain.");
			}
		}

		public static new string NormalizeCommandLinePath(FileSystemReference Reference)
		{
			// Try to use a relative path to shorten command line length and to enable remote distribution where absolute paths are not desired
			if (Reference.IsUnderDirectory(Unreal.EngineDirectory))
			{
				string RelativePath = Reference.MakeRelativeTo(Unreal.EngineSourceDirectory);
				CheckCommandLinePathLength(RelativePath);
				return RelativePath;
			}

			CheckCommandLinePathLength(Reference.FullName);
			return Reference.FullName;
		}

		public static new string NormalizeCommandLinePath(FileItem Item)
		{
			return NormalizeCommandLinePath(Item.Location);
		}

		public static void AddSourceFile(List<string> Arguments, FileItem SourceFile)
		{
			string SourceFileString = NormalizeCommandLinePath(SourceFile);
			Arguments.Add(Utils.MakePathSafeToUseWithCommandLine(SourceFileString));
		}

		private static void AddIncludePath(List<string> Arguments, DirectoryReference IncludePath, WindowsCompiler Compiler, bool bSystemInclude)
		{
			// Try to use a relative path to shorten command line length.
			string IncludePathString = NormalizeCommandLinePath(IncludePath);

			if (Compiler.IsClang() && bSystemInclude)
			{
				// Clang has special treatment for system headers; only system include directories are searched when include directives use angle brackets,
				// and warnings are disabled to allow compiler toolchains to be upgraded separately.
				Arguments.Add("/imsvc " + Utils.MakePathSafeToUseWithCommandLine(IncludePathString));
			}
			else if (Compiler.IsMSVC() && Compiler >= WindowsCompiler.VisualStudio2022 && bSystemInclude)
			{
				if (!Arguments.Contains("/external:W0"))
				{
					Arguments.Add("/external:W0");
				}
				// Defines a root directory that contains external headers.
				Arguments.Add("/external:I " + Utils.MakePathSafeToUseWithCommandLine(IncludePathString));
			}
			else
			{
				Arguments.Add("/I " + Utils.MakePathSafeToUseWithCommandLine(IncludePathString));
			}
		}

		public static void AddIncludePath(List<string> Arguments, DirectoryReference IncludePath, WindowsCompiler Compiler)
		{
			AddIncludePath(Arguments, IncludePath, Compiler, false);
		}

		public static void AddSystemIncludePath(List<string> Arguments, DirectoryReference IncludePath, WindowsCompiler Compiler)
		{
			AddIncludePath(Arguments, IncludePath, Compiler, true);
		}

		public static void AddForceIncludeFile(List<string> Arguments, FileItem ForceIncludeFile)
		{
			string ForceIncludeFileString = NormalizeCommandLinePath(ForceIncludeFile);
			Arguments.Add($"/FI\"{ForceIncludeFileString}\"");
		}

		public static void AddCreatePchFile(List<string> Arguments, FileItem PchThroughHeaderFile, FileItem CreatePchFile)
		{
			string PchThroughHeaderFilePath = NormalizeCommandLinePath(PchThroughHeaderFile);
			string CreatePchFilePath = NormalizeCommandLinePath(CreatePchFile);
			Arguments.Add($"/Yc\"{PchThroughHeaderFilePath}\"");
			Arguments.Add($"/Fp\"{CreatePchFilePath}\"");
		}

		public static void AddUsingPchFile(List<string> Arguments, FileItem PchThroughHeaderFile, FileItem UsingPchFile)
		{
			string PchThroughHeaderFilePath = NormalizeCommandLinePath(PchThroughHeaderFile);
			string UsingPchFilePath = NormalizeCommandLinePath(UsingPchFile);
			Arguments.Add($"/Yu\"{PchThroughHeaderFilePath}\"");
			Arguments.Add($"/Fp\"{UsingPchFilePath}\"");
		}

		public static void AddPreprocessedFile(List<string> Arguments, FileItem PreprocessedFile, ILogger Logger)
		{
			string PreprocessedFileString = NormalizeCommandLinePath(PreprocessedFile);
			Arguments.Add("/P"); // Preprocess
			Arguments.Add("/C"); // Preserve comments when preprocessing
			Arguments.Add($"/Fi\"{PreprocessedFileString}\""); // Preprocess to a file

			// this is parsed by external tools wishing to open this file directly.
			Logger.LogInformation("PreProcessPath: {Path}", PreprocessedFile);
		}

		public static void AddObjectFile(List<string> Arguments, FileItem ObjectFile)
		{
			string ObjectFileString = NormalizeCommandLinePath(ObjectFile);
			Arguments.Add($"/Fo\"{ObjectFileString}\"");
		}

		public static void AddAssemblyFile(List<string> Arguments, FileItem AssemblyFile)
		{
			string AssemblyFileString = NormalizeCommandLinePath(AssemblyFile);
			Arguments.Add("/FAs");// Write out an assembly file (.asm) with the c++ code embedded in it via comments
			Arguments.Add($"/Fa\"{AssemblyFileString}\""); // Set the output patj for the asm file
		}

		public static void AddAnalyzeLogFile(List<string> Arguments, FileItem AnalyzeLogFile)
		{
			string AnalyzeLogFileString = NormalizeCommandLinePath(AnalyzeLogFile);
			Arguments.Add("/analyze:log " + Utils.MakePathSafeToUseWithCommandLine(AnalyzeLogFileString));
		}

		public static void AddExperimentalLogFile(List<string> Arguments, FileItem ExperimentalLogFile)
		{
			string ExperimentalLogFileString = NormalizeCommandLinePath(ExperimentalLogFile);
			Arguments.Add("/experimental:log " + Utils.MakePathSafeToUseWithCommandLine(ExperimentalLogFileString));
		}

		public static void AddSourceDependenciesFile(List<string> Arguments, FileItem SourceDependenciesFile)
		{
			string SourceDependenciesFileString = NormalizeCommandLinePath(SourceDependenciesFile);
			Arguments.Add("/sourceDependencies " + Utils.MakePathSafeToUseWithCommandLine(SourceDependenciesFileString));
		}

		public static void AddSourceDependsFile(List<string> Arguments, FileItem SourceDependsFile)
		{
			string SourceDependsFileString = NormalizeCommandLinePath(SourceDependsFile);
			Arguments.Add($"/clang:-MD /clang:-MF\"{SourceDependsFileString}\"");
		}

		protected virtual void AppendCLArguments_Global(CppCompileEnvironment CompileEnvironment, List<string> Arguments)
		{
			// Suppress generation of object code for unreferenced inline functions. Enabling this option is more standards compliant, and causes a big reduction
			// in object file sizes (and link times) due to the amount of stuff we inline.
			Arguments.Add("/Zc:inline");

			// @todo clang: Clang on Windows doesn't respect "#pragma warning (error: ####)", and we're not passing "/WX", so warnings are not
			// treated as errors when compiling on Windows using Clang right now.

			// NOTE re: clang: the arguments for clang-cl can be found at http://llvm.org/viewvc/llvm-project/cfe/trunk/include/clang/Driver/CLCompatOptions.td?view=markup
			// This will show the cl.exe options that map to clang.exe ones, which ones are ignored and which ones are unsupported.
			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				// Sync the compatibility version with the MSVC toolchain version (14.xx which maps to advertised
				// compiler version of 19.xx).
				Arguments.Add($"-fms-compatibility-version=19.{EnvVars.ToolChainVersion.GetComponent(1)}");

				if (Target.StaticAnalyzer == StaticAnalyzer.Default && CompileEnvironment.PrecompiledHeaderAction != PrecompiledHeaderAction.Create && !CompileEnvironment.bDisableStaticAnalysis)
				{
					Arguments.Add("-Wno-unused-command-line-argument");

					// Enable the static analyzer with default checks.
					Arguments.Add("--analyze");

					// Deprecated in LLVM 15
					if (EnvVars.CompilerVersion <= new VersionNumber(14))
					{
						// Make sure we check inside nested blocks (e.g. 'if ((foo = getchar()) == 0) {}')
						Arguments.Add("-Xclang -analyzer-opt-analyze-nested-blocks");
					}

					// Write out a pretty web page with navigation to understand how the analysis was derived if HTML is enabled.
					Arguments.Add($"-Xclang -analyzer-output={Target.StaticAnalyzerOutputType.ToString().ToLowerInvariant()}");

					// Needed for some of the C++ checkers.
					Arguments.Add("-Xclang -analyzer-config -Xclang aggressive-binary-operation-simplification=true");

					// If writing to HTML, use the source filename as a basis for the report filename. 
					Arguments.Add("-Xclang -analyzer-config -Xclang stable-report-filename=true");
					Arguments.Add("-Xclang -analyzer-config -Xclang report-in-main-source-file=true");
					Arguments.Add("-Xclang -analyzer-config -Xclang path-diagnostics-alternate=true");

					// Run shallow analyze if requested.
					if (Target.StaticAnalyzerMode == StaticAnalyzerMode.Shallow)
					{
						Arguments.Add("-Xclang -analyzer-config -Xclang mode=shallow");
					}

					if (CompileEnvironment.StaticAnalyzerCheckers.Count > 0)
					{
						// Disable all default checks
						Arguments.Add("--analyzer-no-default-checks");

						// Only enable specific checks.
						foreach (string Checker in CompileEnvironment.StaticAnalyzerCheckers)
						{
							Arguments.Add($"-Xclang -analyzer-checker -Xclang {Checker}");
						}
					}
					else
					{
						// Disable default checks.
						foreach (string Checker in CompileEnvironment.StaticAnalyzerDisabledCheckers)
						{
							Arguments.Add($"-Xclang -analyzer-disable-checker -Xclang {Checker}");
						}
						// Enable additional non-default checks.
						foreach (string Checker in CompileEnvironment.StaticAnalyzerAdditionalCheckers)
						{
							Arguments.Add($"-Xclang -analyzer-checker -Xclang {Checker}");
						}
					}
				}
				else if (Target.StaticAnalyzer == StaticAnalyzer.Default && CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
				{
					AddDefinition(Arguments, "__clang_analyzer__");
				}
			}
			else if (Target.StaticAnalyzer == StaticAnalyzer.Default && !CompileEnvironment.bDisableStaticAnalysis)
			{
				Arguments.Add("/analyze");

				if (CompileEnvironment.bStaticAnalyzerExtensions)
				{
					FileReference EspxEngine = FileReference.Combine(EnvVars.CompilerPath.Directory, "EspXEngine.dll");
					Arguments.Add($"/analyze:plugin\"{NormalizeCommandLinePath(EspxEngine)}\"");
				}

				foreach (FileReference Ruleset in CompileEnvironment.StaticAnalyzerRulesets)
				{
					Arguments.Add($"/analyze:ruleset\"{NormalizeCommandLinePath(Ruleset)}\"");
				}

				if (Target.WindowsPlatform.Compiler >= WindowsCompiler.VisualStudio2022)
				{
					// Ignore warnings in external headers
					Arguments.Add("/analyze:external-");
				}

				// Report functions that use a LOT of stack space. You can lower this value if you
				// want more aggressive checking for functions that use a lot of stack memory.
				Arguments.Add("/analyze:stacksize" + CompileEnvironment.AnalyzeStackSizeWarning);

				// Don't bother generating code, only analyze code (may report fewer warnings though.)
				//Arguments.Add("/analyze:only");

				// Re-evalulate new analysis warnings at a later time
				if (EnvVars.CompilerVersion >= new VersionNumber(14, 32))
				{
					Arguments.Add("/wd6031"); // return value ignored: called-function could return unexpected value
				}
			}

			// Prevents the compiler from displaying its logo for each invocation.
			Arguments.Add("/nologo");

			// Enable intrinsic functions.
			Arguments.Add("/Oi");

			// Trace includes
			if (Target.WindowsPlatform.bShowIncludes)
			{
				if (Target.WindowsPlatform.Compiler.IsClang())
				{
					Arguments.Add("/clang:--trace-includes");
				}
				else if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					Arguments.Add("/showIncludes");
				}
			}

			// Print absolute paths in diagnostics
			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				Arguments.Add("-fdiagnostics-absolute-paths");
			}
			else if (Target.WindowsPlatform.Compiler.IsMSVC())
			{
				Arguments.Add("/FC");
			}

			if (Target.WindowsPlatform.Compiler.IsClang() && Target.Platform.IsInGroup(UnrealPlatformGroup.Windows) && CompileEnvironment.Architecture == UnrealArch.X64)
			{
				// Tell the Clang compiler to generate 64-bit code
				Arguments.Add("--target=x86_64-pc-windows-msvc");

				// UE5 minspec is 4.2
				Arguments.Add("-msse4.2");

				// DevelVitorF pushing it a bit further
				// Not all the new Cpu's have AVX512- enabled, playing it safe and refine the cpu specs. // Dont mix engine modules/plugins mixed specs, it will break at some point.
				if (CompileEnvironment.MinCpuArchX64 >= MinimumCpuArchitectureX64.AVX2)
				{
					Arguments.Add("-mavx -mbmi -mavx2");

					// AVX available implies sse4 and sse2 available.
					// Inform Unreal code that we have sse2, sse4, and AVX, both available to compile and available to run
					// By setting the ALWAYS_HAS defines, we we direct Unreal code to skip cpuid checks to verify that the running hardware supports sse/avx.
					AddDefinition(Arguments, "PLATFORM_ENABLE_VECTORINTRINSICS=1");
					AddDefinition(Arguments, "PLATFORM_PERFORMANT_TYPES=1");
					AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_2=1");
					AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_AVX=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX_2=1");
					AddDefinition(Arguments, "PLATFORM_SUPPORTS_AVX=1");
					AddDefinition(Arguments, "PLATFORM_SUPPORTS_AVX_2=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_FMA3=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_F16C=1");
					// Define /arch:AVX for the current compilation unit.  Machines without AVX support will crash on any SSE/AVX instructions if they run this compilation unit.
					if (CompileEnvironment.MinCpuArchX64 == MinimumCpuArchitectureX64.AVX512)
					{
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Znver4:
								Arguments.Add("/clang:-march=znver4 /clang:-mtune=znver4 -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							// This processors requires CPU checks, most of UE required AVX512 CPU features are not supported at all in this Intel processors.
							/*case DefaultCpuArchitectureX64.Knl:
								Arguments.Add("/clang:-march=knl /clang:-mtune=knl -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								break;
							case DefaultCpuArchitectureX64.Knm:
								Arguments.Add("/clang:-march=knm /clang:-mtune=knm -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								break;*/
							case DefaultCpuArchitectureX64.Skylake_avx512:
								Arguments.Add("/clang:-march=skylake-avx512 /clang:-mtune=skylake-avx512 -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Cannonlake:
								Arguments.Add("/clang:-march=cannonlake /clang:-mtune=cannonlake -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Icelake_client:
								Arguments.Add("/clang:-march=icelake-client /clang:-mtune=icelake-client -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Icelake_server:
								Arguments.Add("/clang:-march=icelake-server /clang:-mtune=icelake-server -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Cascadelake:
								Arguments.Add("/clang:-march=cascadelake /clang:-mtune=cascadelake -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Cooperlake:
								Arguments.Add("/clang:-march=cooperlake /clang:-mtune=cooperlake -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Tigerlake:
								Arguments.Add("/clang:-march=tigerlake /clang:-mtune=tigerlake -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Sapphirerapids:
								Arguments.Add("/clang:-march=sapphirerapids /clang:-mtune=sapphirerapids -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_FP16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							/*case DefaultCpuArchitectureX64.Alderlake_avx512:*/ // Intel fused of AVX512. Removed.
							case DefaultCpuArchitectureX64.Rocketlake:
								Arguments.Add("/clang:-march=rocketlake /clang:-mtune=rocketlake -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Graniterapids:
								Arguments.Add("/clang:-march=graniterapids /clang:-mtune=graniterapids -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_FP16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							case DefaultCpuArchitectureX64.Graniterapids_d:
								Arguments.Add("/clang:-march=graniterapids-d /clang:-mtune=graniterapids-d -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_FP16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								Arguments.Add("-mavx512f -mavx512vl -mavx512bw -mavx512dq -mavx512cd");
								break;
							default:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=512 -mllvm -force-vector-width=16 -mllvm -force-vector-interleave=8");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
						}
					}
					else
					{
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Haswell:
								Arguments.Add("/clang:-march=haswell /clang:-mtune=haswell -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Broadwell:
								Arguments.Add("/clang:-march=broadwell /clang:-mtune=broadwell -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Skylake:
								Arguments.Add("/clang:-march=skylake /clang:-mtune=skylake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Alderlake:
								Arguments.Add("/clang:-march=alderlake /clang:-mtune=alderlake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Sierraforest:
								Arguments.Add("/clang:-march=sierraforest /clang:-mtune=sierraforest -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Grandridge:
								Arguments.Add("/clang:-march=grandridge /clang:-mtune=grandridge -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Bdver4:
								Arguments.Add("/clang:-march=bdver4 /clang:-mtune=bdver4 -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Znver1:
								Arguments.Add("/clang:-march=znver1 /clang:-mtune=znver1 -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Znver2:
								Arguments.Add("/clang:-march=znver2 /clang:-mtune=znver2 -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Znver3:
								Arguments.Add("/clang:-march=znver3 /clang:-mtune=znver3 -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Znver4:
								Arguments.Add("/clang:-march=znver4 /clang:-mtune=znver4 -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								break;
							/*case DefaultCpuArchitectureX64.Knl: // Removed, incompatible for use with accelerators processors
								Arguments.Add("/clang:-march=knl /clang:-mtune=knl -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
							case DefaultCpuArchitectureX64.Knm:
								Arguments.Add("/clang:-march=knm /clang:-mtune=knm -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;*/
							case DefaultCpuArchitectureX64.Skylake_avx512:
								Arguments.Add("/clang:-march=skylake-avx512 /clang:-mtune=skylake-avx512 -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1");
								break;
							case DefaultCpuArchitectureX64.Cannonlake:
								Arguments.Add("/clang:-march=cannonlake /clang:-mtune=cannonlake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1");
								break;
							case DefaultCpuArchitectureX64.Icelake_client:
								Arguments.Add("/clang:-march=icelake-client /clang:-mtune=icelake-client -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Icelake_server:
								Arguments.Add("/clang:-march=icelake-server /clang:-mtune=icelake-server -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Cascadelake:
								Arguments.Add("/clang:-march=cascadelake /clang:-mtune=cascadelake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1");
								break;
							case DefaultCpuArchitectureX64.Cooperlake:
								Arguments.Add("/clang:-march=cooperlake /clang:-mtune=cooperlake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								break;
							case DefaultCpuArchitectureX64.Tigerlake:
								Arguments.Add("/clang:-march=tigerlake /clang:-mtune=tigerlake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								break;
							case DefaultCpuArchitectureX64.Sapphirerapids:
								Arguments.Add("/clang:-march=sapphirerapids /clang:-mtune=sapphirerapids -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								break;
							/*case DefaultCpuArchitectureX64.Alderlake_avx512:*/ // Intel fused of AVX512. Removed.
							case DefaultCpuArchitectureX64.Rocketlake:
								Arguments.Add("/clang:-march=rocketlake /clang:-mtune=rocketlake -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Graniterapids:
								Arguments.Add("/clang:-march=graniterapids /clang:-mtune=graniterapids -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								break;
							case DefaultCpuArchitectureX64.Graniterapids_d:
								Arguments.Add("/clang:-march=graniterapids-d /clang:-mtune=graniterapids-d -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								break;
							default:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=256 -mllvm -force-vector-width=8 -mllvm -force-vector-interleave=4");
								break;
						}
					}

					if (Target.WindowsPlatform.Compiler == WindowsCompiler.Intel)
					{
						Arguments.Add("/Qfma");
					}
				}
				else
				{
					AddDefinition(Arguments, "PLATFORM_ENABLE_VECTORINTRINSICS=1");
					AddDefinition(Arguments, "PLATFORM_PERFORMANT_TYPES=1");
					AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_2=1");
					if (CompileEnvironment.MinCpuArchX64 >= MinimumCpuArchitectureX64.AVX)
					{
						// Apparently MSVC enables (a subset?) of BMI (bit manipulation instructions) when /arch:AVX is set. Some code relies on this, so mirror it by enabling BMI1
						Arguments.Add("-mavx -mbmi");

						AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_AVX=1");
						AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX=1");
						AddDefinition(Arguments, "PLATFORM_SUPPORTS_AVX=1");
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Sandybridge:
								Arguments.Add("/clang:-march=sandybridge /clang:-mtune=sandybridge -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Ivybridge:
								Arguments.Add("/clang:-march=ivybridge /clang:-mtune=ivybridge -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1");
								break;
							case DefaultCpuArchitectureX64.Btver2:
								Arguments.Add("/clang:-march=btver2 /clang:-mtune=btver2 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1");
								break;
							case DefaultCpuArchitectureX64.Bdver1:
								Arguments.Add("/clang:-march=bdver1 /clang:-mtune=bdver1 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Bdver2:
								Arguments.Add("/clang:-march=bdver2 /clang:-mtune=bdver2 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_ALWAYS_HAS_FMA3=1");
								break;
							case DefaultCpuArchitectureX64.Bdver3:
								Arguments.Add("/clang:-march=bdver3 /clang:-mtune=bdver3 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_ALWAYS_HAS_FMA3=1");
								break;
							default:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
						}
					}
					else
					{
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Nehalem:
								Arguments.Add("/clang:-march=nehalem /clang:-mtune=nehalem -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Westmere:
								Arguments.Add("/clang:-march=westmere /clang:-mtune=westmere -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Sandybridge:
								Arguments.Add("/clang:-march=sandybridge /clang:-mtune=sandybridge -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Ivybridge:
								Arguments.Add("/clang:-march=ivybridge /clang:-mtune=ivybridge -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Silvermont:
								Arguments.Add("/clang:-march=silvermont /clang:-mtune=silvermont -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Goldmont:
								Arguments.Add("/clang:-march=goldmont /clang:-mtune=goldmont -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Goldmont_plus:
								Arguments.Add("/clang:-march=goldmont-plus /clang:-mtune=goldmont-plus -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Tremont:
								Arguments.Add("/clang:-march=tremont /clang:-mtune=tremont -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
							case DefaultCpuArchitectureX64.Btver2:
								Arguments.Add("/clang:-march=btver2 /clang:-mtune=btver2 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Bdver1:
								Arguments.Add("/clang:-march=bdver1 /clang:-mtune=bdver1 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Bdver2:
								Arguments.Add("/clang:-march=bdver2 /clang:-mtune=bdver2 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_ALWAYS_HAS_FMA3=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Bdver3:
								Arguments.Add("/clang:-march=bdver3 /clang:-mtune=bdver3 -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_ALWAYS_HAS_FMA3=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							default:
								Arguments.Add("/clang:-march=native /clang:-mtune=native -Xclang -mprefer-vector-width=128 -mllvm -force-vector-width=4 -mllvm -force-vector-interleave=2");
								break;
						}
					}
				}

				if ((CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Nehalem && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Tremont) || (CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Haswell && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Grandridge) || (CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Skylake_avx512 && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Graniterapids_d))
				{
					AddDefinition(Arguments, "PLATFORM_FAVOR_INTEL=1");
				}
				else if ((CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Btver2 && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Bdver3) || (CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Bdver4 && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Znver4))
				{
					AddDefinition(Arguments, "PLATFORM_FAVOR_AMD=1");
				}

				// Use tpause on supported processors.
				Arguments.Add("-mwaitpkg");

				// This matches Intel oneAPI clang compiler
				//if (EnvVars.CompilerVersion >= 202110)
				//{
				//	AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SVML=1");
				//}

				if (Target.WindowsPlatform.Compiler == WindowsCompiler.Intel)
				{
					Arguments.Add("/Qimf-precision:high");
					Arguments.Add("/Qimf-use-svml:true");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SVML=1");
				}
			}

			if (Target.WindowsPlatform.Compiler.IsMSVC() && Target.Platform.IsInGroup(UnrealPlatformGroup.Windows) && CompileEnvironment.Architecture == UnrealArch.X64)
			{
				// DevelVitorF pushing it a bit further
				if (CompileEnvironment.MinCpuArchX64 >= MinimumCpuArchitectureX64.AVX2)
				{
					// AVX available implies sse4 and sse2 available.
					// Inform Unreal code that we have sse2, sse4, and AVX, both available to compile and available to run
					// By setting the ALWAYS_HAS defines, we we direct Unreal code to skip cpuid checks to verify that the running hardware supports sse/avx.
					AddDefinition(Arguments, "PLATFORM_ENABLE_VECTORINTRINSICS=1");
					AddDefinition(Arguments, "PLATFORM_PERFORMANT_TYPES=1");
					AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_2=1");
					AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_AVX=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX_2=1");
					AddDefinition(Arguments, "PLATFORM_SUPPORTS_AVX=1");
					AddDefinition(Arguments, "PLATFORM_SUPPORTS_AVX_2=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_FMA3=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_F16C=1");
					// Define /arch:AVX for the current compilation unit.  Machines without AVX support will crash on any SSE/AVX instructions if they run this compilation unit.
					if (CompileEnvironment.MinCpuArchX64 == MinimumCpuArchitectureX64.AVX512)
					{
						Arguments.Add("/arch:AVX512");
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Znver4:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								break;
							// This processors requires CPU checks, most of UE required AVX512 CPU features are not supported at all in this Intel processors.
							/*case DefaultCpuArchitectureX64.Knl:
								break;
							case DefaultCpuArchitectureX64.Knm:
								break;*/
							case DefaultCpuArchitectureX64.Skylake_avx512:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Cannonlake:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Icelake_client:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Icelake_server:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Cascadelake:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Cooperlake:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Tigerlake:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
							case DefaultCpuArchitectureX64.Sapphirerapids:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_FP16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								break;
							/*case DefaultCpuArchitectureX64.Alderlake_avx512:*/ // Intel fused of AVX512. Removed.
							case DefaultCpuArchitectureX64.Rocketlake:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Graniterapids:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_FP16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VP2INTERSECT=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								break;
							case DefaultCpuArchitectureX64.Graniterapids_d:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_AVX_512=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VL=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BW=1 /DPLATFORM_ALWAYS_HAS_AVX_512_DQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_CD=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VNNI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VBMI2=1 /DPLATFORM_ALWAYS_HAS_AVX_512_IFMA=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BITALG=1 /DPLATFORM_ALWAYS_HAS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_ALWAYS_HAS_AVX_512_BF16=1 /DPLATFORM_ALWAYS_HAS_AVX_512_FP16=1");
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								break;
							default:
								Arguments.Add("/DPLATFORM_REQUIRES_AVX_512_CHECKS=1");
								break;
						}
					}
					else
					{
						Arguments.Add("/arch:AVX2");
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								break;
							case DefaultCpuArchitectureX64.Haswell:
								break;
							case DefaultCpuArchitectureX64.Broadwell:
								break;
							case DefaultCpuArchitectureX64.Skylake:
								break;
							case DefaultCpuArchitectureX64.Alderlake:
								break;
							case DefaultCpuArchitectureX64.Sierraforest:
								break;
							case DefaultCpuArchitectureX64.Grandridge:
								break;
							case DefaultCpuArchitectureX64.Bdver4:
								break;
							case DefaultCpuArchitectureX64.Znver1:
								break;
							case DefaultCpuArchitectureX64.Znver2:
								break;
							case DefaultCpuArchitectureX64.Znver3:
								break;
							case DefaultCpuArchitectureX64.Znver4:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								break;
							/*case DefaultCpuArchitectureX64.Knl: // Removed, incompatible for use with accelerators processors
								break;
							case DefaultCpuArchitectureX64.Knm:
								break;*/
							case DefaultCpuArchitectureX64.Skylake_avx512:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1");
								break;
							case DefaultCpuArchitectureX64.Cannonlake:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1");
								break;
							case DefaultCpuArchitectureX64.Icelake_client:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Icelake_server:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Cascadelake:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1");
								break;
							case DefaultCpuArchitectureX64.Cooperlake:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1");
								break;
							case DefaultCpuArchitectureX64.Tigerlake:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								break;
							case DefaultCpuArchitectureX64.Sapphirerapids:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								break;
							/*case DefaultCpuArchitectureX64.Alderlake_avx512:*/ // Intel fused of AVX512. Removed.
							case DefaultCpuArchitectureX64.Rocketlake:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1");
								break;
							case DefaultCpuArchitectureX64.Graniterapids:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1 /DPLATFORM_SUPPORTS_AVX_512_VP2INTERSECT=1");
								break;
							case DefaultCpuArchitectureX64.Graniterapids_d:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX_512=1 /DPLATFORM_SUPPORTS_AVX_512_VL=1 /DPLATFORM_SUPPORTS_AVX_512_BW=1 /DPLATFORM_SUPPORTS_AVX_512_DQ=1 /DPLATFORM_SUPPORTS_AVX_512_CD=1 /DPLATFORM_SUPPORTS_AVX_512_VNNI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI=1 /DPLATFORM_SUPPORTS_AVX_512_VBMI2=1 /DPLATFORM_SUPPORTS_AVX_512_IFMA=1 /DPLATFORM_SUPPORTS_AVX_512_BITALG=1 /DPLATFORM_SUPPORTS_AVX_512_VPOPCNTDQ=1 /DPLATFORM_SUPPORTS_AVX_512_BF16=1 /DPLATFORM_SUPPORTS_AVX_512_FP16=1");
								break;
							default:
								break;
						}
					}
				}
				else
				{
					AddDefinition(Arguments, "PLATFORM_ENABLE_VECTORINTRINSICS=1");
					AddDefinition(Arguments, "PLATFORM_PERFORMANT_TYPES=1");
					AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_1=1");
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SSE4_2=1");
					if (CompileEnvironment.MinCpuArchX64 >= MinimumCpuArchitectureX64.AVX)
					{
						Arguments.Add("/arch:AVX");
						AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_AVX=1");
						AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX=1");
						AddDefinition(Arguments, "PLATFORM_SUPPORTS_AVX=1");
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								break;
							case DefaultCpuArchitectureX64.Sandybridge:
								break;
							case DefaultCpuArchitectureX64.Ivybridge:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1");
								break;
							case DefaultCpuArchitectureX64.Btver2:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1");
								break;
							case DefaultCpuArchitectureX64.Bdver1:
								break;
							case DefaultCpuArchitectureX64.Bdver2:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_ALWAYS_HAS_FMA3=1");
								break;
							case DefaultCpuArchitectureX64.Bdver3:
								Arguments.Add("/DPLATFORM_ALWAYS_HAS_F16C=1 /DPLATFORM_ALWAYS_HAS_FMA3=1");
								break;
							default:
								break;
						}
					}
					else
					{
						Arguments.Add("/arch:SSE2");
						switch (CompileEnvironment.DefaultCpuArchX64)
						{
							case DefaultCpuArchitectureX64.Native:
								break;
							case DefaultCpuArchitectureX64.Nehalem:
								break;
							case DefaultCpuArchitectureX64.Westmere:
								break;
							case DefaultCpuArchitectureX64.Sandybridge:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Ivybridge:
								Arguments.Add("/DPLATFORM_SUPPORTS_HAS_F16C=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Silvermont:
								break;
							case DefaultCpuArchitectureX64.Goldmont:
								break;
							case DefaultCpuArchitectureX64.Goldmont_plus:
								break;
							case DefaultCpuArchitectureX64.Tremont:
								break;
							case DefaultCpuArchitectureX64.Btver2:
								Arguments.Add("/DPLATFORM_SUPPORTS_HAS_F16C=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Bdver1:
								Arguments.Add("/DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Bdver2:
								Arguments.Add("/DPLATFORM_SUPPORTS_HAS_F16C=1 /DPLATFORM_SUPPORTS_HAS_FMA3=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							case DefaultCpuArchitectureX64.Bdver3:
								Arguments.Add("/DPLATFORM_SUPPORTS_HAS_F16C=1 /DPLATFORM_SUPPORTS_HAS_FMA3=1 /DPLATFORM_SUPPORTS_AVX=1");
								break;
							default:
								break;
						}
					}
				}

				if ((CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Nehalem && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Tremont) || (CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Haswell && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Grandridge) || (CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Skylake_avx512 && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Graniterapids_d))
				{
					Arguments.Add("/favor:INTEL64");
					AddDefinition(Arguments, "PLATFORM_FAVOR_INTEL=1");
				}
				else if ((CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Btver2 && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Bdver3) || (CompileEnvironment.DefaultCpuArchX64 >= DefaultCpuArchitectureX64.Bdver4 && CompileEnvironment.DefaultCpuArchX64 <= DefaultCpuArchitectureX64.Znver4))
				{
					Arguments.Add("/favor:AMD64");
					AddDefinition(Arguments, "PLATFORM_FAVOR_AMD=1");
					//	Arguments.Add("/d2vzeroupper-");  // DevelVitorF probably poor performance with Intel arch.
				}

				if (EnvVars.CompilerVersion >= new VersionNumber(14, 20, 0) && Target.WindowsPlatform.Compiler >= WindowsCompiler.VisualStudio2019)
				{
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_SVML=1");
				}
			}

			// Compile into an .obj file, and skip linking.
			Arguments.Add("/c");

			// Put symbols into different sections so the linker can remove them.
			if (Target.WindowsPlatform.bOptimizeGlobalData)
			{
				Arguments.Add("/Gw");
			}

			// Separate functions for linker.
			Arguments.Add("/Gy");

			// Microsoft recommends not passing /Zm except in very limited circumstances, please see:
			// https://learn.microsoft.com/en-us/cpp/build/reference/zm-specify-precompiled-header-memory-allocation-limit
			if (Target.WindowsPlatform.PCHMemoryAllocationFactor > 0)
			{
				Arguments.Add($"/Zm{Target.WindowsPlatform.PCHMemoryAllocationFactor}");
			}

			// Fix Incredibuild errors with helpers using heterogeneous character sets
			Arguments.Add("/utf-8");

			// Disable "The file contains a character that cannot be represented in the current code page" warning for non-US windows.
			Arguments.Add("/wd4819");

			// Disable Microsoft extensions on VS2017+ for improved standards compliance.
			if (Target.WindowsPlatform.Compiler.IsMSVC())
			{
				if (Target.WindowsPlatform.bStrictConformanceMode)
				{
					// This define is needed to ensure that MSVC static analysis mode doesn't declare attributes that are incompatible with strict conformance mode
					AddDefinition(Arguments, "SAL_NO_ATTRIBUTE_DECLARATIONS=1");

					Arguments.Add("/permissive-");
					Arguments.Add("/Zc:strictStrings-"); // Have to disable strict const char* semantics due to Windows headers not being compliant.
					if (EnvVars.CompilerVersion >= new VersionNumber(14, 32) && EnvVars.CompilerVersion < new VersionNumber(14, 33, 31629))
					{
						Arguments.Add("/Zc:lambda-");
					}
				}
				else
				{
					Arguments.Add("/Zc:hiddenFriend");
				}

				if (Target.WindowsPlatform.bUpdatedCPPMacro)
				{
					Arguments.Add("/Zc:__cplusplus");
				}

				if (Target.WindowsPlatform.bStrictInlineConformance)
				{
					Arguments.Add("/Zc:inline");
				}

				if (Target.WindowsPlatform.bStrictPreprocessorConformance)
				{
					Arguments.Add("/Zc:preprocessor");
				}

				if (Target.WindowsPlatform.bStrictEnumTypesConformance && EnvVars.CompilerVersion >= new VersionNumber(14, 34, 31931))
				{
					Arguments.Add("/Zc:enumTypes");
				}
			}

			// @todo HoloLens: UE is non-compliant when it comes to use of %s and %S
			// Previously %s meant "the current character set" and %S meant "the other one".
			// Now %s means multibyte and %S means wide. %Ts means "natural width".
			// Reverting this behaviour until the UE source catches up.
			AddDefinition(Arguments, "_CRT_STDIO_LEGACY_WIDE_SPECIFIERS=1");

			// @todo HoloLens: Silence the hash_map deprecation errors for now. This should be replaced with unordered_map for the real fix.
			AddDefinition(Arguments, "_SILENCE_STDEXT_HASH_DEPRECATION_WARNINGS=1");

			// Ignore secure CRT warnings on Clang
			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				AddDefinition(Arguments, "_CRT_SECURE_NO_WARNINGS");
			}

			// If compiling as a DLL, set the relevant defines
			if (CompileEnvironment.bIsBuildingDLL)
			{
				AddDefinition(Arguments, "_WINDLL");
			}

			// Maintain the old std::aligned_storage behavior from VS from v15.8 onwards, in case of prebuilt third party libraries are reliant on it
			AddDefinition(Arguments, "_DISABLE_EXTENDED_ALIGNED_STORAGE");

			// Do not allow inline method expansion if E&C support is enabled or inline expansion has been disabled
			if (!CompileEnvironment.bSupportEditAndContinue && CompileEnvironment.bUseInlining)
			{
				Arguments.Add($"/Ob{Math.Clamp(Target.WindowsPlatform.InlineFunctionExpansionLevel, 1, 3)}");
			}
			else
			{
				// Specifically disable inline expansion to override /O1,/O2/ or /Ox if set
				Arguments.Add("/Ob0");
			}

			// Deterministic compile support
			if (CompileEnvironment.bDeterministic)
			{
				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					Arguments.Add("/experimental:deterministic");

					// warning C5049: Embedding a full path may result in machine-dependent output
					Arguments.Add("/wd5049");

					if (CompileEnvironment.DeterministicWarningLevel == WarningLevel.Error)
					{
						Arguments.Add("/we5048");
					}
					else if (CompileEnvironment.DeterministicWarningLevel == WarningLevel.Off)
					{
						Arguments.Add("/wd5048");
					}
				}
				else if (Target.WindowsPlatform.Compiler.IsClang())
				{
					Arguments.Add("/Brepro");
				}
			}

			if (Target.WindowsPlatform.Compiler.IsMSVC() && Target.WindowsPlatform.bVCFastFail)
			{
				Arguments.Add("/fastfail");
			}

			// Address sanitizer
			if (Target.WindowsPlatform.bEnableAddressSanitizer)
			{
				// Enable address sanitizer. This also requires companion libraries at link time.
				// Works for clang too.
				Arguments.Add("/fsanitize=address");

				// Use the CRT allocator so that ASan is able to hook into it for better error
				// detection.
				AddDefinition(Arguments, "FORCE_ANSI_ALLOCATOR=1");

				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					// MSVC has no support for __has_feature(address_sanitizer)
					AddDefinition(Arguments, "USING_ADDRESS_SANITISER=1");

					// Disabling a couple annotations to workaround an issue related to building third party libraries with different options than the main binary.
					// This fixes an error that looks like this: error LNK2038: mismatch detected for 'annotate_string': value '0' doesn't match value '1' in Module.Core.XX_of_YY.cpp.obj
					AddDefinition(Arguments, "_DISABLE_STRING_ANNOTATION=1");
					AddDefinition(Arguments, "_DISABLE_VECTOR_ANNOTATION=1");
				}

				// Currently the ASan headers are not default around. They can be found at this location so lets use this until this is resolved in the toolchain
				// Jira with some more info and the MSVC bug at UE-144727
				AddSystemIncludePath(Arguments, DirectoryReference.Combine(EnvVars.CompilerDir, "crt", "src"), Target.WindowsPlatform.Compiler);
			}

			if (Target.WindowsPlatform.bEnableLibFuzzer)
			{
				if (Target.WindowsPlatform.Compiler.IsClang())
				{
					Arguments.Add("-fsanitize=fuzzer");
				}
				else
				{
					Arguments.Add("/fsanitize=fuzzer");
				}
			}

			//
			//	Debug
			//
			if (CompileEnvironment.Configuration == CppConfiguration.Debug)
			{
				// Disable compiler optimization.
				Arguments.Add("/Od");

				// Favor code size (especially useful for embedded platforms).
				Arguments.Add("/Os");

				// Runtime checks and ASan are incompatible.
				if (!Target.WindowsPlatform.bEnableAddressSanitizer)
				{
					Arguments.Add("/RTCs");
				}
			}
			//
			//	Development
			//
			else
			{
				if (!CompileEnvironment.bOptimizeCode)
				{
					// Disable compiler optimization.
					Arguments.Add("/Od");
				}
				else
				{
					// Maximum optimizations.
					Arguments.Add("/Ox");

					if (Target.WindowsPlatform.Compiler == WindowsCompiler.Intel)
					{
						Arguments.Add("/Qvec");
						Arguments.Add("/Qvec-with-mask");
						Arguments.Add("/Qvec-peel-loops");
						Arguments.Add("/Qvec-remainder-loops");
					}

					if (CompileEnvironment.OptimizationLevel != OptimizationMode.Speed)
					{
						Arguments.Add("/Os");
					}
					else
					{
						// Favor code speed.
						Arguments.Add("/Ot");
					}

					// Coalesce duplicate strings
					Arguments.Add("/GF");

					// Only omit frame pointers on the PC (which is implied by /Ox) if wanted.
					if (CompileEnvironment.bOmitFramePointers == false)
					{
						Arguments.Add("/Oy-");
					}
				}
			}

			//
			// LTCG and PGO
			//
			bool bEnableLTCG =
				CompileEnvironment.bPGOProfile ||
				CompileEnvironment.bPGOOptimize ||
				CompileEnvironment.bAllowLTCG;
			if (bEnableLTCG && !Target.WindowsPlatform.Compiler.IsIntel())
			{
				// Enable link-time code generation.
				Arguments.Add("/GL");
			}

			if (Target.WindowsPlatform.Compiler.IsIntel())
			{
				if (CompileEnvironment.bAllowLTCG)
				{
					// Enable link-time code generation.
					Arguments.Add("-flto");
				}

				if (CompileEnvironment.bPGOProfile)
				{
					// Generate instrumented code.
					Arguments.Add($"-fprofile-instr-generate=\"{CompileEnvironment.PGOFilenamePrefix}\"-%p-%m.profraw");
				}
				else if (CompileEnvironment.bPGOOptimize)
				{
					// Use a merged profdata file.
					Arguments.Add("-Wno-profile-instr-out-of-date");
					Arguments.Add($"-fprofile-instr-use=\"{Path.Combine(CompileEnvironment.PGODirectory!, CompileEnvironment.PGOFilenamePrefix!)}\".profdata");
				}
			}

				// DevelVitorF Remove defined above
			//
			//	PC
			//
			/*if (CompileEnvironment.Architecture == UnrealArch.X64 && CompileEnvironment.MinCpuArchX64 != MinimumCpuArchitectureX64.None)
			{
				// Define /arch:AVX[2,512] for the current compilation unit.  Machines without AVX support will crash on any SSE/AVX instructions if they run this compilation unit.
				Arguments.Add($"/arch:{CompileEnvironment.MinCpuArchX64}");

				// AVX available implies sse4 and sse2 available.
				// Inform Unreal code that we have sse2, sse4, and AVX, both available to compile and available to run
				// By setting the ALWAYS_HAS defines, we we direct Unreal code to skip cpuid checks to verify that the running hardware supports sse/avx.
				AddDefinition(Arguments, "PLATFORM_ENABLE_VECTORINTRINSICS=1");
				AddDefinition(Arguments, "PLATFORM_MAYBE_HAS_AVX=1");
				AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX=1");
				if (CompileEnvironment.MinCpuArchX64 >= MinimumCpuArchitectureX64.AVX2)
				{
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX_2=1");
				}
				if (CompileEnvironment.MinCpuArchX64 >= MinimumCpuArchitectureX64.AVX512)
				{
					AddDefinition(Arguments, "PLATFORM_ALWAYS_HAS_AVX_512=1");
				}
			}*/

			// Prompt the user before reporting internal errors to Microsoft.
			Arguments.Add("/errorReport:prompt");

			// Enable C++ exceptions when building with the editor or when building UHT.
			if (CompileEnvironment.bEnableExceptions)
			{
				// Enable C++ exception handling, but not C exceptions.
				Arguments.Add("/EHsc");
				Arguments.Add("/DPLATFORM_EXCEPTIONS_DISABLED=0");
			}
			else
			{
				// This is required to disable exception handling in VC platform headers.
				AddDefinition(Arguments, "_HAS_EXCEPTIONS=0");
				Arguments.Add("/DPLATFORM_EXCEPTIONS_DISABLED=1");
			}

			// If enabled, create debug information.
			if (CompileEnvironment.bCreateDebugInfo)
			{
				// Store debug info in .pdb files.
				// @todo clang: PDB files are emited from Clang but do not fully work with Visual Studio yet (breakpoints won't hit due to "symbol read error")
				// @todo clang (update): as of clang 3.9 breakpoints work with PDBs, and the callstack is correct, so could be used for crash dumps. However debugging is still impossible due to the large amount of unreadable variables and unpredictable breakpoint stepping behaviour
				if (CompileEnvironment.bUsePDBFiles || CompileEnvironment.bSupportEditAndContinue)
				{
					// Create debug info suitable for E&C if wanted.
					if (CompileEnvironment.bSupportEditAndContinue)
					{
						Arguments.Add("/ZI");
					}
					// Regular PDB debug information.
					else
					{
						Arguments.Add("/Zi");
					}
					// We need to add this so VS won't lock the PDB file and prevent synchronous updates. This forces serialization through MSPDBSRV.exe.
					// See http://msdn.microsoft.com/en-us/library/dn502518.aspx for deeper discussion of /FS switch.
					if (CompileEnvironment.bUseIncrementalLinking)
					{
						Arguments.Add("/FS");
					}
				}
				// Store C7-format debug info in the .obj files, which is faster.
				else
				{
					Arguments.Add("/Z7");
				}
			}

			// Specify the appropriate runtime library based on the platform and config.
			if (CompileEnvironment.bUseStaticCRT)
			{
				if (CompileEnvironment.bUseDebugCRT)
				{
					Arguments.Add("/MTd");
				}
				else
				{
					Arguments.Add("/MT");
				}
			}
			else
			{
				if (CompileEnvironment.bUseDebugCRT)
				{
					Arguments.Add("/MDd");
				}
				else
				{
					Arguments.Add("/MD");
				}
			}

			if (Target.WindowsPlatform.Compiler.IsMSVC())
			{
				// Allow large object files to avoid hitting the 2^16 section limit when running with -StressTestUnity.
				// Note: not needed for clang, it implicitly upgrades COFF files to bigobj format when necessary.
				Arguments.Add("/bigobj");
			}

			FPSemanticsMode FPSemantics = CompileEnvironment.FPSemantics;
			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				// FMath::Sqrt calls get inlined and when reciprical is taken, turned into an rsqrtss instruction,
				// which is *too* imprecise for, e.g., TestVectorNormalize_Sqrt in UnrealMathTest.cpp
				// TODO: Observed in clang 7.0, presumably the same in Intel C++ Compiler?
				FPSemantics = FPSemanticsMode.Precise;
			}

			switch (FPSemantics)
			{
				case FPSemanticsMode.Default: // Default is imprecise FP semantics.
				case FPSemanticsMode.Imprecise: Arguments.Add("/fp:fast"); break;
				case FPSemanticsMode.Precise: Arguments.Add("/fp:precise"); break;
				default:
					throw new BuildException($"Unsupported FP semantics: {FPSemantics}");
			}

			// Intel oneAPI compiler does not support /Zo
			if (CompileEnvironment.bOptimizeCode && Target.WindowsPlatform.Compiler != WindowsCompiler.Intel)
			{
				// Allow optimized code to be debugged more easily.  This makes PDBs a bit larger, but doesn't noticeably affect
				// compile times.  The executable code is not affected at all by this switch, only the debugging information.
				Arguments.Add("/Zo");
			}

			// Pack struct members on 8-byte boundaries.
			Arguments.Add("/Zp8");

			 // LLVM status indicates that clang-cl's /W4 `-Wall` maps to `-Weverything`, turns too much warnings on.
			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				// Treat all warnings as errors by default
				//Arguments.Add("-Werror");
				//Arguments.Add("-Wall");
				//Arguments.Add("-Wextra");
				Arguments.Add("-W4");
				if (CompileEnvironment.bWarningsAsErrors || CompileEnvironment.DefaultWarningLevel == WarningLevel.Error)
				{
					// Treat all warnings as errors by default
					Arguments.Add("-Werror");
				}
				else
				{
					Arguments.Add("-Wno-error");
				}
			}
			else
			{
				// Set warning level.
				// Restrictive during regular compilation.
				Arguments.Add("/W4");

				// Treat warnings as errors
				if (CompileEnvironment.bWarningsAsErrors || CompileEnvironment.DefaultWarningLevel == WarningLevel.Error)
				{
					Arguments.Add("/WX");
				}
			}

			if (CompileEnvironment.DeprecationWarningLevel == WarningLevel.Off)
			{
				Arguments.Add("/wd4996");
			}
			else if (CompileEnvironment.DeprecationWarningLevel == WarningLevel.Error)
			{
				Arguments.Add("/we4996");
			}

			//@todo: Disable warnings for VS2017. These should be reenabled as we clear the reasons for them out of the engine source and the VS2015 toolchain evolves.
			if (Target.WindowsPlatform.Compiler.IsMSVC())
			{
				// Disable shadow variable warnings
				if (CompileEnvironment.ShadowVariableWarningLevel == WarningLevel.Off)
				{
					Arguments.Add("/wd4456"); // 4456 - declaration of 'LocalVariable' hides previous local declaration
					Arguments.Add("/wd4458"); // 4458 - declaration of 'parameter' hides class member
					Arguments.Add("/wd4459"); // 4459 - declaration of 'LocalVariable' hides global declaration
				}
				else if (CompileEnvironment.ShadowVariableWarningLevel == WarningLevel.Error)
				{
					Arguments.Add("/we4456"); // 4456 - declaration of 'LocalVariable' hides previous local declaration
					Arguments.Add("/we4458"); // 4458 - declaration of 'parameter' hides class member
					Arguments.Add("/we4459"); // 4459 - declaration of 'LocalVariable' hides global declaration
				}
			}

			if (CompileEnvironment.bEnableUndefinedIdentifierWarnings && !CompileEnvironment.bPreprocessOnly)
			{
				if (CompileEnvironment.bUndefinedIdentifierWarningsAsErrors)
				{
					Arguments.Add("/we4668");
				}
				else
				{
					Arguments.Add("/w44668");
				}
			}

			// The unsafe type cast warnings setting controls the following warnings currently:
			//   4244: conversion from 'type1' to 'type2', possible loss of data
			//   4838: conversion from 'type1' to 'type2' requires a narrowing conversion
			//@TODO: FLOATPRECISION: Consider doing the following as well:
			//   4267: 'var' : conversion from 'size_t' to 'type', possible loss of data
			//   4305: 'identifier' : truncation from 'type1' to 'type2'
			WarningLevel EffectiveCastWarningLevel = (Target.Platform == UnrealTargetPlatform.Win64) ? CompileEnvironment.UnsafeTypeCastWarningLevel : WarningLevel.Off;
			if (EffectiveCastWarningLevel == WarningLevel.Error)
			{
				Arguments.Add("/we4244");
				Arguments.Add("/we4838");
			}
			else if (EffectiveCastWarningLevel == WarningLevel.Warning)
			{
				// Note: The extra 4 is not a typo, /wLXXXX sets warning XXXX to level L
				Arguments.Add("/w44244");
				Arguments.Add("/w44838");
			}
			else
			{
				Arguments.Add("/wd4244");
				Arguments.Add("/wd4838");
			}

			if (CompileEnvironment.Architecture == UnrealArch.Arm64ec)
			{
				Arguments.Add("/arm64EC");
				// The latest vc toolchain requires that these be manually set for arm64ec.
				AddDefinition(Arguments, "_ARM64EC_");
				AddDefinition(Arguments, "_ARM64EC_WORKAROUND_");
				AddDefinition(Arguments, "ARM64EC");
				AddDefinition(Arguments, "AMD64");
				AddDefinition(Arguments, "_AMD64_");
				AddDefinition(Arguments, "_WINDOWS");
				AddDefinition(Arguments, "WIN32");
			}
		}

		protected virtual void AppendCLArguments_CPP(CppCompileEnvironment CompileEnvironment, List<string> Arguments)
		{
			if (Target.WindowsPlatform.Compiler.IsMSVC())
			{
				// Explicitly compile the file as C++.
				Arguments.Add("/TP");
			}
			else
			{
				string FileSpecifier = "c++";
				if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
				{
					// Tell Clang to generate a PCH header
					FileSpecifier += "-header";
				}

				Arguments.Add($"-Xclang -x -Xclang \"{FileSpecifier}\"");
			}

			if (!CompileEnvironment.bEnableBufferSecurityChecks)
			{
				// This will disable buffer security checks (which are enabled by default) that the MS compiler adds around arrays on the stack,
				// Which can add some performance overhead, especially in performance intensive code
				// Only disable this if you know what you are doing, because it will be disabled for the entire module!
				Arguments.Add("/GS-");
			}

			// Configure RTTI
			if (CompileEnvironment.bUseRTTI)
			{
				// Enable C++ RTTI.
				Arguments.Add("/GR");
			}
			else
			{
				// Disable C++ RTTI.
				Arguments.Add("/GR-");
			}

			// DevelVitorF Remove defined above
			// Set warning level.
			// Restrictive during regular compilation.
			/*Arguments.Add("/W4");

			// Treat warnings as errors
			if (CompileEnvironment.bWarningsAsErrors)
			{
				Arguments.Add("/WX");
			}*/

			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				switch (CompileEnvironment.CppStandard)
				{
					case CppStandardVersion.Cpp14:
						Arguments.Add("/clang:-std=c++14");
						//Arguments.Add("/clang:-Wc++14-compat /clang:-Wc++14-extensions");
						break;
					case CppStandardVersion.Cpp17:
						Arguments.Add("/clang:-std=c++17");
						//Arguments.Add("/clang:-Wc++17-compat /clang:-Wc++17-extensions");
						break;
					case CppStandardVersion.Cpp20:
						Arguments.Add("/clang:-std=c++20");
						//Arguments.Add("/clang:-Wc++20-compat /clang:-Wc++20-extensions");

						break;
					case CppStandardVersion.Latest:
						Arguments.Add("/clang:-std=c++2b");
						//Arguments.Add("/clang:-Wc++2a-compat /clang:-Wc++2a-extensions /clang:-Wc++2b-extensions");
						break;
					default:
						throw new BuildException($"Unsupported C++ standard type set: {CompileEnvironment.CppStandard}");
				}
			}
			else
			{
				switch (CompileEnvironment.CppStandard)
				{
					case CppStandardVersion.Cpp14:
						Arguments.Add("/std:c++14");
						break;
					case CppStandardVersion.Cpp17:
						Arguments.Add("/std:c++17");
						break;
					case CppStandardVersion.Cpp20:
						Arguments.Add("/std:c++20");

						break;
					case CppStandardVersion.Latest:
						Arguments.Add("/std:c++latest");

						break;
					default:
						throw new BuildException($"Unsupported C++ standard type set: {CompileEnvironment.CppStandard}");
				}
			}

			if (CompileEnvironment.CppStandard >= CppStandardVersion.Cpp20)
			{
				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					Arguments.Add("/Zc:preprocessor");
				}

				// warning C5054: operator ___: deprecated between enumerations of different types
				// re: http://www.open-std.org/jtc1/sc22/wg21/docs/papers/2018/p1120r0.html

				// It seems unclear whether the deprecation will be enacted in C++23 or not
				// e.g. http://www.open-std.org/jtc1/sc22/wg21/docs/papers/2020/p2139r2.html
				// Until the path forward is clearer, it seems reasonable to leave things as they are.
				Arguments.Add("/wd5054");

				// Disable VS2019 C++20 warnings fixed with VS2022
				if (Target.WindowsPlatform.Compiler == WindowsCompiler.VisualStudio2019)
				{
					Arguments.Add("/wd4005");
					Arguments.Add("/wd4668");
					Arguments.Add("/wd5105");
				}
			}

			if (CompileEnvironment.bEnableCoroutines)
			{
				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					Arguments.Add("/await:strict");
				}
				else
				{
					Arguments.Add("-fcoroutines-ts");
				}
			}

			if (Target.WindowsPlatform.Compiler.IsClang())
			{
				// Enable codeview ghash for faster lld links
				if (Target.WindowsPlatform.Compiler == WindowsCompiler.Clang && Target.WindowsPlatform.bAllowClangLinker)
				{
					Arguments.Add("-gcodeview-ghash");
				}

				// Disable specific warnings that cause problems with Clang
				// NOTE: These must appear after we set the MSVC warning level

				// @todo clang: Ideally we want as few warnings disabled as possible
				//

				// DeveVitorF defined above
				// Treat all warnings as errors by default
				//Arguments.Add("-Werror");                                   // https://clang.llvm.org/docs/UsersManual.html#cmdoption-werror

				ClangWarnings.GetEnabledWarnings(Arguments);

				VersionNumber ClangVersion = Target.WindowsPlatform.Compiler == WindowsCompiler.Intel ? MicrosoftPlatformSDK.GetClangVersionForIntelCompiler(EnvVars.CompilerPath) : EnvVars.CompilerVersion;
				ClangWarnings.GetDisabledWarnings(CompileEnvironment,
					Target.StaticAnalyzer,
					ClangVersion,
					Arguments);

				// Additional disabled warnings for msvc. Everything below should be checked if it is necessary
				ClangWarnings.GetVCDisabledWarnings(Arguments);

				if (CompileEnvironment.bAllowAutoRTFMInstrumentation)
				{
					Arguments.Add("-fautortfm");
				}
			}

			if (Target.WindowsPlatform.Compiler == WindowsCompiler.Intel)
			{
				ClangWarnings.GetIntelDisabledWarnings(Arguments);
			}
		}

		static void AppendCLArguments_C(CppCompileEnvironment CompileEnvironment, List<string> Arguments)
		{
			// Explicitly compile the file as C.
			Arguments.Add("/TC");

			// Level 0 warnings.  Needed for external C projects that produce warnings at higher warning levels.
			Arguments.Add("/W0");

			// Select C Standard version available
			// Select C Standard version available
			switch (CompileEnvironment.CStandard)
			{
				case CStandardVersion.None:
				case CStandardVersion.C89:
				case CStandardVersion.C99:
					break;
				case CStandardVersion.C11:
					Arguments.Add("/std:c11");
					break;
				case CStandardVersion.C17:
				case CStandardVersion.Latest:
					Arguments.Add("/std:c17");
					break;
				default:
					throw new BuildException($"Unsupported C standard type set: {CompileEnvironment.CStandard}");
			}
		}

		protected virtual void AppendLinkArguments(LinkEnvironment LinkEnvironment, List<string> Arguments)
		{
			if (Target.WindowsPlatform.Compiler == WindowsCompiler.Clang && Target.WindowsPlatform.bAllowClangLinker)
			{
				// @todo clang: The following static libraries aren't linking correctly with Clang:
				//		tbbmalloc.lib, zlib_64.lib, libpng_64.lib, freetype2412MT.lib, IlmImf.lib
				//		LLD: Assertion failed: result.size() == 1, file ..\tools\lld\lib\ReaderWriter\FileArchive.cpp, line 71
				//

				// Only omit frame pointers on the PC (which is implied by /Ox) if wanted.
				if (!LinkEnvironment.bOmitFramePointers)
				{
					Arguments.Add("--disable-fp-elim");
				}
			}

			// Don't create a side-by-side manifest file for the executable.
			if (Target.WindowsPlatform.ManifestFile == null)
			{
				Arguments.Add("/MANIFEST:NO");
			}
			else
			{
				Arguments.Add("/MANIFEST:EMBED");
				FileItem ManifestFile = FileItem.GetItemByPath(Target.WindowsPlatform.ManifestFile);
				Arguments.Add($"/MANIFESTINPUT:\"{NormalizeCommandLinePath(ManifestFile)}\"");
			}

			// Prevents the linker from displaying its logo for each invocation.
			Arguments.Add("/NOLOGO");

			// Address sanitizer requires debug info for symbolizing callstacks whether
			// we're building debug or shipping.
			if (LinkEnvironment.bCreateDebugInfo || Target.WindowsPlatform.bEnableAddressSanitizer)
			{
				// Output debug info for the linked executable.
				// Beginning in Visual Studio 2017 /DEBUG defaults to /DEBUG:FASTLINK for debug builds
				Arguments.Add("/DEBUG:FULL");
			}

			if (LinkEnvironment.bCreateDebugInfo && LinkEnvironment.bUseFastPDBLinking)
			{
				// Allow partial PDBs for faster linking
				if (Target.WindowsPlatform.Compiler == WindowsCompiler.Clang && Target.WindowsPlatform.bAllowClangLinker)
				{
					Arguments[Arguments.Count - 1] = "/DEBUG:GHASH";
				}
				else
				{
					Arguments[Arguments.Count - 1] = "/DEBUG:FASTLINK";
				}
			}

			// Prompt the user before reporting internal errors to Microsoft.
			Arguments.Add("/errorReport:prompt");

			//
			//	PC
			//
			if (UseWindowsArchitecture(LinkEnvironment.Platform))
			{
				Arguments.Add($"/MACHINE:{WindowsExports.GetArchitectureName(Target.WindowsPlatform.Architecture)}");
				{
					if (LinkEnvironment.bIsBuildingConsoleApplication)
					{
						Arguments.Add("/SUBSYSTEM:CONSOLE");
					}
					else
					{
						Arguments.Add("/SUBSYSTEM:WINDOWS");
					}
				}

				if (LinkEnvironment.bIsBuildingConsoleApplication && !LinkEnvironment.bIsBuildingDLL && !String.IsNullOrEmpty(LinkEnvironment.WindowsEntryPointOverride))
				{
					// Use overridden entry point
					Arguments.Add("/ENTRY:" + LinkEnvironment.WindowsEntryPointOverride);
				}

				// Allow the OS to load the EXE at different base addresses than its preferred base address.
				Arguments.Add("/FIXED:No");

				// Explicitly declare that the executable is compatible with Data Execution Prevention.
				Arguments.Add("/NXCOMPAT");
			}

			// Set the default stack size.
			if (LinkEnvironment.Platform.IsInGroup(UnrealPlatformGroup.Microsoft))
			{
				if (LinkEnvironment.DefaultStackSize > 0)
				{
					if (LinkEnvironment.DefaultStackSizeCommit > 0)
					{
						Arguments.Add("/STACK:" + LinkEnvironment.DefaultStackSize + "," + LinkEnvironment.DefaultStackSizeCommit);
					}
					else
					{
						Arguments.Add("/STACK:" + LinkEnvironment.DefaultStackSize);
					}
				}
			}

			// Allow delay-loaded DLLs to be explicitly unloaded.
			if (Target.WindowsPlatform.Architecture == UnrealArch.X64)
			{
				Arguments.Add("/DELAY:UNLOAD");
			}

			if (LinkEnvironment.bIsBuildingDLL)
			{
				Arguments.Add("/DLL");
			}

			if (String.IsNullOrEmpty(Target.WindowsPlatform.PdbAlternatePath))
			{
				// Don't embed the full PDB path; we want to be able to move binaries elsewhere. They will always be side by side.
				Arguments.Add("/PDBALTPATH:%_PDB%");
			}
			else
			{
				// Embed an alternate PDB path into the executable
				Arguments.Add($"/PDBALTPATH:\"{Target.WindowsPlatform.PdbAlternatePath}\"");
			}

			// Deterministic link support
			if (LinkEnvironment.bDeterministic)
			{
				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					Arguments.Add("/experimental:deterministic");
				}
				else if (Target.WindowsPlatform.Compiler.IsClang())
				{
					Arguments.Add("/Brepro");
				}
			}

			if (Target.WindowsPlatform.Compiler.IsMSVC() && Target.WindowsPlatform.bVCFastFail && !LinkEnvironment.bPGOOptimize && !LinkEnvironment.bPGOProfile)
			{
				Arguments.Add("/fastfail");
			}

			// Allow for PDBs larger than 4GB
			if (Target.WindowsPlatform.PdbPageSize.HasValue)
			{
				Arguments.Add($"/PDBPAGESIZE:{System.Numerics.BitOperations.RoundUpToPowerOf2(Target.WindowsPlatform.PdbPageSize.Value)}");
				//Arguments.Add("/PDBCompress"); // Do not turn this on, it makes link times almost 2x slower. This is _only_ to save local disk space. Will _not_ make actual file smaller for network transfer
			}

			//
			//	Shipping & LTCG
			//
			if (LinkEnvironment.bAllowLTCG)
			{
				// Use link-time code generation.
				Arguments.Add("/LTCG");

				// This is where we add in the PGO-Lite linkorder.txt if we are using PGO-Lite
				//Result += " /ORDER:@linkorder.txt";
				//Result += " /VERBOSE";
			}

			//
			//	Shipping binary
			//
			if (LinkEnvironment.Configuration == CppConfiguration.Shipping)
			{
				// Generate an EXE checksum.
				Arguments.Add("/RELEASE");
			}

			// Eliminate unreferenced symbols.
			if (Target.WindowsPlatform.bStripUnreferencedSymbols)
			{
				Arguments.Add("/OPT:REF");
			}
			else
			{
				Arguments.Add("/OPT:NOREF");
			}

			// Identical COMDAT folding
			if (Target.WindowsPlatform.bMergeIdenticalCOMDATs)
			{
				Arguments.Add("/OPT:ICF");
			}
			else
			{
				Arguments.Add("/OPT:NOICF");
			}

			// Enable incremental linking if wanted. ( avoid /INCREMENTAL getting ignored (LNK4075) due to /LTCG, /RELEASE, and /OPT:ICF )
			if (LinkEnvironment.bUseIncrementalLinking &&
				LinkEnvironment.Configuration != CppConfiguration.Shipping &&
				!Target.WindowsPlatform.bMergeIdenticalCOMDATs &&
				!LinkEnvironment.bAllowLTCG &&
				Target.WindowsPlatform.Architecture == UnrealArch.X64)
			{
				Arguments.Add("/INCREMENTAL");
				Arguments.Add("/verbose:incr");
			}
			else
			{
				Arguments.Add("/INCREMENTAL:NO");
			}

			// Add any extra options from the target
			if (!String.IsNullOrEmpty(Target.WindowsPlatform.AdditionalLinkerOptions))
			{
				Arguments.Add(Target.WindowsPlatform.AdditionalLinkerOptions);
			}

			// Disable
			//LINK : warning LNK4199: /DELAYLOAD:nvtt_64.dll ignored; no imports found from nvtt_64.dll
			// type warning as we leverage the DelayLoad option to put third-party DLLs into a
			// non-standard location. This requires the module(s) that use said DLL to ensure that it
			// is loaded prior to using it.
			Arguments.Add("/ignore:4199");

			// Suppress warnings about missing PDB files for statically linked libraries.  We often don't want to distribute
			// PDB files for these libraries.
			Arguments.Add("/ignore:4099");      // warning LNK4099: PDB '<file>' was not found with '<file>'

			// Workaround for linker errors when linking against static libraries that were compiled with an older msvc
			// https://github.com/microsoft/STL/issues/2655
			Arguments.Add("/ALTERNATENAME:__imp___std_init_once_begin_initialize=__imp_InitOnceBeginInitialize");
			Arguments.Add("/ALTERNATENAME:__imp___std_init_once_complete=__imp_InitOnceComplete");
		}

		protected virtual void AppendLibArguments(LinkEnvironment LinkEnvironment, List<string> Arguments)
		{
			// Prevents the linker from displaying its logo for each invocation.
			Arguments.Add("/NOLOGO");

			// Prompt the user before reporting internal errors to Microsoft.
			Arguments.Add("/errorReport:prompt");

			//
			//	PC
			//
			if (UseWindowsArchitecture(LinkEnvironment.Platform))
			{
				Arguments.Add($"/MACHINE:{WindowsExports.GetArchitectureName(Target.WindowsPlatform.Architecture)}");
				{
					if (LinkEnvironment.bIsBuildingConsoleApplication)
					{
						Arguments.Add("/SUBSYSTEM:CONSOLE");
					}
					else
					{
						Arguments.Add("/SUBSYSTEM:WINDOWS");
					}
				}
			}

			//
			//	Shipping & LTCG
			//
			if (LinkEnvironment.bAllowLTCG || LinkEnvironment.Configuration == CppConfiguration.Shipping)
			{
				// Use link-time code generation.
				Arguments.Add("/LTCG");
			}
		}

		private VCCompileAction CreateBaseCompileAction(CppCompileEnvironment CompileEnvironment)
		{
			VCCompileAction BaseCompileAction = new VCCompileAction(EnvVars);

			// Add additional response files
			foreach (FileItem AdditionalRsp in CompileEnvironment.AdditionalResponseFiles)
			{
				BaseCompileAction.Arguments.Add($"@\"{NormalizeCommandLinePath(AdditionalRsp.Location)}\"");
			}

			AppendCLArguments_Global(CompileEnvironment, BaseCompileAction.Arguments);

			// Add include paths to the argument list.
			BaseCompileAction.IncludePaths.AddRange(CompileEnvironment.UserIncludePaths);
			BaseCompileAction.SystemIncludePaths.AddRange(CompileEnvironment.SystemIncludePaths);

			if (!CompileEnvironment.bHasSharedResponseFile)
			{
				BaseCompileAction.SystemIncludePaths.AddRange(EnvVars.IncludePaths);
			}

			// Remember the architecture
			BaseCompileAction.Architecture = CompileEnvironment.Architecture;

			// Add preprocessor definitions to the argument list.
			BaseCompileAction.Definitions.AddRange(CompileEnvironment.Definitions);

			// Add the force included headers
			BaseCompileAction.ForceIncludeFiles.AddRange(CompileEnvironment.ForceIncludeFiles);

			// If we're using precompiled headers, set that up now
			if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Include)
			{
				FileItem IncludeHeader = FileItem.GetItemByFileReference(CompileEnvironment.PrecompiledHeaderIncludeFilename!);
				BaseCompileAction.ForceIncludeFiles.Insert(0, IncludeHeader);

				BaseCompileAction.UsingPchFile = CompileEnvironment.PrecompiledHeaderFile;
				BaseCompileAction.PchThroughHeaderFile = IncludeHeader;
			}

			// Generate the timing info
			if (CompileEnvironment.bPrintTimingInfo || Target.WindowsPlatform.bCompilerTrace)
			{
				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					if (CompileEnvironment.bPrintTimingInfo)
					{
						BaseCompileAction.Arguments.Add("/Bt+ /d2cgsummary");
					}

					BaseCompileAction.Arguments.Add("/d1reportTime");
				}
			}

			// MSVC uses multiple threads when compiling CPP files, so the "weight" is more than 1
			if (Target.WindowsPlatform.Compiler.IsMSVC())
			{
				// If deterministic is enabled, MSVC does not use multiple threads
				if (!CompileEnvironment.bDeterministic)
				{
					BaseCompileAction.Weight = Target.MSVCCompileActionWeight;
				}
			}
			else if (Target.WindowsPlatform.Compiler.IsClang())
			{
				BaseCompileAction.Weight = Target.ClangCompileActionWeight;
			}

			// Don't farm out creation of precompiled headers as it is the critical path task.
			BaseCompileAction.bCanExecuteRemotely =
				CompileEnvironment.PrecompiledHeaderAction != PrecompiledHeaderAction.Create ||
				CompileEnvironment.bAllowRemotelyCompiledPCHs
				;

			// When compiling with SN-DBS, modules that contain a #import must be built locally
			BaseCompileAction.bCanExecuteRemotelyWithSNDBS = BaseCompileAction.bCanExecuteRemotely && !CompileEnvironment.bBuildLocallyWithSNDBS;

			return BaseCompileAction;
		}

		public override FileItem? CopyDebuggerVisualizer(FileItem SourceFile, DirectoryReference IntermediateDirectory, IActionGraphBuilder Graph)
		{
			if (SourceFile.HasExtension(".natvis") || SourceFile.HasExtension(".natstepfilter"))
			{
				FileReference IntermediateFile = FileReference.Combine(IntermediateDirectory, SourceFile.Name);
				if (!UnrealBuildTool.IsFileInstalled(IntermediateFile))
				{
					FileItem Item = FileItem.GetItemByFileReference(IntermediateFile);
					Graph.CreateCopyAction(SourceFile, Item);
					return Item; // Only return the item if we are responsible for copying it, for addition to makefile/manifests
				}
			}
			return null;
		}

		public override FileItem? LinkDebuggerVisualizer(FileItem SourceFile, DirectoryReference IntermediateDirectory)
		{
			if (SourceFile.HasExtension(".natvis") || SourceFile.HasExtension(".natstepfilter"))
			{
				FileItem IntermediateFile = FileItem.GetItemByFileReference(FileReference.Combine(IntermediateDirectory, SourceFile.Name));
				return IntermediateFile;
			}
			return null;
		}

		protected override CPPOutput CompileCPPFiles(CppCompileEnvironment CompileEnvironment, List<FileItem> InputFiles, DirectoryReference OutputDir, string ModuleName, IActionGraphBuilder Graph)
		{
			VCCompileAction BaseCompileAction = CreateBaseCompileAction(CompileEnvironment);

			// Create a compile action for each source file.
			List<VCCompileAction> Actions = new List<VCCompileAction>();
			foreach (FileItem SourceFile in InputFiles)
			{
				VCCompileAction CompileAction = new VCCompileAction(BaseCompileAction);
				CompileAction.SourceFile = SourceFile;

				bool bIsPlainCFile = Path.GetExtension(SourceFile.AbsolutePath).ToUpperInvariant() == ".C";

				if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
				{
					// Generate a CPP File that just includes the precompiled header.
					string PrecompiledHeaderIncludeFilenameString = NormalizeCommandLinePath(CompileEnvironment.PrecompiledHeaderIncludeFilename!);
					string PchCppFile = $"// Compiler: {EnvVars.CompilerVersion}\n#include \"{PrecompiledHeaderIncludeFilenameString.Replace('\\', '/')}\"\r\n";
					CompileAction.SourceFile = FileItem.GetItemByFileReference(CompileEnvironment.PrecompiledHeaderIncludeFilename!.ChangeExtension(".cpp"));
					Graph.CreateIntermediateTextFile(CompileAction.SourceFile, PchCppFile);

					// Add the precompiled header file to the produced items list.
					CompileAction.CreatePchFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, SourceFile.Location.GetFileName() + ".pch"));
					CompileAction.PchThroughHeaderFile = FileItem.GetItemByFileReference(CompileEnvironment.PrecompiledHeaderIncludeFilename);

					// If we're creating a PCH that will be used to compile source files for a library, we need
					// the compiled modules to retain a reference to PCH's module, so that debugging information
					// will be included in the library.  This is also required to avoid linker warning "LNK4206"
					// when linking an application that uses this library.
					if (CompileEnvironment.bIsBuildingLibrary)
					{
						// NOTE: The symbol name we use here is arbitrary, and all that matters is that it is
						// unique per PCH module used in our library
						string FakeUniquePCHSymbolName = CompileEnvironment.PrecompiledHeaderIncludeFilename.GetFileNameWithoutExtension();
						CompileAction.Arguments.Add($"/Yl{FakeUniquePCHSymbolName}");
					}
				}

				string FileName = SourceFile.Name;
				if (CompileEnvironment.CollidingNames != null && CompileEnvironment.CollidingNames.Contains(SourceFile))
				{
					string HashString = ContentHash.MD5(SourceFile.AbsolutePath.Substring(Unreal.RootDirectory.FullName.Length)).GetHashCode().ToString("X4");
					FileName = Path.GetFileNameWithoutExtension(FileName) + "_" + HashString + Path.GetExtension(FileName);
				}

				if (CompileEnvironment.bPreprocessOnly)
				{
					CompileAction.PreprocessedFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, FileName + ".i"));
					CompileAction.ResponseFile = FileItem.GetItemByFileReference(GetResponseFileName(CompileEnvironment, CompileAction.PreprocessedFile));
				}
				else if (Target.WindowsPlatform.Compiler.IsClang() && Target.StaticAnalyzer == StaticAnalyzer.Default)
				{
					// Clang analysis does not actually create an object, use the dependency list as the response filename
					string DependencyListFilename = FileName + ".d";
					FileItem DependencyListFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, DependencyListFilename));
					CompileAction.ResponseFile = FileItem.GetItemByFileReference(GetResponseFileName(CompileEnvironment, DependencyListFile));
				}
				else
				{
					// Add the object file to the produced item list.
					string ObjectLeafFilename = FileName + ".obj";
					FileItem ObjectFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, ObjectLeafFilename));

					CompileAction.ObjectFile = ObjectFile;
					CompileAction.ResponseFile = FileItem.GetItemByFileReference(GetResponseFileName(CompileEnvironment, ObjectFile));

					if (Target.WindowsPlatform.ObjSrcMapFile != null)
					{
						using (StreamWriter Writer = File.AppendText(Target.WindowsPlatform.ObjSrcMapFile))
						{
							Writer.WriteLine($"\"{ObjectLeafFilename}\" -> \"{SourceFile.AbsolutePath}\"");
						}
					}

					if (Target.WindowsPlatform.Compiler.IsMSVC() && CompileEnvironment.bWithAssembly && SourceFile.HasExtension(".cpp"))
					{
						CompileAction.AssemblyFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, FileName + ".asm"));
					}

					// Experimental: support for JSON output of timing data
					if (Target.WindowsPlatform.Compiler.IsClang() && (Target.bPrintToolChainTimingInfo || Target.WindowsPlatform.bClangTimeTrace))
					{
						CompileAction.Arguments.Add("-Xclang -ftime-trace");
						CompileAction.AdditionalProducedItems.Add(FileItem.GetItemByFileReference(ObjectFile.Location.ChangeExtension(".json")));
					}
				}

				if (CompileEnvironment.bDeterministic && !Target.WindowsPlatform.Compiler.IsClang())
				{
					if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
					{
						CompileAction.ArtifactMode |= ArtifactMode.PropagateInputs;
					}
					else
					{
						CompileAction.ArtifactMode |= ArtifactMode.Enabled | ArtifactMode.AbsolutePath; // deps output file contains absolute paths
					}
				}

				// Create PDB files if we were configured to do that.
				if (CompileEnvironment.bUsePDBFiles || CompileEnvironment.bSupportEditAndContinue)
				{
					FileReference PDBLocation;
					if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Include)
					{
						// All files using the same PCH are required to share the same PDB that was used when compiling the PCH
						PDBLocation = CompileEnvironment.PrecompiledHeaderFile!.Location.ChangeExtension(".pdb");

						// Enable synchronous file writes, since we'll be modifying the existing PDB
						CompileAction.Arguments.Add("/FS");
					}
					else if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
					{
						// Files creating a PCH use a PDB per file.
						PDBLocation = FileReference.Combine(OutputDir, CompileEnvironment.PrecompiledHeaderIncludeFilename!.GetFileName() + ".pdb");

						// Enable synchronous file writes, since we'll be modifying the existing PDB
						CompileAction.Arguments.Add("/FS");
					}
					else if (!bIsPlainCFile)
					{
						// Ungrouped C++ files use a PDB per file.
						PDBLocation = FileReference.Combine(OutputDir, FileName + ".pdb");
					}
					else
					{
						// Group all plain C files that doesn't use PCH into the same PDB
						PDBLocation = FileReference.Combine(OutputDir, "MiscPlainC.pdb");
					}

					// Specify the PDB file that the compiler should write to.
					CompileAction.Arguments.Add($"/Fd\"{NormalizeCommandLinePath(PDBLocation)}\"");

					// Don't allow remote execution when PDB files are enabled; we need to modify the same files. XGE works around this by generating separate
					// PDB files per agent, but this functionality is only available with the Visual C++ extension package (via the VCCompiler=true tool option).
					CompileAction.bCanExecuteRemotely = false;
				}

				// Add C or C++ specific compiler arguments.
				if (bIsPlainCFile)
				{
					AppendCLArguments_C(CompileEnvironment, CompileAction.Arguments);

					// AppendCLArguments_C is a static and we cannot use the Target to check if we are using clang, so shoving here for now
					if (Target.WindowsPlatform.Compiler.IsClang())
					{
						if (CompileEnvironment.bAllowAutoRTFMInstrumentation)
						{
							CompileAction.Arguments.Add("-fautortfm");
						}

						// If we are using the AutoRTFM compiler, we make the compile action depend on the version of the compiler itself.
						// This lets us update the compiler (which might not cause a version update of the compiler, which instead tracks
						// the LLVM versioning scheme that Clang uses), but ensure that we rebuild the source if the compiler has changed.
						if (CompileEnvironment.bUseAutoRTFMCompiler)
						{
							FileReference? CompilerPath = GetCppCompilerPath();
							if (null != CompilerPath)
							{
								BaseCompileAction.AdditionalPrerequisiteItems.Add(FileItem.GetItemByFileReference(CompilerPath));
							}
						}
					}
				}
				else
				{
					AppendCLArguments_CPP(CompileEnvironment, CompileAction.Arguments);
				}

				List<FileItem>? InlinedFiles;
				if (CompileEnvironment.FileInlineGenCPPMap.TryGetValue(SourceFile, out InlinedFiles))
				{
					CompileAction.AdditionalPrerequisiteItems.AddRange(InlinedFiles);
				}

				CompileAction.AdditionalPrerequisiteItems.AddRange(CompileEnvironment.ForceIncludeFiles);
				CompileAction.AdditionalPrerequisiteItems.AddRange(CompileEnvironment.AdditionalPrerequisites);

				if (!String.IsNullOrEmpty(CompileEnvironment.AdditionalArguments))
				{
					CompileAction.Arguments.Add(CompileEnvironment.AdditionalArguments);
				}

				if (SourceFile.HasExtension(".ixx"))
				{
					FileItem IfcFile = FileItem.GetItemByFileReference(FileReference.Combine(GetModuleInterfaceDir(OutputDir), SourceFile.Location.ChangeExtension(".ifc").GetFileName()));
					CompileAction.Arguments.Add("/interface");
					CompileAction.Arguments.Add($"/ifcOutput \"{IfcFile.Location}\"");
					CompileAction.CompiledModuleInterfaceFile = IfcFile;

					FileItem IfcDepsFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, FileName + ".md.json"));

					VCCompileAction CompileDepsAction = new VCCompileAction(CompileAction);
					CompileDepsAction.ActionType = ActionType.GatherModuleDependencies;
					CompileDepsAction.ResponseFile = FileItem.GetItemByFileReference(GetResponseFileName(CompileEnvironment, IfcDepsFile));
					CompileDepsAction.ObjectFile = null;
					CompileDepsAction.DependencyListFile = IfcDepsFile;
					CompileDepsAction.Arguments.Add($"/sourceDependencies:directives \"{IfcDepsFile.Location}\"");
					CompileDepsAction.AdditionalPrerequisiteItems.Add(SourceFile);
					CompileDepsAction.AdditionalPrerequisiteItems.AddRange(CompileEnvironment.ForceIncludeFiles);
					CompileDepsAction.AdditionalPrerequisiteItems.AddRange(CompileEnvironment.AdditionalPrerequisites);
					CompileDepsAction.AdditionalProducedItems.Add(IfcDepsFile);
					Graph.AddAction(CompileDepsAction);

					if (!ProjectFileGenerator.bGenerateProjectFiles)
					{
						CompileDepsAction.WriteResponseFile(Graph, Logger);
					}

					CompileAction.ActionType = ActionType.CompileModuleInterface;
					CompileAction.AdditionalPrerequisiteItems.Add(IfcDepsFile); // Force the dependencies file into the action graph
					CompileAction.AdditionalProducedItems.Add(IfcFile);
					CompileAction.CompiledModuleInterfaceFile = IfcFile;
				}

				if ((Target.bPrintToolChainTimingInfo || Target.WindowsPlatform.bCompilerTrace) && Target.WindowsPlatform.Compiler.IsMSVC())
				{
					CompileAction.ForceClFilter = true;
					CompileAction.TimingFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, $"{FileName}.timing"));
					GenerateParseTimingInfoAction(SourceFile, CompileAction.TimingFile, Graph);
				}

				if (Target.WindowsPlatform.Compiler.IsMSVC() && !CompileAction.ForceClFilter)
				{
					CompileAction.DependencyListFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, $"{FileName}.dep.json"));
				}
				else if (Target.WindowsPlatform.Compiler.IsClang())
				{
					CompileAction.DependencyListFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, $"{FileName}.d"));
				}
				else
				{
					CompileAction.DependencyListFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, $"{FileName}.txt"));
					CompileAction.bShowIncludes = Target.WindowsPlatform.bShowIncludes;
				}

				// Write cl errors and warnings to a file if supported
				if (Target.WindowsPlatform.Compiler.IsMSVC() && Target.WindowsPlatform.bWriteSarif && EnvVars.CompilerVersion >= new VersionNumber(14, 34, 31933))
				{
					if (Target.StaticAnalyzer == StaticAnalyzer.Default && !CompileEnvironment.bDisableStaticAnalysis)
					{
						CompileAction.AnalyzeLogFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, $"{FileName}.sa.sarif"));
					}

					CompileAction.ExperimentalLogFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, $"{FileName}.sarif"));
				}

				// Allow derived toolchains to make further changes
				ModifyFinalCompileAction(CompileAction, CompileEnvironment, SourceFile, OutputDir, ModuleName);

				if (!ProjectFileGenerator.bGenerateProjectFiles)
				{
					CompileAction.WriteResponseFile(Graph, Logger);
				}

				CompileAction.bIsAnalyzing = Target.StaticAnalyzer != StaticAnalyzer.None;

				// Update the output
				Graph.AddAction(CompileAction);
				Actions.Add(CompileAction);
			}

			CPPOutput Result = new CPPOutput();
			Result.ObjectFiles.AddRange(Actions.Where(x => x.ObjectFile != null || x.PreprocessedFile != null).Select(x => x.ObjectFile != null ? x.ObjectFile! : x.PreprocessedFile!));
			// Clang static analysis doesn't create object files, so treat the dependency list file as the output
			if (Target.WindowsPlatform.Compiler.IsClang() && Target.StaticAnalyzer == StaticAnalyzer.Default)
			{
				Result.ObjectFiles.AddRange(Actions.Where(x => x.DependencyListFile != null ).Select(x => x.DependencyListFile!));
			}
			Result.CompiledModuleInterfaces.AddRange(Actions.Where(x => x.CompiledModuleInterfaceFile != null).Select(x => x.CompiledModuleInterfaceFile!));
			Result.PrecompiledHeaderFile = Actions.Select(x => x.CreatePchFile).Where(x => x != null).FirstOrDefault();
			return Result;
		}

		protected virtual void ModifyFinalCompileAction(VCCompileAction CompileAction, CppCompileEnvironment CompileEnvironment, FileItem SourceFile, DirectoryReference OutputDir, string ModuleName)
		{
		}

		private Action GenerateParseTimingInfoAction(FileItem SourceFile, FileItem TimingFile, IActionGraphBuilder Graph)
		{
			FileItem TimingJsonFile = FileItem.GetItemByPath(Path.ChangeExtension(TimingFile.AbsolutePath, ".cta"));

			string ParseTimingArguments = $"-TimingFile=\"{TimingFile}\"";
			if (Target.bParseTimingInfoForTracing)
			{
				ParseTimingArguments += " -Tracing";
			}

			Action ParseTimingInfoAction = Graph.CreateRecursiveAction<ParseMsvcTimingInfoMode>(ActionType.ParseTimingInfo, ParseTimingArguments);
			ParseTimingInfoAction.WorkingDirectory = Unreal.EngineSourceDirectory;
			ParseTimingInfoAction.StatusDescription = Path.GetFileName(TimingFile.AbsolutePath);
			ParseTimingInfoAction.bCanExecuteRemotely = true;
			ParseTimingInfoAction.bCanExecuteRemotelyWithSNDBS = true;
			ParseTimingInfoAction.PrerequisiteItems.Add(SourceFile);
			ParseTimingInfoAction.PrerequisiteItems.Add(TimingFile);
			ParseTimingInfoAction.ProducedItems.Add(TimingJsonFile);
			return ParseTimingInfoAction;
		}

		public override void FinalizeOutput(ReadOnlyTargetRules Target, TargetMakefileBuilder MakefileBuilder)
		{
			if (Target.bPrintToolChainTimingInfo || Target.WindowsPlatform.bCompilerTrace)
			{
				TargetMakefile Makefile = MakefileBuilder.Makefile;

				List<FileItem> TimingJsonFiles = new List<FileItem>();

				if (Target.WindowsPlatform.Compiler.IsMSVC())
				{
					List<IExternalAction> ParseTimingActions = Makefile.Actions.Where(x => x.ActionType == ActionType.ParseTimingInfo).ToList();
					TimingJsonFiles = ParseTimingActions.SelectMany(a => a.ProducedItems.Where(i => i.HasExtension(".cta"))).ToList();
				}
				else if (Target.WindowsPlatform.Compiler.IsClang())
				{
					List<IExternalAction> CompileActions = Makefile.Actions.Where(x => x.ActionType == ActionType.Compile && x.ProducedItems.Any(i => i.HasExtension(".json"))).ToList();
					TimingJsonFiles = CompileActions.SelectMany(a => a.ProducedItems.Where(i => i.HasExtension(".json"))).ToList();
				}

				Makefile.OutputItems.AddRange(TimingJsonFiles);

				// Handing generating aggregate timing information if we compiled more than one file.
				if (TimingJsonFiles.Count > 1)
				{
					// Generate the file manifest for the aggregator.
					if (!DirectoryReference.Exists(Makefile.ProjectIntermediateDirectory))
					{
						DirectoryReference.CreateDirectory(Makefile.ProjectIntermediateDirectory);
					}

					if (Target.WindowsPlatform.Compiler.IsMSVC())
					{
						FileReference ManifestFile = FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}TimingManifest.txt");
						File.WriteAllLines(ManifestFile.FullName, TimingJsonFiles.Select(f => f.AbsolutePath));

						FileReference ExpectedCompileTimeFile = FileReference.FromString(Path.Combine(Makefile.ProjectIntermediateDirectory.FullName, $"{Target.Name}.json"));
						List<string> ActionArgs = new List<string>()
						{
							$"-Name={Target.Name}",
							$"-ManifestFile={ManifestFile.FullName}",
							$"-CompileTimingFile={ExpectedCompileTimeFile}",
						};

						Action AggregateTimingInfoAction = MakefileBuilder.CreateRecursiveAction<AggregateParsedTimingInfo>(ActionType.ParseTimingInfo, String.Join(" ", ActionArgs));
						AggregateTimingInfoAction.WorkingDirectory = Unreal.EngineSourceDirectory;
						AggregateTimingInfoAction.StatusDescription = $"Aggregating {TimingJsonFiles.Count} Timing File(s)";
						AggregateTimingInfoAction.bCanExecuteRemotely = false;
						AggregateTimingInfoAction.bCanExecuteRemotelyWithSNDBS = false;
						AggregateTimingInfoAction.PrerequisiteItems.UnionWith(TimingJsonFiles);

						FileItem AggregateOutputFile = FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.cta"));
						AggregateTimingInfoAction.ProducedItems.Add(AggregateOutputFile);
						Makefile.OutputItems.Add(AggregateOutputFile);
					}
					else if (Target.WindowsPlatform.Compiler.IsClang())
					{
						FileReference ManifestFile = FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}TimingManifest.csv");
						File.WriteAllLines(ManifestFile.FullName, TimingJsonFiles.Select(f => f.FullName.Remove(f.FullName.Length - ".json".Length)));

						FileItem AggregateOutputFile = FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.trace.csv"));
						FileItem HeadersOutputFile = FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.headers.csv"));
						List<string> AggregateActionArgs = new List<string>()
						{
							$"-ManifestFile={ManifestFile.FullName}",
							$"-AggregateFile={AggregateOutputFile.FullName}",
							$"-HeadersFile={HeadersOutputFile.FullName}",
						};

						Action AggregateTimingInfoAction = MakefileBuilder.CreateRecursiveAction<AggregateClangTimingInfo>(ActionType.ParseTimingInfo, String.Join(" ", AggregateActionArgs));
						AggregateTimingInfoAction.WorkingDirectory = Unreal.EngineSourceDirectory;
						AggregateTimingInfoAction.StatusDescription = $"Aggregating {TimingJsonFiles.Count} Timing File(s)";
						AggregateTimingInfoAction.bCanExecuteRemotely = false;
						AggregateTimingInfoAction.bCanExecuteRemotelyWithSNDBS = false;
						AggregateTimingInfoAction.PrerequisiteItems.UnionWith(TimingJsonFiles);

						AggregateTimingInfoAction.ProducedItems.Add(AggregateOutputFile);
						AggregateTimingInfoAction.ProducedItems.Add(HeadersOutputFile);
						Makefile.OutputItems.AddRange(AggregateTimingInfoAction.ProducedItems);

						FileItem ArchiveOutputFile = FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.traces.zip"));
						List<string> ArchiveActionArgs = new List<string>()
						{
							$"-ManifestFile={ManifestFile.FullName}",
							$"-ArchiveFile={ArchiveOutputFile.FullName}",
						};

						Action ArchiveTimingInfoAction = MakefileBuilder.CreateRecursiveAction<AggregateClangTimingInfo>(ActionType.ParseTimingInfo, String.Join(" ", ArchiveActionArgs));
						ArchiveTimingInfoAction.WorkingDirectory = Unreal.EngineSourceDirectory;
						ArchiveTimingInfoAction.StatusDescription = $"Archiving {TimingJsonFiles.Count} Timing File(s)";
						ArchiveTimingInfoAction.bCanExecuteRemotely = false;
						ArchiveTimingInfoAction.bCanExecuteRemotelyWithSNDBS = false;
						ArchiveTimingInfoAction.PrerequisiteItems.UnionWith(TimingJsonFiles);

						ArchiveTimingInfoAction.ProducedItems.Add(ArchiveOutputFile);
						Makefile.OutputItems.AddRange(ArchiveTimingInfoAction.ProducedItems);

						// Extract CompileScore data from traces
						FileReference ScoreDataExtractor = FileReference.Combine(Unreal.RootDirectory, "Engine", "Extras", "ThirdPartyNotUE", "CompileScore", "ScoreDataExtractor.exe");
						if (RuntimePlatform.IsWindows && FileReference.Exists(ScoreDataExtractor))
						{
							FileItem CompileScoreOutput = FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.scor"));

							Action CompileScoreExtractorAction = MakefileBuilder.CreateAction(ActionType.ParseTimingInfo);
							CompileScoreExtractorAction.WorkingDirectory = Unreal.EngineSourceDirectory;
							CompileScoreExtractorAction.StatusDescription = $"Extracting CompileScore";
							CompileScoreExtractorAction.bCanExecuteRemotely = false;
							CompileScoreExtractorAction.bCanExecuteRemotelyWithSNDBS = false;
							CompileScoreExtractorAction.PrerequisiteItems.UnionWith(TimingJsonFiles);
							CompileScoreExtractorAction.CommandPath = ScoreDataExtractor;
							CompileScoreExtractorAction.CommandArguments = $"-clang -verbosity 0 -timelinepack 1000000 -extract -i \"{NormalizeCommandLinePath(Makefile.ProjectIntermediateDirectory)}\" -o \"{NormalizeCommandLinePath(CompileScoreOutput)}\"";

							CompileScoreExtractorAction.ProducedItems.Add(CompileScoreOutput);
							CompileScoreExtractorAction.ProducedItems.Add(FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.scor.gbl")));
							CompileScoreExtractorAction.ProducedItems.Add(FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.scor.incl")));
							CompileScoreExtractorAction.ProducedItems.Add(FileItem.GetItemByFileReference(FileReference.Combine(Makefile.ProjectIntermediateDirectory, $"{Target.Name}.scor.t0000")));
							Makefile.OutputItems.AddRange(CompileScoreExtractorAction.ProducedItems);
						}
					}
				}
			}
		}

		public override void PrepareRuntimeDependencies(List<RuntimeDependency> RuntimeDependencies, Dictionary<FileReference, FileReference> TargetFileToSourceFile, DirectoryReference ExeDir)
		{
			// If ASan is enabled we need to copy the companion helper libraries from the MSVC tools bin folder to the
			// target executable folder.
			if (Target.WindowsPlatform.bEnableAddressSanitizer)
			{
				DirectoryReference ASanRuntimeDir;
				String ASanArchSuffix;
				if (EnvVars.Architecture == UnrealArch.X64)
				{
					ASanRuntimeDir = DirectoryReference.Combine(EnvVars.ToolChainDir, "bin", "Hostx64", "x64");
					ASanArchSuffix = "x86_64";
				}
				else
				{
					throw new BuildException("Unsupported build architecture for Address Sanitizer");
				}

				string ASanRuntimeDLL = $"clang_rt.asan_dynamic-{ASanArchSuffix}.dll";
				string ASanDebugRuntimeDLL = $"clang_rt.asan_dbg_dynamic-{ASanArchSuffix}.dll";

				RuntimeDependencies.Add(new RuntimeDependency(FileReference.Combine(ExeDir, ASanRuntimeDLL), StagedFileType.NonUFS));
				TargetFileToSourceFile[FileReference.Combine(ExeDir, ASanRuntimeDLL)] = FileReference.Combine(ASanRuntimeDir, ASanRuntimeDLL);
				if (Target.bDebugBuildsActuallyUseDebugCRT)
				{
					RuntimeDependencies.Add(new RuntimeDependency(FileReference.Combine(ExeDir, ASanDebugRuntimeDLL), StagedFileType.NonUFS));
					TargetFileToSourceFile[FileReference.Combine(ExeDir, ASanDebugRuntimeDLL)] = FileReference.Combine(ASanRuntimeDir, ASanDebugRuntimeDLL);
				}
			}

			// copy PGO support binaries for Windows from $(VC_PGO_RunTime_Dir)
			if (Target.bPGOProfile && Target.Platform.IsInGroup(UnrealPlatformGroup.Windows) && !Target.WindowsPlatform.Compiler.IsIntel())
			{
				string[] PGOFiles = {
					"pgort140.dll",
					"pgosweep.exe",
				};
				foreach (string PGOFile in PGOFiles)
				{
					FileReference SrcFile = FileReference.Combine(EnvVars.ToolChainDir, "bin", "Hostx64", "x64", PGOFile);
					FileReference DstFile = FileReference.Combine(ExeDir, PGOFile);
					TargetFileToSourceFile[DstFile] = SrcFile;
					RuntimeDependencies.Add(new RuntimeDependency(DstFile, StagedFileType.NonUFS));
				}
			}
		}

		public virtual FileReference GetApplicationIcon(FileReference? ProjectFile)
		{
			if (Target.WindowsPlatform.ApplicationIcon != null)
			{
				return new FileReference(Target.WindowsPlatform.ApplicationIcon);
			}
			return WindowsPlatform.GetWindowsApplicationIcon(ProjectFile);
		}

		protected virtual bool UseWindowsArchitecture(UnrealTargetPlatform Platform)
		{
			return Platform.IsInGroup(UnrealPlatformGroup.Windows);
		}

		public override CPPOutput CompileRCFiles(CppCompileEnvironment CompileEnvironment, List<FileItem> InputFiles, DirectoryReference OutputDir, IActionGraphBuilder Graph)
		{
			CPPOutput Result = new CPPOutput();

			foreach (FileItem RCFile in InputFiles)
			{
				Action CompileAction = Graph.CreateAction(ActionType.Compile);
				CompileAction.CommandDescription = "Resource";
				CompileAction.WorkingDirectory = Unreal.EngineSourceDirectory;
				CompileAction.CommandPath = EnvVars.ResourceCompilerPath;
				CompileAction.StatusDescription = Path.GetFileName(RCFile.AbsolutePath);
				CompileAction.PrerequisiteItems.UnionWith(CompileEnvironment.ForceIncludeFiles);
				CompileAction.PrerequisiteItems.UnionWith(CompileEnvironment.AdditionalPrerequisites);

				// Resource tool can run remotely if possible
				CompileAction.bCanExecuteRemotely = true;
				CompileAction.bCanExecuteRemotelyWithSNDBS = false; // no tool template for SN-DBS results in warnings

				List<string> Arguments = new List<string>();

				// Suppress header spew
				Arguments.Add("/nologo");

				// If we're compiling for 64-bit Windows, also add the _WIN64 definition to the resource
				// compiler so that we can switch on that in the .rc file using #ifdef.
				AddDefinition(Arguments, "_WIN64");

				// Language
				Arguments.Add("/l 0x409");

				// Include paths. Don't use AddIncludePath() here, since it uses the full path and exceeds the max command line length.
				foreach (DirectoryReference IncludePath in CompileEnvironment.UserIncludePaths)
				{
					Arguments.Add($"/I \"{NormalizeCommandLinePath(IncludePath)}\"");
				}

				// System include paths.
				foreach (DirectoryReference SystemIncludePath in CompileEnvironment.SystemIncludePaths)
				{
					Arguments.Add($"/I \"{NormalizeCommandLinePath(SystemIncludePath)}\"");
				}
				foreach (DirectoryReference SystemIncludePath in EnvVars.IncludePaths)
				{
					Arguments.Add($"/I \"{NormalizeCommandLinePath(SystemIncludePath)}\"");
				}

				// Preprocessor definitions.
				foreach (string Definition in CompileEnvironment.Definitions)
				{
					if (!Definition.Contains("_API"))
					{
						AddDefinition(Arguments, Definition);
					}
				}

				// Figure the icon to use. We can only use a custom icon when compiling to a project-specific intermediate directory (and not for the shared editor executable, for example).
				FileReference IconFile;
				if (Target.ProjectFile != null && !CompileEnvironment.bUseSharedBuildEnvironment)
				{
					IconFile = GetApplicationIcon(Target.ProjectFile);
				}
				else
				{
					IconFile = GetApplicationIcon(null);
				}
				CompileAction.PrerequisiteItems.Add(FileItem.GetItemByFileReference(IconFile));

				// Setup the compile environment, setting the icon to use via a macro. This is used in Default.rc2.
				AddDefinition(Arguments, $"BUILD_ICON_FILE_NAME=\"\\\"{NormalizeCommandLinePath(IconFile).Replace("\\", "\\\\")}\\\"\"");

				// Apply the target settings for the resources
				if (!CompileEnvironment.bUseSharedBuildEnvironment)
				{
					if (!String.IsNullOrEmpty(Target.WindowsPlatform.CompanyName))
					{
						AddDefinition(Arguments, $"PROJECT_COMPANY_NAME={SanitizeMacroValue(Target.WindowsPlatform.CompanyName)}");
					}

					if (!String.IsNullOrEmpty(Target.WindowsPlatform.CopyrightNotice))
					{
						AddDefinition(Arguments, $"PROJECT_COPYRIGHT_STRING={SanitizeMacroValue(Target.WindowsPlatform.CopyrightNotice)}");
					}

					if (!String.IsNullOrEmpty(Target.WindowsPlatform.ProductName))
					{
						AddDefinition(Arguments, $"PROJECT_PRODUCT_NAME={SanitizeMacroValue(Target.WindowsPlatform.ProductName)}");
					}

					if (Target.ProjectFile != null)
					{
						AddDefinition(Arguments, $"PROJECT_PRODUCT_IDENTIFIER={SanitizeMacroValue(Target.ProjectFile.GetFileNameWithoutExtension())}");
					}
				}

				// Add the RES file to the produced item list.
				FileItem CompiledResourceFile = FileItem.GetItemByFileReference(
					FileReference.Combine(
						OutputDir,
						Path.GetFileName(RCFile.AbsolutePath) + ".res"
						)
					);
				CompileAction.ProducedItems.Add(CompiledResourceFile);
				Arguments.Add($"/fo \"{NormalizeCommandLinePath(CompiledResourceFile)}\"");
				Result.ObjectFiles.Add(CompiledResourceFile);

				// Add the RC file as a prerequisite of the action.
				Arguments.Add($"\"{NormalizeCommandLinePath(RCFile)}\"");

				// Create a response file for the resource compilier
				FileItem ResponseFile = FileItem.GetItemByFileReference(GetResponseFileName(CompileEnvironment, CompiledResourceFile));
				Graph.CreateIntermediateTextFile(ResponseFile, Arguments);
				CompileAction.PrerequisiteItems.Add(ResponseFile);

				/* rc.exe currently errors when using a response file
				string ResponseFileString = NormalizeCommandLinePath(ResponseFile);

				// cl.exe can't handle response files with a path longer than 260 characters, and relative paths can push it over the limit
				if (!System.IO.Path.IsPathRooted(ResponseFileString) && System.IO.Path.Combine(CompileAction.WorkingDirectory.FullName, ResponseFileString).Length > 260)
				{
					ResponseFileString = ResponseFile.FullName;
				}

				CompileAction.CommandArguments = $"@{Utils.MakePathSafeToUseWithCommandLine(ResponseFileString)}";
				*/
				CompileAction.CommandArguments = String.Join(" ", Arguments);

				// Add the C++ source file and its included files to the prerequisite item list.
				CompileAction.PrerequisiteItems.Add(RCFile);
			}

			return Result;
		}

		public override void GenerateTypeLibraryHeader(CppCompileEnvironment CompileEnvironment, ModuleRules.TypeLibrary TypeLibrary, FileReference OutputFile, IActionGraphBuilder Graph)
		{
			// Create the input file
			StringBuilder Contents = new StringBuilder();
			Contents.AppendLine("#include <windows.h>");
			Contents.AppendLine("#include <unknwn.h>");
			Contents.AppendLine();

			Contents.AppendFormat("#import \"{0}\"", TypeLibrary.FileName);
			if (!String.IsNullOrEmpty(TypeLibrary.Attributes))
			{
				Contents.Append(' ');
				Contents.Append(TypeLibrary.Attributes);
			}
			Contents.AppendLine();

			FileItem InputFile = Graph.CreateIntermediateTextFile(OutputFile.ChangeExtension(".cpp"), Contents.ToString());

			// Build the argument list for the compiler
			FileItem ObjectFile = FileItem.GetItemByFileReference(OutputFile.ChangeExtension(".obj"));

			List<string> Arguments = new List<string>
			{
				$"\"{InputFile.Location}\"",
				"/c",
				"/nologo",
				$"/Fo\"{ObjectFile.Location}\""
			};

			foreach (DirectoryReference IncludePath in CompileEnvironment.UserIncludePaths)
			{
				AddIncludePath(Arguments, IncludePath, EnvVars.ToolChain);
			}

			foreach (DirectoryReference IncludePath in CompileEnvironment.SystemIncludePaths)
			{
				AddSystemIncludePath(Arguments, IncludePath, EnvVars.ToolChain);
			}

			foreach (DirectoryReference IncludePath in EnvVars.IncludePaths)
			{
				AddSystemIncludePath(Arguments, IncludePath, EnvVars.ToolChain);
			}

			FileItem ResponseFile = Graph.CreateIntermediateTextFile(OutputFile.ChangeExtension(ResponseExt), String.Join(" ", Arguments));

			// Build the command for touching the output file to update the time stamp
			string TouchActionCommand;
			if (BuildHostPlatform.Current.IsRunningOnWine())
			{
				TouchActionCommand = $"copy /Y \"{OutputFile.FullName}\" \"%TMP%/{OutputFile.GetFileName()}\" && type \"%TMP%/{OutputFile.GetFileName()}\" > \"{OutputFile.FullName}\"";
			}
			else
			{
				TouchActionCommand = $"copy /b \"{OutputFile.FullName}\"+,, \"{OutputFile.FullName}\"";
			}

			// Build the batch file
			StringBuilder BatchFileContents = new();
			BatchFileContents.AppendLine("@echo off");
			BatchFileContents.AppendLine($"\"{EnvVars.ToolchainCompilerPath}\" @\"{ResponseFile.FullName}\"");
			BatchFileContents.AppendLine(TouchActionCommand);

			FileItem BatchFile = Graph.CreateIntermediateTextFile(OutputFile.ChangeExtension(".bat"), BatchFileContents.ToString());

			// Create the batch file action that will compile then touch the output file
			Action CompileAction = Graph.CreateAction(ActionType.Compile);
			CompileAction.CommandDescription = "GenerateTLH";
			CompileAction.PrerequisiteItems.Add(FileItem.GetItemByFileReference(EnvVars.ToolchainCompilerPath));
			CompileAction.PrerequisiteItems.Add(InputFile);
			CompileAction.PrerequisiteItems.Add(ResponseFile);
			CompileAction.PrerequisiteItems.Add(BatchFile);
			CompileAction.ProducedItems.Add(ObjectFile);
			CompileAction.ProducedItems.Add(FileItem.GetItemByFileReference(OutputFile));
			CompileAction.DeleteItems.Add(FileItem.GetItemByFileReference(OutputFile));
			CompileAction.StatusDescription = TypeLibrary.Header;
			CompileAction.WorkingDirectory = Unreal.EngineSourceDirectory;
			CompileAction.CommandPath = BuildHostPlatform.Current.Shell;
			CompileAction.CommandArguments = $"/C \"{BatchFile}\"";
			CompileAction.CommandVersion = EnvVars.ToolChainVersion.ToString();
			CompileAction.bShouldOutputStatusDescription = false;
			CompileAction.bCanExecuteRemotely = false; // Incompatible with remote distribution
		}

		public override CppCompileEnvironment CreateSharedResponseFile(CppCompileEnvironment CompileEnvironment, FileReference OutResponseFile, IActionGraphBuilder Graph)
		{
			CppCompileEnvironment NewCompileEnvironment = new CppCompileEnvironment(CompileEnvironment);
			List<string> Arguments = new List<string>();

			foreach (DirectoryReference IncludePath in NewCompileEnvironment.UserIncludePaths)
			{
				AddIncludePath(Arguments, IncludePath, Target.WindowsPlatform.Compiler);
			}

			foreach (DirectoryReference IncludePath in NewCompileEnvironment.SystemIncludePaths)
			{
				AddSystemIncludePath(Arguments, IncludePath, Target.WindowsPlatform.Compiler);
			}

			foreach (DirectoryReference IncludePath in EnvVars.IncludePaths)
			{
				AddSystemIncludePath(Arguments, IncludePath, Target.WindowsPlatform.Compiler);
			}

			// Stash shared include paths for validation purposes
			NewCompileEnvironment.SharedUserIncludePaths = new(NewCompileEnvironment.UserIncludePaths);
			NewCompileEnvironment.SharedSystemIncludePaths = new(NewCompileEnvironment.SystemIncludePaths);
			NewCompileEnvironment.SharedSystemIncludePaths.UnionWith(EnvVars.IncludePaths);

			NewCompileEnvironment.UserIncludePaths.Clear();
			NewCompileEnvironment.SystemIncludePaths.Clear();

			FileItem FileItem = FileItem.GetItemByFileReference(OutResponseFile);
			Graph.CreateIntermediateTextFile(FileItem, Arguments);

			NewCompileEnvironment.AdditionalPrerequisites.Add(FileItem);
			NewCompileEnvironment.AdditionalResponseFiles.Add(FileItem);
			NewCompileEnvironment.bHasSharedResponseFile = true;

			return NewCompileEnvironment;
		}

		public override void CreateSpecificFileAction(CppCompileEnvironment CompileEnvironment, DirectoryReference SourceDir, DirectoryReference OutputDir, IActionGraphBuilder Graph)
		{
			// This is not supported for now.. If someone wants it we can implement it
			if (CompileEnvironment.Architectures.bIsMultiArch)
			{
				return;
			}

			VCCompileAction BaseCompileAction = CreateBaseCompileAction(CompileEnvironment);
			AppendCLArguments_CPP(CompileEnvironment, BaseCompileAction.Arguments);
			BaseCompileAction.AdditionalPrerequisiteItems.AddRange(CompileEnvironment.AdditionalPrerequisites); // Primarily for ispc.generated.h files
			Graph.AddAction(new VcSpecificFileAction(SourceDir, OutputDir, BaseCompileAction, CompileEnvironment));
		}

		/// <summary>
		/// Macros passed via the command line have their quotes stripped, and are tokenized before being re-stringized by the compiler. This conversion
		/// back and forth is normally ok, but certain characters such as single quotes must always be paired. Remove any such characters here.
		/// </summary>
		/// <param name="Value">The macro value</param>
		/// <returns>The sanitized value</returns>
		static string SanitizeMacroValue(string Value)
		{
			StringBuilder Result = new StringBuilder(Value.Length);
			for (int Idx = 0; Idx < Value.Length; Idx++)
			{
				if (Value[Idx] != '\'' && Value[Idx] != '\"')
				{
					Result.Append(Value[Idx]);
				}
			}
			return Result.ToString();
		}

		public override FileItem[] LinkImportLibrary(LinkEnvironment LinkEnvironment, IActionGraphBuilder Graph)
		{
			if (LinkEnvironment.bIsCrossReferenced)
			{
				return LinkAllFiles(LinkEnvironment, true, Graph);
			}
			// by default do nothing
			return new FileItem[] { };
		}

		public override FileItem LinkFiles(LinkEnvironment LinkEnvironment, bool bBuildImportLibraryOnly, IActionGraphBuilder Graph)
		{
			if (LinkEnvironment.bIsBuildingDotNetAssembly)
			{
				return FileItem.GetItemByFileReference(LinkEnvironment.OutputFilePath);
			}

			bool bIsBuildingLibraryOrImportLibrary = LinkEnvironment.bIsBuildingLibrary || bBuildImportLibraryOnly;

			// Get link arguments.
			List<string> Arguments = new List<string>();
			if (bIsBuildingLibraryOrImportLibrary)
			{
				AppendLibArguments(LinkEnvironment, Arguments);
			}
			else
			{
				AppendLinkArguments(LinkEnvironment, Arguments);
			}

			if (Target.WindowsPlatform.Compiler.IsMSVC() && LinkEnvironment.bPrintTimingInfo)
			{
				Arguments.Add("/time+");
			}

			// If we're only building an import library, add the '/DEF' option that tells the LIB utility
			// to simply create a .LIB file and .EXP file, and don't bother validating imports
			if (bBuildImportLibraryOnly)
			{
				Arguments.Add("/DEF");

				// Ensure that the import library references the correct filename for the linked binary.
				Arguments.Add($"/NAME:\"{LinkEnvironment.OutputFilePath.GetFileName()}\"");

				// Ignore warnings about object files with no public symbols.
				Arguments.Add("/IGNORE:4221");
			}

			if (!bIsBuildingLibraryOrImportLibrary)
			{
				// Delay-load these DLLs.
				foreach (string DelayLoadDLL in LinkEnvironment.DelayLoadDLLs.Distinct())
				{
					Arguments.Add($"/DELAYLOAD:\"{DelayLoadDLL}\"");
				}

				// Pass the module definition file to the linker if we have one
				if (LinkEnvironment.ModuleDefinitionFile != null && LinkEnvironment.ModuleDefinitionFile.Length > 0)
				{
					Arguments.Add($"/DEF:\"{LinkEnvironment.ModuleDefinitionFile}\"");
				}
			}

			// Set up the library paths for linking this binary
			if (bBuildImportLibraryOnly)
			{
				// When building an import library, ignore all the libraries included via embedded #pragma lib declarations.
				// We shouldn't need them to generate exports.
				Arguments.Add("/NODEFAULTLIB");
			}
			else if (!LinkEnvironment.bIsBuildingLibrary)
			{
				// Add the library paths to the argument list.
				foreach (DirectoryReference LibraryPath in LinkEnvironment.SystemLibraryPaths)
				{
					Arguments.Add($"/LIBPATH:\"{NormalizeCommandLinePath(LibraryPath)}\"");
				}
				foreach (DirectoryReference LibraryPath in EnvVars.LibraryPaths)
				{
					Arguments.Add($"/LIBPATH:\"{NormalizeCommandLinePath(LibraryPath)}\"");
				}

				// Add the excluded default libraries to the argument list.
				foreach (string ExcludedLibrary in LinkEnvironment.ExcludedLibraries)
				{
					Arguments.Add($"/NODEFAULTLIB:\"{ExcludedLibrary}\"");
				}
			}

			// If we're building either an executable or a DLL, make sure we link in the 
			// correct address sanitizer helper libs.
			// Note: As of MSVC 16.9, this is automatically done if the /fsanitize=address flag is used.
			if (!bBuildImportLibraryOnly && !LinkEnvironment.bIsBuildingLibrary && Target.WindowsPlatform.bEnableAddressSanitizer &&
				EnvVars.CompilerVersion < new VersionNumber(14, 28, 0))
			{
				String ASanArchSuffix = "";
				if (EnvVars.Architecture == UnrealArch.X64)
				{
					ASanArchSuffix = "x86_64";
				}
				else
				{
					throw new BuildException("Unsupported build architecture for Address Sanitizer");
				}

				String ASanDebugInfix = "";
				if (LinkEnvironment.bUseDebugCRT)
				{
					ASanDebugInfix = "_dbg";
				}

				if (LinkEnvironment.bUseStaticCRT)
				{
					if (LinkEnvironment.bIsBuildingDLL)
					{
						Arguments.Add($"/wholearchive:clang_rt.asan{ASanDebugInfix}_dll_thunk-{ASanArchSuffix}.lib");
					}
					else
					{
						Arguments.Add($"/wholearchive:clang_rt.asan{ASanDebugInfix}-{ASanArchSuffix}.lib");
						Arguments.Add($"/wholearchive:clang_rt.asan_cxx{ASanDebugInfix}-{ASanArchSuffix}.lib");
					}
				}
				else
				{
					Arguments.Add($"/wholearchive:clang_rt.asan{ASanDebugInfix}_dynamic-{ASanArchSuffix}.lib");
					Arguments.Add($"/wholearchive:clang_rt.asan{ASanDebugInfix}_dynamic_runtime_thunk-{ASanArchSuffix}.lib");
				}
			}

			// Enable function level hot-patching
			if (!bBuildImportLibraryOnly && Target.WindowsPlatform.bCreateHotpatchableImage)
			{
				Arguments.Add("/FUNCTIONPADMIN:6"); // For some reason, not providing the number causes full linking to happen all the time
			}

			// For targets that are cross-referenced, we don't want to write a LIB file during the link step as that
			// file will clobber the import library we went out of our way to generate during an earlier step.  This
			// file is not needed for our builds, but there is no way to prevent MSVC from generating it when
			// linking targets that have exports.  We don't want this to clobber our LIB file and invalidate the
			// existing timstamp, so instead we simply emit it with a different name
			FileReference ImportLibraryFilePath;
			if (LinkEnvironment.bIsCrossReferenced && !bBuildImportLibraryOnly)
			{
				ImportLibraryFilePath = FileReference.Combine(LinkEnvironment.IntermediateDirectory!, LinkEnvironment.OutputFilePath.GetFileNameWithoutExtension() + ".sup.lib");
			}
			else if (Target.bShouldCompileAsDLL)
			{
				ImportLibraryFilePath = FileReference.Combine(LinkEnvironment.OutputDirectory!, LinkEnvironment.OutputFilePath.GetFileNameWithoutExtension() + ".lib");
			}
			else
			{
				ImportLibraryFilePath = FileReference.Combine(LinkEnvironment.IntermediateDirectory!, LinkEnvironment.OutputFilePath.GetFileNameWithoutExtension() + ".lib");
			}

			FileItem OutputFile;
			if (bBuildImportLibraryOnly)
			{
				OutputFile = FileItem.GetItemByFileReference(ImportLibraryFilePath);
			}
			else
			{
				OutputFile = FileItem.GetItemByFileReference(LinkEnvironment.OutputFilePath);
			}

			List<FileItem> ProducedItems = new List<FileItem>();
			ProducedItems.Add(OutputFile);

			List<FileItem> PrerequisiteItems = new List<FileItem>();

			// Add the input files to a response file, and pass the response file on the command-line.
			List<string> InputFileNames = new List<string>();
			foreach (FileItem InputFile in LinkEnvironment.InputFiles)
			{
				InputFileNames.Add(String.Format("\"{0}\"", NormalizeCommandLinePath(InputFile)));
				PrerequisiteItems.Add(InputFile);
			}

			if (!bIsBuildingLibraryOrImportLibrary)
			{
				foreach (FileReference Library in LinkEnvironment.Libraries)
				{
					InputFileNames.Add(String.Format("\"{0}\"", NormalizeCommandLinePath(Library)));
					PrerequisiteItems.Add(FileItem.GetItemByFileReference(Library));
				}
				foreach (string SystemLibrary in LinkEnvironment.SystemLibraries)
				{
					InputFileNames.Add(String.Format("\"{0}\"", SystemLibrary));
				}

				foreach (FileItem NatvisFile in LinkEnvironment.DebuggerVisualizerFiles)
				{
					PrerequisiteItems.Add(NatvisFile);
					Arguments.Add(String.Format("/NATVIS:\"{0}\"",
						NormalizeCommandLinePath(NatvisFile)));
				}

				if (Target.WindowsPlatform.ManifestFile != null)
				{
					PrerequisiteItems.Add(FileItem.GetItemByPath(Target.WindowsPlatform.ManifestFile));
				}
			}

			Arguments.AddRange(InputFileNames);

			// Add the output file to the command-line.
			Arguments.Add($"/OUT:\"{NormalizeCommandLinePath(OutputFile)}\"");

			// For import libraries and exports generated by cross-referenced builds, we don't track output files. VS 15.3+ doesn't touch timestamps for libs
			// and exp files with no modifications, breaking our dependency checking, but incremental linking will fall back to a full link if we delete it.
			// Since all DLLs are typically marked as cross referenced now anyway, we can just ignore this file to allow incremental linking to work.
			if (LinkEnvironment.bHasExports && !LinkEnvironment.bIsBuildingLibrary && !LinkEnvironment.bIsCrossReferenced)
			{
				FileReference ExportFilePath = ImportLibraryFilePath.ChangeExtension(".exp");
				FileItem ExportFile = FileItem.GetItemByFileReference(ExportFilePath);
				ProducedItems.Add(ExportFile);
			}

			if (!bIsBuildingLibraryOrImportLibrary)
			{
				// There is anything to export
				if (LinkEnvironment.bHasExports)
				{
					// Write the import library to the output directory for nFringe support.
					FileItem ImportLibraryFile = FileItem.GetItemByFileReference(ImportLibraryFilePath);
					Arguments.Add($"/IMPLIB:\"{NormalizeCommandLinePath(ImportLibraryFilePath)}\"");

					// Like the export file above, don't add the import library as a produced item when it's cross referenced.
					if (!LinkEnvironment.bIsCrossReferenced)
					{
						ProducedItems.Add(ImportLibraryFile);
					}
				}

				if (LinkEnvironment.bCreateDebugInfo)
				{
					// Write the PDB file to the output directory.
					{
						FileReference? PDBFilePath = FileReference.Combine(LinkEnvironment.OutputDirectory!, Path.GetFileNameWithoutExtension(OutputFile.AbsolutePath) + ".pdb");

						// If stripping private symbols, write the full pdb as .full.pdb
						if (Target.WindowsPlatform.bStripPrivateSymbols)
						{
							FileItem StrippedPDBFile = FileItem.GetItemByFileReference(PDBFilePath);
							Arguments.Add($"/PDBSTRIPPED:\"{NormalizeCommandLinePath(StrippedPDBFile)}\"");
							ProducedItems.Add(StrippedPDBFile);

							PDBFilePath = FileReference.Combine(LinkEnvironment.OutputDirectory!, Path.GetFileNameWithoutExtension(OutputFile.AbsolutePath) + ".full.pdb");
						}

						FileItem PDBFile = FileItem.GetItemByFileReference(PDBFilePath);
						Arguments.Add($"/PDB:\"{NormalizeCommandLinePath(PDBFilePath)}\"");
						ProducedItems.Add(PDBFile);
					}

					// Write the MAP file to the output directory.
					if (LinkEnvironment.bCreateMapFile)
					{
						FileReference MAPFilePath = FileReference.Combine(LinkEnvironment.OutputDirectory!, Path.GetFileNameWithoutExtension(OutputFile.AbsolutePath) + ".map");
						FileItem MAPFile = FileItem.GetItemByFileReference(MAPFilePath);
						Arguments.Add($"/MAP:\"{NormalizeCommandLinePath(MAPFilePath)}\"");
						ProducedItems.Add(MAPFile);

						// Export a list of object file paths, so we can locate the object files referenced by the map file
						ExportObjectFilePaths(LinkEnvironment, Path.ChangeExtension(MAPFilePath.FullName, ".objpaths"), EnvVars);
					}
				}

				// Add the additional arguments specified by the environment.
				if (!String.IsNullOrEmpty(LinkEnvironment.AdditionalArguments))
				{
					Arguments.Add(LinkEnvironment.AdditionalArguments.Trim());
				}
			}

			// Add any forced references to functions
			foreach (string IncludeFunction in LinkEnvironment.IncludeFunctions)
			{
				Arguments.Add($"/INCLUDE:{IncludeFunction}");
			}

			// Allow the toolchain to adjust/process the link arguments
			ModifyFinalLinkArguments(LinkEnvironment, Arguments, bBuildImportLibraryOnly);

			// Create a response file for the linker, unless we're generating IntelliSense data
			FileReference ResponseFileName = GetResponseFileName(LinkEnvironment, OutputFile);
			if (!ProjectFileGenerator.bGenerateProjectFiles)
			{
				FileItem ResponseFile = Graph.CreateIntermediateTextFile(ResponseFileName, Arguments);
				PrerequisiteItems.Add(ResponseFile);
			}

			// Create an action that invokes the linker.
			Action LinkAction = Graph.CreateAction(ActionType.Link);
			string ReadableArch = UnrealArchitectureConfig.ForPlatform(LinkEnvironment.Platform).ConvertToReadableArchitecture(LinkEnvironment.Architecture);
			LinkAction.CommandDescription += $"Link [{ReadableArch}]";
			LinkAction.WorkingDirectory = Unreal.EngineSourceDirectory;
			if (bIsBuildingLibraryOrImportLibrary)
			{
				LinkAction.CommandPath = EnvVars.LibraryManagerPath;
			}
			else
			{
				LinkAction.CommandPath = EnvVars.LinkerPath;
			}
			LinkAction.CommandArguments = $"@\"{ResponseFileName}\"";
			LinkAction.CommandVersion = EnvVars.ToolChainVersion.ToString();
			LinkAction.ProducedItems.UnionWith(ProducedItems);
			LinkAction.PrerequisiteItems.UnionWith(PrerequisiteItems);
			LinkAction.StatusDescription = Path.GetFileName(OutputFile.AbsolutePath);

			// VS 15.3+ does not touch lib files if they do not contain any modifications, but we need to ensure the timestamps are updated to avoid repeatedly building them.
			if (bBuildImportLibraryOnly || (LinkEnvironment.bHasExports && !bIsBuildingLibraryOrImportLibrary))
			{
				LinkAction.DeleteItems.UnionWith(LinkAction.ProducedItems.Where(x => x.Location.HasExtension(".lib") || x.Location.HasExtension(".exp")));
			}

			// Delete PDB files for all produced items, since incremental updates are slower than full ones.
			if (!LinkEnvironment.bUseIncrementalLinking)
			{
				LinkAction.DeleteItems.UnionWith(LinkAction.ProducedItems.Where(x => x.Location.HasExtension(".pdb") || x.Location.HasExtension(".full.pdb")));
			}

			// Delete any .sup.lib files before building, even if they're not tracked
			if (ImportLibraryFilePath.GetFileName().EndsWith(".sup.lib", StringComparison.OrdinalIgnoreCase))
			{
				FileReference ExportFilePath = ImportLibraryFilePath.ChangeExtension(".exp");
				LinkAction.DeleteItems.Add(FileItem.GetItemByFileReference(ImportLibraryFilePath));
				LinkAction.DeleteItems.Add(FileItem.GetItemByFileReference(ExportFilePath));
			}

			// Tell the action that we're building an import library here and it should conditionally be
			// ignored as a prerequisite for other actions
			LinkAction.bProducesImportLibrary = bBuildImportLibraryOnly || LinkEnvironment.bIsBuildingDLL;

			// Allow remote linking. Note that this may be overriden by the executor (eg. XGE.bAllowRemoteLinking)
			LinkAction.bCanExecuteRemotely = true;

			Logger.LogDebug("     Linking: {StatusDescription}", LinkAction.StatusDescription);
			Logger.LogDebug("     Command: {CommandArguments}", LinkAction.CommandArguments);

			return OutputFile;
		}

		protected bool PreparePGOFiles(LinkEnvironment LinkEnvironment)
		{
			if (LinkEnvironment.bPGOOptimize && LinkEnvironment.OutputFilePath.FullName.EndsWith(".exe"))
			{
				// The linker expects the .pgd and any .pgc files to be in the output directory.
				// Copy the files there and make them writable...
				Logger.LogInformation("...copying the profile guided optimization files to output directory...");

				// prefer a PGD file that matches the output file
				string PGDFile = Path.Combine(LinkEnvironment.PGODirectory!, LinkEnvironment.PGOFilenamePrefix + ".pgd");
				if (!File.Exists(PGDFile))
				{
					string[] PGDFiles = Directory.GetFiles(LinkEnvironment.PGODirectory!, "*.pgd");
					if (PGDFiles.Length > 1)
					{
						throw new BuildException("More than one .pgd file found in \"{0}\" and \"{1}\" not found ", LinkEnvironment.PGODirectory, PGDFile);
					}
					else if (PGDFiles.Length == 0)
					{
						Logger.LogWarning("No .pgd files found in \"{PgoDir}\".", LinkEnvironment.PGODirectory);
						return false;
					}

					PGDFile = PGDFiles.First();
				}

				string[] PGCFiles = Directory.GetFiles(LinkEnvironment.PGODirectory!, "*.pgc");
				if (PGCFiles.Length == 0)
				{
					Logger.LogWarning("No .pgc files found in \"{PgoDir}\".", LinkEnvironment.PGODirectory);
					return false;
				}

				// Make sure the destination directory exists!
				Directory.CreateDirectory(LinkEnvironment.OutputDirectory!.FullName);

				// Copy the .pgd to the linker output directory, renaming it to match the PGO filename prefix.
				string DestPGDFile = Path.Combine(LinkEnvironment.OutputDirectory.FullName, LinkEnvironment.PGOFilenamePrefix + ".pgd");
				Logger.LogInformation("{Source} -> {Target}", PGDFile, DestPGDFile);
				File.Copy(PGDFile, DestPGDFile, true);
				File.SetAttributes(DestPGDFile, FileAttributes.Normal);

				// Copy the *!n.pgc files (where n is an integer), renaming them to match the PGO filename prefix and ensuring they are numbered sequentially
				int PGCFileIndex = 0;
				foreach (string SrcFilePath in PGCFiles)
				{
					string DestFileName = String.Format("{0}!{1}.pgc", LinkEnvironment.PGOFilenamePrefix, ++PGCFileIndex);
					string DestFilePath = Path.Combine(LinkEnvironment.OutputDirectory.FullName, DestFileName);

					Logger.LogInformation("{Source} -> {Target}", SrcFilePath, DestFilePath);
					File.Copy(SrcFilePath, DestFilePath, true);
					File.SetAttributes(DestFilePath, FileAttributes.Normal);
				}
			}

			return true;
		}

		static readonly string[] PGOIgnoredLinkerSwitch =
		{
			"/USEPROFILE",
			"/GENPROFILE",
			"/FASTGENPROFILE",
			"/OUT:",
			"/PDB:",
			"/PDBSTRIPPED:",
			"/NOLOGO",
			"/LIBPATH:",
			"/errorReport:",
			"/d2:",
			"/NATVIS:",
			"/experimental:deterministic"
		};
		private IEnumerable<string> RemovePGOIgnoredSwitches(IEnumerable<string> SourceArguments)
		{
			return SourceArguments.Where(Argument => Argument.StartsWith("/") && !PGOIgnoredLinkerSwitch.Any(Switch => Argument.StartsWith(Switch, StringComparison.OrdinalIgnoreCase)));
		}

		protected virtual void AddPGOLinkArguments(LinkEnvironment LinkEnvironment, List<string> Arguments)
		{
			bool bPGOOptimize = LinkEnvironment.bPGOOptimize;
			bool bPGOProfile = LinkEnvironment.bPGOProfile;

			if (!Target.WindowsPlatform.Compiler.IsIntel())
			{
				if (bPGOOptimize || bPGOProfile)
				{
					// serialize the linker arguments for comparing /USEPROFILE and /GENPROFILE
					IEnumerable<string> PGOArguments = RemovePGOIgnoredSwitches(Arguments);
					string PGOProfileLinkArgsFile = Path.Combine(LinkEnvironment.PGODirectory!, "PGOProfileLinkArgs.txt");

					if (bPGOProfile)
					{
						// save the latest linker arguments for /GENPROFILE alongside the PGO data
						Utils.WriteFileIfChanged(new FileReference(PGOProfileLinkArgsFile), PGOArguments, Logger);
					}
					else if (bPGOOptimize && Target.WindowsPlatform.bIgnoreStalePGOData && File.Exists(PGOProfileLinkArgsFile))
					{
						// compare latest linker arguments for /USEPROFILE to the last known /GENPROFILE and disable /USEPROFILE if they are different to avoid LNK1268
						IEnumerable<string> PGOProfileLinkArgs = RemovePGOIgnoredSwitches(File.ReadAllLines(PGOProfileLinkArgsFile));
						IEnumerable<string> AddedPGOArgs = PGOArguments.Except(PGOProfileLinkArgs);
						IEnumerable<string> RemovedPGOArgs = PGOProfileLinkArgs.Except(PGOArguments);

						if (AddedPGOArgs.Any() || RemovedPGOArgs.Any())
						{
							Logger.LogWarning("PGO Profile and PGO Optimize linker arguments do not match. Profile Guided Optimization will be disabled for {Config} until new PGO Profile data is generated.", Target.Configuration.ToString());
							bPGOOptimize = false;

							if (AddedPGOArgs.Any())
							{
								Logger.LogInformation("Added PGO args:");
								foreach (string Arg in AddedPGOArgs)
								{
									Logger.LogInformation("  {Arg}", Arg);
								}
							}
							if (RemovedPGOArgs.Any())
							{
								Logger.LogInformation("Removed PGO args:");
								foreach (string Arg in RemovedPGOArgs)
								{
									Logger.LogInformation("  {Arg}", Arg);
								}
							}
						}
					}
				}

				// If PGO was requested, apply Link-time code generation even if PGO was disabled due to mismatched args
				if (LinkEnvironment.bPGOOptimize || LinkEnvironment.bPGOProfile)
				{
					if (!Arguments.Contains("/LTCG"))
					{
						Arguments.Add("/LTCG");
					}
					Log.TraceInformationOnce("Enabling Link-time code generation (LTGC) as Profile Guided Optimization (PGO) was requested. Linking will take a while.");
				}

				if (bPGOOptimize)
				{
					if (PreparePGOFiles(LinkEnvironment))
					{
						Arguments.Add("/USEPROFILE");
						Log.TraceInformationOnce("Enabling using Profile Guided Optimization (PGO). Linking will take a while.");
					}
					else
					{
						Logger.LogWarning("Unable to prepare Profile Guided Optimization (PGO) files. Using PGO Optimize build will be disabled.");
						bPGOOptimize = false;
					}
				}
				else if (bPGOProfile)
				{
					if (Target.WindowsPlatform.bUseFastGenProfile)
					{
						Log.TraceInformationOnce("Enabling generating Fastgen Profile Guided Optimization (PGO). Linking will take a while.");
						Arguments.Add("/FASTGENPROFILE");
					}
					else
					{
						if (Target.WindowsPlatform.bPGONoExtraCounters)
						{
							Log.TraceInformationOnce("Enabling generating Profile Guided Optimization No Extra Counters (PGO). Linking will take a while.");
							Arguments.Add("/GENPROFILE:NOTRACKEH");
						}
						else
						{
							Log.TraceInformationOnce("Enabling generating Profile Guided Optimization (PGO). Linking will take a while.");
							Arguments.Add("/GENPROFILE");
						}
					}
				}
			}
			else
			{
				// No link arguments used for PGO on Intel compiler
				if (bPGOOptimize)
				{
					Log.TraceInformationOnce("Enabling Profile Guided Optimization (PGO) on Intel Compiler. Linking will take a while.");
				}
				else if (bPGOProfile)
				{
					Log.TraceInformationOnce("Enabling generating Profile Guided Optimization (PGO) on Intel Compiler. Linking will take a while.");
				}
			}
		}

		protected virtual void ModifyFinalLinkArguments(LinkEnvironment LinkEnvironment, List<string> Arguments, bool bBuildImportLibraryOnly)
		{
			// IMPLEMENT_MODULE_ is not required - it only exists to ensure developers add an IMPLEMENT_MODULE() declaration in code. These are always removed for PGO so that adding/removing a module won't invalidate PGC data.
			Arguments.RemoveAll(Argument => Argument.StartsWith("/INCLUDE:IMPLEMENT_MODULE_"));

			AddPGOLinkArguments(LinkEnvironment, Arguments);
		}

		private void ExportObjectFilePaths(LinkEnvironment LinkEnvironment, string FileName, VCEnvironment EnvVars)
		{
			// Write the list of object file directories
			HashSet<DirectoryReference> ObjectFileDirectories = new HashSet<DirectoryReference>();
			foreach (FileItem InputFile in LinkEnvironment.InputFiles)
			{
				ObjectFileDirectories.Add(InputFile.Location.Directory);
			}
			foreach (FileReference Library in LinkEnvironment.Libraries)
			{
				ObjectFileDirectories.Add(Library.Directory);
			}
			foreach (DirectoryReference LibraryPath in LinkEnvironment.SystemLibraryPaths)
			{
				ObjectFileDirectories.Add(LibraryPath);
			}
			foreach (string LibraryPath in (Environment.GetEnvironmentVariable("LIB") ?? "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				ObjectFileDirectories.Add(new DirectoryReference(LibraryPath));
			}
			foreach (DirectoryReference LibraryPath in EnvVars.LibraryPaths)
			{
				ObjectFileDirectories.Add(LibraryPath);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
			File.WriteAllLines(FileName, ObjectFileDirectories.Select(x => x.FullName).OrderBy(x => x).ToArray());
		}

		/// <summary>
		/// Gets the default include paths for the given platform.
		/// </summary>
		[SupportedOSPlatform("windows")]
		public static string GetVCIncludePaths(UnrealTargetPlatform Platform, WindowsCompiler Compiler, string? CompilerVersion, string? ToolchainVersion, ILogger Logger)
		{
			// Make sure we've got the environment variables set up for this target
			VCEnvironment EnvVars = VCEnvironment.Create(Compiler, WindowsCompiler.Default, Platform, UnrealArch.X64, CompilerVersion, ToolchainVersion, null, null, false, false, Logger);

			// Also add any include paths from the INCLUDE environment variable.  MSVC is not necessarily running with an environment that
			// matches what UBT extracted from the vcvars*.bat using SetEnvironmentVariablesFromBatchFile().  We'll use the variables we
			// extracted to populate the project file's list of include paths
			// @todo projectfiles: Should we only do this for VC++ platforms?
			StringBuilder IncludePaths = new StringBuilder();
			foreach (DirectoryReference IncludePath in EnvVars.IncludePaths)
			{
				IncludePaths.AppendFormat("{0};", IncludePath);
			}
			return IncludePaths.ToString();
		}

		public override void ModifyBuildProducts(ReadOnlyTargetRules Target, UEBuildBinary Binary, List<string> Libraries, List<UEBuildBundleResource> BundleResources, Dictionary<FileReference, BuildProductType> BuildProducts)
		{
			if (Binary.Type == UEBuildBinaryType.DynamicLinkLibrary)
			{
				if (Target.bShouldCompileAsDLL)
				{
					BuildProducts.Add(FileReference.Combine(Binary.OutputDir, Binary.OutputFilePath.GetFileNameWithoutExtension() + ".lib"), BuildProductType.BuildResource);
				}
				else
				{
					BuildProducts.Add(FileReference.Combine(Binary.IntermediateDirectory, Binary.OutputFilePath.GetFileNameWithoutExtension() + ".lib"), BuildProductType.BuildResource);
				}
			}
			if (Binary.Type == UEBuildBinaryType.Executable && Target.bCreateMapFile)
			{
				foreach (FileReference OutputFilePath in Binary.OutputFilePaths)
				{
					BuildProducts.Add(FileReference.Combine(OutputFilePath.Directory, OutputFilePath.GetFileNameWithoutExtension() + ".map"), BuildProductType.MapFile);
					BuildProducts.Add(FileReference.Combine(OutputFilePath.Directory, OutputFilePath.GetFileNameWithoutExtension() + ".objpaths"), BuildProductType.MapFile);
				}
			}
		}
	}
}

// Copyright Epic Games, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using EpicGames.Core;
using UnrealBuildBase;

namespace UnrealBuildTool
{

	/// <summary>
	/// Compiler configuration. This controls whether to use define debug macros and other compiler settings. Note that optimization level should be based on the bOptimizeCode variable rather than
	/// this setting, so it can be modified on a per-module basis without introducing an incompatibility between object files or PCHs.
	/// </summary>
	enum CppConfiguration
	{
		Debug,
		Development,
		Shipping
	}

	/// <summary>
	/// Specifies which language standard to use. This enum should be kept in order, so that toolchains can check whether the requested setting is >= values that they support.
	/// </summary>
	public enum CppStandardVersion
	{
		/// <summary>
		/// Supports C++14
		/// </summary>
		Cpp14,

		/// <summary>
		/// Supports C++17
		/// </summary>
		Cpp17,

		/// <summary>
		/// Supports C++20
		/// </summary>
		Cpp20,

		/// <summary>
		/// Latest standard supported by the compiler
		/// </summary>
		Latest,

		/// <summary>
		/// Use the default standard version
		/// </summary>
		Default = Cpp17,
	}

	/// <summary>
	/// Specifies which C language standard to use. This enum should be kept in order, so that toolchains can check whether the requested setting is >= values that they support.
	/// </summary>
	public enum CStandardVersion
	{
		/// <summary>
		/// Use the default standard version
		/// </summary>
		Default,

		/// <summary>
		/// Supports C89
		/// </summary>
		C89,

		/// <summary>
		/// Supports C99
		/// </summary>
		C99,

		/// <summary>
		/// Supports C11
		/// </summary>
		C11,

		/// <summary>
		/// Supports C17
		/// </summary>
		C17,

		/// <summary>
		/// Latest standard supported by the compiler
		/// </summary>
		Latest,
	}

	/// <summary>
	/// Specifies which processor to use for compiling this module and to tuneup and optimize to specifics of micro-architectures. This enum should be kept in order, so that toolchains can check whether the requested setting is >= values that they support.
	/// Note that by enabling this you are changing the minspec for the PC platform, and the resultant executable might cause worse performance or will crash on incompatible processors.
	/// </summary>
	public enum DefaultCPU
	{
		/// <summary>
		/// A generic CPU with 64-bit extensions.
		/// </summary>
		Generic,

		/// <summary>
		/// This selects the CPU to generate code for at compilation time by determining the processor type of the compiling machine. Using -march=native enables all instruction subsets supported by the local machine (hence the result might not run on different machines). Using -mtune=native produces code optimized for the local machine under the constraints of the selected instruction set.
		/// In MSVC defaults to Generic.
		/// </summary>
		Native,

		/// <summary>
		/// Intel Bonnell CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3 and SSSE3 instruction set support.
		/// </summary>
		Bonnell,

		/// <summary>
		/// Intel Core 2 CPU with 64-bit extensions, MMX, SSE, SSE2, SSE3, SSSE3, CX16, SAHF and FXSR instruction set support.
		/// </summary>
		Core2,

		//SSE4.1 Supported CPUs

		/// <summary>
		/// Intel Nehalem CPU with 64-bit extensions, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF and FXSR instruction set support.
		/// </summary>
		Nehalem,

		/// <summary>
		/// Intel Westmere CPU with 64-bit extensions, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR and PCLMUL instruction set support.
		/// </summary>
		Westmere,

		/// <summary>
		/// Intel Sandy Bridge CPU with 64-bit extensions, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE and PCLMUL instruction set support.
		/// </summary>
		Sandybridge,

		/// <summary>
		/// Intel Ivy Bridge CPU with 64-bit extensions, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND and F16C instruction set support.
		/// </summary>
		Ivybridge,

		/// <summary>
		/// Intel Silvermont CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, PCLMUL, PREFETCHW and RDRND instruction set support.
		/// </summary>
		Silvermont,

		/// <summary>
		/// Intel Goldmont CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, PCLMUL, PREFETCHW, RDRND, AES, SHA, RDSEED, XSAVE, XSAVEC, XSAVES, XSAVEOPT, CLFLUSHOPT and FSGSBASE instruction set support.
		/// </summary>
		Goldmont,

		/// <summary>
		/// Intel Goldmont Plus CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, PCLMUL, PREFETCHW, RDRND, AES, SHA, RDSEED, XSAVE, XSAVEC, XSAVES, XSAVEOPT, CLFLUSHOPT, FSGSBASE, PTWRITE, RDPID and SGX instruction set support.
		/// </summary>
		Goldmont_plus,

		/// <summary>
		/// Intel Tremont CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, PCLMUL, PREFETCHW, RDRND, AES, SHA, RDSEED, XSAVE, XSAVEC, XSAVES, XSAVEOPT, CLFLUSHOPT, FSGSBASE, PTWRITE, RDPID, SGX, CLWB, GFNI-SSE, MOVDIRI, MOVDIR64B, CLDEMOTE and WAITPKG instruction set support.
		/// </summary>
		Tremont,

		/// <summary>
		/// AMD Family 16h cores based CPUs with x86-64 instruction set support. This includes MOVBE, F16C, BMI, AVX, PCLMUL, AES, SSE4.2, SSE4.1, CX16, ABM, SSE4A, SSSE3, SSE3, SSE2, SSE, MMX and 64-bit instruction set extensions.
		/// </summary>
		Btver2,

		/// <summary>
		/// AMD Bulldozer Family 15h core based CPUs with x86-64 instruction set support. This supersets FMA4, AVX, XOP, LWP, AES, PCLMUL, CX16, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM and 64-bit instruction set extensions.
		/// </summary>
		Bdver1,

		/// <summary>
		/// AMD Bulldozer Family 15h core based CPUs with x86-64 instruction set support. This supersets BMI, TBM, F16C, FMA, FMA4, AVX, XOP, LWP, AES, PCLMUL, CX16, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM and 64-bit instruction set extensions.
		/// </summary>
		Bdver2,

		/// <summary>
		/// AMD Bulldozer Family 15h core based CPUs with x86-64 instruction set support. This supersets BMI, TBM, F16C, FMA, FMA4, FSGSBASE, AVX, XOP, LWP, AES, PCLMUL, CX16, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM and 64-bit instruction set extensions.
		/// </summary>
		Bdver3,

		//AVX2 Supported CPUs

		/// <summary>
		/// Intel Haswell CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE and HLE instruction set support.
		/// </summary>
		Haswell,

		/// <summary>
		/// Intel Broadwell CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX and PREFETCHW instruction set support.
		/// </summary>
		Broadwell,

		/// <summary>
		/// Intel Skylake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES and SGX instruction set support.
		/// </summary>
		Skylake,

		/// <summary>
		/// Intel Alderlake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, AES, PREFETCHW, PCLMUL, RDRND, XSAVE, XSAVEC, XSAVES, XSAVEOPT, FSGSBASE, PTWRITE, RDPID, SGX, GFNI-SSE, CLWB, MOVDIRI, MOVDIR64B, CLDEMOTE, WAITPKG, ADCX, AVX, AVX2, BMI, BMI2, F16C, FMA, LZCNT, PCONFIG, PKU, VAES, VPCLMULQDQ, SERIALIZE, HRESET, KL, WIDEKL and AVX-VNNI instruction set support.
		/// </summary>
		Alderlake,

		/// <summary>
		/// Intel Sierra Forest CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, AES, PREFETCHW, PCLMUL, RDRND, XSAVE, XSAVEC, XSAVES, XSAVEOPT, FSGSBASE, PTWRITE, RDPID, SGX, GFNI-SSE, CLWB, MOVDIRI, MOVDIR64B, CLDEMOTE, WAITPKG, ADCX, AVX, AVX2, BMI, BMI2, F16C, FMA, LZCNT, PCONFIG, PKU, VAES, VPCLMULQDQ, SERIALIZE, HRESET, KL, WIDEKL, AVX-VNNI, AVXIFMA, AVXVNNIINT8, AVXNECONVERT and CMPCCXADD instruction set support.
		/// </summary>
		Sierraforest,

		/// <summary>
		/// Intel Grand Ridge CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, AES, PREFETCHW, PCLMUL, RDRND, XSAVE, XSAVEC, XSAVES, XSAVEOPT, FSGSBASE, PTWRITE, RDPID, SGX, GFNI-SSE, CLWB, MOVDIRI, MOVDIR64B, CLDEMOTE, WAITPKG, ADCX, AVX, AVX2, BMI, BMI2, F16C, FMA, LZCNT, PCONFIG, PKU, VAES, VPCLMULQDQ, SERIALIZE, HRESET, KL, WIDEKL, AVX-VNNI, AVXIFMA, AVXVNNIINT8, AVXNECONVERT, CMPCCXADD and RAOINT instruction set support.
		/// </summary>
		Grandridge,

		/// <summary>
		/// AMD Bulldozer Family 15h core based CPUs with x86-64 instruction set support. This supersets BMI, BMI2, TBM, F16C, FMA, FMA4, FSGSBASE, AVX, AVX2, XOP, LWP, AES, PCLMUL, CX16, MOVBE, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM and 64-bit instruction set extensions.
		/// </summary>
		Bdver4,

		/// <summary>
		/// AMD Zen1 Ryzen/Epic-7xx1-series Family 17h core based CPUs with x86-64 instruction set support. (This supersets BMI, BMI2, F16C, FMA, FSGSBASE, AVX, AVX2, ADCX, RDSEED, MWAITX, SHA, CLZERO, AES, PCLMUL, CX16, MOVBE, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM, XSAVEC, XSAVES, CLFLUSHOPT, POPCNT, and 64-bit instruction set extensions.)
		/// </summary>
		Znver1,

		/// <summary>
		/// AMD Zen2 Ryzen/Epic-7xx2-series Family 17h core based CPUs with x86-64 instruction set support. (This supersets BMI, BMI2, CLWB, F16C, FMA, FSGSBASE, AVX, AVX2, ADCX, RDSEED, MWAITX, SHA, CLZERO, AES, PCLMUL, CX16, MOVBE, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM, XSAVEC, XSAVES, CLFLUSHOPT, POPCNT, RDPID, WBNOINVD, and 64-bit instruction set extensions.)
		/// </summary>
		Znver2,

		/// <summary>
		/// AMD Zen3 Ryzen/Epic-7xx3-series Family 19h core based CPUs with x86-64 instruction set support. (This supersets BMI, BMI2, CLWB, F16C, FMA, FSGSBASE, AVX, AVX2, ADCX, RDSEED, MWAITX, SHA, CLZERO, AES, PCLMUL, CX16, MOVBE, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM, XSAVEC, XSAVES, CLFLUSHOPT, POPCNT, RDPID, WBNOINVD, PKU, VPCLMULQDQ, VAES, and 64-bit instruction set extensions.)
		/// </summary>
		Znver3,

		//AVX512 Supported CPUs

		/// <summary>
		/// AMD Zen4 Ryzen/Epic-7xx4-series Family 19h core based CPUs with x86-64 instruction set support. (This supersets BMI, BMI2, CLWB, F16C, FMA, FSGSBASE, AVX, AVX2, ADCX, RDSEED, MWAITX, SHA, CLZERO, AES, PCLMUL, CX16, MOVBE, MMX, SSE, SSE2, SSE3, SSE4A, SSSE3, SSE4.1, SSE4.2, ABM, XSAVEC, XSAVES, CLFLUSHOPT, POPCNT, RDPID, WBNOINVD, PKU, VPCLMULQDQ, VAES, AVX512F, AVX512DQ, AVX512IFMA, AVX512CD, AVX512BW, AVX512VL, AVX512BF16, AVX512VBMI, AVX512VBMI2, AVX512VNNI, AVX512BITALG, AVX512VPOPCNTDQ, GFNI and 64-bit instruction set extensions.)
		/// </summary>
		Znver4,

		// Start of. Most of UE required AVX512 CPU features are not supporterd at all in this Intel processors.
		// Start of. This Intel accelerators processors only runs AVX512.
		// Removed, incompatible for use with accelerators processors.
		// <summary>
		// Intel Knight’s Landing CPU with 64-bit extensions, AVX512PF, AVX512ER, AVX512F, AVX512CD and PREFETCHWT1 instruction set support.
		// </summary>
		/*Knl,*/

		// <summary>
		// Intel Knights Mill CPU with 64-bit extensions, AVX512PF, AVX512ER, AVX512F, AVX512CD and PREFETCHWT1, AVX5124VNNIW, AVX5124FMAPS and AVX512VPOPCNTDQ instruction set support.
		// </summary>
		/*Knm,*/
		// End of. This Intel accelerators processors only runs AVX512.

		// Start of. This Intel processors not all products has the required AVX512 CPU features.
		/// <summary>
		/// Intel Skylake Server CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, CLWB, AVX512VL, AVX512BW, AVX512DQ and AVX512CD instruction set support.
		/// </summary>
		Skylake_avx512,

		/// <summary>
		/// Intel Cannonlake Server CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD, PKU, AVX512VBMI, AVX512IFMA and SHA instruction set support.
		/// </summary>
		Cannonlake,

		/// <summary>
		/// Intel Icelake Client CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD, PKU, AVX512VBMI, AVX512IFMA, SHA, AVX512VNNI, GFNI, VAES, AVX512VBMI2 , VPCLMULQDQ, AVX512BITALG, RDPID and AVX512VPOPCNTDQ instruction set support.
		/// </summary>
		Icelake_client,

		/// <summary>
		/// Intel Icelake Server CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD, PKU, AVX512VBMI, AVX512IFMA, SHA, AVX512VNNI, GFNI, VAES, AVX512VBMI2 , VPCLMULQDQ, AVX512BITALG, RDPID, AVX512VPOPCNTDQ, PCONFIG, WBNOINVD and CLWB instruction set support.
		/// </summary>
		Icelake_server,

		/// <summary>
		/// Intel Cascadelake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, CLWB, AVX512VL, AVX512BW, AVX512DQ, AVX512CD and AVX512VNNI instruction set support.
		/// </summary>
		Cascadelake,

		/// <summary>
		/// Intel cooperlake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, CLWB, AVX512VL, AVX512BW, AVX512DQ, AVX512CD, AVX512VNNI and AVX512BF16 instruction set support.
		/// </summary>
		Cooperlake,

		/// <summary>
		/// Intel Tigerlake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD PKU, AVX512VBMI, AVX512IFMA, SHA, AVX512VNNI, GFNI, VAES, AVX512VBMI2, VPCLMULQDQ, AVX512BITALG, RDPID, AVX512VPOPCNTDQ, MOVDIRI, MOVDIR64B, CLWB, AVX512VP2INTERSECT and KEYLOCKER instruction set support.
		/// </summary>
		Tigerlake,

		/// <summary>
		/// Intel sapphirerapids CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD, PKU, AVX512VBMI, AVX512IFMA, SHA, AVX512VNNI, GFNI, VAES, AVX512VBMI2, VPCLMULQDQ, AVX512BITALG, RDPID, AVX512VPOPCNTDQ, PCONFIG, WBNOINVD, CLWB, MOVDIRI, MOVDIR64B, ENQCMD, CLDEMOTE, PTWRITE, WAITPKG, SERIALIZE, TSXLDTRK, UINTR, AMX-BF16, AMX-TILE, AMX-INT8, AVX-VNNI, AVX512-FP16 and AVX512BF16 instruction set support.
		/// </summary>
		Sapphirerapids,

		// <summary>
		// Intel Alderlake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, AES, PREFETCHW, PCLMUL, RDRND, XSAVE, XSAVEC, XSAVES, XSAVEOPT, FSGSBASE, PTWRITE, RDPID, SGX, GFNI-SSE, CLWB, MOVDIRI, MOVDIR64B, CLDEMOTE, WAITPKG, ADCX, AVX, AVX2, BMI, BMI2, F16C, FMA, LZCNT, PCONFIG, PKU, VAES, VPCLMULQDQ, SERIALIZE, HRESET, KL, WIDEKL, AVX-VNNI and AVX512F enabled instruction set support.
		// </summary>
		/*Alderlake_avx512,*/ // Intel fused of AVX512. Removed.
		// End of. This Intel processors not all products has the required AVX512 CPU features.
		// End of. Most of UE required AVX512 CPU features are not supporterd at all in this Intel processors.

		/// <summary>
		/// Intel Rocketlake CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3 , SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD PKU, AVX512VBMI, AVX512IFMA, SHA, AVX512VNNI, GFNI, VAES, AVX512VBMI2, VPCLMULQDQ, AVX512BITALG, RDPID and AVX512VPOPCNTDQ instruction set support.
		/// </summary>
		Rocketlake,

		/// <summary>
		/// Intel graniterapids CPU with 64-bit extensions, MOVBE, MMX, SSE, SSE2, SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CX16, SAHF, FXSR, AVX, XSAVE, PCLMUL, FSGSBASE, RDRND, F16C, AVX2, BMI, BMI2, LZCNT, FMA, MOVBE, HLE, RDSEED, ADCX, PREFETCHW, AES, CLFLUSHOPT, XSAVEC, XSAVES, SGX, AVX512F, AVX512VL, AVX512BW, AVX512DQ, AVX512CD, PKU, AVX512VBMI, AVX512IFMA, SHA, AVX512VNNI, GFNI, VAES, AVX512VBMI2, VPCLMULQDQ, AVX512BITALG, RDPID, AVX512VPOPCNTDQ, PCONFIG, WBNOINVD, CLWB, MOVDIRI, MOVDIR64B, AVX512VP2INTERSECT, ENQCMD, CLDEMOTE, PTWRITE, WAITPKG, SERIALIZE, TSXLDTRK, UINTR, AMX-BF16, AMX-TILE, AMX-INT8, AVX-VNNI, AVX512-FP16, AVX512BF16, AMX-FP16 and PREFETCHI instruction set support.
		/// </summary>
		Graniterapids,

		/// <summary>
		/// Use the default Generic version
		/// </summary>
		Default = Generic,
	}

	/// <summary>
	/// To what extent a module supports AVX version
	/// </summary>
	public enum AVXSupport
	{
		/// <summary>
		/// None means code does not compile with AVX.
		/// </summary>
		None,

		/// <summary>
		/// Direct the compiler to generate AVX instructions wherever SSE or AVX intrinsics are used, on the platforms that support it.
		/// Note that by enabling this you are changing the minspec for the PC platform, and the resultant executable will crash on machines without AVX support.
		/// </summary>
		AVX256,

		/// <summary>
		/// Direct the compiler to generate AVX instructions wherever SSE or AVX or AVX-512 intrinsics are used, on the platforms that support it.
		/// Note that by enabling this you are changing the minspec for the PC platform, and the resultant executable will crash on machines without AVX and AVX-512 support.
		/// </summary>
		AVX512,
	}

	/// <summary>
	/// The optimization level that may be compilation targets for C# files.
	/// </summary>
	enum CSharpTargetConfiguration
	{
		Debug,
		Development,
	}

	/// <summary>
	/// The possible interactions between a precompiled header and a C++ file being compiled.
	/// </summary>
	enum PrecompiledHeaderAction
	{
		None,
		Include,
		Create
	}

	/// <summary>
	/// Encapsulates the compilation output of compiling a set of C++ files.
	/// </summary>
	class CPPOutput
	{
		public List<FileItem> ObjectFiles = new List<FileItem>();
		public List<FileItem> CompiledModuleInterfaces = new List<FileItem>();
		public List<FileItem> GeneratedHeaderFiles = new List<FileItem>();
		public FileItem? PrecompiledHeaderFile = null;
		public Dictionary<UnrealArch, FileItem> PerArchPrecompiledHeaderFiles = new();

		public void Merge(CPPOutput Other, UnrealArch Architecture)
		{
			ObjectFiles.AddRange(Other.ObjectFiles);
			CompiledModuleInterfaces.AddRange(Other.CompiledModuleInterfaces);
			GeneratedHeaderFiles.AddRange(Other.GeneratedHeaderFiles);
			ObjectFiles.AddRange(Other.ObjectFiles);
			if (Other.PrecompiledHeaderFile != null)
			{
				PerArchPrecompiledHeaderFiles[Architecture] = Other.PrecompiledHeaderFile;
			}
		}
	}

	/// <summary>
	/// Encapsulates the environment that a C++ file is compiled in.
	/// </summary>
	class CppCompileEnvironment
	{
		/// <summary>
		/// The platform to be compiled/linked for.
		/// </summary>
		public readonly UnrealTargetPlatform Platform;

		/// <summary>
		/// The configuration to be compiled/linked for.
		/// </summary>
		public readonly CppConfiguration Configuration;

		/// <summary>
		/// The architecture that is being compiled/linked (empty string by default)
		/// </summary>
		public UnrealArchitectures Architectures;

		/// <summary>
		/// Gets the Architecture in the normal case where there is a single Architecture in Architectures
		/// </summary>
		public UnrealArch Architecture => Architectures.SingleArchitecture;

		/// <summary>
		/// Cache of source file metadata
		/// </summary>
		public readonly SourceFileMetadataCache MetadataCache;

		/// <summary>
		/// Templates for shared precompiled headers
		/// </summary>
		public readonly List<PrecompiledHeaderTemplate> SharedPCHs;

		/// <summary>
		/// The name of the header file which is precompiled.
		/// </summary>
		public FileReference? PrecompiledHeaderIncludeFilename = null;

		/// <summary>
		/// Whether the compilation should create, use, or do nothing with the precompiled header.
		/// </summary>
		public PrecompiledHeaderAction PrecompiledHeaderAction = PrecompiledHeaderAction.None;

		/// <summary>
		/// Whether artifacts from this compile are shared with other targets. If so, we should not apply any target-wide modifications to the compile environment.
		/// </summary>
		public bool bUseSharedBuildEnvironment;

		/// <summary>
		/// Use run time type information
		/// </summary>
		public bool bUseRTTI = false;

		/// <summary>
		/// Use Position Independent Executable (PIE). Has an overhead cost
		/// </summary>
		public bool bUsePIE = false;

		/// <summary>
		/// Use Stack Protection. Has an overhead cost
		/// </summary>
		public bool bUseStackProtection = false;

		/// <summary>
		/// Enable inlining.
		/// </summary>
		public bool bUseInlining = false;

		/// <summary>
		/// Whether to compile ISPC files.
		/// </summary>
		public bool bCompileISPC = false;

		/// <summary>
		/// Direct the compiler to generate AVX instructions wherever SSE or AVX intrinsics are used.
		/// Note that by enabling this you are changing the minspec for the PC platform, and the resultant executable will crash on machines without AVX support.
		/// </summary>
		public bool bUseAVX = false;

		/// <summary>
		/// Direct the compiler to generate AVX instructions wherever SSE or AVX or AVX-512 intrinsics are used, on the platforms that support it.
		/// Note that by enabling this you are changing the minspec for the PC platform, and the resultant executable will crash on machines without AVX or AVX-512 support.
		/// </summary>
		public AVXSupport AVXSupport = AVXSupport.None;

		/// <summary>
		/// Enable buffer security checks.   This should usually be enabled as it prevents severe security risks.
		/// </summary>
		public bool bEnableBufferSecurityChecks = true;

		/// <summary>
		/// If unity builds are enabled this can be used to override if this specific module will build using Unity.
		/// This is set using the per module configurations in BuildConfiguration.
		/// </summary>
		public bool bUseUnity = false;

		/// <summary>
		/// The number of source files in this module before unity build will be activated for that module.  If set to
		/// anything besides -1, will override the default setting which is controlled by MinGameModuleSourceFilesForUnityBuild
		/// </summary>
		public int MinSourceFilesForUnityBuildOverride = 0;

		/// <summary>
		/// The minimum number of files that must use a pre-compiled header before it will be created and used.
		/// </summary>
		public int MinFilesUsingPrecompiledHeaderOverride = 0;

		/// <summary>
		/// Module uses a #import so must be built locally when compiling with SN-DBS
		/// </summary>
		public bool bBuildLocallyWithSNDBS = false;

		/// <summary>
		/// Whether to retain frame pointers
		/// </summary>
		public bool bRetainFramePointers = true;

		/// <summary>
		/// Enable exception handling
		/// </summary>
		public bool bEnableExceptions = false;

		/// <summary>
		/// Enable objective C exception handling
		/// </summary>
		public bool bEnableObjCExceptions = false;

		/// <summary>
		/// Enable objective C automatic reference counting (ARC)
		/// If you set this to true you should not use shared PCHs for this module. The engine won't be extensively using ARC in the short term
		/// Not doing this will result in a compile errors because shared PCHs were compiled with different flags than consumer
		/// </summary>
		public bool bEnableObjCAutomaticReferenceCounting = false;

		/// <summary>
		/// How to treat any warnings in the code
		/// </summary>
		public WarningLevel DefaultWarningLevel = WarningLevel.Warning;

		/// <summary>
		/// Whether to warn about deprecated variables
		/// </summary>
		public WarningLevel DeprecationWarningLevel = WarningLevel.Warning;

		/// <summary>
		/// Whether to warn about the use of shadow variables
		/// </summary>
		public WarningLevel ShadowVariableWarningLevel = WarningLevel.Warning;

		/// <summary>
		/// How to treat unsafe implicit type cast warnings (e.g., double->float or int64->int32)
		/// </summary>
		public WarningLevel UnsafeTypeCastWarningLevel = WarningLevel.Off;

		/// <summary>
		/// Whether to warn about the use of undefined identifiers in #if expressions
		/// </summary>
		public bool bEnableUndefinedIdentifierWarnings = true;

		/// <summary>
		/// Whether to treat undefined identifier warnings as errors.
		/// </summary>
		public bool bUndefinedIdentifierWarningsAsErrors = false;

		/// <summary>
		/// Whether to treat all warnings as errors
		/// </summary>
		public bool bWarningsAsErrors = false;

		/// <summary>
		/// Whether to disable all static analysis - Clang, MSVC, PVS-Studio.
		/// </summary>
		public bool bDisableStaticAnalysis = false;

		/// <summary>
		/// Enable additional analyzer extension warnings using the EspXEngine plugin. This is only supported for MSVC.
		/// See https://learn.microsoft.com/en-us/cpp/code-quality/using-the-cpp-core-guidelines-checkers
		/// This will add a large number of warnings by default. It's recommended to use StaticAnalyzerRulesets if this is enabled.
		/// </summary>
		public bool bStaticAnalyzerExtensions = false;

		/// <summary>
		/// The static analyzer rulesets that should be used to filter warnings. This is only supported for MSVC.
		/// See https://learn.microsoft.com/en-us/cpp/code-quality/using-rule-sets-to-specify-the-cpp-rules-to-run
		/// </summary>
		public HashSet<FileReference> StaticAnalyzerRulesets = new HashSet<FileReference>();

		/// <summary>
		/// The static analyzer checkers that should be enabled rather than the defaults. This is only supported for Clang.
		/// </summary>
		public HashSet<string> StaticAnalyzerCheckers = new HashSet<string>();

		/// <summary>
		/// The static analyzer default checkers that should be disabled. Unused if StaticAnalyzerCheckers is populated. This is only supported for Clang.
		/// </summary>
		public HashSet<string> StaticAnalyzerDisabledCheckers = new HashSet<string>();

		/// <summary>
		/// The static analyzer non-default checkers that should be enabled. Unused if StaticAnalyzerCheckers is populated. This is only supported for Clang.
		/// </summary>
		public HashSet<string> StaticAnalyzerAdditionalCheckers = new HashSet<string>();

		/// <summary>
		/// True if compiler optimizations should be enabled. This setting is distinct from the configuration (see CPPTargetConfiguration).
		/// </summary>
		public bool bOptimizeCode = false;

		/// <summary>
		/// Allows to fine tune optimizations level for speed and\or code size
		/// </summary>
		public OptimizationMode OptimizationLevel = OptimizationMode.Speed;

		/// <summary>
		/// True if debug info should be created.
		/// </summary>
		public bool bCreateDebugInfo = true;

		/// <summary>
		/// True if we're compiling .cpp files that will go into a library (.lib file)
		/// </summary>
		public bool bIsBuildingLibrary = false;

		/// <summary>
		/// True if we're compiling a DLL
		/// </summary>
		public bool bIsBuildingDLL = false;

		/// <summary>
		/// Whether we should compile using the statically-linked CRT. This is not widely supported for the whole engine, but is required for programs that need to run without dependencies.
		/// </summary>
		public bool bUseStaticCRT = false;

		/// <summary>
		/// Whether to use the debug CRT in debug configurations
		/// </summary>
		public bool bUseDebugCRT = false;

		/// <summary>
		/// Whether to omit frame pointers or not. Disabling is useful for e.g. memory profiling on the PC
		/// </summary>
		public bool bOmitFramePointers = true;

		/// <summary>
		/// Whether we should compile with support for OS X 10.9 Mavericks. Used for some tools that we need to be compatible with this version of OS X.
		/// </summary>
		public bool bEnableOSX109Support = false;

		/// <summary>
		/// Whether PDB files should be used for Visual C++ builds.
		/// </summary>
		public bool bUsePDBFiles = false;

		/// <summary>
		/// Whether to just preprocess source files
		/// </summary>
		public bool bPreprocessOnly = false;

		/// <summary>
		/// Whether to support edit and continue.  Only works on Microsoft compilers in 32-bit compiles.
		/// </summary>
		public bool bSupportEditAndContinue;

		/// <summary>
		/// Whether to use incremental linking or not.
		/// </summary>
		public bool bUseIncrementalLinking;

		/// <summary>
		/// Whether to allow the use of LTCG (link time code generation)
		/// </summary>
		public bool bAllowLTCG;

		/// <summary>
		/// Whether to enable Profile Guided Optimization (PGO) instrumentation in this build.
		/// </summary>
		public bool bPGOProfile;

		/// <summary>
		/// Whether to optimize this build with Profile Guided Optimization (PGO).
		/// </summary>
		public bool bPGOOptimize;

		/// <summary>
		/// Platform specific directory where PGO profiling data is stored.
		/// </summary>
		public string? PGODirectory;

		/// <summary>
		/// Platform specific filename where PGO profiling data is saved.
		/// </summary>
		public string? PGOFilenamePrefix;

		/// <summary>
		/// Whether to log detailed timing info from the compiler
		/// </summary>
		public bool bPrintTimingInfo;

		/// <summary>
		/// When enabled, allows XGE to compile pre-compiled header files on remote machines.  Otherwise, PCHs are always generated locally.
		/// </summary>
		public bool bAllowRemotelyCompiledPCHs = false;

		/// <summary>
		/// Ordered list of include paths for the module
		/// </summary>
		public HashSet<DirectoryReference> UserIncludePaths;

		/// <summary>
		/// The include paths where changes to contained files won't cause dependent C++ source files to
		/// be recompiled, unless BuildConfiguration.bCheckSystemHeadersForModification==true.
		/// </summary>
		public HashSet<DirectoryReference> SystemIncludePaths;

		/// <summary>
		/// List of paths to search for compiled module interface (*.ifc) files
		/// </summary>
		public HashSet<DirectoryReference> ModuleInterfacePaths;

		/// <summary>
		/// Whether headers in system paths should be checked for modification when determining outdated actions.
		/// </summary>
		public bool bCheckSystemHeadersForModification;

		/// <summary>
		/// List of header files to force include
		/// </summary>
		public List<FileItem> ForceIncludeFiles = new List<FileItem>();

		/// <summary>
		/// List of files that need to be up to date before compile can proceed
		/// </summary>
		public List<FileItem> AdditionalPrerequisites = new List<FileItem>();

		/// <summary>
		/// A dictionary of the source file items and the inlined gen.cpp files contained in it
		/// </summary>
		public Dictionary<FileItem, List<FileItem>> FileInlineGenCPPMap = new();

		/// <summary>
		/// The C++ preprocessor definitions to use.
		/// </summary>
		public List<string> Definitions = new List<string>();

		/// <summary>
		/// Additional arguments to pass to the compiler.
		/// </summary>
		public string AdditionalArguments = "";

		/// <summary>
		/// A list of additional frameworks whose include paths are needed.
		/// </summary>
		public List<UEBuildFramework> AdditionalFrameworks = new List<UEBuildFramework>();

		/// <summary>
		/// The file containing the precompiled header data.
		/// </summary>
		public FileItem? PrecompiledHeaderFile = null;

		/// <summary>
		/// A dictionary of PCH files for multiple architectures
		/// </summary>
		public Dictionary<UnrealArch, FileItem>? PerArchPrecompiledHeaderFiles = null;

		/// <summary>
		/// True if a single PRecompiledHeader exists, or at least one PerArchPrecompiledHeaderFile exists
		/// </summary>
		public bool bHasPrecompiledHeader => PrecompiledHeaderAction == PrecompiledHeaderAction.Include;

		/// <summary>
		/// Whether or not UHT is being built
		/// </summary>
		public bool bHackHeaderGenerator;

		/// <summary>
		/// Whether to hide symbols by default
		/// </summary>
		public bool bHideSymbolsByDefault = true;

		/// <summary>
		/// Which C++ standard to support. May not be compatible with all platforms.
		/// </summary>
		public CppStandardVersion CppStandard = CppStandardVersion.Default;

		/// <summary>
		/// Which C standard to support. May not be compatible with all platforms.
		/// </summary>
		public CStandardVersion CStandard = CStandardVersion.Default;

		/// <summary>
		/// Specifies which processor to use for compiling this module and to tuneup and optimize to specifics of micro-architectures. This enum should be kept in order, so that toolchains can check whether the requested setting is >= values that they support.
		/// Note that by enabling this you are changing the minspec for the PC platform, and the resultant executable might cause worse performance or will crash on incompatible processors.
		/// </summary>
		public DefaultCPU DefaultCPU = DefaultCPU.Default;

		/// <summary>
		/// The amount of the stack usage to report static analysis warnings.
		/// </summary>
		public int AnalyzeStackSizeWarning = 300000;

		/// <summary>
		/// Enable C++ coroutine support.
		/// For MSVC, adds "/await:strict" to the command line. Program should #include &lt;coroutine&gt;
		/// For Clang, adds "-fcoroutines-ts" to the command line. Program should #include &lt;experimental/coroutine&gt; (not supported in every clang toolchain)
		/// </summary>
		public bool bEnableCoroutines = false;

		/// <summary>
		/// What version of include order specified by the module rules. Used to determine shared PCH variants.
		/// </summary>
		public EngineIncludeOrderVersion IncludeOrderVersion = EngineIncludeOrderVersion.Latest;

		/// <summary>
		/// Set flags for determinstic compiles (experimental).
		/// </summary>
		public bool bDeterministic = false;

		/// <summary>
		/// Directory where to put crash report files for platforms that support it
		/// </summary>
		public string? CrashDiagnosticDirectory;

		/// <summary>
		/// Default constructor.
		/// </summary>
		public CppCompileEnvironment(UnrealTargetPlatform Platform, CppConfiguration Configuration, UnrealArchitectures Architectures, SourceFileMetadataCache MetadataCache)
		{
			this.Platform = Platform;
			this.Configuration = Configuration;
			this.Architectures = Architectures;
			this.MetadataCache = MetadataCache;
			this.SharedPCHs = new List<PrecompiledHeaderTemplate>();
			this.UserIncludePaths = new HashSet<DirectoryReference>();
			this.SystemIncludePaths = new HashSet<DirectoryReference>();
			this.ModuleInterfacePaths = new HashSet<DirectoryReference>();
		}

		/// <summary>
		/// Copy constructor.
		/// </summary>
		/// <param name="Other">Environment to copy settings from</param>
		public CppCompileEnvironment(CppCompileEnvironment Other)
		{
			Platform = Other.Platform;
			Configuration = Other.Configuration;
			Architectures = new(Other.Architectures);
			MetadataCache = Other.MetadataCache;
			SharedPCHs = Other.SharedPCHs;
			PrecompiledHeaderIncludeFilename = Other.PrecompiledHeaderIncludeFilename;
			if (Other.PerArchPrecompiledHeaderFiles != null)
			{
				PerArchPrecompiledHeaderFiles = new(Other.PerArchPrecompiledHeaderFiles);
			}
			PrecompiledHeaderAction = Other.PrecompiledHeaderAction;
			bUseSharedBuildEnvironment = Other.bUseSharedBuildEnvironment;
			bUseRTTI = Other.bUseRTTI;
			bUsePIE = Other.bUsePIE;
			bUseStackProtection = Other.bUseStackProtection;
			bUseInlining = Other.bUseInlining;
			bCompileISPC = Other.bCompileISPC;
			bUseAVX = Other.bUseAVX;
			AVXSupport = Other.AVXSupport;
			bUseUnity = Other.bUseUnity;
			MinSourceFilesForUnityBuildOverride = Other.MinSourceFilesForUnityBuildOverride;
			MinFilesUsingPrecompiledHeaderOverride = Other.MinFilesUsingPrecompiledHeaderOverride;
			bBuildLocallyWithSNDBS = Other.bBuildLocallyWithSNDBS;
			bRetainFramePointers = Other.bRetainFramePointers;
			bEnableExceptions = Other.bEnableExceptions;
			bEnableObjCExceptions = Other.bEnableObjCExceptions;
			bEnableObjCAutomaticReferenceCounting = Other.bEnableObjCAutomaticReferenceCounting;
			DefaultWarningLevel = Other.DefaultWarningLevel;
			DeprecationWarningLevel = Other.DeprecationWarningLevel;
			ShadowVariableWarningLevel = Other.ShadowVariableWarningLevel;
			UnsafeTypeCastWarningLevel = Other.UnsafeTypeCastWarningLevel;
			bUndefinedIdentifierWarningsAsErrors = Other.bUndefinedIdentifierWarningsAsErrors;
			bEnableUndefinedIdentifierWarnings = Other.bEnableUndefinedIdentifierWarnings;
			bWarningsAsErrors = Other.bWarningsAsErrors;
			bDisableStaticAnalysis = Other.bDisableStaticAnalysis;
			StaticAnalyzerCheckers = new HashSet<string>(Other.StaticAnalyzerCheckers);
			StaticAnalyzerDisabledCheckers = new HashSet<string>(Other.StaticAnalyzerDisabledCheckers);
			StaticAnalyzerAdditionalCheckers = new HashSet<string>(Other.StaticAnalyzerAdditionalCheckers);
			bOptimizeCode = Other.bOptimizeCode;
			OptimizationLevel = Other.OptimizationLevel;
			bCreateDebugInfo = Other.bCreateDebugInfo;
			bIsBuildingLibrary = Other.bIsBuildingLibrary;
			bIsBuildingDLL = Other.bIsBuildingDLL;
			bUseStaticCRT = Other.bUseStaticCRT;
			bUseDebugCRT = Other.bUseDebugCRT;
			bOmitFramePointers = Other.bOmitFramePointers;
			bEnableOSX109Support = Other.bEnableOSX109Support;
			bUsePDBFiles = Other.bUsePDBFiles;
			bPreprocessOnly = Other.bPreprocessOnly;
			bSupportEditAndContinue = Other.bSupportEditAndContinue;
			bUseIncrementalLinking = Other.bUseIncrementalLinking;
			bAllowLTCG = Other.bAllowLTCG;
			bPGOOptimize = Other.bPGOOptimize;
			bPGOProfile = Other.bPGOProfile;
			PGOFilenamePrefix = Other.PGOFilenamePrefix;
			PGODirectory = Other.PGODirectory;
			bPrintTimingInfo = Other.bPrintTimingInfo;
			bAllowRemotelyCompiledPCHs = Other.bAllowRemotelyCompiledPCHs;
			UserIncludePaths = new HashSet<DirectoryReference>(Other.UserIncludePaths);
			SystemIncludePaths = new HashSet<DirectoryReference>(Other.SystemIncludePaths);
			ModuleInterfacePaths = new HashSet<DirectoryReference>(Other.ModuleInterfacePaths);
			bCheckSystemHeadersForModification = Other.bCheckSystemHeadersForModification;
			ForceIncludeFiles.AddRange(Other.ForceIncludeFiles);
			AdditionalPrerequisites.AddRange(Other.AdditionalPrerequisites);
			FileInlineGenCPPMap = new Dictionary<FileItem, List<FileItem>>(Other.FileInlineGenCPPMap);
			Definitions.AddRange(Other.Definitions);
			AdditionalArguments = Other.AdditionalArguments;
			AdditionalFrameworks.AddRange(Other.AdditionalFrameworks);
			PrecompiledHeaderFile = Other.PrecompiledHeaderFile;
			bHackHeaderGenerator = Other.bHackHeaderGenerator;
			bHideSymbolsByDefault = Other.bHideSymbolsByDefault;
			CppStandard = Other.CppStandard;
			CStandard = Other.CStandard;
			DefaultCPU = Other.DefaultCPU;
			bEnableCoroutines = Other.bEnableCoroutines;
			IncludeOrderVersion = Other.IncludeOrderVersion;
			bDeterministic = Other.bDeterministic;
			CrashDiagnosticDirectory = Other.CrashDiagnosticDirectory;
		}

		public CppCompileEnvironment(CppCompileEnvironment Other, UnrealArch OverrideArchitecture)
			: this(Other)
		{
			Architectures = new UnrealArchitectures(OverrideArchitecture);
			if (Other.PerArchPrecompiledHeaderFiles != null)
			{
				Other.PerArchPrecompiledHeaderFiles.TryGetValue(OverrideArchitecture, out PrecompiledHeaderFile);
			}
			else
			{
				PrecompiledHeaderFile = null;
			}
			if (PrecompiledHeaderFile == null && PrecompiledHeaderAction == PrecompiledHeaderAction.Include)
			{
				Console.WriteLine("badness");
			}
		}
	}
}

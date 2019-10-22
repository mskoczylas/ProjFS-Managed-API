@ECHO OFF
SETLOCAL ENABLEDELAYEDEXPANSION

CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")
IF "%2"=="" (SET "ProjFSManagedVersion=0.2.173.2") ELSE (SET "ProjFSManagedVersion=%2")

SET SolutionConfiguration=%Configuration%

:: Make the build version available in the DevOps environment.
@echo ##vso[task.setvariable variable=PROJFS_MANAGED_VERSION]%ProjFSManagedVersion%

SET nuget="%PROJFS_TOOLSDIR%\nuget.exe"
IF NOT EXIST %nuget% (
  mkdir %nuget%\..
  powershell -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile %nuget%"
)

:: Acquire vswhere to find dev15/dev16 installations reliably.
SET vswherever=2.5.2
%nuget% install vswhere -Version %vswherever% || exit /b 1
SET vswhere=%PROJFS_PACKAGESDIR%\vswhere.%vswherever%\tools\vswhere.exe

:: Use vswhere to find the latest VS installation (including prerelease installations) with the msbuild component.
:: See https://github.com/Microsoft/vswhere/wiki/Find-MSBuild
for /f "usebackq tokens=*" %%i in (`%vswhere% -all -prerelease -latest -version "[15.0,17.0)" -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.ManagedDesktop Microsoft.VisualStudio.Workload.NativeDesktop Microsoft.VisualStudio.Workload.NetCoreTools Microsoft.VisualStudio.Component.Windows10SDK.17763 Microsoft.VisualStudio.Component.VC.CLI.Support -property installationPath`) do (
  set VsInstallDir=%%i
)

set VsInstallDir=C:\Program Files (x86)\Microsoft Visual Studio\2017\BuildTools

IF NOT DEFINED VsInstallDir (
  echo ERROR: Could not locate a Visual Studio installation with required components.
  echo Refer to Readme.md for a list of the required Visual Studio components.
  exit /b 10
)

SET msbuild="%VsInstallDir%\MSBuild\15.0\Bin\amd64\msbuild.exe"
SET PlatformToolset="v141"
IF NOT EXIST %msbuild% (
  :: dev16 has msbuild.exe at a different location
  SET msbuild="%VsInstallDir%\MSBuild\Current\Bin\amd64\msbuild.exe"
  SET PlatformToolset="v142"
  IF NOT EXIST !msbuild! (
    echo ERROR: Could not find msbuild
    exit /b 1
  )
)

:: Restore all dependencies.
echo Step1
%nuget% -MSBuildPath '%VsInstallDir%' restore %PROJFS_SRCDIR%\ProjectedFSLib.Managed.sln
echo Step2
"%VsInstallDir%\Common7\Tools\VsDevCmd.bat"&dotnet restore %PROJFS_SRCDIR%\ProjectedFSLib.Managed.sln /p:Configuration=%SolutionConfiguration% /p:VCTargetsPath="C:\Program Files (x86)\MSBuild\Microsoft.Cpp\v4.0\V140" --packages %PROJFS_PACKAGESDIR% || exit /b 1
echo Step3
:: Kick off the build.
"%VsInstallDir%\Common7\Tools\VsDevCmd.bat"&msbuild %PROJFS_SRCDIR%\ProjectedFSLib.Managed.sln /p:ProjFSManagedVersion=%ProjFSManagedVersion% /p:Configuration=%SolutionConfiguration% /p:Platform=x64 /p:PlatformToolset=!PlatformToolset! || exit /b 1
echo Step4
ENDLOCAL
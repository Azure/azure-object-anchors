# Azure Object Anchors for Unity

To run the samples or to add Azure Object Anchors to an existing Unity project, you can download the Azure Object Anchors
UPM package (**com.microsoft.azure.object-anchors.runtime**) and [import the package into the Unity project using the
Unity Package Manager](https://docs.unity3d.com/Manual/upm-ui-tarball.html).

## Download the UPM package

To download the **com.microsoft.azure.object-anchors.runtime** UPM package, you can either run the `Download-UPM-Package.ps1`
PowerShell script or manually some NPM commands.

### Use PowerShell to download the UPM package

Requirements:

* A Linux, macOS, or Windows computer.
* [NPM](https://www.npmjs.com/get-npm)
* [PowerShell](https://docs.microsoft.com/powershell/scripting/install/installing-powershell)

The following commands will instruct you to run the `Download-UPM-Package.ps1` PowerShell script from this folder
(`quickstarts/apps/unity`) in this repository to download the UPM package to the current folder.

Run the following PowerShell command to download the latest version of the Azure Object Anchors UPM package:

```powershell
.\Download-UPM-Package.ps1
```

To install a specific version, run the following command substituting `version_number` with the version of Azure Object
Anchors you want:

```powershell
.\Download-UPM-Package.ps1 -PackageVersion <package_version>
```

To list the versions available and download a specific version, run the following:

```powershell
.\Download-UPM-Package.ps1 -ListVersions
```

### Use NPM to download the UPM package

Requirements:

* A Linux, macOS, or Windows computer.
* [NPM](https://www.npmjs.com/get-npm)

Run the following command substituting `<version_number>` with the version of Azure Object Anchors you want to download
to the current folder:

```bash
npm pack com.microsoft.azure.object-anchors.runtime@<version_number> --registry https://api.bintray.com/npm/microsoft/AzureMixedReality-NPM
```

To list the available versions of the Azure Object Anchors UPM package, run the following:

```bash
npm view com.microsoft.azure.object-anchors.runtime --registry https://api.bintray.com/npm/microsoft/AzureMixedReality-NPM versions
```

## Install the UPM package into a sample or another project

After downloading the package using the instructions above, follow the instructions [here](https://docs.unity3d.com/Manual/upm-ui-tarball.html)
to import the package into the Unity project using the Unity Package Manager.

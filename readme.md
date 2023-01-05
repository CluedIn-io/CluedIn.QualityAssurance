# CluedIn.QualityAssurance

CluedIn Quality Assurance tool.

------

## Overview

This repository contains the tool to run several quality assurance tests on a CluedIn environment.

There are two ways to 'get' the tool
1. dotnet tool. This requires dotnet SDK
2. build the tool yourself. This requires the access to this github repository.
To run this, first build the solution using Visual Studio or using `dotnet build` command.


### Getting the tool via dotnet tool
To install via dotnet tool, perform the following steps:
1. Add the nuget repository    
   ```
   dotnet nuget add source https://pkgs.dev.azure.com/CluedIn-io/_packaging/develop/nuget/v3/index.json --name "CluedIn" --username "CanBeAnything" --password "YourNuGetPAT"
   ```
2. Install the tool. 
   ```
   dotnet tool install --global CluedIn.QualityAssurance.Cli --version "version of tool"
   ```
3. Open the terminal and enter `cluedin.qa.cli` in the terminal window. You should see output similar to the following 
   ```
   CluedIn.QualityAssurance.Cli 1.0.0
   Copyright (C) 2023 CluedIn.QualityAssurance.Cli
   ...
   ```
### Getting the tool by building the tool.
1. Build the solution using Visual Studio or using `dotnet build` command.
2. After that go to the output directory (e.g: `C:\Code\cluedin\CluedIn.PerformanceTester\src\PerformanceTester\bin\Debug\net6.0`) and open up a powershell window. After that, execute the tool and you will see output similar to the following
   ```
   CluedIn.QualityAssurance.Cli 1.0.0
   Copyright (C) 2023 CluedIn.QualityAssurance.Cli
   ...
   ```

4. There are multiple verbs available in the tool.

Pick a verb that you want and pass the relevant parameters. For example, 
```
.\CluedIn.QualityAssurance.exe file-upload --local --number-of-runs 1
```

You can also view the required parameters using
```
.\CluedIn.QualityAssurance.exe [verb] --help
```
For example, to view the help for the `file-upload` verb, run the following command.
```
.\CluedIn.QualityAssurance.exe file-upload --help
```

## Customization of mapping and test results

To perform customizations of mapping and test results, a file need to be created in the same location as the input file path and with `.customizations.json` suffix. 
The format of the file shown below.

```
{
  "customMapping": {
	"shouldAutoGenerateOriginEntityCodeKey": false,
    "requests": []
  },
  "customResult": {
    "testResultValues": []
  }
}
```

For example, if the input file is `C:\temp\input.csv`, then a mapping customization file with name `C:\temp\input.csv.customization.json` needs to be created.

For mapping customizations, set the `customMapping.shouldAutoGenerateOriginEntityCodeKey` to false if you want to disable setting of origin entity key to be auto generated.
To send custom request, add the request body to `customMapping.requests` array. 

For test result customization, add the customizations to `customResult.testResultValues` array. 

For an example customization file, please refer to example here [example csv file and its customization](./docs/customizations)

To run a test using the example file, 
```
.\CluedIn.QualityAssurance.exe file-upload --local --number-of-runs 1 --input-file C:/Path/To/The/Example/File.csv
```
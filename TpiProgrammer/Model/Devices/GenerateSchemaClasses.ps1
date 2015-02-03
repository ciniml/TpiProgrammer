$toolsPath = "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows\v8.1A\bin\NETFX 4.5.1 Tools"
$env:path += ';' + $toolsPath

xsd.exe 'DeviceInformation.xsd' /classes /namespace:TpiProgrammer.Model.Devices
Move-Item 'DeviceInformation.cs' 'DeviceInformation.xsd.cs' -Force


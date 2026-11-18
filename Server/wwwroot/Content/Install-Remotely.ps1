<#
.SYNOPSIS
   Installs the Remotely Client.
.DESCRIPTION
   Do not modify this script.  It was generated specifically for your account.
.EXAMPLE
   powershell.exe -f Install-Remotely.ps1
   powershell.exe -f Install-Remotely.ps1 -DeviceAlias "My Super Computer" -DeviceGroup "My Stuff"
#>

param (
	[string]$DeviceAlias,
	[string]$DeviceGroup,
	[string]$Path,
	[string]$OrganizationId,
	[string]$ServerUrl,
	[switch]$Uninstall,
	[switch]$Quiet
)

#region Set SecurityProtocol
# This will include all security protocols greater than or equal to TLS 1.2.
[System.Net.SecurityProtocolType]$SecurityProtocols = 0;
[System.Enum]::GetValues([System.Net.SecurityProtocolType]) | Where-Object {
	$_ -ge [System.Net.SecurityProtocolType]::Tls12
} | ForEach-Object {
	$SecurityProtocols = $SecurityProtocols -bor $_
}
[System.Net.ServicePointManager]::SecurityProtocol = $SecurityProtocols
#endregion

#region Set Variables
$LogPath = "$env:TEMP\Remotely_Install.txt"

[string]$HostName = $null
if ($ServerUrl) {
	$HostName = $ServerUrl
}

[string]$Organization = $null
if ($OrganizationId) {
	$Organization = $OrganizationId
}

$ConnectionInfo = $null

if ([System.Environment]::Is64BitOperatingSystem) {
	$Platform = "x64"
}
else {
	$Platform = "x86"
}

$InstallPath = "$env:ProgramFiles\Remotely"
#endregion

#region Functions
function Write-Log($Message) {
	if (!$Quiet) {
		Write-Host $Message
	}
	"$((Get-Date).ToString()) - $Message" | Out-File -FilePath $LogPath -Append
}
function Do-Exit() {
	Write-Log "Exiting..."
	Start-Sleep -Seconds 3
	exit
}
function Is-Administrator() {
	$Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$Principal = New-Object System.Security.Principal.WindowsPrincipal -ArgumentList $Identity
	return $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Run-StartupChecks {

	# Позволяем повторную установку/восстановление без параметров:
	# если рядом уже лежит ConnectionInfo.json, берём адрес сервера и организацию оттуда.
	if ((!$HostName -or !$Organization) -and (Test-Path -Path "$InstallPath\ConnectionInfo.json")) {
		try {
			$ExistingConnectionInfo = Get-Content -Path "$InstallPath\ConnectionInfo.json" -Raw | ConvertFrom-Json
			if (!$HostName -and $ExistingConnectionInfo.Host) {
				$script:HostName = $ExistingConnectionInfo.Host
			}
			if (!$Organization -and $ExistingConnectionInfo.OrganizationID) {
				$script:Organization = $ExistingConnectionInfo.OrganizationID
			}
			Write-Log "Using existing ConnectionInfo.json for server URL and organization."
		}
		catch {
			Write-Log "Failed to read existing ConnectionInfo.json.  Continuing with supplied parameters."
		}
	}

	if (!$HostName -or !$Organization) {
		Write-Log "Required parameters are missing.  Please try downloading the installer again."
		Do-Exit
	}

	if (!(Is-Administrator)) {
		Write-Log -Message "Install script requires elevation.  Attempting to self-elevate..."
		Start-Sleep -Seconds 3

		$Params = "-File `"$($MyInvocation.ScriptName)`"";
		if ($OrganizationId) {
			$Params += " -OrganizationId $OrganizationId"
		}
		if ($ServerUrl) {
			$Params += " -ServerUrl $ServerUrl"
		}
		if ($DeviceAlias) {
			$Params += " -DeviceAlias $DeviceAlias"
		}
		if ($DeviceGroup) {
			$Params += " -DeviceGroup $DeviceGroup"
		}
		if ($Path) {
			$Params += " -Path `"$Path`""
		}
		if ($Uninstall) {
			$Params += " -Uninstall"
		}
		if ($Quiet) {
			$Params += " -Quiet"
		}
		Start-Process -FilePath powershell.exe -ArgumentList $Params -Verb RunAs
		exit
	}
}

function Stop-Remotely {
	Start-Process -FilePath "cmd.exe" -ArgumentList "/c sc stop Remotely_Service & sc delete Remotely_Service" -Wait -WindowStyle Hidden
	Stop-Process -Name Remotely_Agent -Force -ErrorAction SilentlyContinue
	Stop-Process -Name Remotely_Desktop -Force -ErrorAction SilentlyContinue

	# Ждём, пока процесс действительно завершится и отпустит файлы.
	# Распаковка поверх ещё залоченных файлов оставляла пустые deps.json,
	# после чего агент не запускался.
	for ($i = 0; $i -lt 60; $i++) {
		if (!(Get-Process -Name Remotely_Agent -ErrorAction SilentlyContinue)) {
			break
		}
		Start-Sleep -Milliseconds 500
	}
	Start-Sleep -Milliseconds 500
}

function Uninstall-Remotely {
	Stop-Remotely
	Remove-Item -Path $InstallPath -Force -Recurse -ErrorAction SilentlyContinue
	Remove-NetFirewallRule -Name "Remotely Desktop Unattended" -ErrorAction SilentlyContinue
}

function Install-Remotely {
	$HeadResponse = Invoke-WebRequest -Uri "$HostName/Content/Remotely-Win-$Platform.zip" -Method Head -UseBasicParsing
	$ETag = $HeadResponse.Headers["ETag"]
	if (!$Etag) {
		Write-Log "Failed to get ETag from server.  Aborting install."
	}

	if ((Test-Path -Path "$InstallPath") -and (Test-Path -Path "$InstallPath\ConnectionInfo.json")) {
		$ConnectionInfo = Get-Content -Path "$InstallPath\ConnectionInfo.json" | ConvertFrom-Json
		if ($ConnectionInfo) {
			$ConnectionInfo.Host = $HostName
			$ConnectionInfo.OrganizationID = $Organization
			$ConnectionInfo.ServerVerificationToken = ""
		}
	}
	else {
		New-Item -ItemType Directory -Path "$InstallPath" -Force
	}

	if (!$ConnectionInfo) {
		$NewDeviceId = [System.Guid]::NewGuid().ToString();
		$ConnectionInfo = @{
			DeviceID                = $NewDeviceId;
			Host                    = $HostName;
			OrganizationID          = $Organization;
			ServerVerificationToken = "";
		}
	}

	if ($HostName.EndsWith("/")) {
		$HostName = $HostName.Substring(0, $HostName.LastIndexOf("/"))
	}

	if ($Path) {
		Write-Log "Copying install files..."
		Copy-Item -Path $Path -Destination "$env:TEMP\Remotely-Win-$Platform.zip"

	}
	else {
		$ProgressPreference = 'SilentlyContinue'
		Write-Log "Downloading client..."
		Invoke-WebRequest -Uri "$HostName/Content/Remotely-Win-$Platform.zip" -OutFile "$env:TEMP\Remotely-Win-$Platform.zip" -UseBasicParsing
		$ProgressPreference = 'Continue'
	}

	if (!(Test-Path -Path "$env:TEMP\Remotely-Win-$Platform.zip")) {
		Write-Log "Client files failed to download."
		Do-Exit
	}

	# Распаковываем и проверяем новую версию ДО того, как трогать рабочую установку.
	# Прерванная переустановка раньше оставляла пустые файлы в рабочем каталоге
	# (например, Remotely_Agent.deps.json), и агент не мог запуститься.
	$StagingPath = "$InstallPath.new"
	Remove-Item -Path $StagingPath -Recurse -Force -ErrorAction SilentlyContinue
	Expand-Archive -Path "$env:TEMP\Remotely-Win-$Platform.zip" -DestinationPath $StagingPath -Force

	$StagedDepsPath = "$StagingPath\Remotely_Agent.deps.json"
	$PackageIsValid = (Test-Path -Path "$StagingPath\Remotely_Agent.exe") -and
		(Test-Path -Path $StagedDepsPath) -and
		((Get-Item -Path $StagedDepsPath).Length -gt 100)

	if (!$PackageIsValid) {
		Write-Log "Downloaded package is incomplete or corrupted.  Aborting without changing the current installation."
		Remove-Item -Path $StagingPath -Recurse -Force -ErrorAction SilentlyContinue
		Do-Exit
	}

	Stop-Remotely

	# Замена каталога: сначала переименовываем старый (это почти атомарная операция),
	# затем ставим на его место новый. Если переименовать не удалось, рабочая
	# установка остаётся нетронутой.
	$OldInstallPath = "$InstallPath.old"
	Remove-Item -Path $OldInstallPath -Recurse -Force -ErrorAction SilentlyContinue

	if (Test-Path -Path $InstallPath) {
		try {
			Rename-Item -Path $InstallPath -NewName $OldInstallPath -ErrorAction Stop
		}
		catch {
			Write-Log "Failed to move the current installation aside.  Aborting to keep the current version working."
			Remove-Item -Path $StagingPath -Recurse -Force -ErrorAction SilentlyContinue
			Do-Exit
		}
	}

	Move-Item -Path $StagingPath -Destination $InstallPath -Force
	Remove-Item -Path $OldInstallPath -Recurse -Force -ErrorAction SilentlyContinue

	New-Item -ItemType File -Path "$InstallPath\ConnectionInfo.json" -Value (ConvertTo-Json -InputObject $ConnectionInfo) -Force

	New-Item -ItemType File -Path "$InstallPath\etag.txt" -Value $ETag -Force

	if ($DeviceAlias -or $DeviceGroup) {
		$DeviceSetupOptions = @{
			DeviceAlias     = $DeviceAlias;
			DeviceGroupName = $DeviceGroup;
			OrganizationID  = $Organization;
			DeviceID        = $ConnectionInfo.DeviceID;
		}

		$Body = $DeviceSetupOptions | ConvertTo-Json
		Invoke-RestMethod -Method Post -ContentType "application/json" -Uri "$HostName/api/devices" -Body $Body
	}

	# Служба могла остаться зарегистрированной (например, при обновлении),
	# тогда New-Service упадёт.
	if (Get-Service -Name "Remotely_Service" -ErrorAction SilentlyContinue) {
		Start-Process -FilePath "cmd.exe" -ArgumentList "/c sc delete Remotely_Service" -Wait -WindowStyle Hidden
		Start-Sleep -Seconds 2
	}

	New-Service -Name "Remotely_Service" -BinaryPathName "`"$InstallPath\Remotely_Agent.exe`"" -DisplayName "Remotely Service" -StartupType Automatic -Description "Background service that maintains a connection to the Remotely server.  The service is used for remote support and maintenance by this computer's administrators."
	Start-Process -FilePath "cmd.exe" -ArgumentList "/c sc.exe failure `"Remotely_Service`" reset=5 actions=restart/5000" -Wait -WindowStyle Hidden
	Start-Service -Name Remotely_Service
}

#endregion

#region Main

try {
	Run-StartupChecks

	Write-Log "Install/uninstall logs are being written to `"$LogPath`""

	if ($Uninstall) {
		Write-Log "Uninstall started."
		Uninstall-Remotely
		Write-Log "Uninstall completed."
		exit
	}
	else {
		Write-Log "Install started."
		Install-Remotely
		Write-Log "Install completed."
		exit
	}
}
catch {
	Write-Log -Message "Error occurred: $($Error[0].InvocationInfo.PositionMessage)"
	throw $Error[0]
	Do-Exit
}
#endregion
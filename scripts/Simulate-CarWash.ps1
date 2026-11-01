#Requires -Version 7
<#
.SYNOPSIS
    Simulates ESP32 car wash controllers posting service events to CarWash Hub.

.DESCRIPTION
    Each event is exactly what the real controller sends:

        POST /api/v1/machine-messages
        { "machineId": "<MAC>", "data": { "service": "soap", "macAddress": "<MAC>", "readAt": "2026-10-31T13:03:45Z" } }

    Use it to watch the dashboard live, to drive a service past its refill capacity, or to load-test
    the processing worker. Machine ids default to locally-administered MAC addresses (02:00:00:...),
    which no real controller uses.

.EXAMPLE
    ./scripts/Simulate-CarWash.ps1
    Three machines, one event every 2 seconds, until Ctrl+C - against the local Development host.

.EXAMPLE
    ./scripts/Simulate-CarWash.ps1 -Machines 10 -IntervalSeconds 0.1 -Count 2000
    A burst of 2,000 events across ten machines.

.EXAMPLE
    ./scripts/Simulate-CarWash.ps1 -MachineIds 'B0:CB:D8:D0:64:2C' -Services soap -Count 50
    Fifty soap uses on one machine - enough to cross a small refill's low and empty thresholds.

.EXAMPLE
    ./scripts/Simulate-CarWash.ps1 -BaseUrl https://staging.example.com -Key $env:CARWASH_INGEST_KEY -RetryPercent 10
    Against staging, re-sending one event in ten as a machine retrying after a timeout would.
#>
[CmdletBinding()]
param(
    # The host. The Development profile listens on http://localhost:5180.
    [string] $BaseUrl = 'http://localhost:5180',

    # The client-secret-key. Defaults to the Development key from appsettings.Development.json.
    [string] $Key = 'dev-only-ingest-key-0123456789abcdef',

    # How many simulated machines, when -MachineIds is not given.
    [ValidateRange(1, 500)]
    [int] $Machines = 3,

    # Explicit machine ids (MAC addresses), instead of generated ones.
    [string[]] $MachineIds,

    # The services to use. Soap and water are drawn twice as often as anything else.
    [string[]] $Services = @('soap', 'water', 'vacuum'),

    # Seconds between events.
    [ValidateRange(0, 3600)]
    [double] $IntervalSeconds = 2,

    # How many events to send; 0 sends until Ctrl+C.
    [ValidateRange(0, [int]::MaxValue)]
    [int] $Count = 0,

    # Shifts readAt, as a controller with a drifting clock would. Negative is behind.
    [int] $SkewSeconds = 0,

    # The share of events, in percent, sent again a few seconds later - a controller retrying
    # after its request timed out. The hub counts each of those once.
    [ValidateRange(0, 100)]
    [int] $RetryPercent = 0,

    # How long a simulated controller waits before retrying.
    [ValidateRange(1, 60)]
    [int] $RetryAfterSeconds = 3
)

$ErrorActionPreference = 'Stop'

if (-not $MachineIds) {
    $MachineIds = 1..$Machines | ForEach-Object { '02:00:00:00:{0:X2}:{1:X2}' -f ($_ -shr 8), ($_ -band 0xFF) }
}

$weighted = foreach ($service in $Services) {
    $service
    if ($service -in 'soap', 'water') { $service }
}

$uri = '{0}/api/v1/machine-messages' -f $BaseUrl.TrimEnd('/')
$headers = @{ 'client-secret-key' = $Key }

function Send-Event([string] $machineId, [string] $service, [string] $readAt) {
    $body = @{
        machineId = $machineId
        data      = @{ service = $service; macAddress = $machineId; readAt = $readAt }
    } | ConvertTo-Json -Compress

    $response = Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -Body $body `
        -ContentType 'application/json' -SkipHttpErrorCheck -UserAgent 'CarWashHubSimulator/1.0'

    if ($response.StatusCode -eq 201) {
        $id = ($response.Content | ConvertFrom-Json).data.messageId
        return "201 #$id"
    }

    return "$($response.StatusCode) $($response.Content)"
}

Write-Host "Sending to $uri as $($MachineIds.Count) machine(s): $($MachineIds -join ', ')" -ForegroundColor Cyan
Write-Host 'Press Ctrl+C to stop.' -ForegroundColor DarkGray

$retries = [System.Collections.Generic.List[object]]::new()

function Send-DueRetries([switch] $All) {
    foreach ($retry in @($retries)) {
        if ($All -or $retry.Due -le [DateTimeOffset]::UtcNow) {
            if ($All) {
                $wait = $retry.Due - [DateTimeOffset]::UtcNow
                if ($wait -gt [TimeSpan]::Zero) { Start-Sleep -Milliseconds ([int]$wait.TotalMilliseconds) }
            }

            $result = Send-Event $retry.MachineId $retry.Service $retry.ReadAt
            Write-Host ('[{0:HH:mm:ss}] {1,-17} {2,-8} {3} (retry of {4}, counted once)' -f (Get-Date), $retry.MachineId, $retry.Service, $result, $retry.ReadAt) -ForegroundColor DarkYellow
            [void]$retries.Remove($retry)
        }
    }
}

$sent = 0
while ($Count -eq 0 -or $sent -lt $Count) {
    Send-DueRetries

    $machineId = Get-Random -InputObject $MachineIds
    $service = Get-Random -InputObject $weighted
    $readAt = [DateTimeOffset]::UtcNow.AddSeconds($SkewSeconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [cultureinfo]::InvariantCulture)

    $result = Send-Event $machineId $service $readAt
    $sent++
    Write-Host ('[{0:HH:mm:ss}] {1,-17} {2,-8} {3}' -f (Get-Date), $machineId, $service, $result)

    if ((Get-Random -Maximum 100) -lt $RetryPercent) {
        $retries.Add([pscustomobject]@{
                MachineId = $machineId
                Service   = $service
                ReadAt    = $readAt
                Due       = [DateTimeOffset]::UtcNow.AddSeconds($RetryAfterSeconds)
            })
    }

    if ($IntervalSeconds -gt 0) {
        Start-Sleep -Milliseconds ([int]($IntervalSeconds * 1000))
    }
}

Send-DueRetries -All
Write-Host "Sent $sent event(s)." -ForegroundColor Cyan

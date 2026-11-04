#Requires -Version 7
<#
.SYNOPSIS
    Simulates ESP32 + A7670G controllers on the event firmware (protocol baseline 2026-11-03).

.DESCRIPTION
    Each simulated machine behaves like the real firmware:

      - posts device_boot once, then water_status about every 30 seconds;
      - posts payment_success for random services, with price_qirsh, paid_qirsh and duration_seconds;
      - polls GET /api/v1/settings?machineId=<MAC> about every 15 seconds, and acknowledges each new
        revision with settings_ack ("saved", or "busy" first now and then);
      - can resend a payment with the same event_id, as the firmware does after a timeout - the hub
        answers 201 again and counts it once.

    Machine ids default to locally-administered MAC addresses (02:00:00:...), which no real
    controller uses. A new id appears in the dashboard's inbox; assign it to a station whose prices
    and run times are set to see settings revisions flow.

.EXAMPLE
    ./scripts/Simulate-Esp32Device.ps1
    Two machines against the local Development host, until Ctrl+C.

.EXAMPLE
    ./scripts/Simulate-Esp32Device.ps1 -MachineIds '02:00:00:00:00:01' -NoWater -DurationSeconds 120
    One machine reporting no water, for two minutes - enough to raise the no-water alert.
#>
[CmdletBinding()]
param(
    # The host. The Development profile listens on http://localhost:5180.
    [string] $BaseUrl = 'http://localhost:5180',

    # The client-secret-key. Defaults to the Development key from appsettings.Development.json.
    [string] $Key = 'dev-only-ingest-key-0123456789abcdef',

    # How many simulated machines, when -MachineIds is not given.
    [ValidateRange(1, 50)]
    [int] $Machines = 2,

    # Explicit machine ids (MAC addresses), instead of generated ones.
    [string[]] $MachineIds,

    # Seconds between payments on each machine, on average.
    [ValidateRange(1, 3600)]
    [int] $PaymentEverySeconds = 20,

    # Seconds between water_status reports.
    [ValidateRange(1, 3600)]
    [int] $WaterEverySeconds = 30,

    # Seconds between settings polls.
    [ValidateRange(1, 3600)]
    [int] $PollEverySeconds = 15,

    # Report water as unavailable.
    [switch] $NoWater,

    # The share of payments, in percent, posted twice with the same event_id.
    [ValidateRange(0, 100)]
    [int] $RetryPercent = 10,

    # How long to run, in seconds; 0 runs until Ctrl+C.
    [ValidateRange(0, [int]::MaxValue)]
    [int] $DurationSeconds = 0
)

$ErrorActionPreference = 'Stop'

if (-not $MachineIds) {
    $MachineIds = 1..$Machines | ForEach-Object { '02:00:00:00:{0:X2}:{1:X2}' -f ($_ -shr 8), ($_ -band 0xFF) }
}

$base = $BaseUrl.TrimEnd('/')
$headers = @{ 'client-secret-key' = $Key }
$services = @(
    @{ Code = 'water'; Price = 50; Durations = @(180, 180, 210) }
    @{ Code = 'soap'; Price = 50; Durations = @(60, 90, 90) }
    @{ Code = 'minute_water'; Price = 25; Durations = @(60) }
    @{ Code = 'vacuum'; Price = 50; Durations = @(180, 180, 210) }
)

$started = [DateTimeOffset]::UtcNow
$state = @{}
foreach ($mac in $MachineIds) {
    $state[$mac] = [pscustomobject]@{
        BootId      = -join ((1..16) | ForEach-Object { '{0:X}' -f (Get-Random -Maximum 16) })
        Sequence    = 0
        Applied     = 0
        NextWater   = [DateTimeOffset]::UtcNow
        NextPoll    = [DateTimeOffset]::UtcNow
        NextPayment = [DateTimeOffset]::UtcNow.AddSeconds((Get-Random -Maximum $PaymentEverySeconds))
    }
}

function Uptime { [long]([DateTimeOffset]::UtcNow - $started).TotalMilliseconds }

function Send-DeviceEvent([string] $mac, [string] $eventName, [hashtable] $fields, [string] $eventId) {
    $machine = $state[$mac]
    if (-not $eventId) {
        $machine.Sequence++
        $eventId = '{0}-{1}-{2}-{3}' -f $mac, $machine.BootId, $eventName, $machine.Sequence
    }

    $data = [ordered]@{ event = $eventName; device_mac = $mac; boot_id = $machine.BootId; event_id = $eventId }
    foreach ($name in $fields.Keys) { $data[$name] = $fields[$name] }

    $body = @{ machineId = $mac; data = $data } | ConvertTo-Json -Compress -Depth 5
    $response = Invoke-WebRequest -Uri "$base/api/v1/machine-messages" -Method Post -Headers $headers -Body $body `
        -ContentType 'application/json' -SkipHttpErrorCheck -UserAgent 'CarWashHubEsp32Simulator/1.0'

    $result = if ($response.StatusCode -eq 201) {
        $stored = ($response.Content | ConvertFrom-Json).data
        if ($stored.duplicate) { "201 #$($stored.messageId) (duplicate)" } else { "201 #$($stored.messageId)" }
    }
    else {
        "$($response.StatusCode) $($response.Content)"
    }

    Write-Host ('[{0:HH:mm:ss}] {1} {2,-16} {3}' -f (Get-Date), $mac, $eventName, $result)
    return $eventId
}

function Get-Settings([string] $mac) {
    $response = Invoke-WebRequest -Uri "$base/api/v1/settings?machineId=$([uri]::EscapeDataString($mac))" -Headers $headers `
        -SkipHttpErrorCheck -UserAgent 'CarWashHubEsp32Simulator/1.0'
    if ($response.StatusCode -ne 200) {
        Write-Host ('[{0:HH:mm:ss}] {1} settings poll -> {2}' -f (Get-Date), $mac, $response.StatusCode) -ForegroundColor Red
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

Write-Host "Simulating $($MachineIds.Count) event-firmware machine(s) against $base" -ForegroundColor Cyan
Write-Host 'Press Ctrl+C to stop.' -ForegroundColor DarkGray

foreach ($mac in $MachineIds) {
    [void](Send-DeviceEvent $mac 'device_boot' @{ water_available = -not $NoWater })
}

while ($DurationSeconds -eq 0 -or ([DateTimeOffset]::UtcNow - $started).TotalSeconds -lt $DurationSeconds) {
    $now = [DateTimeOffset]::UtcNow

    foreach ($mac in $MachineIds) {
        $machine = $state[$mac]

        if ($now -ge $machine.NextWater) {
            [void](Send-DeviceEvent $mac 'water_status' @{ water_available = -not $NoWater; uptime_ms = (Uptime) })
            $machine.NextWater = $now.AddSeconds($WaterEverySeconds)
        }

        if ($now -ge $machine.NextPoll) {
            $machine.NextPoll = $now.AddSeconds($PollEverySeconds)
            $settings = Get-Settings $mac
            if ($settings -and $settings.revision -gt 0 -and $settings.revision -ne $machine.Applied) {
                Write-Host ('[{0:HH:mm:ss}] {1} settings revision {2}' -f (Get-Date), $mac, $settings.revision) -ForegroundColor Green
                foreach ($service in $services) {
                    $sent = $settings.services.($service.Code)
                    if ($sent) {
                        $service.Price = $sent.price_qirsh
                        $service.Durations = if ($sent.durations_seconds) { @($sent.durations_seconds) } else { @($sent.duration_seconds) }
                    }
                }

                # Busy now and then, as a machine in the middle of a wash would be; it applies next poll.
                if ((Get-Random -Maximum 4) -eq 0) {
                    [void](Send-DeviceEvent $mac 'settings_ack' @{ revision = $settings.revision; status = 'busy'; water_available = -not $NoWater })
                }
                else {
                    [void](Send-DeviceEvent $mac 'settings_ack' @{ revision = $settings.revision; status = 'saved'; water_available = -not $NoWater })
                    $machine.Applied = $settings.revision
                }
            }
        }

        if ($now -ge $machine.NextPayment -and -not $NoWater) {
            $service = Get-Random -InputObject $services
            $paid = $service.Price + (Get-Random -InputObject @(0, 0, 0, 5, 25))
            $fields = @{
                service          = $service.Code
                price_qirsh      = $service.Price
                paid_qirsh       = $paid
                duration_seconds = Get-Random -InputObject $service.Durations
                uptime_ms        = (Uptime)
                water_available  = $true
            }

            $eventId = Send-DeviceEvent $mac 'payment_success' $fields
            if ((Get-Random -Maximum 100) -lt $RetryPercent) {
                [void](Send-DeviceEvent $mac 'payment_success' $fields $eventId)
            }

            $machine.NextPayment = $now.AddSeconds((Get-Random -Minimum ([math]::Max(1, $PaymentEverySeconds / 2)) -Maximum ($PaymentEverySeconds * 1.5)))
        }
    }

    Start-Sleep -Milliseconds 500
}

Write-Host 'Done.' -ForegroundColor Cyan

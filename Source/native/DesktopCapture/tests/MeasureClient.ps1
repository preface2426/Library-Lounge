param([int]$ClientId,[string]$OutputFile)
$ErrorActionPreference='Stop'
$lastCpu=0.0; $lastTime=Get-Date
while($true) {
    $client=Get-Process -Id $ClientId -ErrorAction SilentlyContinue
    if(!$client) { break }
    $now=Get-Date
    $elapsed=($now-$lastTime).TotalSeconds
    $cpu=if($lastCpu -gt 0 -and $elapsed -gt 0) {($client.TotalProcessorTime.TotalSeconds-$lastCpu)/$elapsed*100/[Environment]::ProcessorCount} else {0}
    $lastCpu=$client.TotalProcessorTime.TotalSeconds; $lastTime=$now
    $memory=Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory | Where-Object Name -Like "pid_${ClientId}_*"
    $engines=Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine | Where-Object Name -Like "pid_${ClientId}_*"
    $threeD=($engines | Where-Object Name -Like '*engtype_3D' | Measure-Object UtilizationPercentage -Sum).Sum
    $copy=($engines | Where-Object Name -Like '*engtype_Copy' | Measure-Object UtilizationPercentage -Sum).Sum
    $dedicated=($memory | Measure-Object DedicatedUsage -Sum).Sum
    $shared=($memory | Measure-Object SharedUsage -Sum).Sum
    [pscustomobject]@{Utc=$now.ToUniversalTime().ToString('O');ClientId=$ClientId;CpuMachinePercent=[math]::Round($cpu,3);PrivateMB=[math]::Round($client.PrivateMemorySize64/1MB,2);WorkingMB=[math]::Round($client.WorkingSet64/1MB,2);DedicatedGpuMB=[math]::Round($dedicated/1MB,2);SharedGpuMB=[math]::Round($shared/1MB,2);Gpu3DPercent=$threeD;GpuCopyPercent=$copy} | Export-Csv -LiteralPath $OutputFile -Append -NoTypeInformation -Encoding utf8
    Start-Sleep -Seconds 20
}

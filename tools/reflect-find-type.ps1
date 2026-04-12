$paths = @(
    'D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll',
    'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll'
)
$p = $paths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $p) { throw 'sts2.dll not found' }
$dir = Split-Path $p -Parent
[System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'GodotSharp.dll')) | Out-Null
$a = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $p).Path)
$a.GetTypes() | Where-Object { $_.Name -match 'CardReward' } | ForEach-Object { $_.FullName }

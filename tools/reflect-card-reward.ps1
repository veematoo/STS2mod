$paths = @(
    'D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll',
    'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll'
)
$p = $paths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $p) { throw 'sts2.dll not found' }
$dir = Split-Path $p -Parent
# Full load (not reflection-only) so GodotSharp resolves for type inspection
[System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'GodotSharp.dll')) | Out-Null
$a = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $p).Path)
$t = $a.GetType('MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen')
if ($null -eq $t) { Write-Output 'TYPE_NULL'; exit 1 }
Write-Output '--- Declared instance fields ---'
$t.GetFields([System.Reflection.BindingFlags]'NonPublic,Public,Instance,DeclaredOnly') |
    ForEach-Object { $_.Name } | Sort-Object
Write-Output '--- Declared methods (name only) ---'
$t.GetMethods([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly') |
    Select-Object -ExpandProperty Name | Sort-Object -Unique

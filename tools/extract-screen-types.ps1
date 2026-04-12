$dll = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll'
$b = [IO.File]::ReadAllBytes($dll)
$s = [Text.Encoding]::ASCII.GetString($b)
$rx = [regex]'MegaCrit\.Sts2\.Core\.Nodes\.Screens\.CardSelection\.N[A-Za-z0-9_]+Screen'
$rx.Matches($s) | ForEach-Object { $_.Value } | Sort-Object -Unique

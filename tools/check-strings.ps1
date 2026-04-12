$dll = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll'
$raw = [IO.File]::ReadAllBytes($dll)
$text = [Text.Encoding]::UTF8.GetString($raw)
@(
    'NCardRewardSelectionScreen',
    'NChooseACardSelectionScreen',
    'AfterOverlayOpened',
    '_cardRow',
    '_cardChoices'
) | ForEach-Object { "${_}: $($text.Contains($_))" }

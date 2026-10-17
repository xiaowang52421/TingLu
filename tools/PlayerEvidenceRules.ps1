function Get-TingLuSpeechStartEvidence([string]$LogText) {
    $matches = @([regex]::Matches($LogText, '(?m)^TingLu speech started; backend=(?<backend>[^;\r\n]+); frames=(?<frames>[1-9]\d*)\s*$'))
    return [pscustomobject]@{
        Verified = ($matches.Count -gt 0)
        Count = $matches.Count
        Backends = @($matches | ForEach-Object { $_.Groups['backend'].Value } | Select-Object -Unique)
    }
}

function Get-TingLuVisualLayoutStatus([bool]$VisibleWindow) {
    if ($VisibleWindow) { return 'NOT_REVIEWED' }
    return 'NOT_VERIFIED_HIDDEN_WINDOW'
}

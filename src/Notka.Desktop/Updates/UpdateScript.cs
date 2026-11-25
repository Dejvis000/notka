namespace Notka.Desktop.Updates
{
    internal static class UpdateScript
    {
        public const string Text = """
            param(
              [Parameter(Mandatory)][int]$ProcessId,
              [Parameter(Mandatory)][string]$Source,
              [Parameter(Mandatory)][string]$Target,
              [Parameter(Mandatory)][string]$Exe,
              [Parameter(Mandatory)][string]$Version,
              [Parameter(Mandatory)][string]$Work
            )
            $ErrorActionPreference = 'Stop'
            $log = Join-Path $Work 'update.log'
            $resultPath = Join-Path $Work 'result.json'
            $backup = Join-Path $Work 'backup'
            function Log($m) { Add-Content -LiteralPath $log -Value ('{0:s} {1}' -f (Get-Date), $m) }
            function Copy-Tree($from, $to) {
              & robocopy.exe $from $to /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
              if ($LASTEXITCODE -ge 8) { throw "robocopy failed with code $LASTEXITCODE" }
            }
            $ok = $false
            $attempted = $true
            $restored = $false
            $message = ''
            $needRestore = $false
            try {
              Log "waiting for process $ProcessId"
              $app = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
              if ($app -and -not $app.WaitForExit(120000)) { $attempted = $false; $message = 'the app did not exit in time'; Log $message; return }
              $prefix = [IO.Path]::GetFullPath($Target).TrimEnd('\') + '\'
              for ($i = 0; $i -lt 20; $i++) {
                $servers = @(Get-CimInstance Win32_Process -Filter "Name = 'Notka.Mcp.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
                if ($servers.Count -eq 0) { break }
                foreach ($s in $servers) { Stop-Process -Id $s.ProcessId -Force -ErrorAction SilentlyContinue }
                Start-Sleep -Milliseconds 500
              }
              if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
              Log 'backing up the files that will be replaced'
              $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
              foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
                $relative = $file.FullName.Substring($sourceRoot.Length)
                $existing = Join-Path $Target $relative
                if (Test-Path -LiteralPath $existing) {
                  $copy = Join-Path $backup $relative
                  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $copy) | Out-Null
                  Copy-Item -LiteralPath $existing -Destination $copy -Force
                }
              }
              $needRestore = $true
              Log 'copying the new version'
              Copy-Tree $Source $Target
              $ok = $true
              $needRestore = $false
              Log 'update finished'
            } catch {
              $message = $_.Exception.Message
              Log "error: $message"
            } finally {
              if ($needRestore) {
                try { Copy-Tree $backup $Target; $restored = $true; Log 'restored the previous version' } catch { Log "restore failed: $($_.Exception.Message)" }
              }
              @{ ok = $ok; version = $Version; message = $message; attempted = $attempted; restored = $restored } | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding utf8
              if ($ok -or $restored) { Remove-Item -LiteralPath $Source, $backup -Recurse -Force -ErrorAction SilentlyContinue }
              Start-Process -FilePath $Exe
            }
            """;
    }
}

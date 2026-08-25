param(
    [Parameter(Mandatory = $true)]
    [string]$WorkbookPath,

    [Parameter(Mandatory = $true)]
    [string[]]$Keywords,

    [int]$MaximumMatches = 160
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $WorkbookPath).Path
$excel = $null
$workbook = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AskToUpdateLinks = $false
    $workbook = $excel.Workbooks.Open($resolvedPath, 0, $true)

    $matchCount = 0
    foreach ($worksheet in $workbook.Worksheets) {
        $used = $worksheet.UsedRange
        $rows = [Math]::Min([int]$used.Rows.Count, 5000)
        $columns = [Math]::Min([int]$used.Columns.Count, 200)
        for ($row = 1; $row -le $rows; $row++) {
            for ($column = 1; $column -le $columns; $column++) {
                $cell = $used.Cells.Item($row, $column)
                $text = [string]$cell.Text
                $formula = [string]$cell.Formula
                if ([string]::IsNullOrWhiteSpace($text) -and
                    [string]::IsNullOrWhiteSpace($formula)) {
                    continue
                }

                $haystack = "$text $formula"
                $matched = $false
                foreach ($keyword in $Keywords) {
                    if ($haystack.IndexOf($keyword, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        $matched = $true
                        break
                    }
                }

                if (-not $matched) {
                    continue
                }

                [PSCustomObject]@{
                    Workbook = [IO.Path]::GetFileName($resolvedPath)
                    Sheet = [string]$worksheet.Name
                    Cell = [string]$cell.Address($false, $false)
                    Value = $text
                    Formula = $formula
                }
                $matchCount++
                if ($matchCount -ge $MaximumMatches) {
                    return
                }
            }
        }
    }
}
finally {
    if ($workbook) {
        $workbook.Close($false)
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($workbook) | Out-Null
    }
    if ($excel) {
        $excel.Quit()
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel) | Out-Null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
